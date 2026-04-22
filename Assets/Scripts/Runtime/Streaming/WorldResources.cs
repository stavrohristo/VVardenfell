using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Managed process-wide storage for anything the runtime pipeline needs to touch that
    /// Burst cannot: Unity Mesh/Material/Texture2D references, the cache loader, per-bucket
    /// <see cref="RenderMeshArray"/>s, ref prefab entities.
    ///
    /// Kept as a static singleton rather than a managed <see cref="IComponentData"/> —
    /// the managed systems access this directly on the main thread; Burst systems never
    /// touch it.
    /// </summary>
    public static class WorldResources
    {
        public static CacheLoader Cache;
        public static Shader TerrainShader;
        /// <summary>
        /// Per-cell terrain materials are cloned from this asset. Holds shader +
        /// tweakable scalars (<c>_TileScale</c>, <c>_SplatSize</c>). <c>_Splat</c>
        /// (per-cell) and <c>_LayerArray</c> (runtime-built) are injected on the
        /// clone. Asset lives in the <see cref="MaterialRegistry"/>.
        /// </summary>
        public static Material TerrainTemplate;
        /// <summary>
        /// Shared across every cell whose LAND has no VTEX data. Also an asset in
        /// <see cref="MaterialRegistry"/> when in editor.
        /// </summary>
        public static Material TerrainFallbackMat;
        /// <summary>
        /// One <see cref="RenderMeshArray"/> per texture-dimension bucket. Each RMA holds
        /// <see cref="BlendVariantCount"/> materials (blend variants bound to that bucket's
        /// <see cref="RefBaseArrays"/> slot) + the full mesh set. Refs are Instantiated from
        /// the <see cref="RefPrefabs"/> entry whose RMA matches their texture's bucket.
        /// </summary>
        public static RenderMeshArray[] RefsRmas;
        public static RenderMeshDescription Desc;
        /// <summary>
        /// One prefab-tagged ref entity per texture-dimension bucket, parallel to
        /// <see cref="RefsRmas"/>. A ref is Instantiated from the prefab whose bucket
        /// matches its texture (<see cref="TexBucketInfo"/>). Batch Instantiate carries
        /// the per-prefab RMA onto the clones — the only efficient way to assign a managed
        /// shared component to N entities in Entities 1.3.
        /// </summary>
        public static Entity[] RefPrefabs;

        /// <summary>
        /// Per-mesh local AABB, indexed by MeshIndex. Built once at bootstrap from
        /// <c>cache.Meshes[i].bounds</c> so the ref-spawn path can write a correct
        /// <see cref="RenderBounds"/> per ref — without this, all clones inherit the
        /// prototype's mesh-0 bounds and culling goes wrong once MaterialMeshInfo is
        /// rewritten.
        /// </summary>
        public static NativeArray<AABB> MeshBounds;
        public static NativeArray<int2> RefShardMeshRanges;
        public static NativeArray<int> RefShardGlobalMeshIndices;

        /// <summary>
        /// Per-bucket Texture2DArrays. Each array is native-sized (<c>w × h</c>) with a full
        /// mip chain — no texture up/down-scale, proper trilinear + anisotropic filtering.
        /// Parallel to <see cref="RefsRmas"/>: bucket <c>b</c>'s RT is bound as <c>_BaseArray</c>
        /// on every material in <c>RefsRmas[b]</c>.
        /// </summary>
        public static RenderTexture[] RefBaseArrays;

        /// <summary>
        /// Per-global-texture bucket info: <c>(bucketIdx, localSliceIdx)</c>. Indexed by the
        /// bake-time global texture index. Spawn reads this per ref to write the
        /// per-instance <c>_Slice</c> uniform with the bucket-local slice.
        /// </summary>
        public static NativeArray<int2> TexBucketInfo;

        /// <summary>
        /// Bucket + slice used for refs whose bake-time <c>SliceIndex</c> is <c>-1</c>
        /// (no texture). Points at the trailing white slice in bucket 0 by convention.
        /// </summary>
        public static int2 FallbackBucketSlice;

        /// <summary>
        /// Number of blend-variant materials per bucket (typically ≤3: Opaque / AlphaTest /
        /// AlphaBlend). Equal to <c>cache.Materials.Length / RefsRmas.Length</c>.
        /// </summary>
        public static int BlendVariantCount;

        /// <summary>
        /// Managed per-cell Unity objects created at spawn. Keyed by grid coord.
        /// Populated by <see cref="WorldSpawner.SpawnAll"/>, disposed by <see cref="Reset"/>.
        /// </summary>
        public static readonly Dictionary<int2, PerCellManaged> LoadedManaged = new();

        /// <summary>
        /// Every baked cell, preloaded once at bootstrap. Keyed by grid coord.
        /// ~60-80 MB retained (1404 cells × ~40 KB + managed overhead). Trades
        /// gen2 heap for zero per-stream disk I/O — the ref-spawn path is a dict
        /// lookup instead of <see cref="CellFile.Read"/>.
        /// </summary>
        public static readonly Dictionary<int2, CellData> Cells = new();
        public static readonly Dictionary<string, CellData> InteriorCells = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Global deduped interactable-collider blobs (indexed by <see cref="RefEntry.CollisionIndex"/>).
        /// Populated from <c>collisions.bin</c> at boot, disposed on <see cref="Reset"/>.
        /// Entries may be <c>default</c> for empty/failed payloads — the spawn path
        /// skips any ref whose blob is unset.
        /// </summary>
        public static BlobAssetReference<Collider>[] ColliderBlobs;

        /// <summary>Per-cell combined STAT collider (null for wilderness cells).</summary>
        public static readonly Dictionary<int2, BlobAssetReference<Collider>> StaticCellColliders = new();

        /// <summary>Per-cell terrain heightfield collider (null for cells without LAND).</summary>
        public static readonly Dictionary<int2, BlobAssetReference<Collider>> TerrainColliders = new();

        public struct PerCellManaged
        {
            public Mesh TerrainMesh;
            public Texture2D SplatMap;
            public Material TerrainMat;      // null if shared fallback
            public RenderMeshArray TerrainRma;
        }

        public static void Reset()
        {
            // Dispose any per-cell Unity objects still registered, then clear every
            // managed reference. Call on world teardown.
            foreach (var kv in LoadedManaged)
            {
                var m = kv.Value;
                if (m.TerrainMesh != null) Object.Destroy(m.TerrainMesh);
                if (m.SplatMap    != null) Object.Destroy(m.SplatMap);
                // Per-cell terrain mats are clones of the template — always ephemeral.
                // Skip only if it happens to be the shared fallback (guarded below).
                if (m.TerrainMat  != null && m.TerrainMat != TerrainFallbackMat) Object.Destroy(m.TerrainMat);
            }
            LoadedManaged.Clear();
            Cells.Clear();
            foreach (var kv in InteriorCells)
            {
                var data = kv.Value;
                if (data == null)
                    continue;
                if (data.StaticColliderBlob.IsCreated)
                    data.StaticColliderBlob.Dispose();
                if (data.TerrainColliderBlob.IsCreated)
                    data.TerrainColliderBlob.Dispose();
            }
            InteriorCells.Clear();

            // Dispose per-cell static + terrain collider blobs before clearing the dicts.
            foreach (var kv in StaticCellColliders)
                if (kv.Value.IsCreated) kv.Value.Dispose();
            StaticCellColliders.Clear();
            foreach (var kv in TerrainColliders)
                if (kv.Value.IsCreated) kv.Value.Dispose();
            TerrainColliders.Clear();
            if (ColliderBlobs != null)
            {
                for (int i = 0; i < ColliderBlobs.Length; i++)
                    if (ColliderBlobs[i].IsCreated) ColliderBlobs[i].Dispose();
                ColliderBlobs = null;
            }
            if (MeshBounds.IsCreated) MeshBounds.Dispose();
            if (RefShardMeshRanges.IsCreated) RefShardMeshRanges.Dispose();
            if (RefShardGlobalMeshIndices.IsCreated) RefShardGlobalMeshIndices.Dispose();
            if (RefBaseArrays != null)
            {
                for (int i = 0; i < RefBaseArrays.Length; i++)
                {
                    var rt = RefBaseArrays[i];
                    if (rt != null) { rt.Release(); Object.Destroy(rt); }
                }
                RefBaseArrays = null;
            }
            if (TexBucketInfo.IsCreated) TexBucketInfo.Dispose();
            FallbackBucketSlice = default;
            BlendVariantCount = 0;
            // TerrainFallbackMat / TerrainTemplate are registry-owned assets in editor —
            // Object.Destroy would log an error and no-op. Just drop the references.
            TerrainFallbackMat = null;
            TerrainTemplate = null;
            TerrainShader = null;
            RefsRmas = null;
            Cache = null;
            RefPrefabs = null;
            Desc = default;
            RuntimeContentDatabase.Clear();
        }
    }
}
