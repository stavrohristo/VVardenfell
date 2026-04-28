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
                    int leftWeaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, ActorSkeletonNameHash.WeaponBoneLeft);
                    if (leftWeaponBone >= 0)
                        return leftWeaponBone;
                }

                int weaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, ActorSkeletonNameHash.WeaponBone);
                return weaponBone >= 0
                    ? weaponBone
                    : ResolveAttachBoneIndex(ref catalog, skeletonIndex, ActorSkeletonNameHash.Bip01RHand);
            }

            int shieldBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, ActorSkeletonNameHash.ShieldBone);
            return shieldBone >= 0
                ? shieldBone
                : ResolveAttachBoneIndex(ref catalog, skeletonIndex, ActorSkeletonNameHash.Bip01LForearm);
        }

        static int ResolveAttachBoneIndex(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            ulong nameHash)
        {
            var skeleton = new ActorSkeleton
            {
                SkeletonIndex = skeletonIndex,
                BoneCount = ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex),
            };
            return ActorSkeletonUtility.ResolveBoneIndex(ref catalog, skeleton, nameHash);
        }
    }
}
