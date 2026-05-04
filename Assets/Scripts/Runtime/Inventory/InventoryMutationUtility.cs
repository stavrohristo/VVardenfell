using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Inventory
{
    static class InventoryMutationUtility
    {
        const string Gold001 = "gold_001";

        public static bool TryAddPlayerItem(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, string itemId, int count)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveAddContent(contentDb, itemId, 0u, out var content))
                return false;

            ContainerLootUtility.AddInventoryStack(inventory, content, count);
            return true;
        }

        public static bool TryRemovePlayerItem(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, string itemId, int count)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveDirectCarryable(contentDb, itemId, out var content))
                return false;

            RemovePlayerInventoryStack(inventory, content, count);
            return true;
        }

        public static bool TryAddActorItem(RuntimeContentDatabase contentDb, DynamicBuffer<ActorInventoryItem> inventory, string itemId, int count, uint resolutionSeed)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveAddContent(contentDb, itemId, resolutionSeed, out var content))
                return false;

            AddActorInventoryStack(inventory, content, count);
            return true;
        }

        public static bool TryRemoveActorItem(RuntimeContentDatabase contentDb, DynamicBuffer<ActorInventoryItem> inventory, string itemId, int count)
        {
            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (!TryResolveDirectCarryable(contentDb, itemId, out var content))
                return false;

            RemoveActorInventoryStack(inventory, content, count);
            return true;
        }

        static bool TryResolveAddContent(RuntimeContentDatabase contentDb, string itemId, uint resolutionSeed, out ContentReference content)
        {
            if (TryResolveDirectCarryable(contentDb, itemId, out content))
                return true;

            string normalizedId = NormalizeGoldId(itemId);
            if (contentDb != null
                && contentDb.TryGetItemLeveledListHandle(normalizedId, out var listHandle)
                && ContainerLootUtility.TryResolveLooseLeveledCarryable(contentDb, listHandle, resolutionSeed, out content, out _))
            {
                return content.IsValid;
            }

            content = default;
            return false;
        }

        static bool TryResolveDirectCarryable(RuntimeContentDatabase contentDb, string itemId, out ContentReference content)
            => ContainerLootUtility.TryResolveDirectCarryable(contentDb, NormalizeGoldId(itemId), out content, out _);

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

        static void AddActorInventoryStack(DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            if (!content.IsValid || count <= 0)
                return;

            int condition = InventoryConditionUtility.ResolveInitialCondition(RuntimeContentDatabase.Active, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Content.Kind != content.Kind
                    || inventory[i].Content.HandleValue != content.HandleValue
                    || !inventory[i].SoulId.IsEmpty
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
