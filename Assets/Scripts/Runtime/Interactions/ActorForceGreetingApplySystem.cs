using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct ActorForceGreetingApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorForceGreetingRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<MorrowindDialogueState>();
            systemState.RequireForUpdate<MorrowindDialogueSession>();
            systemState.RequireForUpdate<DialogueReadinessState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ActorForceGreetingRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] ForceGreeting requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, ref contentBlob, ref worldCells, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, in ActorForceGreetingRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is not an actor.");

            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is disabled.");
            }

            string displayName = InteractionMetadataResolver.ResolveDisplayName(ref contentBlob, ref worldCells, systemState.EntityManager, target, InteractableKind.Npc)
                ?? InteractionMetadataResolver.ResolveKindLabel(InteractableKind.Npc);
            ActorDefHandle actor = systemState.EntityManager.GetComponentData<ActorSpawnSource>(target).Definition;
            uint placedRefId = request.TargetPlacedRefId;
            if (placedRefId == 0u && systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                placedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;

            ref var readiness = ref SystemAPI.GetSingletonRW<DialogueReadinessState>().ValueRW;
            readiness.PendingTargetEntity = target;
            readiness.PendingTargetPlacedRefId = placedRefId;
            readiness.PendingActor = actor;
            readiness.LastActivationSequence++;

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
                SpeakerActor = actor,
                SelectedTopicDialogueIndex = -1,
                LastInfoIndex = -1,
                SpeakerId = RuntimeFixedStringUtility.ToFixed128OrDefault(displayName),
            };
        }
    }
}
