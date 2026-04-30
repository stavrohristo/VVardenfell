using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionActivationSystem))]
    [UpdateBefore(typeof(ContainerActivationSystem))]
    public partial class ScriptDefaultActivationRequestPumpSystem : SystemBase
    {
        EntityQuery _runtimeQuery;
        EntityQuery _requestQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadWrite<InteractionRuntimeState>(),
                ComponentType.ReadWrite<ScriptDefaultActivationRequest>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate(_requestQuery);
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending != 0)
                return;

            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var queuedRequests = EntityManager.GetBuffer<ScriptDefaultActivationRequest>(runtimeEntity);
            if (queuedRequests.Length == 0)
                return;

            var queued = queuedRequests[0];
            queuedRequests.RemoveAt(0);
            request = new InteractionActivationRequest
            {
                TargetEntity = queued.TargetEntity,
                TargetPlacedRefId = queued.TargetPlacedRefId,
                Sequence = queued.Sequence,
                Kind = queued.Kind,
                Pending = 1,
            };
        }
    }
}
