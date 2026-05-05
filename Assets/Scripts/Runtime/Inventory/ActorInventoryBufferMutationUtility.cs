using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Inventory
{
    static class ActorInventoryBufferMutationUtility
    {
        public static int RemoveActorItems(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return 0;

            int remaining = count;
            RemoveUnequippedActorItems(inventory, equipment, content, soulId, soulActorHandleValue, ref remaining);
            if (remaining > 0)
            {
                UnequipContent(inventory, equipment, content);
                RemoveUnequippedActorItems(inventory, equipment, content, soulId, soulActorHandleValue, ref remaining);
            }

            return count - remaining;
        }

        public static void AddActorItems(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorInventoryItem> inventory,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            int count)
        {
            if (!content.IsValid || count <= 0)
                return;

            int condition = InventoryConditionUtility.ResolveInitialCondition(ref contentBlob, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (!Matches(entry.Content, content)
                    || !entry.SoulId.Equals(soulId)
                    || entry.SoulActorHandleValue != soulActorHandleValue
                    || !InventoryConditionUtility.CanStackCondition(content, entry.Condition, condition))
                {
                    continue;
                }

                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new ActorInventoryItem
            {
                Content = content,
                SoulId = soulId,
                SoulActorHandleValue = soulActorHandleValue,
                Count = count,
                Condition = condition,
                AuthoredOrder = inventory.Length,
            });
        }

        static void RemoveUnequippedActorItems(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            ref int remaining)
        {
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (!Matches(entry.Content, content)
                    || !entry.SoulId.Equals(soulId)
                    || entry.SoulActorHandleValue != soulActorHandleValue
                    || IsEquipped(equipment, i, content))
                {
                    continue;
                }

                int removed = math.min(remaining, entry.Count);
                remaining -= removed;
                RemoveActorCountAt(inventory, equipment, i, removed);
            }
        }

        static void RemoveActorCountAt(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            int index,
            int count)
        {
            var entry = inventory[index];
            if (count >= entry.Count)
            {
                inventory.RemoveAt(index);
                AdjustEquipmentAfterInventoryRemove(equipment, index);
                return;
            }

            entry.Count -= count;
            inventory[index] = entry;
        }

        static void UnequipContent<TInventory>(
            DynamicBuffer<TInventory> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content)
            where TInventory : unmanaged, IBufferElementData
        {
            if (!equipment.IsCreated)
                return;

            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if ((uint)slot.InventoryIndex >= (uint)inventory.Length || !Matches(slot.Content, content))
                    continue;

                equipment.RemoveAt(i);
            }
        }

        static bool IsEquipped(DynamicBuffer<ActorEquipmentSlot> equipment, int inventoryIndex, ContentReference content)
        {
            if (!equipment.IsCreated)
                return false;

            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex && Matches(slot.Content, content))
                    return true;
            }

            return false;
        }

        static void AdjustEquipmentAfterInventoryRemove(DynamicBuffer<ActorEquipmentSlot> equipment, int removedIndex)
        {
            if (!equipment.IsCreated)
                return;

            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == removedIndex)
                {
                    equipment.RemoveAt(i);
                    continue;
                }

                if (slot.InventoryIndex > removedIndex)
                {
                    slot.InventoryIndex--;
                    equipment[i] = slot;
                }
            }
        }

        static bool Matches(ContentReference left, ContentReference right)
            => left.Kind == right.Kind && left.HandleValue == right.HandleValue;
    }
}
