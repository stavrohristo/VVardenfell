using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Animation
{
    static class ActorPresentationEquipmentUtility
    {
        public static void QueueEnsurePresentationEquipmentDirty(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity actor,
            bool enabled)
        {
            if (entityManager.HasComponent<ActorPresentationEquipmentDirty>(actor))
            {
                ecb.SetComponentEnabled<ActorPresentationEquipmentDirty>(actor, enabled);
                return;
            }

            if (!enabled)
                return;

            ecb.AddComponent<ActorPresentationEquipmentDirty>(actor);
        }

        public static ulong BuildEquipmentSignature(DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                for (int i = 0; i < equipment.Length; i++)
                {
                    var slot = equipment[i];
                    hash = (hash ^ (byte)slot.Slot) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.Content.Kind) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.Content.HandleValue) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.InventoryIndex) * 1099511628211ul;
                    hash = (hash ^ slot.VisualMode) * 1099511628211ul;
                }

                return hash;
            }
        }

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
