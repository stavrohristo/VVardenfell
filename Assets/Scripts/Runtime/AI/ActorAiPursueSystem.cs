using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    public partial struct ActorAiPursueSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCrimeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<ActorAiPackageRuntime>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorForceGreetingRequest>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerCrime = systemState.EntityManager.GetComponentData<PlayerCrimeState>(player);
            var playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(player);
            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var forceGreetingRequests = systemState.EntityManager.GetBuffer<ActorForceGreetingRequest>(scriptRuntimeEntity);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;

            foreach (var (aiStateRef, packages, transformRef, entity) in SystemAPI
                         .Query<
                             RefRO<ActorAiState>,
                             DynamicBuffer<ActorAiPackageRuntime>,
                             RefRO<LocalTransform>>()
                         .WithEntityAccess())
            {
                int currentPackageIndex = aiStateRef.ValueRO.CurrentPackageIndex;
                if ((uint)currentPackageIndex >= (uint)packages.Length)
                    continue;

                var package = packages[currentPackageIndex];
                if (package.Type != (byte)ActorAiRuntimePackageType.Pursue)
                    continue;

                if (playerCrime.Bounty <= 0)
                {
                    MorrowindScriptAiPackageUtility.ClearActorPackages(systemState.EntityManager, entity);
                    continue;
                }

                Entity target = package.FollowTargetEntity;
                if (target == Entity.Null || !systemState.EntityManager.Exists(target) || !systemState.EntityManager.HasComponent<LocalTransform>(target))
                    throw new InvalidOperationException($"[VVardenfell][AiPursue] Pursue package actor ref={PlacedRefIdOrZero(ref systemState, entity)} has no live target.");
                if (target != player)
                    throw new InvalidOperationException($"[VVardenfell][AiPursue] Pursue package actor ref={PlacedRefIdOrZero(ref systemState, entity)} targets a non-player entity.");

                SheatheWeapon(ref systemState, entity);

                float3 actorPosition = transformRef.ValueRO.Position;
                float3 playerPosition = playerTransform.Position;
                float distanceSq = math.lengthsq(FlatDelta(playerPosition, actorPosition));
                float greetingDistance = math.max(0.5f, package.FollowDistance);

                if (distanceSq <= greetingDistance * greetingDistance)
                {
                    if (!ActorAiLineOfSightUtility.TryGetLineOfSightOrRequest(
                            systemState.EntityManager,
                            deferredPhysicsQueueEntity,
                            fixedTick,
                            entity,
                            player,
                            EyePosition(actorPosition),
                            EyePosition(playerPosition),
                            out bool hasLineOfSight))
                    {
                        continue;
                    }

                    if (!hasLineOfSight)
                        continue;

                    QueueForceGreeting(ref systemState, forceGreetingRequests, entity);
                    MorrowindScriptAiPackageUtility.ClearActorPackages(systemState.EntityManager, entity);
                }
            }
        }

        static float3 FlatDelta(float3 target, float3 source)
        {
            float3 delta = target - source;
            delta.y = 0f;
            return delta;
        }

        static void SheatheWeapon(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(actor))
                return;

            var weapon = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(actor);
            weapon.Drawn = 0;
            weapon.Phase = ActorWeaponAnimationPhase.Hidden;
            weapon.AttackHeld = 0;
            weapon.AttackPressed = 0;
            weapon.AttackReleased = 0;
            weapon.ReleaseQueued = 0;
            weapon.MeleeHitPending = 0;
            weapon.MeleeSwingPending = 0;
            systemState.EntityManager.SetComponentData(actor, weapon);
        }

        static void QueueForceGreeting(ref SystemState systemState, DynamicBuffer<ActorForceGreetingRequest> requests, Entity actor)
        {
            uint placedRefId = PlacedRefIdOrZero(ref systemState, actor);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.TargetEntity == actor || (placedRefId != 0u && request.TargetPlacedRefId == placedRefId))
                    return;
            }

            requests.Add(new ActorForceGreetingRequest
            {
                TargetEntity = actor,
                TargetPlacedRefId = placedRefId,
            });
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
