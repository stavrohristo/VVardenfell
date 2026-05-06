using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorWeaponAnimationUtility
    {
        public const int NoWeaponType = -1;
        public const int UnsupportedWeaponType = -2;

        public const ulong HandToHandGroupHash = 7030538165725426052UL;
        public const ulong ShortBladeOneHandGroupHash = 15575628719417304706UL;
        public const ulong WeaponOneHandGroupHash = 5493298962411162082UL;
        public const ulong WeaponTwoHandGroupHash = 573993751260719840UL;
        public const ulong BluntOneHandGroupHash = 3424523255587147629UL;
        public const ulong BluntTwoHandGroupHash = 12170368887997081879UL;
        public const ulong WeaponTwoWideGroupHash = 9222977795105608650UL;

        public const ulong EquipStartMarkerHash = 14901750540547209035UL;
        public const ulong EquipStopMarkerHash = 13045998434533941849UL;
        public const ulong UnequipStartMarkerHash = 9503042251966586046UL;
        public const ulong UnequipStopMarkerHash = 2060432748364639942UL;

        public const ulong SlashStartMarkerHash = 7231925739123150094UL;
        public const ulong SlashMinAttackMarkerHash = 5868548225261151386UL;
        public const ulong SlashMaxAttackMarkerHash = 594954991474917052UL;
        public const ulong SlashMinHitMarkerHash = 10189172638827892575UL;
        public const ulong SlashHitMarkerHash = 18402022229661806527UL;
        public const ulong ThrustStartMarkerHash = 5198053705954289387UL;
        public const ulong ThrustMinAttackMarkerHash = 1365717858775309409UL;
        public const ulong ThrustMaxAttackMarkerHash = 3376354111901695887UL;
        public const ulong ThrustMinHitMarkerHash = 7392901142354005470UL;
        public const ulong ThrustHitMarkerHash = 227564622035833678UL;
        public const ulong ChopStartMarkerHash = 4099004214355158201UL;
        public const ulong ChopMinAttackMarkerHash = 10989288825456834207UL;
        public const ulong ChopMaxAttackMarkerHash = 14121736599633494673UL;
        public const ulong ChopMinHitMarkerHash = 15895884148292756392UL;
        public const ulong ChopHitMarkerHash = 5372685552272249692UL;

        public const ulong SlashSmallFollowStartMarkerHash = 1772388003405704430UL;
        public const ulong SlashSmallFollowStopMarkerHash = 14435902076642826422UL;
        public const ulong SlashMediumFollowStartMarkerHash = 10591663546424397654UL;
        public const ulong SlashMediumFollowStopMarkerHash = 3602059486138301310UL;
        public const ulong SlashLargeFollowStartMarkerHash = 5548738810429982994UL;
        public const ulong SlashLargeFollowStopMarkerHash = 17243243023632413394UL;
        public const ulong ThrustSmallFollowStartMarkerHash = 6107804625959361773UL;
        public const ulong ThrustSmallFollowStopMarkerHash = 948917770771375871UL;
        public const ulong ThrustMediumFollowStartMarkerHash = 352559034484430011UL;
        public const ulong ThrustMediumFollowStopMarkerHash = 18368322758051804809UL;
        public const ulong ThrustLargeFollowStartMarkerHash = 6617387054284127445UL;
        public const ulong ThrustLargeFollowStopMarkerHash = 2017967695613217095UL;
        public const ulong ChopSmallFollowStartMarkerHash = 6283358220916616371UL;
        public const ulong ChopSmallFollowStopMarkerHash = 12381689488218200353UL;
        public const ulong ChopMediumFollowStartMarkerHash = 3335207027008547817UL;
        public const ulong ChopMediumFollowStopMarkerHash = 7312004823437750883UL;
        public const ulong ChopLargeFollowStartMarkerHash = 10350352174589209415UL;
        public const ulong ChopLargeFollowStopMarkerHash = 9571362400354250381UL;

        public static bool IsSupportedMelee(int weaponType)
            => weaponType == NoWeaponType || (weaponType >= 0 && weaponType <= 8);

        public static bool TryResolveGroupHashes(int weaponType, out ulong primaryHash, out ulong fallbackHash)
        {
            primaryHash = 0UL;
            fallbackHash = 0UL;
            switch (weaponType)
            {
                case NoWeaponType:
                    primaryHash = HandToHandGroupHash;
                    return true;
                case 0:
                    primaryHash = ShortBladeOneHandGroupHash;
                    fallbackHash = WeaponOneHandGroupHash;
                    return true;
                case 1:
                    primaryHash = WeaponOneHandGroupHash;
                    return true;
                case 2:
                    primaryHash = WeaponTwoHandGroupHash;
                    return true;
                case 3:
                case 7:
                    primaryHash = BluntOneHandGroupHash;
                    fallbackHash = WeaponOneHandGroupHash;
                    return true;
                case 4:
                case 8:
                    primaryHash = BluntTwoHandGroupHash;
                    fallbackHash = WeaponTwoHandGroupHash;
                    return true;
                case 5:
                case 6:
                    primaryHash = WeaponTwoWideGroupHash;
                    fallbackHash = WeaponTwoHandGroupHash;
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
    }
}
