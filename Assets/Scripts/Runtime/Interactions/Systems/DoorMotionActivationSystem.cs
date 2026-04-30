using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial class DoorMotionActivationSystem : SystemBase
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            Entity target = request.TargetEntity;
            if (target == Entity.Null
                || !EntityManager.Exists(target)
                || !EntityManager.HasComponent<DoorMotionState>(target)
                || !EntityManager.HasComponent<DoorActivated>(target))
            {
                return;
            }

            var state = EntityManager.GetComponentData<DoorMotionState>(target);
            state.TargetProgress = state.TargetProgress >= 0.5f ? 0f : 1f;
            EntityManager.SetComponentData(target, state);
            EntityManager.SetComponentEnabled<DoorActivated>(target, true);

            request.Pending = 0;
            request.TargetEntity = Entity.Null;
            ClearFocus();
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }
}
