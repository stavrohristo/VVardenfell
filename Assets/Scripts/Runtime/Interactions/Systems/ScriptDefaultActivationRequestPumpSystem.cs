using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindFramePhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionActivationSystem))]
    [UpdateBefore(typeof(ContainerActivationSystem))]
    public partial struct ScriptDefaultActivationRequestPumpSystem : ISystem
    {
        EntityQuery _runtimeQuery;
        EntityQuery _requestQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _runtimeQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<InteractionRuntimeState>(),
                ComponentType.ReadWrite<ScriptDefaultActivationRequest>());
            _requestQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());

            systemState.RequireForUpdate(_runtimeQuery);
            systemState.RequireForUpdate(_requestQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending != 0)
                return;

            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var queuedRequests = systemState.EntityManager.GetBuffer<ScriptDefaultActivationRequest>(runtimeEntity);
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
