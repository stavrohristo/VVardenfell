using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial class InteractionActorPickFollowSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalTransform>();
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            foreach (var (parent, transform, localToWorld) in
                     SystemAPI.Query<RefRO<LogicalRefParent>, RefRW<LocalTransform>, RefRW<LocalToWorld>>()
                         .WithAll<InteractionActorPickSurfaceTag>())
            {
                Entity target = parent.ValueRO.Value;
                if (target == Entity.Null || !EntityManager.Exists(target))
                    continue;
                if (!EntityManager.HasComponent<LocalTransform>(target) || !EntityManager.HasComponent<LocalToWorld>(target))
                    continue;

                transform.ValueRW = EntityManager.GetComponentData<LocalTransform>(target);
                localToWorld.ValueRW = EntityManager.GetComponentData<LocalToWorld>(target);
            }
        }
    }
}
