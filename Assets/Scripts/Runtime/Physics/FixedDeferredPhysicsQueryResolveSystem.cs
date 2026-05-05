using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsQueryFrameStampSystem))]
    public partial class FixedDeferredPhysicsQueryResolveSystem : SystemBase
    {
        const DeferredPhysicsQueryKindMask FixedOwnedKinds = DeferredPhysicsQueryKindMask.ProjectileSegment;

        protected override void OnCreate()
        {
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            if (!EntityManager.HasComponent<DeferredPhysicsQueryPending>(queueEntity))
                throw new System.InvalidOperationException("[VVardenfell][Physics] Deferred physics query queue is missing its pending marker.");
            if (!SystemAPI.IsComponentEnabled<DeferredPhysicsQueryPending>(queueEntity))
                return;

            CompleteDependency();

            var frame = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>();
            DeferredPhysicsQueryResolveUtility.ResolveOwnedRequests(
                EntityManager,
                queueEntity,
                SystemAPI.GetSingleton<PhysicsWorldSingleton>(),
                frame.FixedTick,
                frame.BuildSequence,
                FixedOwnedKinds,
                DeferredPhysicsQueryResolveDomain.Fixed);
        }
    }
}
