using System;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Content
{
    internal static class RuntimeContentBlobReferenceUtility
    {
        static World s_QueryWorld;
        static EntityQuery s_Query;
        static bool s_QueryCreated;

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

            EntityQuery query = GetQuery(world);
            if (!query.TryGetSingleton(out RuntimeContentBlobReference reference) || !reference.Blob.IsCreated)
                return false;
            blob = reference.Blob;
            return true;
        }

        static EntityQuery GetQuery(World world)
        {
            if (s_QueryCreated && s_QueryWorld == world)
                return s_Query;

            s_QueryWorld = world;
            s_Query = world.EntityManager.CreateEntityQuery(typeof(RuntimeContentBlobReference));
            s_QueryCreated = true;
            return s_Query;
        }
    }
}
