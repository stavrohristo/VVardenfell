using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Inventory
{
    public static class ActorEquipmentRuntimeUtility
    {
        static readonly ItemEquipmentSlot[] s_NpcEquipmentVisualSlotOrder =
        {
            ItemEquipmentSlot.Robe,
            ItemEquipmentSlot.Skirt,
            ItemEquipmentSlot.Helmet,
            ItemEquipmentSlot.Cuirass,
            ItemEquipmentSlot.Greaves,
            ItemEquipmentSlot.LeftPauldron,
            ItemEquipmentSlot.RightPauldron,
            ItemEquipmentSlot.Boots,
            ItemEquipmentSlot.Shoes,
            ItemEquipmentSlot.LeftHand,
            ItemEquipmentSlot.RightHand,
            ItemEquipmentSlot.Shirt,
            ItemEquipmentSlot.Pants,
            ItemEquipmentSlot.Shield,
        };

        public static ReadOnlySpan<ItemEquipmentSlot> NpcEquipmentVisualSlotOrder => s_NpcEquipmentVisualSlotOrder;

        public static bool IsBeastRace(RuntimeContentDatabase contentDb, string raceId)
        {
            if (contentDb == null || string.IsNullOrWhiteSpace(raceId))
                return false;

            if (!contentDb.TryGetRaceHandle(raceId, out var raceHandle))
                return false;

            ref readonly var race = ref contentDb.GetRace(raceHandle);
            return ActorVisualContentRules.IsBeastRaceFlags(race.Flags);
        }

        public static bool TryGetEquipmentInSlot(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ItemEquipmentSlot target,
            out ActorEquipmentSlot result)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Slot == target)
                {
                    result = slot;
                    return true;
                }
            }

            result = default;
            return false;
        }

        public static byte ResolveEquipmentVisualMode(in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon || equipment.Slot == ItemEquipmentSlot.Shield)
                return 2;
            if (equipment.Kind == ItemEquipmentKind.Armor || equipment.Kind == ItemEquipmentKind.Clothing)
                return 1;
            return 0;
        }
    }
}
