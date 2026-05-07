using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial struct DoorMotionActivationSystem : ISystem
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _requestQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            systemState.RequireForUpdate(_requestQuery);
            systemState.RequireForUpdate(_focusQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            Entity target = request.TargetEntity;
            bool exists = target != Entity.Null && systemState.EntityManager.Exists(target);
            bool hasDoorMotion = exists && systemState.EntityManager.HasComponent<DoorMotionState>(target);
            bool hasDoorActivated = exists && systemState.EntityManager.HasComponent<DoorActivated>(target);
            bool hasDoorAuthoring = exists && systemState.EntityManager.HasComponent<DoorAuthoring>(target);
            bool hasDoorInteractable = exists && systemState.EntityManager.HasComponent<DoorInteractable>(target);
            if (hasDoorAuthoring && !hasDoorInteractable)
                throw new System.InvalidOperationException("[VVardenfell][Interaction] authored door motion activation requires DoorInteractable metadata.");

            byte doorIsTeleport = hasDoorInteractable
                ? systemState.EntityManager.GetComponentData<DoorInteractable>(target).IsTeleport
                : (byte)0;
            if (target == Entity.Null
                || !exists
                || doorIsTeleport != 0
                || !hasDoorMotion
                || !hasDoorActivated)
            {
                return;
            }

            var state = systemState.EntityManager.GetComponentData<DoorMotionState>(target);
            state.TargetProgress = state.TargetProgress >= 0.5f ? 0f : 1f;
            systemState.EntityManager.SetComponentData(target, state);
            systemState.EntityManager.SetComponentEnabled<DoorActivated>(target, true);

            bool consumesRequest = request.Kind != (byte)InteractableKind.Door;

            if (!consumesRequest)
                return;

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
