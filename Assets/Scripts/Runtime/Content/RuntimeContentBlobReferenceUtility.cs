using System;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Content
{
    internal static class RuntimeContentBlobReferenceUtility
    {
        public static BlobAssetReference<RuntimeContentBlob> RequireBlob(string context)
        {
            if (!TryGetBlob(out var blob))
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] {context} requires runtime content blob.");
            return blob;
        }

        public static bool TryGetBlob(out BlobAssetReference<RuntimeContentBlob> blob)
        {
            blob = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityQuery query = world.EntityManager.CreateEntityQuery(typeof(RuntimeContentBlobReference));
            try
            {
                if (!query.TryGetSingleton(out RuntimeContentBlobReference reference) || !reference.Blob.IsCreated)
                    return false;
                blob = reference.Blob;
                return true;
            }
            finally
            {
                query.Dispose();
            }
        }
    }
}
