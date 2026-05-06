using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    [UpdateBefore(typeof(ActorAiActivateRequestSystem))]
    public partial struct ActorAiActivateDirectSteeringSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorAiPackageRuntime>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;
            foreach (var (aiStateRef, packages, transformRef, inputRef) in SystemAPI
                         .Query<
                             RefRW<ActorAiState>,
                             DynamicBuffer<ActorAiPackageRuntime>,
                             RefRW<LocalTransform>,
                             RefRW<MorrowindMovementInput>>())
            {
                ref var aiState = ref aiStateRef.ValueRW;
                if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                    continue;

                var package = packages[aiState.CurrentPackageIndex];
                if (package.Type != (byte)ActorAiRuntimePackageType.Activate)
                    continue;

                if (package.FollowTargetEntity == Entity.Null
                    || !systemState.EntityManager.Exists(package.FollowTargetEntity)
                    || !systemState.EntityManager.HasComponent<LocalTransform>(package.FollowTargetEntity))
                {
                    inputRef.ValueRW.LocalMove = float2.zero;
                    continue;
                }

                float3 targetPosition = systemState.EntityManager.GetComponentData<LocalTransform>(package.FollowTargetEntity).Position;
                float3 direction = targetPosition - transformRef.ValueRO.Position;
                direction.y = 0f;
                float distanceSq = math.lengthsq(direction);
                float activationDistance = math.max(0.5f, package.FollowDistance);
                if (distanceSq <= activationDistance * activationDistance)
                {
                    inputRef.ValueRW.LocalMove = float2.zero;
                    aiState.WaitUntilTime = elapsedTime + 0.01f;
                    aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
                    return;
                }

                float3 worldDirection = math.normalizesafe(direction);
                if (math.lengthsq(worldDirection) <= 0.000001f)
                    return;

                transformRef.ValueRW.Rotation = quaternion.LookRotationSafe(worldDirection, math.up());
                inputRef.ValueRW.LocalMove = new float2(0f, 1f);
                inputRef.ValueRW.RunHeld = false;
                inputRef.ValueRW.SneakHeld = false;
                inputRef.ValueRW.JumpPressed = false;
                aiState.Status = (byte)ActorAiPlannerStatus.Traversing;
            }
        }
    }
}
