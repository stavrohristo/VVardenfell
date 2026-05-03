using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ActorForceGreetingApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorForceGreetingRequest>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<MorrowindDialogueState>();
            RequireForUpdate<MorrowindDialogueSession>();
            RequireForUpdate<DialogueReadinessState>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorForceGreetingRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(in ActorForceGreetingRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is not an actor.");

            if (EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                throw new InvalidOperationException($"[VVardenfell][Interaction] ForceGreeting target ref={request.TargetPlacedRefId} is disabled.");
            }

            string displayName = InteractionMetadataResolver.ResolveDisplayName(RuntimeContentDatabase.Active, EntityManager, target, InteractableKind.Npc)
                ?? InteractionMetadataResolver.ResolveKindLabel(InteractableKind.Npc);
            ActorDefHandle actor = EntityManager.GetComponentData<ActorSpawnSource>(target).Definition;
            uint placedRefId = request.TargetPlacedRefId;
            if (placedRefId == 0u && EntityManager.HasComponent<PlacedRefIdentity>(target))
                placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;

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
