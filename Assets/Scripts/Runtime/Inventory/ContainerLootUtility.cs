using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Inventory
{
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

        internal static bool TryResolveDirectCarryable(
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

        internal static bool TryResolveLooseLeveledCarryable(
            RuntimeContentDatabase contentDb,
            ItemLeveledListDefHandle listHandle,
            uint placedRefId,
            out ContentReference content,
            out string diagnostic)
        {
            return TryResolveLeveledResult(
                contentDb,
                listHandle,
                BuildResolutionSeed(placedRefId, 0, 0),
                0,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                out content,
                out diagnostic);
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
}
