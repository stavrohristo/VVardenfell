using Unity.Entities;
using Unity.Collections;
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
        DeferredPhysicsQueryResolveScratch _scratch;

        public void OnCreate(ref SystemState systemState)
        {
            _scratch = new DeferredPhysicsQueryResolveScratch(64, Allocator.Persistent);
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<PhysicsWorldSingleton>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_scratch.IsCreated)
                _scratch.Dispose();
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
                DeferredPhysicsQueryResolveDomain.Fixed,
                ref _scratch);
        }
    }
}
