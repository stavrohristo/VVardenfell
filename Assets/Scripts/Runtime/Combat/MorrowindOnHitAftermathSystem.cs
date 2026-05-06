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

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            systemState.RequireForUpdate<MorrowindDamageAppliedEvent>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeShellState>();
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
            if (!systemState.EntityManager.HasBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no ActorForceGreetingRequest buffer.");

            var hitVoiceRequests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity);
            var hitVoiceResolveRequests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceResolveRequest>(scriptRuntimeEntity);
            var activeSays = systemState.EntityManager.GetBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity, true);
            var forceGreetingRequests = systemState.EntityManager.GetBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity);
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            uint randomState = combatState.RandomState;
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            using var combatStartActors = new NativeList<Entity>(Allocator.Temp);
            using var combatStartTargets = new NativeList<Entity>(Allocator.Temp);

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
                    forceGreetingRequests,
                    combatStartActors,
                    combatStartTargets,
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

                ApplyMurderAftermath(ref systemState, ref content, damage.ValueRO);

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
            DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests,
            NativeList<Entity> combatStartActors,
            NativeList<Entity> combatStartTargets,
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

            if (CanCommitAssaultCrime(ref systemState, ref content, damage.Target, damage.Attacker))
                ApplyAssaultCrime(ref systemState, ref content, damage.Target, damage.Attacker, forceGreetingRequests);

            if (ShouldStartCombatAfterHit(ref systemState, ref content, damage.Target, damage.Attacker))
            {
                combatStartActors.Add(damage.Target);
                combatStartTargets.Add(damage.Attacker);
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
            if (IsAggressiveTo(ref systemState, target, attacker) || IsInAnyCombat(ref systemState, target))
                return false;

            var effects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, VampirismEffectId) <= 0f;
        }

        void ApplyAssaultCrime(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, Entity attacker, DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests)
        {
            if (IsPlayerFollower(ref systemState, target))
                return;

            var settings = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(target);
            if (!IsNpcTarget(ref systemState, ref content, target, out var source))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Assault target ref={PlacedRefIdOrZero(ref systemState, target)} is not an NPC.");

            bool isGuard = IsGuardNpc(ref content, source.Definition);
            bool reported = TryFindCrimeReporter(ref systemState, ref content, attacker, target, out Entity reportingGuard);
            bool setCrimeId = reported;

            int dispositionModifier = 0;
            float victimDisposition = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDispAttacking);
            float witnessDisposition = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.iDispAttackMod);
            if (!isGuard)
            {
                dispositionModifier = (int)victimDisposition;
            }
            else if (!reported)
            {
                dispositionModifier = (int)(victimDisposition * (settings.Alarm * 0.01f));
            }
            else
            {
                dispositionModifier = (int)witnessDisposition;
            }

            if (dispositionModifier != 0)
            {
                ApplyAssaultDispositionPenalty(ref systemState, target, dispositionModifier);
                setCrimeId = true;
            }

            if (!reported && !setCrimeId)
                return;

            int bounty = reported ? RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeAttack) : 0;
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(ref systemState, bounty);
            SetActorCrimeId(ref systemState, target, crimeId);

            if (reported)
            {
                MarkCrimeWitnesses(ref systemState, ref content, attacker, target, crimeId);
                if (reportingGuard != Entity.Null)
                    QueueForceGreeting(ref systemState, forceGreetingRequests, reportingGuard);
            }
        }

        void ApplyAssaultDispositionPenalty(ref SystemState systemState, Entity target, int penalty)
        {
            if (!systemState.EntityManager.HasComponent<ActorDispositionState>(target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Assault target ref={PlacedRefIdOrZero(ref systemState, target)} has no ActorDispositionState.");

            var disposition = systemState.EntityManager.GetComponentData<ActorDispositionState>(target);
            disposition.BaseDisposition = ClampInt(disposition.BaseDisposition + penalty, 0, 100);
            systemState.EntityManager.SetComponentData(target, disposition);
        }

        bool IsGuardNpc(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.ClassIdHash == RuntimeContentKnownHashes.guard;
        }

        bool TryFindCrimeReporter(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, Entity victim, out Entity guard)
        {
            guard = Entity.Null;
            if (player == Entity.Null || !systemState.EntityManager.Exists(player) || !systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Assault crime player has no LocalTransform.");

            float radius = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fAlarmRadius) * WorldScale.MwUnitsToMeters;
            float radiusSq = radius * radius;
            float3 playerPosition = systemState.EntityManager.GetComponentData<LocalTransform>(player).Position;
            float bestDistanceSq = float.PositiveInfinity;
            bool reported = false;

            foreach (var (source, settings, vitals, transform, entity) in
                     SystemAPI.Query<
                             RefRO<ActorSpawnSource>,
                             RefRO<ActorAiSettingsState>,
                             RefRO<ActorVitalSet>,
                             RefRO<LocalTransform>>()
                         .WithNone<PlayerTag>()
                         .WithEntityAccess())
            {
                if (settings.ValueRO.Alarm < 100)
                    continue;
                if (!IsNpcActor(ref content, source.ValueRO.Definition))
                    continue;
                if (!CanReportCrime(ref systemState, entity, victim, vitals.ValueRO))
                    continue;

                float distanceSq = math.lengthsq(transform.ValueRO.Position - playerPosition);
                if (entity != victim && distanceSq > radiusSq)
                    continue;

                reported = true;
                if (!IsGuardNpc(ref content, source.ValueRO.Definition))
                    continue;
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                guard = entity;
            }

            return reported;
        }

        void MarkCrimeWitnesses(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, Entity victim, int crimeId)
        {
            if (player == Entity.Null || !systemState.EntityManager.Exists(player) || !systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness scan player has no LocalTransform.");

            float radius = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fAlarmRadius) * WorldScale.MwUnitsToMeters;
            float radiusSq = radius * radius;
            float3 playerPosition = systemState.EntityManager.GetComponentData<LocalTransform>(player).Position;

            foreach (var (source, settings, vitals, transform, entity) in
                     SystemAPI.Query<
                             RefRO<ActorSpawnSource>,
                             RefRO<ActorAiSettingsState>,
                             RefRO<ActorVitalSet>,
                             RefRO<LocalTransform>>()
                         .WithNone<PlayerTag>()
                         .WithEntityAccess())
            {
                if (!CanReportCrime(ref systemState, entity, victim, vitals.ValueRO))
                    continue;
                if (!IsNpcActor(ref content, source.ValueRO.Definition))
                    continue;

                float distanceSq = math.lengthsq(transform.ValueRO.Position - playerPosition);
                if (entity != victim && distanceSq > radiusSq)
                    continue;

                bool isReporter = settings.ValueRO.Alarm >= 100;
                bool isGuard = isReporter && IsGuardNpc(ref content, source.ValueRO.Definition);
                bool isVictim = entity == victim;
                if (!isReporter && !isVictim)
                    continue;

                SetActorCrimeId(ref systemState, entity, crimeId);
                if (isGuard)
                    SetActorAlarmed(ref systemState, entity);
            }
        }

        bool CanReportCrime(ref SystemState systemState, Entity actor, Entity victim, in ActorVitalSet vitals)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                return false;
            if (systemState.EntityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
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

        bool IsNpcActor(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.Kind == ActorDefKind.Npc;
        }

        void QueueForceGreeting(ref SystemState systemState, DynamicBuffer<ActorForceGreetingRequest> requests, Entity guard)
        {
            uint placedRefId = PlacedRefIdOrZero(ref systemState, guard);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.TargetEntity == guard || (placedRefId != 0u && request.TargetPlacedRefId == placedRefId))
                    return;
            }

            requests.Add(new ActorForceGreetingRequest
            {
                TargetEntity = guard,
                TargetPlacedRefId = placedRefId,
            });
        }

        bool ShouldStartCombatAfterHit(ref SystemState systemState, ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            if (target == Entity.Null
                || attacker == Entity.Null
                || !systemState.EntityManager.Exists(target)
                || !systemState.EntityManager.Exists(attacker)
                || IsInCombatWith(ref systemState, target, attacker))
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

        void ApplyMurderAftermath(ref SystemState systemState, ref RuntimeContentBlob content, in MorrowindDamageAppliedEvent damage)
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
            var crime = systemState.EntityManager.GetComponentData<ActorCrimeState>(damage.Target);
            if (crime.CrimeId < 0)
                return;

            int bounty = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeKilling);
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(ref systemState, bounty);
            SetActorCrimeId(ref systemState, damage.Target, crimeId);

            var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(damage.Target);
            eventState.Murdered = 1;
            systemState.EntityManager.SetComponentData(damage.Target, eventState);
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
            crime.CurrentCrimeId = crime.CurrentCrimeId < 0 ? 0 : crime.CurrentCrimeId + 1;
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


