namespace VVardenfell.Runtime.Animation
{
    static class ActorSkeletonUtility
    {
        public static int ResolveBoneIndex(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            ulong nameHash)
        {
            if (nameHash == 0UL)
                return -1;

            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                if (ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, i, out var bone)
                    && bone.NameHash == nameHash)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
