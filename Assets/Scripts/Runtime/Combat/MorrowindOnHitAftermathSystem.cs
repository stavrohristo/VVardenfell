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
        const float DamageEpsilon = 0.001f;
        const int FriendlyHitForgivenessLimit = 4;
        static readonly short VampirismEffectId = RequireEffectId("sEffectVampirism");

        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
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
            if (!EntityManager.HasBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindCombatHitVoiceSayRequest buffer.");
            if (!EntityManager.HasBuffer<MorrowindCombatHitVoiceResolveRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindCombatHitVoiceResolveRequest buffer.");
            if (!EntityManager.HasBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no MorrowindScriptActiveSay buffer.");
            if (!EntityManager.HasBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][OnHit] Script runtime has no ActorForceGreetingRequest buffer.");

            var hitVoiceRequests = EntityManager.GetBuffer<MorrowindCombatHitVoiceSayRequest>(scriptRuntimeEntity);
            var hitVoiceResolveRequests = EntityManager.GetBuffer<MorrowindCombatHitVoiceResolveRequest>(scriptRuntimeEntity);
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

                if (setOnPcHitMe && IsPlayerAttacker(damage.ValueRO.Attacker))
                    SetOnPcHitMeIfDeclared(ref content, damage.ValueRO.Target);

                if (damage.ValueRO.TargetVital == MorrowindDamageTargetVital.Health
                    && damage.ValueRO.Amount > DamageEpsilon
                    && IsPlayer(damage.ValueRO.Target))
                {
                    RuntimeShellStateUtility.ActivateHitOverlay(ref shell);
                }

                if (damage.ValueRO.Amount <= DamageEpsilon || damage.ValueRO.Attacker == Entity.Null)
                    continue;

                ApplyMurderAftermath(ref content, damage.ValueRO);

                if (!IsNpcTarget(ref content, damage.ValueRO.Target, out var targetSource))
                    continue;

                uint targetRef = PlacedRefIdOrZero(damage.ValueRO.Target);
                bool actorSayingOrPending = IsActorSayingOrPending(activeSays, hitVoiceRequests, hitVoiceResolveRequests, damage.ValueRO.Target, targetRef);
                bool shouldQueueHitVoice = hasHitVoiceDialogue && !actorSayingOrPending && ShouldQueueHitVoice(damage.ValueRO, voiceHitOdds, ref randomState);

                if (shouldQueueHitVoice)
                {
                    QueueHitVoiceResolve(hitDialogueIndex, targetSource.Definition, damage.ValueRO.Target, hitVoiceResolveRequests, randomState);
                }
            }

            for (int i = 0; i < combatStartActors.Length; i++)
                StartCombatAfterHit(ref content, combatStartActors[i], combatStartTargets[i]);

            combatState.RandomState = randomState == 0u ? 1u : randomState;
        }

        bool ApplySocialHitAftermath(
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
                if (complain && hasHitVoiceDialogue && IsNpcTarget(ref content, damage.Target, out var friendlyTargetSource))
                {
                    uint targetRef = PlacedRefIdOrZero(damage.Target);
                    if (!IsActorSayingOrPending(activeSays, hitVoiceRequests, hitVoiceResolveRequests, damage.Target, targetRef))
                        QueueHitVoiceResolve(hitDialogueIndex, friendlyTargetSource.Definition, damage.Target, hitVoiceResolveRequests, randomState);
                }

                return false;
            }

            if (CanCommitAssaultCrime(ref content, damage.Target, damage.Attacker))
                ApplyAssaultCrime(ref content, damage.Target, damage.Attacker, forceGreetingRequests);

            if (ShouldStartCombatAfterHit(ref content, damage.Target, damage.Attacker))
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

        bool CanCommitAssaultCrime(ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            if (!IsPlayer(attacker) || !IsNpcTarget(ref content, target, out _))
                return false;

            RequireSocialCrimeComposition(target);
            if (IsAggressiveTo(target, attacker) || IsInAnyCombat(target))
                return false;

            var effects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, VampirismEffectId) <= 0f;
        }

        void ApplyAssaultCrime(ref RuntimeContentBlob content, Entity target, Entity attacker, DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests)
        {
            if (IsPlayerFollower(target))
                return;

            var settings = EntityManager.GetComponentData<ActorAiSettingsState>(target);
            if (!IsNpcTarget(ref content, target, out var source))
                throw new InvalidOperationException($"[VVardenfell][OnHit] Assault target ref={PlacedRefIdOrZero(target)} is not an NPC.");

            bool isGuard = IsGuardNpc(ref content, source.Definition);
            bool reported = TryFindCrimeReporter(ref content, attacker, target, out Entity reportingGuard);
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
                ApplyAssaultDispositionPenalty(target, dispositionModifier);
                setCrimeId = true;
            }

            if (!reported && !setCrimeId)
                return;

            int bounty = reported ? RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeAttack) : 0;
            int crimeId = AddPlayerBountyAndAdvanceCrimeId(bounty);
            SetActorCrimeId(target, crimeId);

            if (reported)
            {
                MarkCrimeWitnesses(ref content, attacker, target, crimeId);
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

        bool IsGuardNpc(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.ClassIdHash == RuntimeContentKnownHashes.guard;
        }

        bool TryFindCrimeReporter(ref RuntimeContentBlob content, Entity player, Entity victim, out Entity guard)
        {
            guard = Entity.Null;
            if (player == Entity.Null || !EntityManager.Exists(player) || !EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Assault crime player has no LocalTransform.");

            float radius = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fAlarmRadius) * WorldScale.MwUnitsToMeters;
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
                if (!IsNpcActor(ref content, source.ValueRO.Definition))
                    continue;
                if (!CanReportCrime(entity, victim, vitals.ValueRO))
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

        void MarkCrimeWitnesses(ref RuntimeContentBlob content, Entity player, Entity victim, int crimeId)
        {
            if (player == Entity.Null || !EntityManager.Exists(player) || !EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness scan player has no LocalTransform.");

            float radius = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fAlarmRadius) * WorldScale.MwUnitsToMeters;
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

        bool IsNpcActor(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][OnHit] Crime witness has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
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

        bool ShouldStartCombatAfterHit(ref RuntimeContentBlob content, Entity target, Entity attacker)
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
            return settings.Fight != 0 || !DeclaresOnPcHitMeInteger(ref content, target);
        }

        void StartCombatAfterHit(ref RuntimeContentBlob content, Entity target, Entity attacker)
        {
            uint targetPlacedRefId = PlacedRefIdOrZero(target);
            if (!MorrowindCombatTargetUtility.TryStartCombat(
                    ref content,
                    EntityManager,
                    target,
                    targetPlacedRefId,
                    attacker,
                    PlacedRefIdOrZero(attacker)))
            {
                throw new InvalidOperationException($"[VVardenfell][OnHit] Failed to start combat for target ref={targetPlacedRefId}.");
            }
        }

        void ApplyMurderAftermath(ref RuntimeContentBlob content, in MorrowindDamageAppliedEvent damage)
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
                || !IsNpcTarget(ref content, damage.Target, out _))
            {
                return;
            }

            RequireSocialCrimeComposition(damage.Target);
            var crime = EntityManager.GetComponentData<ActorCrimeState>(damage.Target);
            if (crime.CrimeId < 0)
                return;

            int bounty = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeKilling);
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

        void SetOnPcHitMeIfDeclared(ref RuntimeContentBlob content, Entity target)
        {
            Entity actor = RequireLiveActorTarget(target, "OnPCHitMe target");
            if (!TryResolveOnPcHitMeLocal(ref content, actor, out int localIndex, out int declaredLocalCount))
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

        bool DeclaresOnPcHitMeInteger(ref RuntimeContentBlob content, Entity target)
            => TryResolveOnPcHitMeLocal(ref content, target, out _, out _);

        bool TryResolveOnPcHitMeLocal(
            ref RuntimeContentBlob content,
            Entity actor,
            out int localIndex,
            out int declaredLocalCount)
        {
            localIndex = -1;
            declaredLocalCount = 0;

            var source = EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} has invalid actor definition.");

            ref RuntimeActorDefBlob actorDef = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            if (actorDef.ScriptIdHash == 0UL)
                return false;

            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref content, actorDef.ScriptIdHash, out var programHandle) || !programHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} script hash 0x{actorDef.ScriptIdHash:X16} has no compiled runtime program.");

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
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(actor)} local OnPCHitMe is not an integer.");
            return true;
        }

        void QueueHitVoiceResolve(
            int hitDialogueIndex,
            ActorDefHandle targetActor,
            Entity target,
            DynamicBuffer<MorrowindCombatHitVoiceResolveRequest> requests,
            uint randomState)
        {
            requests.Add(new MorrowindCombatHitVoiceResolveRequest
            {
                TargetEntity = target,
                TargetPlacedRefId = PlacedRefIdOrZero(target),
                Actor = targetActor,
                DialogueIndex = hitDialogueIndex,
                RandomState = randomState == 0u ? 1u : randomState,
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

        bool IsNpcTarget(ref RuntimeContentBlob content, Entity target, out ActorSpawnSource source)
        {
            source = default;
            if (target == Entity.Null || !EntityManager.Exists(target) || !EntityManager.HasComponent<ActorSpawnSource>(target))
                return false;

            source = EntityManager.GetComponentData<ActorSpawnSource>(target);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Actor ref={PlacedRefIdOrZero(target)} has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
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
            if (_playerQuery.IsEmptyIgnoreFilter)
                return Entity.Null;

            return _playerQuery.GetSingletonEntity();
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


