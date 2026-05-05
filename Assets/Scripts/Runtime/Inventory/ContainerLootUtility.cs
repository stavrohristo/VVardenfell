using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Inventory
{
    static class ContainerLootUtility
    {
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
            int playerLevel)
        {
            if (placedRefId == 0u || !definition.IsValid)
                return;

            ref BlobArray<RuntimeContainerItemDefBlob> authoredItems = ref RuntimeContentBlobUtility.GetContainerItems(ref contentBlob, definition, out int firstItemIndex, out int itemCount);
            var resolvedItems = new NativeList<MorrowindResolvedLeveledItem>(Allocator.Temp);
            try
            {
                for (int i = 0; i < itemCount; i++)
                {
                    ref RuntimeContainerItemDefBlob authored = ref authoredItems[firstItemIndex + i];
                    if (authored.Count == 0)
                        continue;
                    if (authored.ItemIdHash == 0UL)
                        throw new System.InvalidOperationException($"[VVardenfell][Container] Container {definition.Value} has an authored item with no id at offset {i}.");

                    if (MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(ref contentBlob, authored.ItemIdHash, out var directContent))
                    {
                        if (authored.Count < 0)
                            throw new System.InvalidOperationException($"[VVardenfell][Container] Container {definition.Value} has negative count {authored.Count} for direct item hash {authored.ItemIdHash}.");
                        AddOrIncrementContainerStack(items, placedRefId, directContent, authored.Count);
                        continue;
                    }

                    if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, authored.ItemIdHash, out ItemLeveledListDefHandle listHandle))
                        throw new System.InvalidOperationException($"[VVardenfell][Container] Container {definition.Value} references unresolved authored item hash {authored.ItemIdHash}.");

                    resolvedItems.Clear();
                    MorrowindLeveledItemResolverUtility.ResolveIntoInventory(
                        ref contentBlob,
                        listHandle,
                        playerLevel,
                        MorrowindLeveledItemResolverUtility.BuildResolutionSeed(placedRefId, i, 0),
                        authored.Count,
                        resolvedItems);
                    for (int resolvedIndex = 0; resolvedIndex < resolvedItems.Length; resolvedIndex++)
                    {
                        var resolved = resolvedItems[resolvedIndex];
                        AddOrIncrementContainerStack(items, placedRefId, resolved.Content, resolved.Count);
                    }
                }
            }
            finally
            {
                if (resolvedItems.IsCreated)
                    resolvedItems.Dispose();
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
            return MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(
                ref contentBlob,
                RuntimeContentStableHash.HashId(itemId),
                out content);
        }
    }
}
