using System;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Inventory
{
    public static class InventoryConditionUtility
    {
        public static int ResolveInitialCondition(ref RuntimeContentBlob blob, in ContentReference content)
        {
            if (!content.IsValid || content.Kind != ContentReferenceKind.Item)
                return 0;

            var handle = new ItemDefHandle { Value = content.HandleValue };
            return RuntimeContentBlobUtility.TryGetItemEquipment(ref blob, handle, out var equipment) && equipment.Health > 0
                ? equipment.Health
                : 0;
        }

        public static bool CanStackCondition(in ContentReference content, int lhsCondition, int rhsCondition)
        {
            if (!content.IsValid || content.Kind != ContentReferenceKind.Item)
                return true;

            return lhsCondition == rhsCondition;
        }

        public static int RequireEquippedSingleConditionableStack(int count, int condition, in ItemEquipmentDef equipment, in ContentReference content)
        {
            if (equipment.Health <= 0)
                return 0;
            if (count != 1)
                throw new InvalidOperationException($"[VVardenfell][Inventory] Equipped conditionable item {content.Kind}:{content.HandleValue} has stack count {count}; per-instance condition requires a single item stack.");
            if (condition <= 0)
                return equipment.Health;
            if (condition > equipment.Health)
                throw new InvalidOperationException($"[VVardenfell][Inventory] Equipped item {content.Kind}:{content.HandleValue} condition {condition} exceeds max health {equipment.Health}.");

            return condition;
        }
    }
}
