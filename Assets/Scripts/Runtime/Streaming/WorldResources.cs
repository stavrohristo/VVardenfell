using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Vfx;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Managed process-wide storage for anything the runtime pipeline needs to touch that
    /// Burst cannot: Unity Mesh/Material/Texture2D references, the cache loader, and
    /// model-prefab texture buckets.
    ///
    /// Kept as a static singleton rather than a managed <see cref="IComponentData"/> —
    /// the managed systems access this directly on the main thread; Burst systems never
    /// touch it.
    /// </summary>
    public static class WorldResources
    {
        public struct RuntimeSpawnPrefabDescriptor
        {
            public int ModelPrefabIndex;
            public int CollisionIndex;
            public byte Supported;

            public bool IsSupported => Supported != 0 && ModelPrefabIndex >= 0;
        }

        public static CacheLoader Cache;
        public static BootstrapRuntimeMode RuntimeMode;
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
        /// Shared render settings used when building model-prefab render leaves.
        /// </summary>
        public static RenderMeshDescription Desc;
        public static Entity[] ModelPrefabs;
        public static RuntimeSpawnPrefabDescriptor[] SpawnableCreaturePrefabs;
        public static RuntimeSpawnPrefabDescriptor[] SpawnableItemPrefabs;
        public static RuntimeSpawnPrefabDescriptor[] SpawnableLightPrefabs;
        public static PathGridNavigationWorld PathGridNavigation;
        public static ActorGpuAnimationResources ActorGpuAnimation;
        public static MorrowindVfxResources Vfx;
        public static ActorEntitiesGraphicsRenderResources ActorEntitiesGraphicsRenderer;
        public static float ActorShadowCasterDistance = 64f;
        public static float ActorShadowCasterPadding = 8f;
        public static int MaxActorShadowCasters = 128;

        /// <summary>
        /// Per-bucket Texture2DArrays. Each array is native-sized (<c>w × h</c>) with a full
        /// mip chain — no texture up/down-scale, proper trilinear + anisotropic filtering.
        /// Model-prefab render leaves bind these arrays through their bucket-specific
        /// material variants.
        /// </summary>
        public static Texture2DArray[] RefBaseArrays;
        public static int[] RefBucketKeys;
        public static Dictionary<int, int> RefBucketIndexByKey;

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
        /// AlphaBlend). Equal to the number of material records baked per texture bucket.
        /// </summary>
        public static int BlendVariantCount;
        public static int CombinedRenderVariantCount;

        /// <summary>
        /// Managed per-cell Unity objects created at spawn. Keyed by grid coord.
        /// Populated by <see cref="WorldSpawner.SpawnAll"/>, disposed by <see cref="Reset"/>.
        /// </summary>
        public static readonly Dictionary<int2, PerCellManaged> LoadedManaged = new();
        public static readonly Dictionary<int2, List<Entity>> ExteriorCellEntities = new();

        /// <summary>
        /// Every baked cell, preloaded once at bootstrap. Keyed by grid coord.
        /// ~60-80 MB retained (1404 cells × ~40 KB + managed overhead). Trades
        /// gen2 heap for zero per-stream disk I/O — the ref-spawn path is a dict
        /// lookup instead of <see cref="CellFile.Read"/>.
        /// </summary>
        public static readonly Dictionary<int2, CellData> Cells = new();
        public static readonly Dictionary<string, CellData> InteriorCells = new(System.StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<ulong, CellData> InteriorCellsByHash = new();
        public static readonly Dictionary<ulong, string> InteriorCellIdsByHash = new();

        /// <summary>
        /// Global deduped interactable-collider blobs (indexed by <see cref="RefEntry.CollisionIndex"/>).
        /// Populated from <c>collisions.bin</c> at boot, disposed on <see cref="Reset"/>.
        /// Entries may be <c>default</c> for empty/failed payloads — the spawn path
        /// skips any ref whose blob is unset.
        /// </summary>
        public static BlobAssetReference<Collider>[] ColliderBlobs;
        public static BlobAssetReference<Collider> ActorCapsuleCollider;
        public static BlobAssetReference<Collider> ActorPickCapsuleCollider;

        /// <summary>Per-cell combined STAT collider (null for wilderness cells).</summary>
        public static readonly Dictionary<int2, BlobAssetReference<Collider>> StaticCellColliders = new();

        /// <summary>Per-cell terrain heightfield collider (null for cells without LAND).</summary>
        public static readonly Dictionary<int2, BlobAssetReference<Collider>> TerrainColliders = new();

        public static bool TryGetStaticCellCollider(int2 coord, out BlobAssetReference<Collider> collider)
        {
            return StaticCellColliders.TryGetValue(coord, out collider) && collider.IsCreated;
        }

        public static bool TryGetTerrainCollider(int2 coord, out BlobAssetReference<Collider> collider)
        {
            return TerrainColliders.TryGetValue(coord, out collider) && collider.IsCreated;
        }

        public static bool HasAnyPreloadedCells()
            => Cells.Count > 0 || InteriorCellsByHash.Count > 0;

        public static int ExteriorCellCount
            => Cells.Count;

        public static int InteriorCellHashCount
            => InteriorCellsByHash.Count;

        public static KeyValuePair<int2, CellData>[] CopyExteriorCellEntries()
        {
            var entries = new KeyValuePair<int2, CellData>[Cells.Count];
            int index = 0;
            foreach (var kv in Cells)
                entries[index++] = kv;
            return entries;
        }

        public static KeyValuePair<ulong, CellData>[] CopyInteriorCellHashEntries()
        {
            var entries = new KeyValuePair<ulong, CellData>[InteriorCellsByHash.Count];
            int index = 0;
            foreach (var kv in InteriorCellsByHash)
                entries[index++] = kv;
            return entries;
        }

        public static bool TryGetExteriorCell(int2 coord, out CellData cell)
            => Cells.TryGetValue(coord, out cell) && cell != null;

        public static void ClearPreloadedCells()
        {
            Cells.Clear();
            InteriorCells.Clear();
            InteriorCellsByHash.Clear();
            InteriorCellIdsByHash.Clear();
        }

        public static void EnsurePreloadedCellCapacity(int totalPreloadedCells)
        {
            int capacity = System.Math.Max(totalPreloadedCells, 1);
            Cells.EnsureCapacity(capacity);
            InteriorCells.EnsureCapacity(capacity);
            InteriorCellsByHash.EnsureCapacity(capacity);
            InteriorCellIdsByHash.EnsureCapacity(capacity);
        }

        public static void RegisterExteriorCell(int2 coord, CellData data)
        {
            if (data == null)
                throw new System.InvalidOperationException($"[VVardenfell][Streaming] cannot register null exterior cell ({coord.x},{coord.y}).");
            Cells[coord] = data;
        }

        public static bool TryRegisterInteriorCell(string cellId, CellData data, out string collisionId)
        {
            collisionId = string.Empty;
            if (data == null)
                throw new System.InvalidOperationException($"[VVardenfell][Streaming] cannot register null interior cell '{cellId}'.");

            cellId ??= string.Empty;
            if (!InteriorCells.ContainsKey(cellId))
                InteriorCells[cellId] = data;

            ulong cellHash = InteriorCellIdHash.Hash(cellId);
            if (cellHash == 0UL)
                return true;

            if (InteriorCellsByHash.TryGetValue(cellHash, out var existing) && !ReferenceEquals(existing, data))
            {
                collisionId = ResolveInteriorCellId(cellHash);
                return false;
            }

            InteriorCellsByHash[cellHash] = data;
            InteriorCellIdsByHash[cellHash] = cellId;
            return true;
        }

        public static void RegisterExteriorCellEntity(int2 coord, Entity entity)
        {
            if (entity == Entity.Null)
                return;

            if (!ExteriorCellEntities.TryGetValue(coord, out var entities))
            {
                entities = new List<Entity>(64);
                ExteriorCellEntities[coord] = entities;
            }

            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] == entity)
                    return;
            }

            entities.Add(entity);
        }

        public static bool TryGetInteriorCell(ulong cellHash, out CellData cell)
        {
            cell = null;
            return cellHash != 0UL
                   && InteriorCellsByHash.TryGetValue(cellHash, out cell)
                   && cell != null;
        }

        public static string ResolveInteriorCellId(ulong cellHash)
        {
            return cellHash != 0UL && InteriorCellIdsByHash.TryGetValue(cellHash, out string id)
                ? id
                : string.Empty;
        }

        public static bool TryGetRuntimeSpawnPrefab(ContentReference content, out RuntimeSpawnPrefabDescriptor descriptor)
        {
            descriptor = default;
            if (!content.IsValid)
                return false;

            switch (content.Kind)
            {
                case ContentReferenceKind.Actor:
                    if (SpawnableCreaturePrefabs != null && (uint)(content.HandleValue - 1) < (uint)SpawnableCreaturePrefabs.Length)
                    {
                        descriptor = SpawnableCreaturePrefabs[content.HandleValue - 1];
                        return descriptor.IsSupported;
                    }
                    return false;

                case ContentReferenceKind.Item:
                    if (SpawnableItemPrefabs != null && (uint)(content.HandleValue - 1) < (uint)SpawnableItemPrefabs.Length)
                    {
                        descriptor = SpawnableItemPrefabs[content.HandleValue - 1];
                        return descriptor.IsSupported;
                    }
                    return false;

                case ContentReferenceKind.Light:
                    if (SpawnableLightPrefabs != null && (uint)(content.HandleValue - 1) < (uint)SpawnableLightPrefabs.Length)
                    {
                        descriptor = SpawnableLightPrefabs[content.HandleValue - 1];
                        return descriptor.IsSupported;
                    }
                    return false;

                default:
                    return false;
            }
        }

        public struct PerCellManaged
        {
            public Mesh TerrainMesh;
            public Texture2D SplatMap;
            public Material TerrainMat;      // null if shared fallback
            public RenderMeshArray TerrainRma;
            public List<Mesh> CombinedRenderMeshes;
            public List<RenderMeshArray> CombinedRenderRmas;
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
                if (m.CombinedRenderMeshes != null)
                {
                    for (int i = 0; i < m.CombinedRenderMeshes.Count; i++)
                    {
                        if (m.CombinedRenderMeshes[i] != null)
                            Object.Destroy(m.CombinedRenderMeshes[i]);
                    }
                }
            }
            LoadedManaged.Clear();
            ExteriorCellEntities.Clear();

            foreach (var kv in Cells)
                DisposeCellResidentBlobs(kv.Value);
            Cells.Clear();

            foreach (var kv in InteriorCells)
                DisposeCellResidentBlobs(kv.Value);
            InteriorCells.Clear();
            InteriorCellsByHash.Clear();
            InteriorCellIdsByHash.Clear();

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
            if (ActorCapsuleCollider.IsCreated)
            {
                ActorCapsuleCollider.Dispose();
                ActorCapsuleCollider = default;
            }
            if (ActorPickCapsuleCollider.IsCreated)
            {
                ActorPickCapsuleCollider.Dispose();
                ActorPickCapsuleCollider = default;
            }
            ActorEntitiesGraphicsRenderer?.Dispose();
            ActorEntitiesGraphicsRenderer = null;
            Cache?.DisposeTextureResources();
            if (RefBaseArrays != null)
            {
                for (int i = 0; i < RefBaseArrays.Length; i++)
                {
                    var rt = RefBaseArrays[i];
                    if (rt != null) Object.Destroy(rt);
                }
                RefBaseArrays = null;
            }
            if (TexBucketInfo.IsCreated) TexBucketInfo.Dispose();
            RefBucketKeys = null;
            RefBucketIndexByKey = null;
            ActorGpuAnimation?.Dispose();
            ActorGpuAnimation = null;
            Vfx?.Dispose();
            Vfx = null;
            ActorShadowCasterDistance = 64f;
            ActorShadowCasterPadding = 8f;
            MaxActorShadowCasters = 128;
            FallbackBucketSlice = default;
            BlendVariantCount = 0;
            CombinedRenderVariantCount = 0;
            // TerrainFallbackMat / TerrainTemplate are registry-owned assets in editor —
            // Object.Destroy would log an error and no-op. Just drop the references.
            TerrainFallbackMat = null;
            TerrainTemplate = null;
            TerrainShader = null;
            Cache = null;
            RuntimeMode = BootstrapRuntimeMode.Vanilla;
            ModelPrefabs = null;
            SpawnableCreaturePrefabs = null;
            SpawnableItemPrefabs = null;
            SpawnableLightPrefabs = null;
            Desc = default;
            if (PathGridNavigation.IsCreated)
                PathGridNavigation.Dispose();
        }

        private static void DisposeCellResidentBlobs(CellData data)
        {
            if (data == null)
                return;

            if (data.StaticColliderBlob.IsCreated)
            {
                data.StaticColliderBlob.Dispose();
                data.StaticColliderBlob = default;
            }

            if (data.TerrainColliderBlob.IsCreated)
            {
                data.TerrainColliderBlob.Dispose();
                data.TerrainColliderBlob = default;
            }
        }
    }
}
