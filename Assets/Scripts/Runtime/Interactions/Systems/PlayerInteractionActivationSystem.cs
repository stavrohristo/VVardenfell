using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInteractionSystemGroup))]
    [UpdateAfter(typeof(InteractionTargetResolutionSystem))]
    public partial class PlayerInteractionActivationSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _focusQuery;
        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _interactionRuntimeQuery;
        ComponentLookup<MorrowindScriptInstance> _scriptLookup;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterControl>());
            _focusQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionFocus>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            _interactionRuntimeQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionRuntimeState>(), ComponentType.ReadWrite<ScriptActivationEvent>());
            _scriptLookup = GetComponentLookup<MorrowindScriptInstance>(true);
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_interactionRuntimeQuery);
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            var transition = _transitionQuery.GetSingleton<InteriorTransitionState>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            ref var control = ref controlRef.ValueRW;
            if (!control.InteractPressed)
                return;

            control.InteractPressed = false;

            if (request.Pending != 0 || transition.TransitionInProgress != 0)
                return;

            var focus = _focusQuery.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget == 0 || !EntityManager.Exists(focus.TargetEntity))
                return;

            var resolved = new ResolvedInteractionTarget(
                focus.TargetEntity,
                focus.PlacedRefId,
                (InteractableKind)focus.InteractKind,
                focus.HitDistance);

            var runtimeEntity = _interactionRuntimeQuery.GetSingletonEntity();
            ref var runtimeState = ref _interactionRuntimeQuery.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            uint sequence = runtimeState.NextActivationSequence + 1u;
            runtimeState.NextActivationSequence = sequence;
            _scriptLookup.Update(this);

            if (_scriptLookup.TryGetComponent(resolved.TargetEntity, out var script)
                && script.Status == (byte)MorrowindScriptInstanceStatus.Running
                && script.SuppressActivation != 0)
            {
                var events = EntityManager.GetBuffer<ScriptActivationEvent>(runtimeEntity);
                events.Add(new ScriptActivationEvent
                {
                    TargetEntity = resolved.TargetEntity,
                    TargetPlacedRefId = resolved.PlacedRefId,
                    Sequence = sequence,
                    Kind = (byte)resolved.Kind,
                });
                request = new InteractionActivationRequest
                {
                    TargetEntity = Entity.Null,
                };
                return;
            }

            request = new InteractionActivationRequest
            {
                Pending = 1,
                Sequence = sequence,
                Kind = (byte)resolved.Kind,
                TargetEntity = resolved.TargetEntity,
                TargetPlacedRefId = resolved.PlacedRefId,
            };
        }
    }
}
