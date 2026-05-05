using System;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Inventory
{
    public static class ActorEquipmentConditionUtility
    {
        public static int ResolveInitialCondition(
            in ItemEquipmentDef equipment,
            int inventoryCount,
            int inventoryCondition,
            in ContentReference content)
        {
            if (equipment.Health <= 0)
                return 0;

            return InventoryConditionUtility.RequireEquippedSingleConditionableStack(
                inventoryCount,
                inventoryCondition,
                equipment,
                content);
        }

        public static int RequireEquippedCondition(
            in ActorEquipmentSlot slot,
            in ItemEquipmentDef equipment)
        {
            if (equipment.Health <= 0)
                return 0;
            if (slot.Condition <= 0)
                return equipment.Health;
            if (slot.Condition > equipment.Health)
                throw new InvalidOperationException($"[VVardenfell][Inventory] Equipped item {slot.Content.Kind}:{slot.Content.HandleValue} condition {slot.Condition} exceeds max health {equipment.Health}.");

            return slot.Condition;
        }

        public static void ApplyConditionDamage(
            ref ActorEquipmentSlot slot,
            in ItemEquipmentDef equipment,
            int conditionDamage)
        {
            if (equipment.Health <= 0 || conditionDamage <= 0)
                return;

            int condition = RequireEquippedCondition(slot, equipment);
            slot.Condition = Math.Max(0, condition - conditionDamage);
        }
    }
}
