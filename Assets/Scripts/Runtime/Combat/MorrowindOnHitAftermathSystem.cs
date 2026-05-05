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
    public partial class MorrowindOnHitAftermathSystem : SystemBase
    {
        const string HitVoiceDialogueId = "hit";
        const string OnPcHitMeLocal = "onpchitme";
        const float DamageEpsilon = 0.001f;
        const int FriendlyHitForgivenessLimit = 4;
        static readonly short VampirismEffectId = RequireEffectId("sEffectVampirism");

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][OnHit] Runtime content database is not loaded.");

            int voiceHitOdds = contentDb.RequireGameSettingInt("iVoiceHitOdds");
            if (voiceHitOdds < 0 || voiceHitOdds > 100)
                throw new InvalidOperationException($"[VVardenfell][OnHit] GMST 'iVoiceHitOdds' must be between 0 and 100, got {voiceHitOdds}.");

            bool hasHitVoiceDialogue = TryResolveHitVoiceDialogue(contentDb, out int hitDialogueIndex);
            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!EntityManager.HasBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindCombatHitVoiceSayRequest buffer.");
            if (!EntityManager.HasBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindScriptActiveSay buffer.");
            if (!EntityManager.HasBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no ActorForceGreetingRequest buffer.");

            var hitVoiceRequests = EntityManager.GetBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity);
            var activeSays = EntityManager.GetBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity, true);
            var forceGreetingRequests = EntityManager.GetBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity);
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            uint randomState = combatState.RandomState;
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            using var combatStartActors = new NativeList<Entity>(Allocator.Temp);
            using var combatStartTargets = new NativeList<Entity>(Allocator.Temp);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                ApplyHitMemory(damage.ValueRO);

                bool setOnPcHitMe = ApplySocialHitAftermath(
                    contentDb,
                    damage.ValueRO,
                    activeSays,
                    hitVoiceRequests,
                    hitDialogueIndex,
                    hasHitVoiceDialogue,
                    forceGreetingRequests,
                    combatStartActors,
                    combatStartTargets,
                    ref randomState);

                if (setOnPcHitMe && IsPlayerAttacker(damage.ValueRO.Attacker))
                    SetOnPcHitMeIfDeclared(contentDb, damage.ValueRO.Target);

                if (damage.ValueRO.TargetVital == MorrowindDamageTargetVital.Health
                    && damage.ValueRO.Amount > DamageEpsilon
                    && IsPlayer(damage.ValueRO.Target))
                {
                    RuntimeShellStateUtility.ActivateHitOverlay(ref shell);
                }

                if (damage.ValueRO.Amount <= DamageEpsilon || damage.ValueRO.Attacker == Entity.Null)
                    continue;

                ApplyMurderAftermath(contentDb, damage.ValueRO);

                if (!IsNpcTarget(contentDb, damage.ValueRO.Target, out var targetSource))
                    continue;

                uint targetRef = PlacedRefIdOrZero(damage.ValueRO.Target);
                bool actorSayingOrPending = IsActorSayingOrPending(activeSays, hitVoiceRequests, damage.ValueRO.Target, targetRef);
                bool shouldQueueHitVoice = hasHitVoiceDialogue && !actorSayingOrPending && ShouldQueueHitVoice(damage.ValueRO, voiceHitOdds, ref randomState);

                if (shouldQueueHitVoice)
                {
                    QueueHitVoice(contentDb, hitDialogueIndex, targetSource.Definition, damage.ValueRO.Target, hitVoiceRequests, ref randomState);
                }
            }

            for (int i = 0; i < combatStartActors.Length; i++)
                StartCombatAfterHit(contentDb, combatStartActors[i], combatStartTargets[i]);

            combatState.RandomState = randomState == 0u ? 1u : randomState;
        }

        bool ApplySocialHitAftermath(
            RuntimeContentDatabase contentDb,
            in MorrowindDamageAppliedEvent damage,
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> hitVoiceRequests,
            int hitDialogueIndex,
            bool hasHitVoiceDialogue,
            DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests,
            NativeList<Entity> combatStartActors,
            NativeList<Entity> combatStartTargets,
            ref uint randomState)
        {
            if (damage.Attacker == Entity.Null
                || !IsActorEntity(damage.Attacker)
                || damage.Target == Entity.Null
                || !EntityManager.Exists(damage.Target)
                || !EntityManager.HasComponent<ActorSpawnSource>(damage.Target)
                || IsInCombatWith(damage.Target, damage.Attacker))
            {
                return true;
            }

            if (IsFriendlyHit(damage, out bool complain))
            {
                if (complain && hasHitVoiceDialogue && IsNpcTarget(contentDb, damage.Target, out var friendlyTargetSource))
                {
                    uint targetRef = PlacedRefIdOrZero(damage.Target);
                    if (!IsActorSayingOrPending(activeSays, hitVoiceRequests, damage.Target, targetRef))
                        QueueHitVoice(contentDb, hitDialogueIndex, friendlyTargetSource.Definition, damage.Target, hitVoiceRequests, ref randomState);
                }

                return false;
            }

            if (CanCommitAssaultCrime(contentDb, damage.Target, damage.Attacker))
                ApplyAssaultCrime(contentDb, damage.Target, damage.Attacker, forceGreetingRequests);

            if (ShouldStartCombatAfterHit(contentDb, damage.Target, damage.Attacker))
            {
                combatStartActors.Add(damage.Target);
                combatStartTargets.Add(damage.Attacker);
            }

            return true;
        }

        bool IsFriendlyHit(in MorrowindDamageAppliedEvent damage, out bool complain)
        {
            complain = false;
            if (!IsPlayer(damage.Attacker) || !IsPlayerFollower(damage.Target))
                return false;

            if (!EntityManager.HasComponent<ActorFriendlyHitState>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(damage.Target)} has no ActorFriendlyHitState.");

            if (IsInAnyCombat(damage.Target))
                return true;

            var friendly = EntityManager.GetComponentData<ActorFriendlyHitState>(damage.Target);
            friendly.Count++;
            EntityManager.SetComponentData(damage.Target, friendly);

            if (friendly.Count >= FriendlyHitForgivenessLimit)
                return false;

            complain = damage.SourceKind == MorrowindDamageSourceKind.Weapon
                       || damage.SourceKind == MorrowindDamageSourceKind.HandToHand;
            return true;
        }

        bool IsPlayerFollower(Entity actor)
        {
            if (actor == Entity.Null
                || !EntityManager.Exists(actor)
                || !EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
            {
                return false;
            }

            Entity player = TryGetPlayerEntity();
            if (player == Entity.Null)
                return false;

            var packages = EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
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

        bool CanCommitAssaultCrime(RuntimeContentDatabase contentDb, Entity target, Entity attacker)
        {
            if (!IsPlayer(attacker) || !IsNpcTarget(contentDb, target, out _))
                return false;

            RequireSocialCrimeComposition(target);
            if (IsAggressiveTo(target, attacker) || IsInAnyCombat(target))
                return false;

            var effects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, VampirismEffectId) <= 0f;
        }

        void ApplyAssaultCrime(RuntimeContentDatabase contentDb, Entity target, Entity attacker, DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests)
        {
            if (IsPlayerFollower(target))
                return;

            var settings = EntityManager.GetComponentData<ActorAiSettingsState>(target);
            if (!IsNpcTarget(contentDb, target, out var source))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Assault target ref={PlacedRefIdOrZero(target)} is not an NPC.");

            bool isGuard = IsGuardNpc(contentDb, source.Definition);
            bool reported = TryFindCrimeReporter(contentDb, attacker, target, out Entity reportingGuard);
            bool setCrimeId = reported;

            int dispositionModifier = 0;
            float victimDisposition = contentDb.RequireGameSettingFloat("fDispAttacking");
            float witnessDisposition = contentDb.RequireGameSettingFloat("iDispAttackMod");
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
                ApplyAssaultDispositionPenalty(target, dispositionModifier);
                setCrimeId = true;
            }

            if (!reported && !setCrimeId)
                return;

            int bounty = reported ? contentDb.RequireGameSettingInt("iCrimeAttack") : 0;
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(bounty);
            SetActorCrimeId(target, crimeId);

            if (reported)
            {
                MarkCrimeWitnesses(contentDb, attacker, target, crimeId);
                if (reportingGuard != Entity.Null)
                    QueueForceGreeting(forceGreetingRequests, reportingGuard);
            }
        }

        void ApplyAssaultDispositionPenalty(Entity target, int penalty)
        {
            if (!EntityManager.HasComponent<ActorDispositionState>(target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Assault target ref={PlacedRefIdOrZero(target)} has no ActorDispositionState.");

            var disposition = EntityManager.GetComponentData<ActorDispositionState>(target);
            disposition.BaseDisposition = ClampInt(disposition.BaseDisposition + penalty, 0, 100);
            EntityManager.SetComponentData(target, disposition);
        }

        bool IsGuardNpc(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            ref readonly var actor = ref contentDb.Get(actorHandle);
            return string.Equals(ContentId.NormalizeId(actor.ClassId), "guard", StringComparison.OrdinalIgnoreCase);
        }

        bool TryFindCrimeReporter(RuntimeContentDatabase contentDb, Entity player, Entity victim, out Entity guard)
        {
            guard = Entity.Null;
            if (player == Entity.Null || !EntityManager.Exists(player) || !EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Assault crime player has no LocalTransform.");

            float radius = contentDb.RequireGameSettingFloat("fAlarmRadius") * WorldScale.MwUnitsToMeters;
            float radiusSq = radius * radius;
            float3 playerPosition = EntityManager.GetComponentData<LocalTransform>(player).Position;
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
                if (!IsNpcActor(contentDb, source.ValueRO.Definition))
                    continue;
                if (!CanReportCrime(entity, victim, vitals.ValueRO))
                    continue;

                float distanceSq = math.lengthsq(transform.ValueRO.Position - playerPosition);
                if (entity != victim && distanceSq > radiusSq)
                    continue;

                reported = true;
                if (!IsGuardNpc(contentDb, source.ValueRO.Definition))
                    continue;
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                guard = entity;
            }

            return reported;
        }

        void MarkCrimeWitnesses(RuntimeContentDatabase contentDb, Entity player, Entity victim, int crimeId)
        {
            if (player == Entity.Null || !EntityManager.Exists(player) || !EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness scan player has no LocalTransform.");

            float radius = contentDb.RequireGameSettingFloat("fAlarmRadius") * WorldScale.MwUnitsToMeters;
            float radiusSq = radius * radius;
            float3 playerPosition = EntityManager.GetComponentData<LocalTransform>(player).Position;

            foreach (var (source, settings, vitals, transform, entity) in
                     SystemAPI.Query<
                             RefRO<ActorSpawnSource>,
                             RefRO<ActorAiSettingsState>,
                             RefRO<ActorVitalSet>,
                             RefRO<LocalTransform>>()
                         .WithNone<PlayerTag>()
                         .WithEntityAccess())
            {
                if (!CanReportCrime(entity, victim, vitals.ValueRO))
                    continue;
                if (!IsNpcActor(contentDb, source.ValueRO.Definition))
                    continue;

                float distanceSq = math.lengthsq(transform.ValueRO.Position - playerPosition);
                if (entity != victim && distanceSq > radiusSq)
                    continue;

                bool isReporter = settings.ValueRO.Alarm >= 100;
                bool isGuard = isReporter && IsGuardNpc(contentDb, source.ValueRO.Definition);
                bool isVictim = entity == victim;
                if (!isReporter && !isVictim)
                    continue;

                SetActorCrimeId(entity, crimeId);
                if (isGuard)
                    SetActorAlarmed(entity);
            }
        }

        bool CanReportCrime(Entity actor, Entity victim, in ActorVitalSet vitals)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                return false;
            if (EntityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!EntityManager.HasComponent<ActorSpawnSource>(actor))
                return false;
            if (vitals.CurrentHealth <= 0f)
                return false;
            if (EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled != 0)
            {
                return false;
            }
            if (EntityManager.HasComponent<ActorHitAftermathState>(actor))
            {
                var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(actor);
                if (aftermath.Dead != 0 || aftermath.KnockedDown != 0 || aftermath.KnockedOut != 0)
                    return false;
            }
            if (IsInCombatWith(actor, victim))
                return false;
            if (IsPlayerFollower(actor))
                return false;

            return true;
        }

        bool IsNpcActor(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness has invalid actor definition.");

            ref readonly var actor = ref contentDb.Get(actorHandle);
            return actor.Kind == ActorDefKind.Npc;
        }

        void QueueForceGreeting(DynamicBuffer<ActorForceGreetingRequest> requests, Entity guard)
        {
            uint placedRefId = PlacedRefIdOrZero(guard);
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

        bool ShouldStartCombatAfterHit(RuntimeContentDatabase contentDb, Entity target, Entity attacker)
        {
            if (target == Entity.Null
                || attacker == Entity.Null
                || !EntityManager.Exists(target)
                || !EntityManager.Exists(attacker)
                || IsInCombatWith(target, attacker))
            {
                return false;
            }

            if (!EntityManager.HasComponent<ActorVitalSet>(target)
                || EntityManager.GetComponentData<ActorVitalSet>(target).CurrentHealth <= 0f)
            {
                return false;
            }

            if (!IsPlayer(attacker))
                return IsInCombatWith(attacker, target);

            if (!EntityManager.HasComponent<ActorAiSettingsState>(target))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Combat target ref={PlacedRefIdOrZero(target)} has no ActorAiSettingsState.");

            var settings = EntityManager.GetComponentData<ActorAiSettingsState>(target);
            return settings.Fight != 0 || !DeclaresOnPcHitMeInteger(contentDb, target);
        }

        void StartCombatAfterHit(RuntimeContentDatabase contentDb, Entity target, Entity attacker)
        {
            uint targetPlacedRefId = PlacedRefIdOrZero(target);
            if (!MorrowindCombatTargetUtility.TryStartCombat(
                    contentDb,
                    EntityManager,
                    target,
                    targetPlacedRefId,
                    attacker,
                    PlacedRefIdOrZero(attacker)))
            {
                throw new InvalidOperationException($"[VVardenfell][OnHit] Failed to start combat for target ref={targetPlacedRefId}.");
            }
        }

        void ApplyMurderAftermath(RuntimeContentDatabase contentDb, in MorrowindDamageAppliedEvent damage)
        {
            if (!IsPlayer(damage.Attacker)
                || damage.TargetVital != MorrowindDamageTargetVital.Health
                || damage.Amount <= DamageEpsilon
                || damage.Target == Entity.Null
                || !EntityManager.Exists(damage.Target)
                || !EntityManager.HasComponent<ActorVitalSet>(damage.Target)
                || EntityManager.GetComponentData<ActorVitalSet>(damage.Target).CurrentHealth > 0f
                || !EntityManager.HasComponent<ActorHitAftermathState>(damage.Target)
                || EntityManager.GetComponentData<ActorHitAftermathState>(damage.Target).Dead != 0
                || !IsNpcTarget(contentDb, damage.Target, out _))
            {
                return;
            }

            RequireSocialCrimeComposition(damage.Target);
            var crime = EntityManager.GetComponentData<ActorCrimeState>(damage.Target);
            if (crime.CrimeId < 0)
                return;

            int bounty = contentDb.RequireGameSettingInt("iCrimeKilling");
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(bounty);
            SetActorCrimeId(damage.Target, crimeId);

            var eventState = EntityManager.GetComponentData<ActorScriptEventState>(damage.Target);
            eventState.Murdered = 1;
            EntityManager.SetComponentData(damage.Target, eventState);
        }

        void ApplyHitMemory(in MorrowindDamageAppliedEvent damage)
        {
            Entity target = RequireLiveActorTarget(damage.Target, "target");
            var targetState = EntityManager.GetComponentData<ActorScriptEventState>(target);

            if (damage.SourceKind == MorrowindDamageSourceKind.Weapon)
            {
                if (!damage.SourceContent.IsValid || damage.SourceContent.Kind != ContentReferenceKind.Item)
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Weapon hit target ref={PlacedRefIdOrZero(target)} has invalid source content.");

                targetState.LastHitAttemptObject = damage.SourceContent;
                if (damage.Amount > DamageEpsilon)
                    targetState.LastHitObject = damage.SourceContent;
            }

            if (IsActorEntity(damage.Attacker))
            {
                if (!IsInCombatWith(target, damage.Attacker) && targetState.Attacked == 0)
                    targetState.Attacked = 1;

                if (ShouldStoreHitAttemptActors(damage.Attacker, target))
                {
                    if (targetState.LastHitAttemptActor == Entity.Null)
                    {
                        targetState.LastHitAttemptActor = damage.Attacker;
                        targetState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(damage.Attacker);
                    }

                    var attackerState = RequireScriptEventState(damage.Attacker, "attacker");
                    if (attackerState.LastHitAttemptActor == Entity.Null)
                    {
                        attackerState.LastHitAttemptActor = target;
                        attackerState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(target);
                        EntityManager.SetComponentData(damage.Attacker, attackerState);
                    }
                }
            }

            EntityManager.SetComponentData(target, targetState);
        }

        void SetOnPcHitMeIfDeclared(RuntimeContentDatabase contentDb, Entity target)
        {
            Entity actor = RequireLiveActorTarget(target, "OnPCHitMe target");
            if (!TryResolveOnPcHitMeLocal(contentDb, actor, out int localIndex, out int declaredLocalCount))
                return;

            if (!EntityManager.HasBuffer<MorrowindScriptLocalValue>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} declares OnPCHitMe but has no script locals buffer.");

            var locals = EntityManager.GetBuffer<MorrowindScriptLocalValue>(actor);
            if (locals.Length < declaredLocalCount)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} script locals buffer length {locals.Length} is shorter than declared locals {declaredLocalCount}.");

            var local = locals[localIndex];
            if (local.ValueKind != (byte)MorrowindScriptValueKind.Integer)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} runtime OnPCHitMe local is not an integer.");

            local.IntValue = 1;
            local.FloatValue = 1f;
            locals[localIndex] = local;
        }

        bool DeclaresOnPcHitMeInteger(RuntimeContentDatabase contentDb, Entity target)
            => TryResolveOnPcHitMeLocal(contentDb, target, out _, out _);

        bool TryResolveOnPcHitMeLocal(
            RuntimeContentDatabase contentDb,
            Entity actor,
            out int localIndex,
            out int declaredLocalCount)
        {
            localIndex = -1;
            declaredLocalCount = 0;

            var source = EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has invalid actor definition.");

            ref readonly var actorDef = ref contentDb.Get(source.Definition);
            if (string.IsNullOrWhiteSpace(actorDef.ScriptId))
                return false;

            if (!contentDb.TryGetMorrowindScriptProgramHandle(actorDef.ScriptId, out var programHandle) || !programHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} script '{actorDef.ScriptId}' has no compiled runtime program.");

            var localDefinitions = contentDb.GetMorrowindScriptLocals(programHandle);
            declaredLocalCount = localDefinitions.Length;
            for (int i = 0; i < localDefinitions.Length; i++)
            {
                if (string.Equals(ContentId.NormalizeId(localDefinitions[i].Name), OnPcHitMeLocal, StringComparison.OrdinalIgnoreCase))
                {
                    localIndex = i;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            if (localDefinitions[localIndex].ValueKind != (byte)MorrowindScriptValueKind.Integer)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} local OnPCHitMe is not an integer.");
            return true;
        }

        void QueueHitVoice(
            RuntimeContentDatabase contentDb,
            int hitDialogueIndex,
            ActorDefHandle targetActor,
            Entity target,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> requests,
            ref uint randomState)
        {
            if (!MorrowindDialogueFilterUtility.TryFindRandomMatchingVoicedInfo(
                    contentDb,
                    EntityManager,
                    target,
                    targetActor,
                    hitDialogueIndex,
                    choice: 0,
                    ref randomState,
                    MorrowindVoiceAudioAvailability.IsVoiceAvailable,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Hit voice dialogue for actor ref={PlacedRefIdOrZero(target)} is unsupported: {unsupportedReason}");
                return;
            }

            ref readonly var info = ref contentDb.Data.DialogueInfos[infoIndex];
            if (string.IsNullOrWhiteSpace(info.SoundFile))
                return;

            requests.Add(new MorrowindCombatHitVoiceSayRequest
            {
                TargetEntity = target,
                TargetPlacedRefId = PlacedRefIdOrZero(target),
                VoicePath = RuntimeFixedStringUtility.ToFixed512OrDefaultWhiteSpace(info.SoundFile),
                Subtitle = RuntimeFixedStringUtility.ToFixed512OrDefaultWhiteSpace(info.Response),
            });
        }

        Entity RequireLiveActorTarget(Entity actor, string context)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity is missing.");
            if (!EntityManager.HasComponent<ActorSpawnSource>(actor) && !EntityManager.HasComponent<PlayerTag>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity={actor.Index}:{actor.Version} is not an actor.");
            if (!EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} ref={PlacedRefIdOrZero(actor)} has no ActorScriptEventState.");

            return actor;
        }

        ActorScriptEventState RequireScriptEventState(Entity actor, string context)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} entity is missing.");
            if (!EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Damage {context} ref={PlacedRefIdOrZero(actor)} has no ActorScriptEventState.");

            return EntityManager.GetComponentData<ActorScriptEventState>(actor);
        }

        bool ShouldStoreHitAttemptActors(Entity attacker, Entity target)
            => IsPlayer(attacker) || IsInCombatWith(attacker, target);

        bool IsInCombatWith(Entity actor, Entity target)
        {
            if (actor == Entity.Null
                || !EntityManager.Exists(actor)
                || !EntityManager.HasComponent<ActorCombatTargetState>(actor))
            {
                return false;
            }

            var combat = EntityManager.GetComponentData<ActorCombatTargetState>(actor);
            if (combat.Active == 0)
                return false;

            uint targetRef = PlacedRefIdOrZero(target);
            return combat.TargetEntity == target || (targetRef != 0u && combat.TargetPlacedRefId == targetRef);
        }

        bool IsNpcTarget(RuntimeContentDatabase contentDb, Entity target, out ActorSpawnSource source)
        {
            source = default;
            if (target == Entity.Null || !EntityManager.Exists(target) || !EntityManager.HasComponent<ActorSpawnSource>(target))
                return false;

            source = EntityManager.GetComponentData<ActorSpawnSource>(target);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(target)} has invalid actor definition.");

            ref readonly var actor = ref contentDb.Get(source.Definition);
            return actor.Kind == ActorDefKind.Npc;
        }

        bool IsActorEntity(Entity entity)
            => entity != Entity.Null
               && EntityManager.Exists(entity)
               && (EntityManager.HasComponent<ActorSpawnSource>(entity) || EntityManager.HasComponent<PlayerTag>(entity));

        void RequireSocialCrimeComposition(Entity actor)
        {
            if (!EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorCrimeState.");
            if (!EntityManager.HasComponent<ActorAiSettingsState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorAiSettingsState.");
            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorActiveMagicEffect buffer.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorHitAftermathState.");
        }

        bool IsAggressiveTo(Entity actor, Entity target)
        {
            if (IsInCombatWith(actor, target))
                return true;

            if (!EntityManager.HasComponent<ActorAiSettingsState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorAiSettingsState.");

            return EntityManager.GetComponentData<ActorAiSettingsState>(actor).Fight >= 100;
        }

        bool IsInAnyCombat(Entity actor)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                return false;

            if (!EntityManager.HasComponent<ActorCombatTargetState>(actor))
                return false;

            return EntityManager.GetComponentData<ActorCombatTargetState>(actor).Active != 0;
        }

        Entity TryGetPlayerEntity()
        {
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            if (query.IsEmptyIgnoreFilter)
                return Entity.Null;

            return query.GetSingletonEntity();
        }

        int AddPlayerBountyAndAdvanceCrimeId(int bounty)
        {
            Entity player = TryGetPlayerEntity();
            if (player == Entity.Null)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime reporting requires a loaded player entity.");
            if (!EntityManager.HasComponent<PlayerCrimeState>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Player has no PlayerCrimeState.");

            var crime = EntityManager.GetComponentData<PlayerCrimeState>(player);
            crime.Bounty = Math.Max(0, crime.Bounty + Math.Max(0, bounty));
            crime.CurrentCrimeId = crime.CurrentCrimeId < 0 ? 0 : crime.CurrentCrimeId + 1;
            EntityManager.SetComponentData(player, crime);
            return crime.CurrentCrimeId;
        }

        void SetActorCrimeId(Entity actor, int crimeId)
        {
            if (!EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorCrimeState.");

            var crime = EntityManager.GetComponentData<ActorCrimeState>(actor);
            crime.CrimeId = crimeId;
            EntityManager.SetComponentData(actor, crime);
        }

        void SetActorAlarmed(Entity actor)
        {
            if (!EntityManager.HasComponent<ActorCrimeState>(actor))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has no ActorCrimeState.");

            var crime = EntityManager.GetComponentData<ActorCrimeState>(actor);
            crime.Alarmed = 1;
            EntityManager.SetComponentData(actor, crime);
        }

        bool IsPlayerAttacker(Entity entity) => IsPlayer(entity);

        bool IsPlayer(Entity entity)
            => entity != Entity.Null && EntityManager.Exists(entity) && EntityManager.HasComponent<PlayerTag>(entity);

        bool ShouldQueueHitVoice(in MorrowindDamageAppliedEvent damage, int voiceHitOdds, ref uint randomState)
        {
            if (damage.TargetVital == MorrowindDamageTargetVital.Health
                && damage.Target != Entity.Null
                && EntityManager.Exists(damage.Target)
                && EntityManager.HasComponent<ActorVitalSet>(damage.Target)
                && EntityManager.GetComponentData<ActorVitalSet>(damage.Target).CurrentHealth <= 0f)
            {
                return true;
            }

            return (int)(NextRandom0To99(ref randomState)) < voiceHitOdds;
        }

        bool IsActorSayingOrPending(
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> pendingRequests,
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

        bool TryResolveHitVoiceDialogue(RuntimeContentDatabase contentDb, out int dialogueIndex)
        {
            dialogueIndex = -1;
            if (!contentDb.TryGetDialogueHandle(HitVoiceDialogueId, out var handle) || !handle.IsValid)
                return false;

            ref readonly var dialogue = ref contentDb.Get(handle);
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

        uint PlacedRefIdOrZero(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<PlacedRefIdentity>(entity))
                return 0u;

            return EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
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
