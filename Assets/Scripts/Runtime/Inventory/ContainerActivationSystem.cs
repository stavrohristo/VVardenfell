using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionActivationSystem))]
    public partial class ContainerActivationSystem : SystemBase
    {
        EntityQuery _requestQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<ContainerWindowRequest>();
            RequireForUpdate<ContainerSessionHeader>();
            RequireForUpdate<ContainerSessionItem>();
            RequireForUpdate<WorldJournalEntry>();
            RequireForUpdate<PlayerInteractionFocus>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Container)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)InteractableKind.Container;
            result.Success = 0;
            result.PendingNotification = 0;
            result.NotificationText = default;

            bool isAuthoredContainer = EntityManager.Exists(target) && EntityManager.HasComponent<ContainerAuthoring>(target);
            bool isCorpse = EntityManager.Exists(target) && ActorCorpseLootUtility.IsDeadLootableActor(EntityManager, target);
            if ((!isAuthoredContainer && !isCorpse)
                || !EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] container activation request resolved to a missing or non-container logical entity.");
                ClearFocus();
                return;
            }

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var headers = SystemAPI.GetSingletonBuffer<ContainerSessionHeader>();
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var journal = SystemAPI.GetSingletonBuffer<WorldJournalEntry>();
            ContainerDefHandle definition = default;
            string title;
            if (isCorpse)
            {
                ActorCorpseLootUtility.RequireDeadLootableActor(EntityManager, target, placedRefId);
                ActorCorpseLootUtility.EnsureSessionInitialized(EntityManager, journal, headers, items, target, placedRefId);
                title = ActorCorpseLootUtility.ResolveTitle(ref contentBlob, EntityManager, target);
            }
            else
            {
                var authoring = EntityManager.GetComponentData<ContainerAuthoring>(target);
                definition = authoring.Definition;
                EnsureContainerSessionInitialized(ref contentBlob, journal, headers, items, placedRefId, definition);
                title = ContainerLootUtility.ResolveContainerTitle(ref contentBlob, definition);
            }

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var windowState = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ContainerWindowRuntimeUtility.OpenContainer(
                ref shell,
                ref windowState,
                target,
                placedRefId,
                definition,
                title);
            windowState.SelectedItemIndex = ContainerLootUtility.FindFirstItemIndex(items, placedRefId);

            var requestState = SystemAPI.GetSingletonRW<ContainerWindowRequest>();
            requestState.ValueRW = default;

            ClearFocus();
            result.Success = 1;

        }

        static void EnsureContainerSessionInitialized(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<WorldJournalEntry> journal,
            DynamicBuffer<ContainerSessionHeader> headers,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition)
        {
            if (placedRefId == 0u || !definition.IsValid)
                return;

            if (ContainerLootUtility.FindHeaderIndex(headers, placedRefId) >= 0)
                return;

            headers.Add(new ContainerSessionHeader
            {
                PlacedRefId = placedRefId,
                Definition = definition,
            });

            var diagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ContainerLootUtility.MaterializeContainerContents(ref contentBlob, items, placedRefId, definition, diagnostics);
            WorldJournalUtility.ApplyContainerDeltas(placedRefId, journal, items);

            if (diagnostics.Count > 0)
            {
                int shown = 0;
                var summary = new System.Text.StringBuilder();
                foreach (string message in diagnostics)
                {
                    if (shown >= 4)
                        break;

                    if (summary.Length > 0)
                        summary.Append(" | ");

                    summary.Append(message);
                    shown++;
                }

                if (diagnostics.Count > shown)
                    summary.Append($" | +{diagnostics.Count - shown} more");

                Debug.LogWarning($"[VVardenfell][Container] materialization for placedRef=0x{placedRefId:X8} skipped unsupported content: {summary}");
            }
        }

        void ClearFocus()
        {
            var focus = SystemAPI.GetSingletonRW<PlayerInteractionFocus>();
            focus.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }
}
