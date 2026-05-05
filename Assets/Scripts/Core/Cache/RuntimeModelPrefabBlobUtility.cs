using System;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeModelPrefabBlobUtility
    {
        public static ref RuntimeModelPrefabDefBlob RequireRecord(ref RuntimeModelPrefabBlob blob, int modelPrefabIndex)
        {
            if ((uint)modelPrefabIndex >= (uint)blob.Records.Length)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Invalid model prefab index {modelPrefabIndex}; length {blob.Records.Length}.");

            ref RuntimeModelPrefabDefBlob record = ref blob.Records[modelPrefabIndex];
            if (record.Supported == 0 || record.ModelPrefabIndex != modelPrefabIndex)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab index {modelPrefabIndex} is not supported.");
            return ref record;
        }

        public static bool TryGetIndexByPathHash(ref RuntimeModelPrefabBlob blob, ulong hash, out int modelPrefabIndex)
            => TryFindHashLookup(ref blob.ModelPathHashLookup, hash, out modelPrefabIndex);

        public static bool TryGetIndexByContentModelPathHash(ref RuntimeModelPrefabBlob blob, ulong hash, out int modelPrefabIndex)
            => TryFindHashLookup(ref blob.ContentModelPathHashLookup, hash, out modelPrefabIndex);

        public static int RequireIndexByContentModelPathHash(ref RuntimeModelPrefabBlob blob, ulong hash, string context)
        {
            if (!TryGetIndexByContentModelPathHash(ref blob, hash, out int modelPrefabIndex))
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] {context} missing model prefab for content model path hash 0x{hash:X16}.");
            return modelPrefabIndex;
        }

        public static float RequireCollisionRadius(ref RuntimeModelPrefabBlob blob, int modelPrefabIndex, string context)
        {
            ref RuntimeModelPrefabDefBlob record = ref RequireRecord(ref blob, modelPrefabIndex);
            if (record.CollisionRadius <= 0f)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] {context} model prefab {modelPrefabIndex} has no positive collision radius.");
            return record.CollisionRadius;
        }

        static bool TryFindHashLookup(ref BlobArray<RuntimeContentHashLookupBlob> lookup, ulong hash, out int value)
        {
            value = 0;
            if (hash == 0UL)
                return false;

            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ulong candidate = lookup[mid].Hash;
                if (candidate == hash)
                {
                    value = lookup[mid].HandleValue;
                    return true;
                }
                if (candidate < hash)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            return false;
        }
    }
}
