using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial struct DoorMotionActivationSystem : ISystem
    {
        const string LogPrefix = "[VVardenfell][DoorMotion]";

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
            bool shouldLog = request.Kind == (byte)InteractableKind.Door
                || request.Kind == (byte)InteractableKind.Activator;
            bool exists = target != Entity.Null && systemState.EntityManager.Exists(target);
            bool hasDoorMotion = exists && systemState.EntityManager.HasComponent<DoorMotionState>(target);
            bool hasDoorActivated = exists && systemState.EntityManager.HasComponent<DoorActivated>(target);
            bool hasDoorAuthoring = exists && systemState.EntityManager.HasComponent<DoorAuthoring>(target);
            bool hasDoorInteractable = exists && systemState.EntityManager.HasComponent<DoorInteractable>(target);
            byte doorIsTeleport = hasDoorInteractable
                ? systemState.EntityManager.GetComponentData<DoorInteractable>(target).IsTeleport
                : (byte)0;
            if (target == Entity.Null
                || !exists
                || !hasDoorMotion
                || !hasDoorActivated)
            {
                if (shouldLog)
                    LogIgnoredRequest(ref systemState, 
                        request,
                        target,
                        exists,
                        hasDoorMotion,
                        hasDoorActivated,
                        hasDoorAuthoring,
                        hasDoorInteractable,
                        doorIsTeleport);
                return;
            }

            var state = systemState.EntityManager.GetComponentData<DoorMotionState>(target);
            bool wasEnabled = systemState.EntityManager.IsComponentEnabled<DoorActivated>(target);
            float previousTarget = state.TargetProgress;
            state.TargetProgress = state.TargetProgress >= 0.5f ? 0f : 1f;
            systemState.EntityManager.SetComponentData(target, state);
            systemState.EntityManager.SetComponentEnabled<DoorActivated>(target, true);

            bool consumesRequest = request.Kind != (byte)InteractableKind.Door;
            if (shouldLog)
                LogAcceptedRequest(ref systemState, request, target, state, wasEnabled, previousTarget, consumesRequest);

            if (!consumesRequest)
                return;

            request.Pending = 0;
            request.TargetEntity = Entity.Null;
            ClearFocus();
        }

        void LogIgnoredRequest(ref SystemState systemState, 
            in InteractionActivationRequest request,
            Entity target,
            bool exists,
            bool hasDoorMotion,
            bool hasDoorActivated,
            bool hasDoorAuthoring,
            bool hasDoorInteractable,
            byte doorIsTeleport)
        {
            uint placedRefId = ResolvePlacedRefId(ref systemState, request, target, exists);
            string reason = target == Entity.Null
                ? "NullTarget"
                : !exists
                    ? "MissingTarget"
                    : !hasDoorMotion
                        ? "MissingDoorMotionState"
                        : "MissingDoorActivated";

            Debug.Log(
                $"{LogPrefix} request=Ignored reason={reason} sequence={request.Sequence} kind={FormatKind(request.Kind)} " +
                $"target={FormatEntity(target)} placedRef={FormatPlacedRef(placedRefId)} exists={exists} " +
                $"hasDoorAuthoring={hasDoorAuthoring} hasDoorInteractable={hasDoorInteractable} " +
                $"doorIsTeleport={doorIsTeleport != 0} hasDoorMotion={hasDoorMotion} hasDoorActivated={hasDoorActivated}");
        }

        void LogAcceptedRequest(ref SystemState systemState, 
            in InteractionActivationRequest request,
            Entity target,
            in DoorMotionState state,
            bool wasEnabled,
            float previousTarget,
            bool consumesRequest)
        {
            uint placedRefId = ResolvePlacedRefId(ref systemState, request, target, true);
            Debug.Log(
                $"{LogPrefix} request=Accepted sequence={request.Sequence} kind={FormatKind(request.Kind)} " +
                $"target={FormatEntity(target)} placedRef={FormatPlacedRef(placedRefId)} consumed={consumesRequest} wasEnabled={wasEnabled} " +
                $"progress={state.Progress:0.###} previousTarget={previousTarget:0.###} targetProgress={state.TargetProgress:0.###} " +
                $"axis={FormatAxis(state.Axis)} rangeDegrees={math.degrees(state.RangeRadians):0.###} " +
                $"speedDegreesPerSecond={math.degrees(state.SpeedRadiansPerSecond):0.###}");
        }

        uint ResolvePlacedRefId(ref SystemState systemState, in InteractionActivationRequest request, Entity target, bool exists)
        {
            if (request.TargetPlacedRefId != 0u)
                return request.TargetPlacedRefId;
            return exists && systemState.EntityManager.HasComponent<PlacedRefIdentity>(target)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value
                : 0u;
        }

        static string FormatKind(byte kind)
        {
            return System.Enum.IsDefined(typeof(InteractableKind), kind)
                ? ((InteractableKind)kind).ToString()
                : kind.ToString();
        }

        static string FormatEntity(Entity entity)
        {
            return entity == Entity.Null
                ? "Entity.Null"
                : $"Entity({entity.Index}:{entity.Version})";
        }

        static string FormatPlacedRef(uint placedRefId)
        {
            return placedRefId == 0u
                ? "none"
                : $"0x{placedRefId:X8}";
        }

        static string FormatAxis(byte axis)
        {
            return axis switch
            {
                0 => "X",
                1 => "Y",
                2 => "Z",
                _ => axis.ToString(),
            };
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
