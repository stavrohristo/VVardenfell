using Unity.Collections;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorAnimationCatalogRuntimeUtility
    {
        public static bool TryGetSkeletonBlob(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            out ActorSkeletonBlob skeleton)
        {
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
            {
                skeleton = default;
                return false;
            }

            skeleton = catalog.Skeletons[skeletonIndex];
            return true;
        }

        public static bool TryGetBoneBlob(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            int localBoneIndex,
            out ActorSkeletonBoneBlob bone)
        {
            bone = default;
            if (!TryGetSkeletonBlob(ref catalog, skeleton.SkeletonIndex, out var skeletonBlob))
                return false;

            if ((uint)localBoneIndex >= (uint)skeletonBlob.BoneCount)
                return false;

            int sourceIndex = skeletonBlob.FirstBoneIndex + localBoneIndex;
            if ((uint)sourceIndex >= (uint)catalog.Bones.Length)
                return false;

            bone = catalog.Bones[sourceIndex];
            return true;
        }

        public static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
            => TryGetSkeletonBlob(ref catalog, skeletonIndex, out var skeleton) ? skeleton.BoneCount : 0;

        public static int ResolveFirstBoneIndex(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
            => TryGetSkeletonBlob(ref catalog, skeletonIndex, out var skeleton) ? skeleton.FirstBoneIndex : -1;

        public static FixedString64Bytes ResolveBoneName(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            int localBoneIndex)
            => TryGetBoneBlob(ref catalog, skeleton, localBoneIndex, out var bone) ? bone.Name : default;

        public static int ResolveParentIndex(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            int localBoneIndex)
            => TryGetBoneBlob(ref catalog, skeleton, localBoneIndex, out var bone) ? bone.ParentIndex : -1;

        public static float3 RuntimeBindPosition(in ActorSkeletonBoneBlob bone)
            => ActorAnimationSpaceConversion.SourceTranslationToUnity(bone.BindPosition);

        public static quaternion RuntimeBindRotation(in ActorSkeletonBoneBlob bone)
        {
            quaternion rotation = bone.BindRotation;
            if (math.lengthsq(rotation.value) <= 0f)
                rotation = quaternion.identity;
            return ActorAnimationSpaceConversion.SourceQuaternionToUnity(rotation);
        }

        public static float RuntimeBindScale(in ActorSkeletonBoneBlob bone)
            => bone.BindScale <= 0f ? 1f : bone.BindScale;

        public static float4x4 RuntimeBindLocalMatrix(in ActorSkeletonBoneBlob bone)
            => ActorAnimationSpaceConversion.SourceAffineToUnity(bone.BindLocalMatrix);

        public static float4x4 RuntimeBindLocalToRootMatrix(in ActorSkeletonBoneBlob bone)
            => ActorAnimationSpaceConversion.SourceAffineToUnity(bone.BindLocalToRootMatrix);
    }
}
