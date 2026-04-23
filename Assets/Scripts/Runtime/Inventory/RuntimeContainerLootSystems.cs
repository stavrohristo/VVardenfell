using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    static class ContainerWindowRuntimeUtility
    {
        public static void OpenContainer(
            ref RuntimeShellState shell,
            ref ContainerWindowState state,
            Entity target,
            uint placedRefId,
            ContainerDefHandle definition,
            string title)
        {
            bool inventoryWasAlreadyOpen = shell.InventoryOpen != 0;

            shell.InventoryOpen = 1;
            shell.ContainerOpen = 1;
            shell.PauseMenuOpen = 0;
            shell.ModalOpen = 0;
            shell.ModalTitle = default;
            shell.ModalBody = default;

            state.Visible = 1;
            state.OpenTargetEntity = target;
            state.OpenPlacedRefId = placedRefId;
            state.Definition = definition;
            state.PreserveInventoryOnClose = (byte)(inventoryWasAlreadyOpen ? 1 : 0);
            state.Title = ToFixedTitle(title);
        }

        public static void CloseContainer(ref RuntimeShellState shell, ref ContainerWindowState state)
        {
            shell.ContainerOpen = 0;
            if (state.PreserveInventoryOnClose == 0)
                shell.InventoryOpen = 0;

            state.Visible = 0;
            state.OpenPlacedRefId = 0u;
            state.OpenTargetEntity = Entity.Null;
            state.Definition = default;
            state.SelectedItemIndex = -1;
            state.PreserveInventoryOnClose = 0;
            state.Title = default;
            state.SelectedItemDetailsText = default;
        }

        static FixedString128Bytes ToFixedTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            if (value.Length > 127)
                value = value.Substring(0, 127);

            return new FixedString128Bytes(value);
        }
    }

    static class ContainerLootUtility
    {
        public const int FixedLeveledLootPlayerLevel = 1;
        const int MaxLeveledResolutionDepth = 16;
        const int ItemLeveledEachFlag = 0x01;
        const int ItemLeveledAllLevelsFlag = 0x02;

        public static int FindHeaderIndex(DynamicBuffer<ContainerSessionHeader> headers, uint placedRefId)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        public static int FindFirstItemIndex(DynamicBuffer<ContainerSessionItem> items, uint placedRefId)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].PlacedRefId == placedRefId && items[i].Count > 0)
                    return i;
            }

            return -1;
        }

        public static ContentReference ToContentReference(ItemDefHandle definition)
        {
            return new ContentReference
            {
                Kind = ContentReferenceKind.Item,
                HandleValue = definition.Value,
            };
        }

        public static ContentReference ToContentReference(LightDefHandle definition)
        {
            return new ContentReference
            {
                Kind = ContentReferenceKind.Light,
                HandleValue = definition.Value,
            };
        }

        public static void AddOrIncrementContainerStack(
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContentReference content,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].PlacedRefId != placedRefId
                    || items[i].Content.Kind != content.Kind
                    || items[i].Content.HandleValue != content.HandleValue)
                {
                    continue;
                }

                var entry = items[i];
                entry.Count += count;
                items[i] = entry;
                return;
            }

            items.Add(new ContainerSessionItem
            {
                PlacedRefId = placedRefId,
                Content = content,
                Count = count,
            });
        }

        public static void ApplyContainerDelta(
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContentReference content,
            int deltaCount)
        {
            if (!content.IsValid || deltaCount == 0)
                return;

            if (deltaCount > 0)
            {
                AddOrIncrementContainerStack(items, placedRefId, content, deltaCount);
                return;
            }

            int remaining = -deltaCount;
            for (int i = items.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = items[i];
                if (entry.PlacedRefId != placedRefId
                    || entry.Content.Kind != content.Kind
                    || entry.Content.HandleValue != content.HandleValue)
                {
                    continue;
                }

                if (entry.Count <= remaining)
                {
                    remaining -= math.max(0, entry.Count);
                    items.RemoveAt(i);
                    continue;
                }

                entry.Count -= remaining;
                items[i] = entry;
                remaining = 0;
            }
        }

        public static int AddInventoryStack(
            DynamicBuffer<PlayerInventoryItem> inventory,
            ContentReference content,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return 0;

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Content.Kind != content.Kind || inventory[i].Content.HandleValue != content.HandleValue)
                    continue;

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return entry.Count;
            }

            inventory.Add(new PlayerInventoryItem
            {
                Content = content,
                Count = count,
            });
            return count;
        }

        public static string ResolveContainerTitle(RuntimeContentDatabase contentDb, ContainerDefHandle definition)
        {
            if (contentDb == null || !definition.IsValid)
                return "Container";

            ref readonly var container = ref contentDb.Get(definition);
            if (!string.IsNullOrWhiteSpace(container.Name))
                return container.Name.Trim();
            if (!string.IsNullOrWhiteSpace(container.Id))
                return container.Id.Trim();
            return "Container";
        }

        public static FixedString512Bytes ToFixedDetails(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            if (value.Length > 511)
                value = value.Substring(0, 511);

            return new FixedString512Bytes(value);
        }

        public static void MaterializeContainerContents(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition,
            HashSet<string> diagnostics)
        {
            if (contentDb == null || placedRefId == 0u || !definition.IsValid)
                return;

            ReadOnlySpan<ContainerItemDef> authoredItems = contentDb.GetContainerItems(definition);
            for (int i = 0; i < authoredItems.Length; i++)
            {
                ref readonly var authored = ref authoredItems[i];
                if (authored.Count <= 0 || string.IsNullOrWhiteSpace(authored.ItemId))
                    continue;

                if (TryResolveDirectCarryable(contentDb, authored.ItemId, out var directContent, out string directDiagnostic))
                {
                    AddOrIncrementContainerStack(items, placedRefId, directContent, authored.Count);
                    continue;
                }

                if (!string.IsNullOrEmpty(directDiagnostic))
                {
                    diagnostics?.Add(directDiagnostic);
                    continue;
                }

                if (!contentDb.TryGetItemLeveledListHandle(authored.ItemId, out ItemLeveledListDefHandle listHandle))
                {
                    diagnostics?.Add($"missing authored target '{authored.ItemId}'");
                    continue;
                }

                ResolveLeveledListIntoContainer(contentDb, listHandle, items, placedRefId, authored.Count, i, diagnostics);
            }
        }

        static void ResolveLeveledListIntoContainer(
            RuntimeContentDatabase contentDb,
            ItemLeveledListDefHandle listHandle,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            int authoredCount,
            int authoredEntryIndex,
            HashSet<string> diagnostics)
        {
            ref readonly var list = ref contentDb.Get(listHandle);
            bool resolveEach = (list.Flags & ItemLeveledEachFlag) != 0;

            if (!resolveEach)
            {
                if (TryResolveLeveledResult(contentDb, listHandle, BuildResolutionSeed(placedRefId, authoredEntryIndex, 0), 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out ContentReference content, out string diagnostic)
                    && content.IsValid)
                {
                    AddOrIncrementContainerStack(items, placedRefId, content, authoredCount);
                }
                else if (!string.IsNullOrEmpty(diagnostic))
                {
                    diagnostics?.Add(diagnostic);
                }

                return;
            }

            for (int iteration = 0; iteration < authoredCount; iteration++)
            {
                if (TryResolveLeveledResult(contentDb, listHandle, BuildResolutionSeed(placedRefId, authoredEntryIndex, iteration), 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out ContentReference content, out string diagnostic)
                    && content.IsValid)
                {
                    AddOrIncrementContainerStack(items, placedRefId, content, 1);
                }
                else if (!string.IsNullOrEmpty(diagnostic))
                {
                    diagnostics?.Add(diagnostic);
                }
            }
        }

        static bool TryResolveDirectCarryable(
            RuntimeContentDatabase contentDb,
            string itemId,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (!contentDb.TryResolvePlaceable(itemId, out ContentReference resolved))
                return false;

            switch (resolved.Kind)
            {
                case ContentReferenceKind.Item:
                case ContentReferenceKind.Light:
                    content = resolved;
                    return true;
                default:
                    diagnostic = $"unsupported authored target '{itemId}' ({resolved.Kind})";
                    return false;
            }
        }

        static bool TryResolveLeveledResult(
            RuntimeContentDatabase contentDb,
            ItemLeveledListDefHandle listHandle,
            uint seed,
            int depth,
            HashSet<string> visitedLists,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (contentDb == null || !listHandle.IsValid)
                return false;

            if (depth >= MaxLeveledResolutionDepth)
            {
                diagnostic = $"item leveled-list recursion cap reached at depth {MaxLeveledResolutionDepth}";
                return false;
            }

            ref readonly var list = ref contentDb.Get(listHandle);
            string normalizedId = ContentId.NormalizeId(list.Id);
            if (!visitedLists.Add(normalizedId))
            {
                diagnostic = $"item leveled-list cycle detected at '{list.Id}'";
                return false;
            }

            try
            {
                if (RollPercent(seed) < list.ChanceNone)
                    return false;

                ReadOnlySpan<ItemLeveledListEntryDef> entries = contentDb.GetItemLeveledListEntries(listHandle);
                if (entries.Length == 0)
                    return false;

                bool allLevels = (list.Flags & ItemLeveledAllLevelsFlag) != 0;
                int highestEligibleLevel = 0;
                bool hasEligible = false;
                for (int i = 0; i < entries.Length; i++)
                {
                    int level = entries[i].Level;
                    if (level > highestEligibleLevel && level <= FixedLeveledLootPlayerLevel)
                    {
                        highestEligibleLevel = level;
                        hasEligible = true;
                    }
                }

                if (!hasEligible)
                    return false;

                var candidateIds = new List<string>(entries.Length);
                for (int i = 0; i < entries.Length; i++)
                {
                    int level = entries[i].Level;
                    if (level > FixedLeveledLootPlayerLevel)
                        continue;

                    if (allLevels || level == highestEligibleLevel)
                        candidateIds.Add(entries[i].ItemId);
                }

                if (candidateIds.Count == 0)
                    return false;

                int candidateIndex = NextRandomIndex(ref seed, candidateIds.Count);
                string resolvedId = candidateIds[candidateIndex];
                if (TryResolveDirectCarryable(contentDb, resolvedId, out content, out string directDiagnostic))
                    return true;

                if (!string.IsNullOrEmpty(directDiagnostic))
                {
                    diagnostic = directDiagnostic;
                    return false;
                }

                if (!contentDb.TryGetItemLeveledListHandle(resolvedId, out ItemLeveledListDefHandle nestedHandle))
                {
                    diagnostic = $"missing leveled-list target '{resolvedId}' referenced by '{list.Id}'";
                    return false;
                }

                seed = MixSeed(seed, (uint)candidateIndex + 1u);
                return TryResolveLeveledResult(contentDb, nestedHandle, seed, depth + 1, visitedLists, out content, out diagnostic);
            }
            finally
            {
                visitedLists.Remove(normalizedId);
            }
        }

        static uint BuildResolutionSeed(uint placedRefId, int authoredEntryIndex, int iteration)
        {
            return math.hash(new uint4(
                placedRefId,
                unchecked((uint)authoredEntryIndex + 1u),
                unchecked((uint)iteration + 1u),
                0x9E3779B9u));
        }

        static uint MixSeed(uint seed, uint salt)
        {
            return math.hash(new uint2(seed, salt));
        }

        static int RollPercent(uint seed)
        {
            uint state = seed == 0u ? 0xA341316Cu : seed;
            state = state * 1664525u + 1013904223u;
            return (int)(state % 100u);
        }

        static int NextRandomIndex(ref uint seed, int count)
        {
            seed = seed == 0u ? 0xC8013EA4u : seed;
            seed = seed * 1664525u + 1013904223u;
            return count <= 1 ? 0 : (int)(seed % (uint)count);
        }
    }

    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial class ContainerLootBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            else
                runtimeEntity = EntityManager.CreateEntity();

            EnsureBuffer<ContainerSessionHeader>(runtimeEntity);
            EnsureBuffer<ContainerSessionItem>(runtimeEntity);
            EnsureComponent(runtimeEntity, new ContainerWindowState
            {
                NormalizedX = 0.49f,
                NormalizedY = 0.54f,
                NormalizedWidth = 0.31f,
                NormalizedHeight = 0.38f,
                SelectedItemIndex = -1,
            });
            EnsureComponent(runtimeEntity, new ContainerWindowRequest());
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }

        void EnsureBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity))
                EntityManager.AddBuffer<T>(entity);
        }
    }

    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateBefore(typeof(LooseItemPickupSystem))]
    [UpdateBefore(typeof(NpcInteractionDeferredSystem))]
    [UpdateBefore(typeof(ActivatorInteractionDeferredSystem))]
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

            if (!EntityManager.Exists(target)
                || !EntityManager.HasComponent<ContainerAuthoring>(target)
                || !EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] container activation request resolved to a missing or non-container logical entity.");
                ClearFocus();
                return;
            }

            var authoring = EntityManager.GetComponentData<ContainerAuthoring>(target);
            var contentDb = RuntimeContentDatabase.Active;
            var headers = SystemAPI.GetSingletonBuffer<ContainerSessionHeader>();
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var journal = SystemAPI.GetSingletonBuffer<WorldJournalEntry>();
            EnsureContainerSessionInitialized(contentDb, journal, headers, items, placedRefId, authoring.Definition);

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var windowState = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ContainerWindowRuntimeUtility.OpenContainer(
                ref shell,
                ref windowState,
                target,
                placedRefId,
                authoring.Definition,
                ContainerLootUtility.ResolveContainerTitle(contentDb, authoring.Definition));
            windowState.SelectedItemIndex = ContainerLootUtility.FindFirstItemIndex(items, placedRefId);

            var requestState = SystemAPI.GetSingletonRW<ContainerWindowRequest>();
            requestState.ValueRW = default;

            ClearFocus();
            result.Success = 1;

            Debug.Log($"[VVardenfell][Interaction] opened container '{ContainerLootUtility.ResolveContainerTitle(contentDb, authoring.Definition)}' placedRef=0x{placedRefId:X8}.");
        }

        static void EnsureContainerSessionInitialized(
            RuntimeContentDatabase contentDb,
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

            if (contentDb == null)
                return;

            var diagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ContainerLootUtility.MaterializeContainerContents(contentDb, items, placedRefId, definition, diagnostics);
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

    [UpdateInGroup(typeof(MorrowindInteractionSystemGroup))]
    public partial class ContainerTransferSystem : SystemBase
    {
        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<ContainerWindowRequest>();
            RequireForUpdate<ContainerSessionItem>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<WorldJournalEntry>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<ContainerWindowRequest>().ValueRW;

            if (request.PendingClose != 0)
            {
                request.PendingClose = 0;
                ContainerWindowRuntimeUtility.CloseContainer(ref shell, ref state);
            }

            if (shell.ContainerOpen == 0 || state.OpenPlacedRefId == 0u)
            {
                request.PendingTakeSelected = 0;
                request.PendingTakeAll = 0;
                return;
            }

            if (request.PendingTakeAll == 0 && request.PendingTakeSelected == 0)
                return;

            CompleteDependency();

            uint placedRefId = state.OpenPlacedRefId;
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            int transferredStacks = 0;

            if (request.PendingTakeAll != 0)
            {
                for (int i = items.Length - 1; i >= 0; i--)
                {
                    var entry = items[i];
                    if (entry.PlacedRefId != placedRefId || entry.Count <= 0)
                        continue;

                    WorldJournalUtility.AppendContainerDelta(EntityManager, placedRefId, entry.Content, -entry.Count);
                    ContainerLootUtility.AddInventoryStack(inventory, entry.Content, entry.Count);
                    items.RemoveAt(i);
                    transferredStacks++;
                }

                request.PendingTakeAll = 0;
                request.PendingTakeSelected = 0;
            }
            else if (request.PendingTakeSelected != 0)
            {
                int selectedIndex = request.PendingSelectionChange != 0 ? request.SelectedItemIndex : state.SelectedItemIndex;
                if (selectedIndex >= 0 && selectedIndex < items.Length)
                {
                    var entry = items[selectedIndex];
                    if (entry.PlacedRefId == placedRefId && entry.Count > 0)
                    {
                        WorldJournalUtility.AppendContainerDelta(EntityManager, placedRefId, entry.Content, -entry.Count);
                        ContainerLootUtility.AddInventoryStack(inventory, entry.Content, entry.Count);
                        items.RemoveAt(selectedIndex);
                        transferredStacks = 1;
                    }
                }

                request.PendingTakeSelected = 0;
            }

            if (transferredStacks > 0)
            {
                TryQueueInteractionAudio(state.OpenTargetEntity, InteractionAudioKind.Container, "container");
                Debug.Log($"[VVardenfell][Container] transferred {transferredStacks} stack(s) from placedRef=0x{placedRefId:X8}.");
            }
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!EntityManager.HasComponent<AudioEmitterAuthoring>(target))
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no AudioEmitterAuthoring component; skipping interaction one-shot.");
                return;
            }

            var emitter = EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no primary interaction sound; skipping interaction one-shot.");
                return;
            }

            float3 position = ResolveAudioPosition(target);
            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            uint sequence = audioState.NextSequence + 1u;
            audioState.NextSequence = sequence;

            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = sequence,
                Sound = emitter.PrimarySound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)kind,
            });

            Debug.Log($"[VVardenfell][Audio] queued {label} interaction one-shot: seq={sequence}, placedRef=0x{placedRefId:X8}, pos=({position.x:F2}, {position.y:F2}, {position.z:F2}).");
        }

        float3 ResolveAudioPosition(Entity target)
        {
            if (EntityManager.HasComponent<LocalToWorld>(target))
                return EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (EntityManager.HasComponent<LocalTransform>(target))
                return EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }

        void WarnMissingInteractionSoundOnce(uint placedRefId, string label, string reason)
        {
            if (placedRefId == 0u || !_loggedMissingInteractionSounds.Add(placedRefId))
                return;

            Debug.Log($"[VVardenfell][Audio] {label} 0x{placedRefId:X8} {reason}");
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class ContainerWindowStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<ContainerWindowRequest>();
            RequireForUpdate<ContainerSessionItem>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<ContainerWindowRequest>().ValueRW;
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var contentDb = RuntimeContentDatabase.Active;

            ApplyRequests(ref state, ref request);
            state.Visible = shell.ContainerOpen;

            if (state.Visible == 0 || state.OpenPlacedRefId == 0u)
            {
                state.SelectedItemDetailsText = default;
                state.SelectedItemIndex = -1;
                return;
            }

            int selectedIndex = ValidateSelection(items, state.OpenPlacedRefId, state.SelectedItemIndex);
            if (selectedIndex < 0)
                selectedIndex = ContainerLootUtility.FindFirstItemIndex(items, state.OpenPlacedRefId);

            state.SelectedItemIndex = selectedIndex;
            state.SelectedItemDetailsText = ContainerLootUtility.ToFixedDetails(BuildSelectedItemDetails(contentDb, items, state.OpenPlacedRefId, selectedIndex));
        }

        static void ApplyRequests(ref ContainerWindowState state, ref ContainerWindowRequest request)
        {
            if (request.PendingRectUpdate != 0)
            {
                state.NormalizedX = Clamp01(request.NormalizedX);
                state.NormalizedY = Clamp01(request.NormalizedY);
                state.NormalizedWidth = ClampDimension(request.NormalizedWidth, state.NormalizedWidth);
                state.NormalizedHeight = ClampDimension(request.NormalizedHeight, state.NormalizedHeight);

                if (state.NormalizedX + state.NormalizedWidth > 1f)
                    state.NormalizedX = Math.Max(0f, 1f - state.NormalizedWidth);
                if (state.NormalizedY + state.NormalizedHeight > 1f)
                    state.NormalizedY = Math.Max(0f, 1f - state.NormalizedHeight);
            }

            if (request.PendingSelectionChange != 0)
                state.SelectedItemIndex = request.SelectedItemIndex;

            request.PendingRectUpdate = 0;
            request.PendingSelectionChange = 0;
        }

        static int ValidateSelection(DynamicBuffer<ContainerSessionItem> items, uint placedRefId, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= items.Length)
                return -1;

            var entry = items[selectedIndex];
            return entry.PlacedRefId == placedRefId && entry.Count > 0
                ? selectedIndex
                : -1;
        }

        static string BuildSelectedItemDetails(RuntimeContentDatabase contentDb, DynamicBuffer<ContainerSessionItem> items, uint placedRefId, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= items.Length)
                return "Container is empty.";

            var entry = items[selectedIndex];
            if (entry.PlacedRefId != placedRefId || entry.Count <= 0 || contentDb == null)
                return "Container is empty.";

            if (!InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                return "Container is empty.";

            return InventoryWindowStateSystem.BuildCarryableDetails(metadata, Math.Max(1, entry.Count));
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return Math.Clamp(value, 0f, 1f);
        }

        static float ClampDimension(float requested, float fallback)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0f)
                requested = fallback > 0f ? fallback : 0.1f;

            return Math.Clamp(requested, 0.1f, 1f);
        }
    }
}
