using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    public partial class ActorAiActivateRequestSystem : SystemBase
    {
        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadWrite<InteractionRuntimeState>(),
                ComponentType.ReadWrite<ScriptDefaultActivationRequest>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate<ActorAiPackageRuntime>();
        }

        protected override void OnUpdate()
        {
            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            ref var runtimeState = ref _runtimeQuery.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            var activationRequests = EntityManager.GetBuffer<ScriptDefaultActivationRequest>(runtimeEntity);

            foreach (var (aiStateRef, packages, transform) in SystemAPI
                         .Query<RefRW<ActorAiState>, DynamicBuffer<ActorAiPackageRuntime>, RefRO<LocalTransform>>())
            {
                ref var aiState = ref aiStateRef.ValueRW;
                if (aiState.Status != (byte)ActorAiPlannerStatus.Waiting
                    || elapsedTime >= aiState.WaitUntilTime
                    || aiState.LastPackageActionTime >= aiState.WaitUntilTime)
                {
                    continue;
                }

                if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                    continue;

                var package = packages[aiState.CurrentPackageIndex];
                if (package.Type != (byte)ActorAiRuntimePackageType.Activate)
                    continue;

                if (package.FollowTargetEntity == Entity.Null
                    || !EntityManager.Exists(package.FollowTargetEntity)
                    || !EntityManager.HasComponent<LocalTransform>(package.FollowTargetEntity))
                {
                    continue;
                }

                float activateDistance = math.max(0.5f, package.FollowDistance);
                float3 targetPosition = EntityManager.GetComponentData<LocalTransform>(package.FollowTargetEntity).Position;
                if (math.lengthsq(FlatDelta(targetPosition, transform.ValueRO.Position)) > activateDistance * activateDistance)
                    continue;

                if (!InteractionTargetResolver.TryResolveSupportedKind(EntityManager, package.FollowTargetEntity, out InteractableKind kind))
                {
                    throw new InvalidOperationException(
                        $"[VVardenfell][AI] AiActivate target ref={package.FollowTargetPlacedRefId} is loaded but has no supported activation kind.");
                }

                uint sequence = runtimeState.NextActivationSequence + 1u;
                runtimeState.NextActivationSequence = sequence;
                activationRequests.Add(new ScriptDefaultActivationRequest
                {
                    TargetEntity = package.FollowTargetEntity,
                    TargetPlacedRefId = package.FollowTargetPlacedRefId,
                    Sequence = sequence,
                    Kind = (byte)kind,
                });
                aiState.LastPackageActionTime = aiState.WaitUntilTime;
            }
        }

        static float3 FlatDelta(float3 a, float3 b)
            => new(a.x - b.x, 0f, a.z - b.z);
    }
}
