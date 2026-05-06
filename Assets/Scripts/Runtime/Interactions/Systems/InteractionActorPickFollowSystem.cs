using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial struct InteractionActorPickFollowSystem : ISystem
    {
        EntityQuery _pickSurfaceQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _pickSurfaceQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<InteractionActorPickSurfaceTag>(),
                ComponentType.ReadOnly<LogicalRefParent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());

            systemState.RequireForUpdate(_pickSurfaceQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            systemState.EntityManager.CompleteDependencyBeforeRO<LocalTransform>();
            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            foreach (var (parent, transform, localToWorld) in
                     SystemAPI.Query<RefRO<LogicalRefParent>, RefRW<LocalTransform>, RefRW<LocalToWorld>>()
                         .WithAll<InteractionActorPickSurfaceTag>())
            {
                Entity target = parent.ValueRO.Value;
                if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                    continue;
                if (!systemState.EntityManager.HasComponent<LocalTransform>(target) || !systemState.EntityManager.HasComponent<LocalToWorld>(target))
                    continue;

                transform.ValueRW = systemState.EntityManager.GetComponentData<LocalTransform>(target);
                localToWorld.ValueRW = systemState.EntityManager.GetComponentData<LocalToWorld>(target);
            }
        }
    }
}
