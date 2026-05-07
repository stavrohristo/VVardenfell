using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageApplySystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathStateSystem))]
    public partial struct MorrowindOnHitAftermathSystem : ISystem
    {
        const string HitVoiceDialogueId = "hit";
        const float DamageEpsilon = 0.001f;
        const int FriendlyHitForgivenessLimit = 4;
        static readonly short VampirismEffectId = RequireEffectId("sEffectVampirism");

        EntityQuery _playerQuery;

        enum CombatCrimeType : byte
        {
            Assault = 0,
            Murder = 1,
        }

        struct CombatCrimeReportResult
        {
            public bool Reported;
            public bool TargetPursuitQueued;
            public bool TargetCombatQueued;
        }

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            systemState.RequireForUpdate<MorrowindDamageAppliedEvent>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][OnHit] On-hit aftermath requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            int voiceHitOdds = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iVoiceHitOdds);
            if (voiceHitOdds < 0 || voiceHitOdds > 100)
                throw new InvalidOperationException($"[VVardenfell][OnHit] GMST 'iVoiceHitOdds' must be between 0 and 100, got {voiceHitOdds}.");

            bool hasHitVoiceDialogue = TryResolveHitVoiceDialogue(ref content, out int hitDialogueIndex);
            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!systemState.EntityManager.HasBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindCombatHitVoiceSayRequest buffer.");
            if (!systemState.EntityManager.HasBuffer<MorrowindCombatHitVoiceResolveRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindCombatHitVoiceResolveRequest buffer.");
            if (!systemState.EntityManager.HasBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindScriptActiveSay buffer.");

            var hitVoiceRequests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity);
            var hitVoiceResolveRequests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceResolveRequest>(scriptRuntimeEntity);
            var activeSays = systemState.EntityManager.GetBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity, true);
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            uint randomState = combatState.RandomState;
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            using var combatStartActors = new NativeList<Entity>(Allocator.Temp);
            using var combatStartTargets = new NativeList<Entity>(Allocator.Temp);
            using var guardPursuitActors = new NativeList<Entity>(Allocator.Temp);
            using var guardPursuitTargets = new NativeList<Entity>(Allocator.Temp);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                ApplyHitMemory(ref systemState, damage.ValueRO);

                bool setOnPcHitMe = ApplySocialHitAftermath(ref systemState, 
                    ref content,
                    damage.ValueRO,
                    activeSays,
                    hitVoiceRequests,
                    hitVoiceResolveRequests,
                    hitDialogueIndex,
                    hasHitVoiceDialogue,
                    combatStartActors,
                    combatStartTargets,
                    guardPursuitActors,
                    guardPursuitTargets,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    ref randomState);

                if (setOnPcHitMe && IsPlayerAttacker(ref systemState, damage.ValueRO.Attacker))
                    SetOnPcHitMeIfDeclared(ref systemState, ref content, damage.ValueRO.Target);

                if (damage.ValueRO.TargetVital == MorrowindDamageTargetVital.Health
                    && damage.ValueRO.Amount > DamageEpsilon
                    && IsPlayer(ref systemState, damage.ValueRO.Target))
                {
                    RuntimeShellStateUtility.ActivateHitOverlay(ref shell);
                }

                if (damage.ValueRO.Amount <= DamageEpsilon || damage.ValueRO.Attacker == Entity.Null)
                    continue;

                ApplyMurderAftermath(
                    ref systemState,
                    ref content,
                    damage.ValueRO,
                    combatStartActors,
                    combatStartTargets,
                    guardPursuitActors,
                    guardPursuitTargets,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    ref randomState);

                if (!IsNpcTarget(ref systemState, ref content, damage.ValueRO.Target, out var targetSource))
                    continue;

                uint targetRef = PlacedRefIdOrZero(ref systemState, damage.ValueRO.Target);
                bool actorSayingOrPending = IsActorSayingOrPending(activeSays, hitVoiceRequests, hitVoiceResolveRequests, damage.ValueRO.Target, targetRef);
                bool shouldQueueHitVoice = hasHitVoiceDialogue && !actorSayingOrPending && ShouldQueueHitVoice(ref systemState, damage.ValueRO, voiceHitOdds, ref randomState);

                if (shouldQueueHitVoice)
                {
                    QueueHitVoiceResolve(ref systemState, hitDialogueIndex, targetSource.Definition, damage.ValueRO.Target, hitVoiceResolveRequests, randomState);
                }
            }

            for (int i = 0; i < combatStartActors.Length; i++)
                StartCombatAfterHit(ref systemState, ref content, combatStartActors[i], combatStartTargets[i]);
            for (int i = 0; i < guardPursuitActors.Length; i++)
                ScheduleGuardPursuit(ref systemState, ref content, guardPursuitActors[i], guardPursuitTargets[i]);

            combatState.RandomState = randomState == 0u ? 1u : randomState;
        }

        bool ApplySocialHitAftermath(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in MorrowindDamageAppliedEvent damage,
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> hitVoiceRequests,
            DynamicBuffer<MorrowindCombatHitVoiceResolveRequest> hitVoiceResolveRequests,
            int hitDialogueIndex,
            bool hasHitVoiceDialogue,
            NativeList<Entity> combatStartActors,
            NativeList<Entity> combatStartTargets,
            NativeList<Entity> guardPursuitActors,
            NativeList<Entity> guardPursuitTargets,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            ref uint randomState)
        {
            if (damage.Attacker == Entity.Null
                || !IsActorEntity(ref systemState, damage.Attacker)
                || damage.Target == Entity.Null
                || !systemState.EntityManager.Exists(damage.Target)
                || !systemState.EntityManager.HasComponent<ActorSpawnSource>(damage.Target)
                || IsInCombatWith(ref systemState, damage.Target, damage.Attacker))
            {
                return true;
            }

            if (IsFriendlyHit(ref systemState, damage, out bool complain))
            {
                if (complain && hasHitVoiceDialogue && IsNpcTarget(ref systemState, ref content, damage.Target, out var friendlyTargetSource))
                {
                    uint targetRef = PlacedRefIdOrZero(ref systemState, damage.Target);
                    if (!IsActorSayingOrPending(activeSays, hitVoiceRequests, hitVoiceResolveRequests, damage.Target, targetRef))
                        QueueHitVoiceResolve(ref systemState, hitDialogueIndex, friendlyTargetSource.Definition, damage.Target, hitVoiceResolveRequests, randomState);
                }

                return false;
            }

            CombatCrimeReportResult crimeResult = default;
            if (CanCommitAssaultCrime(ref systemState, ref content, damage.Target, damage.Attacker))
            {
                crimeResult = ReportCombatCrime(
                    ref systemState,
                    ref content,
                    CombatCrimeType.Assault,
                    damage.Attacker,
                    damage.Target,
                    combatStartActors,
                    combatStartTargets,
                    guardPursuitActors,
                    guardPursuitTargets,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    ref randomState);
            }

            if (!crimeResult.TargetPursuitQueued
                && !crimeResult.TargetCombatQueued
                && ShouldStartCombatAfterHit(ref systemState, ref content, damage.Target, damage.Attacker))
            {
                AddUniqueCombat(combatStartActors, combatStartTargets, damage.Target, damage.Attacker);
            }

            return true;
        }

        bool IsFriendlyHit(ref SystemState systemState, in MorrowindDamageAppliedEvent damage, out bool complain)
        {
            complain = false;
            if (!IsPlayer(ref systemState, damage.Attacker) || !IsPlayerFollower(ref systemState, damage.Target))
                return false;

            if (!systemState.EntityManager.HasComponent<ActorFriendlyHitState>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, damage.Target)} has no ActorFriendlyHitState.");

            if (IsInAnyCombat(ref systemState, damage.Target))
                return true;

            var friendly = systemState.EntityManager.GetComponentData<ActorFriendlyHitState>(damage.Target);
            friendly.Count++;
            systemState.EntityManager.SetComponentData(damage.Target, friendly);

            if (friendly.Count >= FriendlyHitForgivenessLimit)
                return false;

            complain = damage.SourceKind == MorrowindDamageSourceKind.Weapon
                       || damage.SourceKind == MorrowindDamageSourceKind.HandToHand;
            return true;
        }

        bool IsPlayerFollower(ref SystemState systemState, Entity actor)
        {
            if (actor == Entity.Null
                || !systemState.EntityManager.Exists(actor)
                || !systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
            {
                return false;
            }

            Entity player = TryGetPlayerEntity();
            if (player == Entity.Null)
                return false;

            var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            for (int i = 0; i < packages.Length; i++)
            {
                byte type = packages[i].Type;
                if ((type == (byte)ActorAiRuntimePackageType.Follow || type == (byte)ActorAiRuntimePackageType.Escort)
                    && packages[i].FollowTargetEntity == player)
                {
                    return true;
                }
            }

            return false;
        }

        bool CanCommitAssaultCrime(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            if (!IsPlayer(ref systemState, attacker) || !IsNpcTarget(ref systemState, ref content, target, out _))
                return false;

            RequireSocialCrimeComposition(ref systemState, target);
            if (IsAggressiveTo(ref systemState, target, attacker)
                || IsInAnyCombat(ref systemState, target)
                || HasCurrentPackage(ref systemState, target, ActorAiRuntimePackageType.Pursue))
            {
                return false;
            }

            var effects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, VampirismEffectId) <= 0f;
        }

        CombatCrimeReportResult ReportCombatCrime(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            CombatCrimeType type,
            Entity player,
            Entity target,
            NativeList<Entity> combatStartActors,
            NativeList<Entity> combatStartTargets,
            NativeList<Entity> guardPursuitActors,
            NativeList<Entity> guardPursuitTargets,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            ref uint randomState)
        {
            if (player == Entity.Null || !systemState.EntityManager.Exists(player) || !systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Combat crime player has no LocalTransform.");
            if (target == Entity.Null || !systemState.EntityManager.Exists(target) || !IsNpcTarget(ref systemState, ref content, target, out _))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Combat crime target ref={PlacedRefIdOrZero(ref systemState, target)} is not an NPC.");

            float radius = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fAlarmRadius) * WorldScale.MwUnitsToMeters;
            float radiusSq = radius * radius;
            LocalTransform playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(player);
            bool crimeSeen = false;
            bool reported = false;
            bool targetPursuitQueued = false;
            bool targetCombatQueued = false;

            using var validActors = new NativeList<Entity>(Allocator.Temp);
            foreach (var (source, settings, vitals, transform, entity) in
                     SystemAPI.Query<
                             RefRO<ActorSpawnSource>,
                             RefRO<ActorAiSettingsState>,
                             RefRO<ActorVitalSet>,
                             RefRO<LocalTransform>>()
                         .WithEntityAccess())
            {
                if (!CanReportCrime(ref systemState, ref content, entity, target, vitals.ValueRO))
                    continue;

                float distanceSq = math.lengthsq(transform.ValueRO.Position - playerTransform.Position);
                if (entity != target && distanceSq > radiusSq)
                    continue;

                validActors.Add(entity);
                if (!CrimeIsSeenBy(
                        ref systemState,
                        ref content,
                        type,
                        player,
                        target,
                        entity,
                        playerTransform,
                        transform.ValueRO,
                        deferredPhysicsQueueEntity,
                        fixedTick,
                        ref randomState))
                {
                    continue;
                }

                crimeSeen = true;
            }

            if (!crimeSeen && type == CombatCrimeType.Assault)
            {
                if (IsGuard(ref systemState, ref content, target) && !HasCurrentPackage(ref systemState, target, ActorAiRuntimePackageType.Pursue))
                {
                    if (!ContainsEntity(validActors, target))
                        validActors.Add(target);
                }
                else
                {
                    AddUniqueCombat(combatStartActors, combatStartTargets, target, player);
                    return new CombatCrimeReportResult { TargetCombatQueued = true };
                }
            }
            else if (!crimeSeen)
            {
                return default;
            }

            for (int i = 0; i < validActors.Length; i++)
            {
                Entity actor = validActors[i];
                if (!systemState.EntityManager.HasComponent<ActorAiSettingsState>(actor))
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Crime witness ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorAiSettingsState.");
                if (systemState.EntityManager.GetComponentData<ActorAiSettingsState>(actor).Alarm >= 100)
                    reported = true;
            }

            int bounty = type == CombatCrimeType.Assault
                ? RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeAttack)
                : RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeKilling);
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(ref systemState, reported ? bounty : 0);

            int fight = type == CombatCrimeType.Assault
                ? RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightAttacking)
                : RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightKilling);
            int fightVictim = type == CombatCrimeType.Assault
                ? RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightAttack)
                : RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightKilling);
            float disp = type == CombatCrimeType.Assault
                ? RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.iDispAttackMod)
                : RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.iDispKilling);
            float dispVictim = type == CombatCrimeType.Assault
                ? RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDispAttacking)
                : RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.iDispKilling);

            for (int i = 0; i < validActors.Length; i++)
            {
                Entity actor = validActors[i];
                if (!CanReportCrime(ref systemState, ref content, actor, target, systemState.EntityManager.GetComponentData<ActorVitalSet>(actor)))
                    continue;

                var settings = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(actor);
                bool isVictim = actor == target;
                bool isGuard = IsGuard(ref systemState, ref content, actor);
                int alarm = settings.Alarm;
                float alarmTerm = 0.01f * alarm;
                float dispTerm = isVictim ? dispVictim : disp;

                ApplyCombatCrimeDisposition(ref systemState, type, actor, isVictim, isGuard, alarm, alarmTerm, dispTerm, out bool setCrimeId);

                if (isGuard && alarm >= 100)
                {
                    SetActorAlarmed(ref systemState, actor);
                    setCrimeId = true;
                    if (!HasCurrentPackage(ref systemState, actor, ActorAiRuntimePackageType.Pursue))
                    {
                        AddUniquePursuit(guardPursuitActors, guardPursuitTargets, actor, player);
                        if (actor == target)
                            targetPursuitQueued = true;
                    }
                }
                else
                {
                    float fightTerm = ResolveFightTerm(ref systemState, ref content, actor, player, isVictim ? fightVictim : fight, dispTerm, alarmTerm);
                    if (settings.Fight + fightTerm >= 100f)
                    {
                        ApplyHostileOnlyCrimeDisposition(ref systemState, type, actor, isVictim, isGuard, alarm, alarmTerm, dispTerm);

                        settings.Fight = ClampInt(settings.Fight + (int)fightTerm, 0, 100);
                        systemState.EntityManager.SetComponentData(actor, settings);
                        SetActorAlarmed(ref systemState, actor);
                        SetHitAttemptActor(ref systemState, actor, player);
                        AddUniqueCombat(combatStartActors, combatStartTargets, actor, player);
                        if (actor == target)
                            targetCombatQueued = true;
                        setCrimeId = true;
                    }
                }

                if (setCrimeId)
                    SetActorCrimeId(ref systemState, actor, crimeId);
            }

            if (type == CombatCrimeType.Murder)
            {
                var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(target);
                eventState.Murdered = 1;
                systemState.EntityManager.SetComponentData(target, eventState);
            }

            return new CombatCrimeReportResult
            {
                Reported = reported,
                TargetPursuitQueued = targetPursuitQueued,
                TargetCombatQueued = targetCombatQueued,
            };
        }

        static void AddUniquePursuit(NativeList<Entity> actors, NativeList<Entity> targets, Entity actor, Entity target)
        {
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i] == actor && targets[i] == target)
                    return;
            }

            actors.Add(actor);
            targets.Add(target);
        }

        static void AddUniqueCombat(NativeList<Entity> actors, NativeList<Entity> targets, Entity actor, Entity target)
        {
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i] == actor && targets[i] == target)
                    return;
            }

            actors.Add(actor);
            targets.Add(target);
        }

        bool CrimeIsSeenBy(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            CombatCrimeType type,
            Entity player,
            Entity victim,
            Entity observer,
            in LocalTransform playerTransform,
            in LocalTransform observerTransform,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            ref uint randomState)
        {
            if (observer == victim && type == CombatCrimeType.Assault)
                return true;
            if (observer != victim && type == CombatCrimeType.Murder)
                return true;

            if (!ActorAiLineOfSightUtility.TryGetLineOfSightOrRequest(
                    systemState.EntityManager,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    player,
                    observer,
                    EyePosition(playerTransform.Position),
                    EyePosition(observerTransform.Position),
                    out bool hasLineOfSight))
            {
                return false;
            }

            return hasLineOfSight
                   && MorrowindCrimeAwarenessUtility.AwarenessCheck(
                       ref content,
                       systemState.EntityManager,
                       player,
                       observer,
                       playerTransform,
                       observerTransform,
                       NextRandom0To99(ref randomState));
        }

        void ApplyCombatCrimeDisposition(
            ref SystemState systemState,
            CombatCrimeType type,
            Entity actor,
            bool isVictim,
            bool isGuard,
            int alarm,
            float alarmTerm,
            float dispTerm,
            out bool setCrimeId)
        {
            setCrimeId = false;
            if (type != CombatCrimeType.Assault)
                return;

            int modifier = 0;
            bool permanent = false;
            if (isVictim && !isGuard)
            {
                permanent = true;
                modifier = (int)dispTerm;
            }
            else if (alarm >= 100)
            {
                modifier = (int)dispTerm;
            }
            else if (isVictim && isGuard)
            {
                permanent = true;
                modifier = (int)(dispTerm * alarmTerm);
            }

            if (modifier == 0)
                return;

            ApplyCrimeDispositionModifier(ref systemState, actor, modifier, permanent);
            setCrimeId = true;
        }

        void ApplyHostileOnlyCrimeDisposition(
            ref SystemState systemState,
            CombatCrimeType type,
            Entity actor,
            bool isVictim,
            bool isGuard,
            int alarm,
            float alarmTerm,
            float dispTerm)
        {
            if (type != CombatCrimeType.Assault || isVictim || isGuard || alarm >= 100)
                return;

            int modifier = (int)(dispTerm * alarmTerm);
            if (modifier != 0)
                ApplyCrimeDispositionModifier(ref systemState, actor, modifier, permanent: false);
        }

        void ApplyCrimeDispositionModifier(ref SystemState systemState, Entity actor, int modifier, bool permanent)
        {
            if (permanent)
            {
                if (!systemState.EntityManager.HasComponent<ActorDispositionState>(actor))
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Crime witness ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorDispositionState.");

                var disposition = systemState.EntityManager.GetComponentData<ActorDispositionState>(actor);
                disposition.BaseDisposition = ClampInt(disposition.BaseDisposition + modifier, 0, 100);
                systemState.EntityManager.SetComponentData(actor, disposition);
                return;
            }

            if (!systemState.EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Crime witness ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorCrimeState.");

            var crime = systemState.EntityManager.GetComponentData<ActorCrimeState>(actor);
            crime.CrimeDispositionModifier = ClampInt(crime.CrimeDispositionModifier + modifier, -100, 100);
            systemState.EntityManager.SetComponentData(actor, crime);
        }

        float ResolveFightTerm(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor, Entity player, int baseFightTerm, float dispTerm, float alarmTerm)
        {
            float fightTerm = baseFightTerm;
            fightTerm += (50f - dispTerm) * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDispMult);
            fightTerm += ResolveFightDistanceBias(ref systemState, ref content, actor, player);
            fightTerm *= alarmTerm;

            int currentFight = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(actor).Fight;
            if (currentFight + fightTerm > 100f)
                fightTerm = 100f - currentFight;
            return math.max(0f, fightTerm);
        }

        float ResolveFightDistanceBias(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor, Entity player)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(actor) || !systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Fight distance bias requires actor and player transforms.");

            float distanceMw = math.distance(
                systemState.EntityManager.GetComponentData<LocalTransform>(actor).Position,
                systemState.EntityManager.GetComponentData<LocalTransform>(player).Position) / WorldScale.MwUnitsToMeters;

            return RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightDistanceBase)
                   - RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDistanceMultiplier) * distanceMw;
        }

        bool IsGuard(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
                return false;

            var source = systemState.EntityManager.GetComponentData<ActorSpawnSource>(actor);
            return IsGuardNpc(ref content, source.Definition);
        }

        bool IsGuardNpc(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.Kind == ActorDefKind.Npc && actor.ClassIdHash == RuntimeContentKnownHashes.guard;
        }

        bool CanReportCrime(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor, Entity victim, in ActorVitalSet vitals)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                return false;
            if (systemState.EntityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
                return false;
            if (!IsNpcActor(ref content, systemState.EntityManager.GetComponentData<ActorSpawnSource>(actor).Definition))
                return false;
            if (vitals.CurrentHealth <= 0f)
                return false;
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled != 0)
            {
                return false;
            }
            if (systemState.EntityManager.HasComponent<ActorHitAftermathState>(actor))
            {
                var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(actor);
                if (aftermath.Dead != 0 || aftermath.KnockedDown != 0 || aftermath.KnockedOut != 0)
                    return false;
            }
            if (IsInCombatWith(ref systemState, actor, victim))
                return false;
            if (IsPlayerFollower(ref systemState, actor))
                return false;

            return true;
        }

        static bool ContainsEntity(NativeList<Entity> actors, Entity actor)
        {
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i] == actor)
                    return true;
            }

            return false;
        }

        bool IsNpcActor(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.Kind == ActorDefKind.Npc;
        }

        void ScheduleGuardPursuit(ref SystemState systemState, ref RuntimeContentBlob content, Entity guard, Entity player)
        {
            if (player == Entity.Null || !systemState.EntityManager.Exists(player) || !systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Guard arrest pursuit target has no LocalTransform.");

            const float arrestGreetingDistanceMw = 100f;
            if (!MorrowindScriptAiPackageUtility.TryApplyPursueRequest(
                    ref content,
                    systemState.EntityManager,
                    guard,
                    PlacedRefIdOrZero(ref systemState, guard),
                    player,
                    PlacedRefIdOrZero(ref systemState, player),
                    systemState.EntityManager.GetComponentData<LocalTransform>(player).Position,
                    arrestGreetingDistanceMw * WorldScale.MwUnitsToMeters))
            {
                throw new InvalidOperationException($"[VVardenfell][OnHit] Failed to schedule guard arrest pursuit for guard ref={PlacedRefIdOrZero(ref systemState, guard)}.");
            }
        }

        bool ShouldStartCombatAfterHit(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            if (target == Entity.Null
                || attacker == Entity.Null
                || !systemState.EntityManager.Exists(target)
                || !systemState.EntityManager.Exists(attacker)
                || IsInCombatWith(ref systemState, target, attacker)
                || HasCurrentPackage(ref systemState, target, ActorAiRuntimePackageType.Pursue))
            {
                return false;
            }

            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target)
                || systemState.EntityManager.GetComponentData<ActorVitalSet>(target).CurrentHealth <= 0f)
            {
                return false;
            }

            if (!IsPlayer(ref systemState, attacker))
                return IsInCombatWith(ref systemState, attacker, target);

            if (!systemState.EntityManager.HasComponent<ActorAiSettingsState>(target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Combat target ref={PlacedRefIdOrZero(ref systemState, target)} has no ActorAiSettingsState.");

            var settings = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(target);
            return settings.Fight != 0 || !DeclaresOnPcHitMeInteger(ref systemState, ref content, target);
        }

        void StartCombatAfterHit(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            uint targetPlacedRefId = PlacedRefIdOrZero(ref systemState, target);
            if (!MorrowindCombatTargetUtility.TryStartCombat(
                    ref content,
                    systemState.EntityManager,
                    target,
                    targetPlacedRefId,
                    attacker,
                    PlacedRefIdOrZero(ref systemState, attacker)))
            {
                throw new InvalidOperationException($"[VVardenfell][OnHit] Failed to start combat for target ref={targetPlacedRefId}.");
            }
        }

        void ApplyMurderAftermath(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            in MorrowindDamageAppliedEvent damage,
            NativeList<Entity> combatStartActors,
            NativeList<Entity> combatStartTargets,
            NativeList<Entity> guardPursuitActors,
            NativeList<Entity> guardPursuitTargets,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            ref uint randomState)
        {
            if (!IsPlayer(ref systemState, damage.Attacker)
                || damage.TargetVital != MorrowindDamageTargetVital.Health
                || damage.Amount <= DamageEpsilon
                || damage.Target == Entity.Null
                || !systemState.EntityManager.Exists(damage.Target)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(damage.Target)
                || systemState.EntityManager.GetComponentData<ActorVitalSet>(damage.Target).CurrentHealth > 0f
                || !systemState.EntityManager.HasComponent<ActorHitAftermathState>(damage.Target)
                || systemState.EntityManager.GetComponentData<ActorHitAftermathState>(damage.Target).Dead != 0
                || !IsNpcTarget(ref systemState, ref content, damage.Target, out _))
            {
                return;
            }

            RequireSocialCrimeComposition(ref systemState, damage.Target);
            ReportCombatCrime(
                ref systemState,
                ref content,
                CombatCrimeType.Murder,
                damage.Attacker,
                damage.Target,
                combatStartActors,
                combatStartTargets,
                guardPursuitActors,
                guardPursuitTargets,
                deferredPhysicsQueueEntity,
                fixedTick,
                ref randomState);
        }

        void ApplyHitMemory(ref SystemState systemState, in MorrowindDamageAppliedEvent damage)
        {
            Entity target = RequireLiveActorTarget(ref systemState, damage.Target, "target");
            var targetState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(target);

            if (damage.SourceKind == MorrowindDamageSourceKind.Weapon)
            {
                if (!damage.SourceContent.IsValid || damage.SourceContent.Kind != ContentReferenceKind.Item)
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Weapon hit target ref={PlacedRefIdOrZero(ref systemState, target)} has invalid source content.");

                targetState.LastHitAttemptObject = damage.SourceContent;
                if (damage.Amount > DamageEpsilon)
                    targetState.LastHitObject = damage.SourceContent;
            }

            if (IsActorEntity(ref systemState, damage.Attacker))
            {
                if (!IsInCombatWith(ref systemState, target, damage.Attacker) && targetState.Attacked == 0)
                    targetState.Attacked = 1;

                if (ShouldStoreHitAttemptActors(ref systemState, damage.Attacker, target))
                {
                    if (targetState.LastHitAttemptActor == Entity.Null)
                    {
                        targetState.LastHitAttemptActor = damage.Attacker;
                        targetState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(ref systemState, damage.Attacker);
                    }

                    var attackerState = RequireScriptEventState(ref systemState, damage.Attacker, "attacker");
                    if (attackerState.LastHitAttemptActor == Entity.Null)
                    {
                        attackerState.LastHitAttemptActor = target;
                        attackerState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(ref systemState, target);
                        systemState.EntityManager.SetComponentData(damage.Attacker, attackerState);
                    }
                }
            }

            systemState.EntityManager.SetComponentData(target, targetState);
        }

        void SetOnPcHitMeIfDeclared(ref SystemState systemState, ref RuntimeContentBlob content, Entity target)
        {
            Entity actor = RequireLiveActorTarget(ref systemState, target, "OnPCHitMe target");
            if (!TryResolveOnPcHitMeLocal(ref systemState, ref content, actor, out int localIndex, out int declaredLocalCount))
                return;

            if (!systemState.EntityManager.HasBuffer<MorrowindScriptLocalValue>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} declares OnPCHitMe but has no script locals buffer.");

            var locals = systemState.EntityManager.GetBuffer<MorrowindScriptLocalValue>(actor);
            if (locals.Length < declaredLocalCount)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} script locals buffer length {locals.Length} is shorter than declared locals {declaredLocalCount}.");

            var local = locals[localIndex];
            if (local.ValueKind != (byte)MorrowindScriptValueKind.Integer)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} runtime OnPCHitMe local is not an integer.");

            local.IntValue = 1;
            local.FloatValue = 1f;
            locals[localIndex] = local;
        }

        bool DeclaresOnPcHitMeInteger(ref SystemState systemState, ref RuntimeContentBlob content, Entity target)
            => TryResolveOnPcHitMeLocal(ref systemState, ref content, target, out _, out _);

        bool TryResolveOnPcHitMeLocal(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity actor,
            out int localIndex,
            out int declaredLocalCount)
        {
            localIndex = -1;
            declaredLocalCount = 0;

            var source = systemState.EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has invalid actor definition.");

            ref RuntimeActorDefBlob actorDef = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            if (actorDef.ScriptIdHash == 0UL)
                return false;

            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref content, actorDef.ScriptIdHash, out var programHandle) || !programHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} script hash 0x{actorDef.ScriptIdHash:X16} has no compiled runtime program.");

            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            RuntimeContentBlobUtility.RequireRange(program.FirstLocalIndex, program.LocalCount, content.MorrowindScriptLocals.Length, "script local");
            declaredLocalCount = program.LocalCount;
            for (int i = 0; i < program.LocalCount; i++)
            {
                ref var localDefinition = ref content.MorrowindScriptLocals[program.FirstLocalIndex + i];
                if (localDefinition.NameHash == RuntimeContentKnownHashes.OnPCHitMe)
                {
                    localIndex = i;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            if (content.MorrowindScriptLocals[program.FirstLocalIndex + localIndex].ValueKind != (byte)MorrowindScriptValueKind.Integer)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} local OnPCHitMe is not an integer.");
            return true;
        }

        void QueueHitVoiceResolve(ref SystemState systemState, 
            int hitDialogueIndex,
            ActorDefHandle targetActor,
            Entity target,
            DynamicBuffer<MorrowindCombatHitVoiceResolveRequest> requests,
            uint randomState)
        {
            requests.Add(new MorrowindCombatHitVoiceResolveRequest
            {
                TargetEntity = target,
                TargetPlacedRefId = PlacedRefIdOrZero(ref systemState, target),
                Actor = targetActor,
                DialogueIndex = hitDialogueIndex,
                RandomState = randomState == 0u ? 1u : randomState,
            });
        }

        Entity RequireLiveActorTarget(ref SystemState systemState, Entity actor, string context)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor) && !systemState.EntityManager.HasComponent<PlayerTag>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity={actor.Index}:{actor.Version} is not an actor.");
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorScriptEventState.");

            return actor;
        }

        ActorScriptEventState RequireScriptEventState(ref SystemState systemState, Entity actor, string context)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorScriptEventState.");

            return systemState.EntityManager.GetComponentData<ActorScriptEventState>(actor);
        }

        bool ShouldStoreHitAttemptActors(ref SystemState systemState, Entity attacker, Entity target)
            => IsPlayer(ref systemState, attacker) || IsInCombatWith(ref systemState, attacker, target);

        bool IsInCombatWith(ref SystemState systemState, Entity actor, Entity target)
        {
            if (actor == Entity.Null
                || !systemState.EntityManager.Exists(actor)
                || !systemState.EntityManager.HasComponent<ActorCombatTargetState>(actor))
            {
                return false;
            }

            var combat = systemState.EntityManager.GetComponentData<ActorCombatTargetState>(actor);
            if (combat.Active == 0)
                return false;

            uint targetRef = PlacedRefIdOrZero(ref systemState, target);
            return combat.TargetEntity == target || (targetRef != 0u && combat.TargetPlacedRefId == targetRef);
        }

        bool HasCurrentPackage(ref SystemState systemState, Entity actor, ActorAiRuntimePackageType type)
        {
            if (actor == Entity.Null
                || !systemState.EntityManager.Exists(actor)
                || !systemState.EntityManager.HasComponent<ActorAiState>(actor)
                || !systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
            {
                return false;
            }

            var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
            var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return false;

            return packages[aiState.CurrentPackageIndex].Type == (byte)type;
        }

        void SetHitAttemptActor(ref SystemState systemState, Entity actor, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Combat crime actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorScriptEventState.");

            var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(actor);
            eventState.LastHitAttemptActor = target;
            eventState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(ref systemState, target);
            systemState.EntityManager.SetComponentData(actor, eventState);
        }

        bool IsNpcTarget(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, out ActorSpawnSource source)
        {
            source = default;
            if (target == Entity.Null || !systemState.EntityManager.Exists(target) || !systemState.EntityManager.HasComponent<ActorSpawnSource>(target))
                return false;

            source = systemState.EntityManager.GetComponentData<ActorSpawnSource>(target);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, target)} has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            return actor.Kind == ActorDefKind.Npc;
        }

        bool IsActorEntity(ref SystemState systemState, Entity entity)
            => entity != Entity.Null
               && systemState.EntityManager.Exists(entity)
               && (systemState.EntityManager.HasComponent<ActorSpawnSource>(entity) || systemState.EntityManager.HasComponent<PlayerTag>(entity));

        void RequireSocialCrimeComposition(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorCrimeState.");
            if (!systemState.EntityManager.HasComponent<ActorAiSettingsState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorAiSettingsState.");
            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorActiveMagicEffect buffer.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorHitAftermathState.");
        }

        bool IsAggressiveTo(ref SystemState systemState, Entity actor, Entity target)
        {
            if (IsInCombatWith(ref systemState, actor, target))
                return true;

            if (!systemState.EntityManager.HasComponent<ActorAiSettingsState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorAiSettingsState.");

            return systemState.EntityManager.GetComponentData<ActorAiSettingsState>(actor).Fight >= 100;
        }

        bool IsInAnyCombat(ref SystemState systemState, Entity actor)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                return false;

            if (!systemState.EntityManager.HasComponent<ActorCombatTargetState>(actor))
                return false;

            return systemState.EntityManager.GetComponentData<ActorCombatTargetState>(actor).Active != 0;
        }

        Entity TryGetPlayerEntity()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return Entity.Null;

            return _playerQuery.GetSingletonEntity();
        }

        int AddPlayerBountyAndAdvanceCrimeId(ref SystemState systemState, int bounty)
        {
            Entity player = TryGetPlayerEntity();
            if (player == Entity.Null)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime reporting requires a loaded player entity.");
            if (!systemState.EntityManager.HasComponent<PlayerCrimeState>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Player has no PlayerCrimeState.");

            var crime = systemState.EntityManager.GetComponentData<PlayerCrimeState>(player);
            crime.Bounty = Math.Max(0, crime.Bounty + Math.Max(0, bounty));
            crime.CurrentCrimeId = crime.CurrentCrimeId <= crime.PaidCrimeId ? crime.PaidCrimeId + 1 : crime.CurrentCrimeId + 1;
            systemState.EntityManager.SetComponentData(player, crime);
            return crime.CurrentCrimeId;
        }

        void SetActorCrimeId(ref SystemState systemState, Entity actor, int crimeId)
        {
            if (!systemState.EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorCrimeState.");

            var crime = systemState.EntityManager.GetComponentData<ActorCrimeState>(actor);
            crime.CrimeId = crimeId;
            systemState.EntityManager.SetComponentData(actor, crime);
        }

        void SetActorAlarmed(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorCrimeState.");

            var crime = systemState.EntityManager.GetComponentData<ActorCrimeState>(actor);
            crime.Alarmed = 1;
            systemState.EntityManager.SetComponentData(actor, crime);
        }

        bool IsPlayerAttacker(ref SystemState systemState, Entity entity) => IsPlayer(ref systemState, entity);

        bool IsPlayer(ref SystemState systemState, Entity entity)
            => entity != Entity.Null && systemState.EntityManager.Exists(entity) && systemState.EntityManager.HasComponent<PlayerTag>(entity);

        bool ShouldQueueHitVoice(ref SystemState systemState, in MorrowindDamageAppliedEvent damage, int voiceHitOdds, ref uint randomState)
        {
            if (damage.TargetVital == MorrowindDamageTargetVital.Health
                && damage.Target != Entity.Null
                && systemState.EntityManager.Exists(damage.Target)
                && systemState.EntityManager.HasComponent<ActorVitalSet>(damage.Target)
                && systemState.EntityManager.GetComponentData<ActorVitalSet>(damage.Target).CurrentHealth <= 0f)
            {
                return true;
            }

            return (int)(NextRandom0To99(ref randomState)) < voiceHitOdds;
        }

        bool IsActorSayingOrPending(
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> pendingRequests,
            DynamicBuffer<MorrowindCombatHitVoiceResolveRequest> pendingResolveRequests,
            Entity actor,
            uint placedRefId)
        {
            if (IsActorSaying(activeSays, actor, placedRefId))
                return true;

            for (int i = 0; i < pendingRequests.Length; i++)
            {
                var pending = pendingRequests[i];
                if (pending.TargetEntity == actor || (placedRefId != 0u && pending.TargetPlacedRefId == placedRefId))
                    return true;
            }

            for (int i = 0; i < pendingResolveRequests.Length; i++)
            {
                var pending = pendingResolveRequests[i];
                if (pending.TargetEntity == actor || (placedRefId != 0u && pending.TargetPlacedRefId == placedRefId))
                    return true;
            }

            return false;
        }

        static bool IsActorSaying(DynamicBuffer<MorrowindScriptActiveSay> activeSays, Entity actor, uint placedRefId)
        {
            for (int i = 0; i < activeSays.Length; i++)
            {
                var active = activeSays[i];
                if (active.SourceEntity == actor || (placedRefId != 0u && active.SourcePlacedRefId == placedRefId))
                    return true;
            }

            return false;
        }

        bool TryResolveHitVoiceDialogue(ref RuntimeContentBlob content, out int dialogueIndex)
        {
            dialogueIndex = -1;
            if (!RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref content, RuntimeContentStableHash.HashId(HitVoiceDialogueId), out var handle) || !handle.IsValid)
                return false;

            ref RuntimeDialogueDefBlob dialogue = ref RuntimeContentBlobUtility.Get(ref content, handle);
            if (dialogue.Type != DialogueDefType.Voice)
                return false;

            dialogueIndex = handle.Index;
            return true;
        }

        uint NextRandom0To99(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state % 100u;
        }

        static float3 EyePosition(float3 position)
            => position + new float3(0f, 1.5f, 0f);

        uint PlacedRefIdOrZero(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity) || !systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity))
                return 0u;

            return systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
        }

        static int ClampInt(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }
}


