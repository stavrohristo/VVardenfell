using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorWeaponAnimationUtility
    {
        public const int NoWeaponType = -1;
        public const int UnsupportedWeaponType = -2;

        public static bool IsSupportedMelee(int weaponType)
            => weaponType == NoWeaponType || (weaponType >= 0 && weaponType <= 8);

        public static bool TryResolveGroupHashes(int weaponType, out ulong primaryHash, out ulong fallbackHash)
        {
            primaryHash = 0UL;
            fallbackHash = 0UL;
            if (!TryResolveGroupNames(weaponType, out var primary, out var fallback))
                return false;

            primaryHash = ActorAnimationGroupHash.Hash(primary);
            fallbackHash = fallback.IsEmpty ? 0UL : ActorAnimationGroupHash.Hash(fallback);
            return true;
        }

        static bool TryResolveGroupNames(
            int weaponType,
            out FixedString64Bytes primary,
            out FixedString64Bytes fallback)
        {
            primary = default;
            fallback = default;
            switch (weaponType)
            {
                case NoWeaponType:
                    primary = Fixed64("handtohand");
                    return true;
                case 0:
                    primary = Fixed64("shortbladeonehand");
                    fallback = Fixed64("weapononehand");
                    return true;
                case 1:
                    primary = Fixed64("weapononehand");
                    return true;
                case 2:
                    primary = Fixed64("weapontwohand");
                    return true;
                case 3:
                case 7:
                    primary = Fixed64("bluntonehand");
                    fallback = Fixed64("weapononehand");
                    return true;
                case 4:
                case 8:
                    primary = Fixed64("blunttwohand");
                    fallback = Fixed64("weapontwohand");
                    return true;
                case 5:
                case 6:
                    primary = Fixed64("weapontwowide");
                    fallback = Fixed64("weapontwohand");
                    return true;
                default:
                    return false;
            }
        }

        public static int ResolveEquippedWeaponType(
            ref RuntimeContentBlob blob,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            out ContentReference content)
        {
            content = default;
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Slot != ItemEquipmentSlot.Weapon || slot.Content.Kind != ContentReferenceKind.Item)
                    continue;

                content = slot.Content;
                var handle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref blob, handle, out var itemEquipment)
                    || itemEquipment.Kind != ItemEquipmentKind.Weapon)
                {
                    return UnsupportedWeaponType;
                }

                return itemEquipment.Type;
            }

            return NoWeaponType;
        }

        static FixedString64Bytes Fixed64(string value) => new(value);
    }
}
