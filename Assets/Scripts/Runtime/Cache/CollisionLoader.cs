using System.IO;
using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Reads <c>collisions.bin</c> - the global table of deduped per-ref collision blobs
    /// written by <see cref="CollisionBakery"/> - back into memory as
    /// <see cref="BlobAssetReference{T}"/>s ready to hand to <c>PhysicsCollider</c>.
    /// Since v19 the table stores pre-built <see cref="Unity.Physics.MeshCollider"/> blobs
    /// with the BVH baked in; loading is one <c>Malloc</c> + one <c>memcpy</c> per entry
    /// (no BVH rebuild), so boot collapses from tens of seconds to sub-second.
    ///
    /// Only interactable refs (DOOR / ACTI / CONT / LIGH / pickable items) consume these
    /// blobs; pure STATs have their collision combined into serialized cell sections.
    ///
    /// Blobs live in <see cref="Streaming.WorldResources.ColliderBlobs"/> and are disposed
    /// on world teardown.
    /// </summary>
    public static class CollisionLoader
    {
        static readonly ProfilerMarker k_LoadAll = new("VV.CollisionLoader.LoadAll");
        static readonly ProfilerMarker k_Read = new("VV.CollisionLoader.ReadBlob");

        public static BlobAssetReference<Collider>[] LoadAll(string path, out string error)
        {
            using var _loadAll = k_LoadAll.Auto();
            error = null;
            if (!File.Exists(path))
            {
                error = "collisions.bin missing";
                return System.Array.Empty<BlobAssetReference<Collider>>();
            }

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != CollisionBakery.MagicCollision)
            {
                error = "collisions.bin magic mismatch";
                return System.Array.Empty<BlobAssetReference<Collider>>();
            }

            uint count = r.ReadUInt32();
            var offsets = new ulong[count];
            for (int i = 0; i < count; i++) offsets[i] = r.ReadUInt64();

            var blobs = new BlobAssetReference<Collider>[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    fs.Position = (long)offsets[i];
                    k_Read.Begin();
                    try
                    {
                        blobs[i] = BlobStreamIO.ReadLengthPrefixed<Collider>(
                            r, CacheFormat.PhysicsBlobVersion);
                    }
                    finally
                    {
                        k_Read.End();
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                for (int i = 0; i < blobs.Length; i++)
                {
                    if (blobs[i].IsCreated)
                        blobs[i].Dispose();
                }

                error = ex.Message;
                return System.Array.Empty<BlobAssetReference<Collider>>();
            }

            return blobs;
        }
    }
}
