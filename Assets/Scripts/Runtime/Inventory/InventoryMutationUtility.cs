using Unity.Entities;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Inventory
{
    static class InventoryMutationUtility
    {
        const string Gold001 = "gold_001";

        public static bool TryAddPlayerItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, string itemId, int count, int playerLevel)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            return TryAddResolvedPlayerItem(ref contentBlob, inventory, itemId, count, playerLevel, 0u);
        }

        public static bool TryRemovePlayerItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, string itemId, int count)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveDirectCarryable(ref contentBlob, itemId, out var content))
                return false;

            RemovePlayerInventoryStack(inventory, content, count);
            return true;
        }

        public static bool TryAddActorItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<ActorInventoryItem> inventory, string itemId, int count, int playerLevel, uint resolutionSeed)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            return TryAddResolvedActorItem(ref contentBlob, inventory, itemId, count, playerLevel, resolutionSeed);
        }

        public static bool TryRemoveActorItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<ActorInventoryItem> inventory, string itemId, int count)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveDirectCarryable(ref contentBlob, itemId, out var content))
                return false;

            RemoveActorInventoryStack(inventory, content, count);
            return true;
        }

        static bool TryAddResolvedPlayerItem(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<PlayerInventoryItem> inventory,
            string itemId,
            int count,
            int playerLevel,
            uint resolutionSeed)
        {
            string normalizedId = NormalizeGoldId(itemId);
            ulong idHash = RuntimeContentStableHash.HashId(normalizedId);
            if (MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(ref contentBlob, idHash, out var content))
            {
                ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, content, count);
                return true;
            }

            if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, idHash, out var listHandle))
                return false;

            using var resolvedItems = new NativeList<MorrowindResolvedLeveledItem>(Allocator.Temp);
            MorrowindLeveledItemResolverUtility.ResolveIntoInventory(
                ref contentBlob,
                listHandle,
                playerLevel,
                MorrowindLeveledItemResolverUtility.BuildResolutionSeed(resolutionSeed, 0, 0),
                count,
                resolvedItems);
            for (int i = 0; i < resolvedItems.Length; i++)
            {
                var resolved = resolvedItems[i];
                ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, resolved.Content, resolved.Count);
            }

            return resolvedItems.Length > 0;
        }

        static bool TryAddResolvedActorItem(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorInventoryItem> inventory,
            string itemId,
            int count,
            int playerLevel,
            uint resolutionSeed)
        {
            string normalizedId = NormalizeGoldId(itemId);
            ulong idHash = RuntimeContentStableHash.HashId(normalizedId);
            if (MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(ref contentBlob, idHash, out var content))
            {
                AddActorInventoryStack(ref contentBlob, inventory, content, count);
                return true;
            }

            if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, idHash, out var listHandle))
                return false;

            using var resolvedItems = new NativeList<MorrowindResolvedLeveledItem>(Allocator.Temp);
            MorrowindLeveledItemResolverUtility.ResolveIntoInventory(
                ref contentBlob,
                listHandle,
                playerLevel,
                MorrowindLeveledItemResolverUtility.BuildResolutionSeed(resolutionSeed, 0, 0),
                count,
                resolvedItems);
            for (int i = 0; i < resolvedItems.Length; i++)
            {
                var resolved = resolvedItems[i];
                AddActorInventoryStack(ref contentBlob, inventory, resolved.Content, resolved.Count);
            }

            return resolvedItems.Length > 0;
        }

        static bool TryResolveDirectCarryable(ref RuntimeContentBlob contentBlob, string itemId, out ContentReference content)
            => ContainerLootUtility.TryResolveDirectCarryable(ref contentBlob, NormalizeGoldId(itemId), out content, out _);

        static string NormalizeGoldId(string itemId)
        {
            if (string.Equals(itemId, "gold_005", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_010", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_025", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_100", System.StringComparison.OrdinalIgnoreCase))
            {
                return Gold001;
            }

            return itemId;
        }

        static int NormalizeScriptCount(int count)
            => count < 0 ? (ushort)count : count;

        static void RemovePlayerInventoryStack(DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, int count)
        {
            int remaining = count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (entry.Content.Kind != content.Kind || entry.Content.HandleValue != content.HandleValue)
                    continue;

                if (entry.Count <= remaining)
                {
                    remaining -= Unity.Mathematics.math.max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }

                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }

        static void AddActorInventoryStack(ref RuntimeContentBlob contentBlob, DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            if (!content.IsValid || count <= 0)
                return;

            int condition = InventoryConditionUtility.ResolveInitialCondition(ref contentBlob, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Content.Kind != content.Kind
                    || inventory[i].Content.HandleValue != content.HandleValue
                    || !inventory[i].SoulId.IsEmpty
                    || inventory[i].Restocking != 0
                    || !InventoryConditionUtility.CanStackCondition(content, inventory[i].Condition, condition))
                {
                    continue;
                }

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new ActorInventoryItem
            {
                Content = content,
                Count = count,
                Condition = condition,
                AuthoredOrder = inventory.Length,
            });
        }

        static void RemoveActorInventoryStack(DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            int remaining = count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (entry.Content.Kind != content.Kind || entry.Content.HandleValue != content.HandleValue)
                    continue;

                if (entry.Count <= remaining)
                {
                    remaining -= Unity.Mathematics.math.max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }

                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }
    }
}
