using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(LooseItemPickupSystem))]
    public partial class NpcInteractionDeferredSystem : SystemBase
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<DialogueReadinessState>();
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<MorrowindDialogueState>();
            RequireForUpdate<MorrowindDialogueSession>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            var kind = (InteractableKind)request.Kind;
            if (kind != InteractableKind.Npc)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target) || !InteractionTargetResolver.TryResolveSupportedKind(EntityManager, target, out InteractableKind resolvedKind) || resolvedKind != kind)
            {
                Debug.LogWarning("[VVardenfell][Interaction] deferred npc activation resolved to a missing or mismatched logical entity.");
                ClearFocus();
                return;
            }

            string displayName = InteractionMetadataResolver.ResolveDisplayName(RuntimeContentDatabase.Active, EntityManager, target, kind)
                ?? InteractionMetadataResolver.ResolveKindLabel(kind);

            ref var dialogue = ref SystemAPI.GetSingletonRW<DialogueReadinessState>().ValueRW;
            dialogue.PendingTargetEntity = target;
            dialogue.PendingTargetPlacedRefId = placedRefId;
            dialogue.PendingActor = EntityManager.HasComponent<ActorSpawnSource>(target)
                ? EntityManager.GetComponentData<ActorSpawnSource>(target).Definition
                : default;
            dialogue.LastActivationSequence = sequence;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            RuntimeShellStateUtility.OpenDialogue(ref shell);

            ref var dialogueState = ref SystemAPI.GetSingletonRW<MorrowindDialogueState>().ValueRW;
            ref var session = ref SystemAPI.GetSingletonRW<MorrowindDialogueSession>().ValueRW;
            session = new MorrowindDialogueSession
            {
                Active = 1,
                NeedsGreeting = 1,
                Sequence = dialogueState.NextSessionSequence++,
                SpeakerEntity = target,
                SpeakerPlacedRefId = placedRefId,
                SpeakerActor = dialogue.PendingActor,
                SelectedTopicDialogueIndex = -1,
                LastInfoIndex = -1,
                SpeakerId = RuntimeFixedStringUtility.ToFixed128OrDefault(displayName),
            };

            ClearFocus();

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)kind;
            result.Success = 1;
            result.PendingNotification = 0;
            result.NotificationText = default;
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

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(NpcInteractionDeferredSystem))]
    public partial class ActivatorInteractionDeferredSystem : SystemBase
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Activator)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target)
                || !InteractionTargetResolver.TryResolveSupportedKind(EntityManager, target, out InteractableKind resolvedKind)
                || resolvedKind != InteractableKind.Activator)
            {
                Debug.LogWarning("[VVardenfell][Interaction] deferred activator activation resolved to a missing or mismatched logical entity.");
                ClearFocus();
                return;
            }

            string displayName = InteractionMetadataResolver.ResolveDisplayName(RuntimeContentDatabase.Active, EntityManager, target, InteractableKind.Activator)
                ?? InteractionMetadataResolver.ResolveKindLabel(InteractableKind.Activator);

            ClearFocus();

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)InteractableKind.Activator;
            result.Success = 1;
            result.PendingNotification = 1;
            result.NotificationText = RuntimeFixedStringUtility.ToFixed128OrDefault(string.IsNullOrWhiteSpace(displayName)
                ? "Nothing happens."
                : $"{displayName}: Nothing happens.");
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

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateAfter(typeof(NpcInteractionDeferredSystem))]
    [UpdateAfter(typeof(ActivatorInteractionDeferredSystem))]
    public partial class DialogueReadinessCleanupSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<DialogueReadinessState>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<DialogueReadinessState>().ValueRW;
            if (state.PendingTargetEntity == Entity.Null)
                return;

            if (EntityManager.Exists(state.PendingTargetEntity)
                && EntityManager.HasComponent<PassiveActorPresence>(state.PendingTargetEntity))
            {
                var actor = EntityManager.GetComponentData<PassiveActorPresence>(state.PendingTargetEntity);
                if (actor.CanTalk != 0)
                    return;
            }

            state.PendingTargetEntity = Entity.Null;
            state.PendingTargetPlacedRefId = 0u;
            state.PendingActor = default;
        }
    }
}
