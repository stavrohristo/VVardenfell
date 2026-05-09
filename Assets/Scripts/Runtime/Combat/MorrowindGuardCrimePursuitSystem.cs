using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindCrimeWitnessCleanupSystem))]
    [UpdateBefore(typeof(ActorAiPackageTargetResolveSystem))]
    public partial struct MorrowindGuardCrimePursuitSystem : ISystem
    {
        const float ArrestGreetingDistanceMw = 100f;

        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCrimeState>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadOnly<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>(),
                ComponentType.ReadOnly<LocalTransform>());
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<ActorCrimeState>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerCrime = systemState.EntityManager.GetComponentData<PlayerCrimeState>(player);
            if (playerCrime.Bounty <= 0)
                return;

            if (!systemState.EntityManager.HasComponent<LocalTransform>(player))
                throw new InvalidOperationException("[VVardenfell][GuardCrime] Player has no LocalTransform.");

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][GuardCrime] Guard crime pursuit requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            int threshold = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeThreshold);
            int thresholdMultiplier = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iCrimeThresholdMultiplier);
            int fightAttack = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightAttack);
            if (threshold <= 0 || thresholdMultiplier <= 0)
                throw new InvalidOperationException($"[VVardenfell][GuardCrime] Crime threshold GMSTs must be positive. threshold={threshold}, multiplier={thresholdMultiplier}.");
            if (playerCrime.Bounty < threshold)
                return;

            float3 playerPosition = systemState.EntityManager.GetComponentData<LocalTransform>(player).Position;
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            uint randomState = combatState.RandomState;
            bool playerCrimeChanged = false;
            using var pursuitGuards = new NativeList<Entity>(Allocator.Temp);
            using var combatGuards = new NativeList<Entity>(Allocator.Temp);

            foreach (var (source, settingsRef, crimeRef, vitals, transform, entity) in SystemAPI
                         .Query<
                             RefRO<ActorSpawnSource>,
                             RefRW<ActorAiSettingsState>,
                             RefRW<ActorCrimeState>,
                             RefRO<ActorVitalSet>,
                             RefRO<LocalTransform>>()
                         .WithEntityAccess())
            {
                if (systemState.EntityManager.HasComponent<PlayerTag>(entity))
                    continue;

                if (crimeRef.ValueRO.CrimeId >= 0 || crimeRef.ValueRO.Alarmed != 0)
                    continue;
                if (settingsRef.ValueRO.Alarm < 100)
                    continue;
                if (ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, entity))
                    continue;
                if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(entity)
                    && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0)
                {
                    continue;
                }
                if (!IsGuardNpc(ref content, source.ValueRO.Definition))
                    continue;
                if (IsCurrentPackage(ref systemState, entity, ActorAiRuntimePackageType.Pursue))
                    continue;
                if (IsInCombat(ref systemState, entity))
                    continue;
                if (MorrowindCrimeAwarenessUtility.IsCalmedHumanoid(systemState.EntityManager, entity))
                    continue;

                if (!ActorAiLineOfSightUtility.TryGetLineOfSightOrRequest(
                        systemState.EntityManager,
                        deferredPhysicsQueueEntity,
                        fixedTick,
                        entity,
                        player,
                        EyePosition(transform.ValueRO.Position),
                        EyePosition(playerPosition),
                        out bool hasLineOfSight))
                {
                    continue;
                }

                if (!hasLineOfSight)
                    continue;
                if (!MorrowindCrimeAwarenessUtility.AwarenessCheck(
                        ref content,
                        systemState.EntityManager,
                        player,
                        entity,
                        systemState.EntityManager.GetComponentData<LocalTransform>(player),
                        transform.ValueRO,
                        NextRandom0To99(ref randomState)))
                {
                    continue;
                }

                int crimeId = AdvanceCrimeId(ref playerCrime);
                playerCrimeChanged = true;
                ref var crime = ref crimeRef.ValueRW;
                crime.CrimeId = crimeId;
                crime.Alarmed = 1;

                if (playerCrime.Bounty >= threshold * thresholdMultiplier)
                {
                    ref var settings = ref settingsRef.ValueRW;
                    settings.Fight = math.max(settings.Fight, fightAttack);
                    SetHitAttemptActor(ref systemState, entity, player);
                    combatGuards.Add(entity);
                }
                else
                {
                    pursuitGuards.Add(entity);
                }
            }

            if (playerCrimeChanged)
                systemState.EntityManager.SetComponentData(player, playerCrime);
            combatState.RandomState = randomState == 0u ? 1u : randomState;

            for (int i = 0; i < combatGuards.Length; i++)
            {
                Entity guard = combatGuards[i];
                if (!MorrowindCombatTargetUtility.TryStartCombat(
                        ref content,
                        systemState.EntityManager,
                        guard,
                        PlacedRefIdOrZero(ref systemState, guard),
                        player,
                        PlacedRefIdOrZero(ref systemState, player)))
                {
                    throw new InvalidOperationException($"[VVardenfell][GuardCrime] Failed to start high-bounty guard combat for actor ref={PlacedRefIdOrZero(ref systemState, guard)}.");
                }
            }

            for (int i = 0; i < pursuitGuards.Length; i++)
                SchedulePursuit(ref systemState, ref content, pursuitGuards[i], player, playerPosition);
        }

        static void SchedulePursuit(ref SystemState systemState, ref RuntimeContentBlob content, Entity guard, Entity player, float3 playerPosition)
        {
            if (!MorrowindScriptAiPackageUtility.TryApplyPursueRequest(
                    ref content,
                    systemState.EntityManager,
                    guard,
                    PlacedRefIdOrZero(ref systemState, guard),
                    player,
                    PlacedRefIdOrZero(ref systemState, player),
                    playerPosition,
                    ArrestGreetingDistanceMw * WorldScale.MwUnitsToMeters))
            {
                throw new InvalidOperationException($"[VVardenfell][GuardCrime] Failed to schedule arrest pursuit for guard ref={PlacedRefIdOrZero(ref systemState, guard)}.");
            }
        }

        static int AdvanceCrimeId(ref PlayerCrimeState playerCrime)
        {
            playerCrime.CurrentCrimeId = playerCrime.CurrentCrimeId <= playerCrime.PaidCrimeId
                ? playerCrime.PaidCrimeId + 1
                : playerCrime.CurrentCrimeId + 1;
            return playerCrime.CurrentCrimeId;
        }

        static bool IsGuardNpc(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][GuardCrime] Guard candidate has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            return actor.Kind == ActorDefKind.Npc && actor.ClassIdHash == RuntimeContentKnownHashes.guard;
        }

        static bool IsCurrentPackage(ref SystemState systemState, Entity actor, ActorAiRuntimePackageType type)
        {
            if (!systemState.EntityManager.HasComponent<ActorAiState>(actor) || !systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
                return false;

            var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
            var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return false;

            return packages[aiState.CurrentPackageIndex].Type == (byte)type;
        }

        static bool IsInCombat(ref SystemState systemState, Entity actor)
            => MorrowindCombatTargetUtility.IsInCombat(systemState.EntityManager, actor);

        static void SetHitAttemptActor(ref SystemState systemState, Entity actor, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(actor))
                throw new InvalidOperationException($"[VVardenfell][GuardCrime] High-bounty guard ref={PlacedRefIdOrZero(ref systemState, actor)} has no ActorScriptEventState.");

            var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(actor);
            eventState.LastHitAttemptActor = target;
            eventState.LastHitAttemptActorPlacedRefId = PlacedRefIdOrZero(ref systemState, target);
            systemState.EntityManager.SetComponentData(actor, eventState);
        }

        static uint NextRandom0To99(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state % 100u;
        }

        static float3 EyePosition(float3 position)
            => position + new float3(0f, 1.5f, 0f);

        static uint PlacedRefIdOrZero(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity) || !systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity))
                return 0u;

            return systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
        }
    }
}
