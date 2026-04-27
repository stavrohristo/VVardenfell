using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Inventory
{
    public static class MorrowindEquipmentAutoEquipUtility
    {
        const int SlotCapacity = 32;
        const uint RaceFlagBeast = 0x02;

        public static void SelectInitialEquipment(
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (contentDb == null || inventory.Length == 0)
                return;

            var bestInventoryIndices = new int[SlotCapacity];
            for (int i = 0; i < SlotCapacity; i++)
                bestInventoryIndices[i] = -1;

            bool isBeastNpc = actor.Kind == ActorDefKind.Npc && IsBeastRace(contentDb, actor.RaceId);
            for (int i = 0; i < inventory.Length; i++)
            {
                var inventoryItem = inventory[i];
                if (inventoryItem.Count <= 0 || inventoryItem.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = inventoryItem.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;
                if (!CanAutoEquip(contentDb, itemEquipment, isBeastNpc))
                    continue;

                int selectionSlot = (int)ResolveSelectionSlot(itemEquipment);
                if ((uint)selectionSlot >= SlotCapacity || selectionSlot == (int)ItemEquipmentSlot.None)
                    continue;

                int existingIndex = bestInventoryIndices[selectionSlot];
                if (existingIndex >= 0
                    && existingIndex < inventory.Length
                    && TryGetEquipment(contentDb, inventory[existingIndex], out var existingEquipment)
                    && !ShouldReplace(existingEquipment, itemEquipment))
                {
                    continue;
                }

                bestInventoryIndices[selectionSlot] = i;
            }

            for (int slot = 0; slot < SlotCapacity; slot++)
            {
                int inventoryIndex = bestInventoryIndices[slot];
                if (inventoryIndex < 0 || inventoryIndex >= inventory.Length)
                    continue;

                var inventoryItem = inventory[inventoryIndex];
                if (!TryGetEquipment(contentDb, inventoryItem, out var itemEquipment))
                    continue;

                equipment.Add(new ActorEquipmentSlot
                {
                    Slot = itemEquipment.Slot,
                    Content = inventoryItem.Content,
                    InventoryIndex = inventoryIndex,
                    VisualMode = ResolveEquipmentVisualMode(itemEquipment),
                });
            }
        }

        static bool TryGetEquipment(
            RuntimeContentDatabase contentDb,
            in ActorInventoryItem inventoryItem,
            out ItemEquipmentDef equipment)
        {
            equipment = default;
            if (inventoryItem.Content.Kind != ContentReferenceKind.Item)
                return false;

            var itemHandle = new ItemDefHandle { Value = inventoryItem.Content.HandleValue };
            return contentDb.TryGetItemEquipment(itemHandle, out equipment);
        }

        static bool CanAutoEquip(RuntimeContentDatabase contentDb, in ItemEquipmentDef equipment, bool isBeastNpc)
        {
            if (equipment.Slot == ItemEquipmentSlot.None)
                return false;
            if (equipment.Kind == ItemEquipmentKind.Armor && equipment.Health == 0)
                return false;
            if (isBeastNpc && HasBeastForbiddenPart(contentDb, equipment))
                return false;

            return equipment.Kind == ItemEquipmentKind.Weapon
                || equipment.Kind == ItemEquipmentKind.Armor
                || equipment.Kind == ItemEquipmentKind.Clothing;
        }

        static bool HasBeastForbiddenPart(RuntimeContentDatabase contentDb, in ItemEquipmentDef equipment)
        {
            ReadOnlySpan<ItemEquipmentBodyPartDef> parts = contentDb.GetItemEquipmentBodyParts(equipment);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Part;
                if (part == ItemEquipmentPartReference.Head
                    || part == ItemEquipmentPartReference.LeftFoot
                    || part == ItemEquipmentPartReference.RightFoot)
                {
                    return true;
                }
            }

            return false;
        }

        static ItemEquipmentSlot ResolveSelectionSlot(in ItemEquipmentDef equipment)
        {
            // OpenMW maps clothing shoes into the same equipment slot as armor boots.
            return equipment.Slot == ItemEquipmentSlot.Shoes
                ? ItemEquipmentSlot.Boots
                : equipment.Slot;
        }

        static bool ShouldReplace(in ItemEquipmentDef existing, in ItemEquipmentDef candidate)
        {
            if (candidate.Kind == ItemEquipmentKind.Weapon)
                return candidate.DamageMax > existing.DamageMax;

            if (candidate.Kind == ItemEquipmentKind.Clothing)
                return existing.Kind == ItemEquipmentKind.Clothing && candidate.Value > existing.Value;

            if (candidate.Kind != ItemEquipmentKind.Armor)
                return false;

            if (existing.Kind != ItemEquipmentKind.Armor)
                return true;

            if (existing.Type == candidate.Type)
                return candidate.Armor > existing.Armor;

            return existing.Type >= candidate.Type;
        }

        static bool IsBeastRace(RuntimeContentDatabase contentDb, string raceId)
        {
            if (string.IsNullOrWhiteSpace(raceId) || !contentDb.TryGetRaceHandle(raceId, out var raceHandle))
                return false;

            ref readonly var race = ref contentDb.GetRace(raceHandle);
            return (race.Flags & RaceFlagBeast) != 0;
        }

        static byte ResolveEquipmentVisualMode(in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon || equipment.Slot == ItemEquipmentSlot.Shield)
                return 2;
            if (equipment.Kind == ItemEquipmentKind.Armor || equipment.Kind == ItemEquipmentKind.Clothing)
                return 1;
            return 0;
        }
    }
}
