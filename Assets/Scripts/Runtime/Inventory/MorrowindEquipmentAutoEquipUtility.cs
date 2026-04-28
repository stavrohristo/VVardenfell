using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Inventory
{
    public static class MorrowindEquipmentAutoEquipUtility
    {
        const int SlotCapacity = 32;

        public static void SelectInitialEquipment(
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            var source = new DynamicBufferInventorySource { Buffer = inventory };
            var sink = new DynamicBufferEquipmentSink { Buffer = equipment };
            SelectInitialEquipment(contentDb, actor, ref source, ref sink);
        }

        public static void SelectInitialEquipment(
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            NativeArray<ActorInventoryItem> inventory,
            NativeList<ActorEquipmentSlot> equipment)
        {
            var source = new NativeArrayInventorySource { Items = inventory };
            var sink = new NativeListEquipmentSink { Items = equipment };
            SelectInitialEquipment(contentDb, actor, ref source, ref sink);
        }

        static void SelectInitialEquipment<TInventory, TEquipment>(
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            ref TInventory inventory,
            ref TEquipment equipment)
            where TInventory : struct, IActorInventorySource
            where TEquipment : struct, IActorEquipmentSink
        {
            int inventoryLength = inventory.Length;
            if (contentDb == null || inventoryLength == 0)
                return;

            Span<int> bestInventoryIndices = stackalloc int[SlotCapacity];
            for (int i = 0; i < SlotCapacity; i++)
                bestInventoryIndices[i] = -1;

            bool isBeastNpc = actor.Kind == ActorDefKind.Npc
                               && ActorEquipmentRuntimeUtility.IsBeastRace(contentDb, actor.RaceId);
            for (int i = 0; i < inventoryLength; i++)
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
                    && existingIndex < inventoryLength
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
                if (inventoryIndex < 0 || inventoryIndex >= inventoryLength)
                    continue;

                var inventoryItem = inventory[inventoryIndex];
                if (!TryGetEquipment(contentDb, inventoryItem, out var itemEquipment))
                    continue;

                equipment.Add(new ActorEquipmentSlot
                {
                    Slot = itemEquipment.Slot,
                    Content = inventoryItem.Content,
                    InventoryIndex = inventoryIndex,
                    VisualMode = ActorEquipmentRuntimeUtility.ResolveEquipmentVisualMode(itemEquipment),
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

        interface IActorInventorySource
        {
            int Length { get; }
            ActorInventoryItem this[int index] { get; }
        }

        interface IActorEquipmentSink
        {
            void Add(ActorEquipmentSlot slot);
        }

        struct DynamicBufferInventorySource : IActorInventorySource
        {
            public DynamicBuffer<ActorInventoryItem> Buffer;
            public int Length => Buffer.Length;
            public ActorInventoryItem this[int index] => Buffer[index];
        }

        struct NativeArrayInventorySource : IActorInventorySource
        {
            public NativeArray<ActorInventoryItem> Items;
            public int Length => Items.Length;
            public ActorInventoryItem this[int index] => Items[index];
        }

        struct DynamicBufferEquipmentSink : IActorEquipmentSink
        {
            public DynamicBuffer<ActorEquipmentSlot> Buffer;
            public void Add(ActorEquipmentSlot slot) => Buffer.Add(slot);
        }

        struct NativeListEquipmentSink : IActorEquipmentSink
        {
            public NativeList<ActorEquipmentSlot> Items;
            public void Add(ActorEquipmentSlot slot) => Items.Add(slot);
        }
    }
}
