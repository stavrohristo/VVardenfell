using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Animation
{
    static class ActorPresentationEquipmentUtility
    {
        public static int ResolveRigidEquipmentAttachBone(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon)
            {
                if (equipment.Type == 9)
                {
                    int leftWeaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("weapon bone left"));
                    if (leftWeaponBone >= 0)
                        return leftWeaponBone;
                }

                int weaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("weapon bone"));
                return weaponBone >= 0
                    ? weaponBone
                    : ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("bip01 r hand"));
            }

            int shieldBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("shield bone"));
            return shieldBone >= 0
                ? shieldBone
                : ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("bip01 l forearm"));
        }

        static int ResolveAttachBoneIndex(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            FixedString64Bytes name)
        {
            if (name.IsEmpty)
                return -1;

            var skeleton = new ActorSkeleton
            {
                SkeletonIndex = skeletonIndex,
                BoneCount = ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex),
            };
            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                var boneName = ActorAnimationCatalogRuntimeUtility.ResolveBoneName(ref catalog, skeleton, i);
                if (FixedStringEqualsIgnoreCase(boneName, name))
                    return i;
            }

            return -1;
        }

        static bool FixedStringEqualsIgnoreCase(FixedString64Bytes a, FixedString64Bytes b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (ToAsciiLower(a[i]) != ToAsciiLower(b[i]))
                    return false;
            }

            return true;
        }

        static byte ToAsciiLower(byte value)
            => value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;
    }
}
