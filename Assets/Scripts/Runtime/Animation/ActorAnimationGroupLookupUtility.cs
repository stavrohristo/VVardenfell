using Unity.Mathematics;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorAnimationGroupLookupUtility
    {
        public static bool TryResolveGroup(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ulong groupHash,
            out ActorAnimationGroupBlob group)
        {
            if (!TryGetRigFamilyAnimationIndex(ref catalog, presentation.RigFamilyIndex, out var index))
            {
                group = default;
                return false;
            }

            int first = index.FirstGroupLookupIndex;
            int count = index.GroupLookupCount;
            int end = math.min(catalog.GroupLookups.Length, first + count);
            if (first < 0 || count <= 0 || first >= end)
            {
                group = default;
                return false;
            }

            int lower = first;
            int upper = end;
            while (lower < upper)
            {
                int mid = lower + ((upper - lower) >> 1);
                if (catalog.GroupLookups[mid].GroupHash < groupHash)
                    lower = mid + 1;
                else
                    upper = mid;
            }

            int duplicateEnd = lower;
            while (duplicateEnd < end && catalog.GroupLookups[duplicateEnd].GroupHash == groupHash)
                duplicateEnd++;

            for (int i = duplicateEnd - 1; i >= lower; i--)
            {
                var lookup = catalog.GroupLookups[i];
                if ((uint)lookup.GroupIndex >= (uint)catalog.Groups.Length)
                    continue;

                group = catalog.Groups[lookup.GroupIndex];
                if ((uint)group.ClipIndex < (uint)catalog.Clips.Length)
                    return true;
            }

            group = default;
            return false;
        }

        static bool TryGetRigFamilyAnimationIndex(
            ref ActorAnimationCatalogBlob catalog,
            int rigFamilyIndex,
            out ActorRigFamilyAnimationIndexBlob index)
        {
            if ((uint)rigFamilyIndex >= (uint)catalog.RigFamilyAnimationIndexes.Length)
            {
                index = default;
                return false;
            }

            index = catalog.RigFamilyAnimationIndexes[rigFamilyIndex];
            return true;
        }
    }
}
