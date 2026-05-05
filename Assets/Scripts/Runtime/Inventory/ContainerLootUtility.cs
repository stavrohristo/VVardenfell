using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

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
            AddOrIncrementContainerStack(items, placedRefId, content, default, 0, count);
        }

        public static void AddOrIncrementContainerStack(
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].PlacedRefId != placedRefId
                    || items[i].Content.Kind != content.Kind
                    || items[i].Content.HandleValue != content.HandleValue
                    || !items[i].SoulId.Equals(soulId))
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
                SoulId = soulId,
                SoulActorHandleValue = soulActorHandleValue,
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
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<PlayerInventoryItem> inventory,
            ContentReference content,
            int count)
        {
            return AddInventoryStack(ref contentBlob, inventory, content, default, 0, count);
        }

        public static int AddInventoryStack(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<PlayerInventoryItem> inventory,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return 0;

            int condition = InventoryConditionUtility.ResolveInitialCondition(ref contentBlob, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Content.Kind != content.Kind
                    || inventory[i].Content.HandleValue != content.HandleValue
                    || !inventory[i].SoulId.Equals(soulId)
                    || !InventoryConditionUtility.CanStackCondition(content, inventory[i].Condition, condition))
                {
                    continue;
                }

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return entry.Count;
            }

            inventory.Add(new PlayerInventoryItem
            {
                Content = content,
                SoulId = soulId,
                SoulActorHandleValue = soulActorHandleValue,
                Count = count,
                Condition = condition,
            });
            return count;
        }

        public static string ResolveContainerTitle(ref RuntimeContentBlob contentBlob, ContainerDefHandle definition)
        {
            if (!definition.IsValid)
                return "Container";

            ref RuntimeBaseDefBlob container = ref RuntimeContentBlobUtility.Get(ref contentBlob, definition);
            string name = container.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            string id = container.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
            return "Container";
        }

        public static void MaterializeContainerContents(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition,
            HashSet<string> diagnostics)
        {
            if (placedRefId == 0u || !definition.IsValid)
                return;

            ref BlobArray<RuntimeContainerItemDefBlob> authoredItems = ref RuntimeContentBlobUtility.GetContainerItems(ref contentBlob, definition, out int firstItemIndex, out int itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                ref RuntimeContainerItemDefBlob authored = ref authoredItems[firstItemIndex + i];
                string itemId = authored.ItemId.ToString();
                if (authored.Count <= 0 || string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (TryResolveDirectCarryable(ref contentBlob, itemId, out var directContent, out string directDiagnostic))
                {
                    AddOrIncrementContainerStack(items, placedRefId, directContent, authored.Count);
                    continue;
                }

                if (!string.IsNullOrEmpty(directDiagnostic))
                {
                    diagnostics?.Add(directDiagnostic);
                    continue;
                }

                if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(itemId), out ItemLeveledListDefHandle listHandle))
                {
                    diagnostics?.Add($"missing authored target '{itemId}'");
                    continue;
                }

                ResolveLeveledListIntoContainer(ref contentBlob, listHandle, items, placedRefId, authored.Count, i, diagnostics);
            }
        }

        static void ResolveLeveledListIntoContainer(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            int authoredCount,
            int authoredEntryIndex,
            HashSet<string> diagnostics)
        {
            ref RuntimeItemLeveledListDefBlob list = ref RuntimeContentBlobUtility.Get(ref contentBlob, listHandle);
            bool resolveEach = (list.Flags & ItemLeveledEachFlag) != 0;

            if (!resolveEach)
            {
                if (TryResolveLeveledResult(ref contentBlob, listHandle, BuildResolutionSeed(placedRefId, authoredEntryIndex, 0), 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out ContentReference content, out string diagnostic)
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
                if (TryResolveLeveledResult(ref contentBlob, listHandle, BuildResolutionSeed(placedRefId, authoredEntryIndex, iteration), 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out ContentReference content, out string diagnostic)
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
            ref RuntimeContentBlob contentBlob,
            string itemId,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(itemId), out ContentReference resolved))
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
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            uint placedRefId,
            out ContentReference content,
            out string diagnostic)
        {
            return TryResolveLeveledResult(
                ref contentBlob,
                listHandle,
                BuildResolutionSeed(placedRefId, 0, 0),
                0,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                out content,
                out diagnostic);
        }

        static bool TryResolveLeveledResult(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            uint seed,
            int depth,
            HashSet<string> visitedLists,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (!listHandle.IsValid)
                return false;

            if (depth >= MaxLeveledResolutionDepth)
            {
                diagnostic = $"item leveled-list recursion cap reached at depth {MaxLeveledResolutionDepth}";
                return false;
            }

            ref RuntimeItemLeveledListDefBlob list = ref RuntimeContentBlobUtility.Get(ref contentBlob, listHandle);
            string listId = list.Id.ToString();
            string normalizedId = ContentId.NormalizeId(listId);
            if (!visitedLists.Add(normalizedId))
            {
                diagnostic = $"item leveled-list cycle detected at '{listId}'";
                return false;
            }

            try
            {
                if (RollPercent(seed) < list.ChanceNone)
                    return false;

                ref BlobArray<RuntimeItemLeveledListEntryDefBlob> entries = ref RuntimeContentBlobUtility.GetItemLeveledListEntries(ref contentBlob, listHandle, out int firstEntryIndex, out int entryCount);
                if (entryCount == 0)
                    return false;

                bool allLevels = (list.Flags & ItemLeveledAllLevelsFlag) != 0;
                int highestEligibleLevel = 0;
                bool hasEligible = false;
                for (int i = 0; i < entryCount; i++)
                {
                    int level = entries[firstEntryIndex + i].Level;
                    if (level > highestEligibleLevel && level <= FixedLeveledLootPlayerLevel)
                    {
                        highestEligibleLevel = level;
                        hasEligible = true;
                    }
                }

                if (!hasEligible)
                    return false;

                var candidateIds = new List<string>(entryCount);
                for (int i = 0; i < entryCount; i++)
                {
                    ref RuntimeItemLeveledListEntryDefBlob entry = ref entries[firstEntryIndex + i];
                    int level = entry.Level;
                    if (level > FixedLeveledLootPlayerLevel)
                        continue;

                    if (allLevels || level == highestEligibleLevel)
                        candidateIds.Add(entry.ItemId.ToString());
                }

                if (candidateIds.Count == 0)
                    return false;

                int candidateIndex = NextRandomIndex(ref seed, candidateIds.Count);
                string resolvedId = candidateIds[candidateIndex];
                if (TryResolveDirectCarryable(ref contentBlob, resolvedId, out content, out string directDiagnostic))
                    return true;

                if (!string.IsNullOrEmpty(directDiagnostic))
                {
                    diagnostic = directDiagnostic;
                    return false;
                }

                if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(resolvedId), out ItemLeveledListDefHandle nestedHandle))
                {
                    diagnostic = $"missing leveled-list target '{resolvedId}' referenced by '{listId}'";
                    return false;
                }

                seed = MixSeed(seed, (uint)candidateIndex + 1u);
                return TryResolveLeveledResult(ref contentBlob, nestedHandle, seed, depth + 1, visitedLists, out content, out diagnostic);
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
