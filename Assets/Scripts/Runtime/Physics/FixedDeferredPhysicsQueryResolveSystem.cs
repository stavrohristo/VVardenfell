using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsQueryFrameStampSystem))]
    public partial struct FixedDeferredPhysicsQueryResolveSystem : ISystem
    {
        const DeferredPhysicsQueryKindMask FixedOwnedKinds = DeferredPhysicsQueryKindMask.ProjectileSegment;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<PhysicsWorldSingleton>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            if (!systemState.EntityManager.HasComponent<DeferredPhysicsQueryPending>(queueEntity))
                throw new System.InvalidOperationException("[VVardenfell][Physics] Deferred physics query queue is missing its pending marker.");
            if (!SystemAPI.IsComponentEnabled<DeferredPhysicsQueryPending>(queueEntity))
                return;

            systemState.Dependency.Complete();

            var frame = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>();
            DeferredPhysicsQueryResolveUtility.ResolveOwnedRequests(
                systemState.EntityManager,
                queueEntity,
                SystemAPI.GetSingleton<PhysicsWorldSingleton>(),
                frame.FixedTick,
                frame.BuildSequence,
                FixedOwnedKinds,
                DeferredPhysicsQueryResolveDomain.Fixed);
        }
    }
}
