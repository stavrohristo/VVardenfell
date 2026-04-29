using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    internal readonly struct WorldBootstrapCollisionLoadResult
    {
        public WorldBootstrapCollisionLoadResult(BlobAssetReference<Collider>[] blobs, string error)
        {
            Blobs = blobs;
            Error = error;
        }

        public BlobAssetReference<Collider>[] Blobs { get; }
        public string Error { get; }
    }

    internal static class WorldBootstrapResourceSetup
    {
        const int MergeBatchSize = 512;

        static readonly ProfilerMarker k_Managed = new("VV.Install.ManagedResources");
        static readonly ProfilerMarker k_MeshBounds = new("VV.Install.MeshBoundsCache");
        static readonly ProfilerMarker k_TerrainAssets = new("VV.Install.TerrainAssetResolve");
        static readonly ProfilerMarker k_RefPrefabs = new("VV.Install.RefPrefabBuild");
        static readonly ProfilerMarker k_RefPrefabCreateEntity = new("VV.Install.RefPrefabBuild.CreateEntity");
        static readonly ProfilerMarker k_RefPrefabAddRenderMesh = new("VV.Install.RefPrefabBuild.AddRenderMesh");
        static readonly ProfilerMarker k_RefPrefabSetup = new("VV.Install.RefPrefabBuild.Setup");
        static readonly ProfilerMarker k_CellPreload = new("VV.Install.CellPreload");
        static readonly ProfilerMarker k_InteractableBlobs = new("VV.Install.InteractableColliderLoad");
        static readonly ProfilerMarker k_StatCellBlobs = new("VV.Install.CellColliderTransfer");

        public static IEnumerable<object> InstallManagedResources(CacheLoader cache, RuntimeLoadProgress progress)
        {
            CachePaths.Warmup();

            progress?.BeginStage("Install managed resources", "Assigning managed globals", 1);
            k_Managed.Begin();
            try
            {
                WorldResources.Cache = cache;
                WorldResources.Desc = new RenderMeshDescription(
                    shadowCastingMode: ShadowCastingMode.On,
                    receiveShadows: true,
                    staticShadowCaster: true);
            }
            finally
            {
                k_Managed.End();
            }
            progress?.Report("Managed globals ready", 1, 1);
            progress?.CompleteStage();
            yield return null;
        }

        public static IEnumerable<object> InstallMeshBounds(CacheLoader cache, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Mesh bounds cache", "Caching mesh bounds", cache.Meshes.Length);
            if (WorldResources.MeshBounds.IsCreated)
                WorldResources.MeshBounds.Dispose();
            WorldResources.MeshBounds = new NativeArray<AABB>(cache.Meshes.Length, Allocator.Persistent);
            for (int i = 0; i < cache.Meshes.Length; i++)
            {
                k_MeshBounds.Begin();
                try
                {
                    var b = cache.Meshes[i].bounds;
                    WorldResources.MeshBounds[i] = new AABB { Center = b.center, Extents = b.extents };
                }
                finally
                {
                    k_MeshBounds.End();
                }

                int completed = i + 1;
                if (completed == cache.Meshes.Length || (completed % 128) == 0)
                {
                    progress?.Report($"Caching mesh bounds {completed}/{cache.Meshes.Length}", completed, cache.Meshes.Length);
                    yield return null;
                }
            }
            progress?.CompleteStage("Mesh bounds ready");
        }

        public static IEnumerable<object> InstallTerrainAssets(CacheLoader cache, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Terrain asset resolve", "Resolving terrain shader and materials", 1);
            k_TerrainAssets.Begin();
            try
            {
                WorldResources.TerrainShader = Shader.Find("VVardenfell/MwTerrain");
                if (WorldResources.TerrainShader == null)
                    Debug.LogWarning("[VVardenfell] VVardenfell/MwTerrain shader missing; terrain will use URP/Lit fallback.");

                var fallbackShader = Shader.Find("Universal Render Pipeline/Lit");
#if UNITY_EDITOR
                var registry = cache.Registry;
                if (registry != null)
                {
                    if (WorldResources.TerrainShader != null)
                        WorldResources.TerrainTemplate = registry.GetOrCreateTerrainTemplate(WorldResources.TerrainShader);
                    WorldResources.TerrainFallbackMat = registry.GetOrCreateTerrainFallback(fallbackShader);
                    UnityEditor.AssetDatabase.SaveAssets();
                }
#endif
                if (WorldResources.TerrainFallbackMat == null)
                {
                    WorldResources.TerrainFallbackMat = new Material(fallbackShader)
                    {
                        name = "VV:TerrainFallback",
                        color = new Color(0.35f, 0.42f, 0.30f),
                    };
                }
            }
            finally
            {
                k_TerrainAssets.End();
            }
            progress?.Report("Terrain assets ready", 1, 1);
            progress?.CompleteStage();
            yield return null;
        }

        public static IEnumerable<object> InstallRenderShardRefPrefabs(EntityManager em, RuntimeLoadProgress progress)
        {
            var rmas = WorldResources.RefsRmas ?? System.Array.Empty<RenderMeshArray>();
            WorldResources.RefPrefabs = new Entity[rmas.Length];

            progress?.BeginStage("Ref prefab build", "Creating ref prefabs", rmas.Length);
            if (rmas.Length > 0)
            {
                k_RefPrefabs.Begin();
                try
                {
                    CreateRenderShardRefPrefabs(em, rmas, WorldResources.RefPrefabs);
                }
                finally
                {
                    k_RefPrefabs.End();
                }
            }

            progress?.Report($"Creating ref prefabs {rmas.Length}/{rmas.Length}", rmas.Length, rmas.Length);
            progress?.CompleteStage("Ref prefabs ready");
            yield return null;
        }

        static void CreateRenderShardRefPrefabs(EntityManager em, RenderMeshArray[] rmas, Entity[] prefabs)
        {
            var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);

            k_RefPrefabCreateEntity.Begin();
            Entity sourcePrefab;
            try
            {
                sourcePrefab = em.CreateEntity();
            }
            finally
            {
                k_RefPrefabCreateEntity.End();
            }

            em.SetName(sourcePrefab, "VVardenfell.RefPrefab[b0]");

            k_RefPrefabAddRenderMesh.Begin();
            try
            {
                ValidateRenderShardPrefabSource(rmas[0], 0, materialMeshInfo, out _);
                RenderMeshUtility.AddComponents(
                    sourcePrefab, em, WorldResources.Desc, rmas[0], materialMeshInfo);
            }
            finally
            {
                k_RefPrefabAddRenderMesh.End();
            }

            k_RefPrefabSetup.Begin();
            try
            {
                em.AddComponentData(sourcePrefab, LocalTransform.Identity);
                em.AddComponentData(sourcePrefab, default(TextureSlice));
                em.AddComponentData(sourcePrefab, new CellLink { Value = int2.zero });
                em.AddComponent<Unity.Transforms.Static>(sourcePrefab);
                em.AddSharedComponent(sourcePrefab, new PhysicsWorldIndex { Value = 0 });
                em.AddComponent<Prefab>(sourcePrefab);

                prefabs[0] = sourcePrefab;
                if (rmas.Length > 1)
                {
                    using var clones = new NativeArray<Entity>(rmas.Length - 1, Allocator.Temp);
                    em.Instantiate(sourcePrefab, clones);
                    em.AddComponent<Prefab>(clones);

                    for (int i = 0; i < clones.Length; i++)
                        prefabs[i + 1] = clones[i];
                }
            }
            finally
            {
                k_RefPrefabSetup.End();
            }

            for (int b = 0; b < rmas.Length; b++)
            {
                Entity prefab = prefabs[b];
                ValidateRenderShardPrefabSource(rmas[b], b, materialMeshInfo, out var bounds);
                em.SetName(prefab, $"VVardenfell.RefPrefab[b{b}]");
                em.SetSharedComponentManaged(prefab, rmas[b]);
                em.SetComponentData(prefab, materialMeshInfo);
                em.SetComponentData(prefab, new RenderBounds { Value = bounds });
            }
        }

        static void ValidateRenderShardPrefabSource(
            RenderMeshArray rma,
            int shardIndex,
            MaterialMeshInfo materialMeshInfo,
            out AABB bounds)
        {
            var mesh = rma.GetMesh(materialMeshInfo);
            if (mesh == null)
                throw new System.InvalidOperationException($"Render shard {shardIndex} prefab source has no mesh at local mesh 0.");

            var materials = rma.GetMaterials(materialMeshInfo);
            if (materials == null)
                throw new System.InvalidOperationException($"Render shard {shardIndex} prefab source has no material at local material 0.");

            var unityBounds = mesh.bounds;
            bounds = new AABB
            {
                Center = new float3(unityBounds.center.x, unityBounds.center.y, unityBounds.center.z),
                Extents = new float3(unityBounds.extents.x, unityBounds.extents.y, unityBounds.extents.z),
            };
        }

        public static WorldBootstrapCollisionLoadResult LoadCollisionBlobs()
        {
            k_InteractableBlobs.Begin();
            try
            {
                return new WorldBootstrapCollisionLoadResult(
                    CollisionLoader.LoadAll(CachePaths.Collisions, out var error),
                    error);
            }
            finally
            {
                k_InteractableBlobs.End();
            }
        }

        public static IEnumerable<object> InstallPreloadedCells(
            CacheLoader cache,
            WorldBootstrapPreloadResult preload,
            NativeHashSet<int2> available,
            RuntimeLoadProgress progress)
        {
            int totalPreloadedCells = CountPreloadedCells(preload);
            progress?.BeginStage("Cell preload merge", "Installing preloaded cells", totalPreloadedCells);
            WorldResources.Cells.Clear();
            WorldResources.Cells.EnsureCapacity(System.Math.Max(totalPreloadedCells, 1));
            WorldResources.InteriorCells.Clear();
            WorldResources.InteriorCells.EnsureCapacity(System.Math.Max(totalPreloadedCells, 1));
            WorldResources.InteriorCellsByHash.Clear();
            WorldResources.InteriorCellsByHash.EnsureCapacity(System.Math.Max(totalPreloadedCells, 1));
            WorldResources.InteriorCellIdsByHash.Clear();
            WorldResources.InteriorCellIdsByHash.EnsureCapacity(System.Math.Max(totalPreloadedCells, 1));
            int installed = 0;
            for (int i = 0; i < cache.Manifest.CellGrid.Length; i++)
            {
                k_CellPreload.Begin();
                try
                {
                    var data = preload.ExteriorCells[i];
                    if (data == null)
                        continue;

                    var g = cache.Manifest.CellGrid[i];
                    var coord = new int2(g.Item1, g.Item2);
                    available.Add(coord);
                    WorldResources.Cells[coord] = data;
                    installed++;
                }
                finally
                {
                    k_CellPreload.End();
                }

                if (installed == totalPreloadedCells || (installed % MergeBatchSize) == 0)
                {
                    progress?.Report($"Installing preloaded cells {installed}/{totalPreloadedCells}", installed, totalPreloadedCells);
                    yield return null;
                }
            }
            for (int i = 0; i < cache.Manifest.InteriorCellCount; i++)
            {
                var data = preload.InteriorCells[i];
                if (data == null)
                    continue;

                string cellId = cache.Manifest.InteriorCellIds[i] ?? string.Empty;
                if (!WorldResources.InteriorCells.ContainsKey(cellId))
                    WorldResources.InteriorCells[cellId] = data;
                ulong cellHash = InteriorCellIdHash.Hash(cellId);
                if (cellHash != 0UL)
                {
                    if (WorldResources.InteriorCellsByHash.TryGetValue(cellHash, out var existing) && !ReferenceEquals(existing, data))
                    {
                        string existingId = WorldResources.ResolveInteriorCellId(cellHash);
                        Debug.LogWarning($"[VVardenfell][Streaming] interior cell hash collision between '{existingId}' and '{cellId}'; keeping the first hash mapping.");
                    }
                    else
                    {
                        WorldResources.InteriorCellsByHash[cellHash] = data;
                        WorldResources.InteriorCellIdsByHash[cellHash] = cellId;
                    }
                }
                installed++;
                if (installed == totalPreloadedCells || (installed % MergeBatchSize) == 0)
                {
                    progress?.Report($"Installing preloaded cells {installed}/{totalPreloadedCells}", installed, totalPreloadedCells);
                    yield return null;
                }
            }
            progress?.CompleteStage("Preloaded cells installed");
        }

        static int CountPreloadedCells(WorldBootstrapPreloadResult preload)
        {
            int count = 0;
            var exterior = preload?.ExteriorCells ?? System.Array.Empty<CellData>();
            for (int i = 0; i < exterior.Length; i++)
                if (exterior[i] != null)
                    count++;

            var interior = preload?.InteriorCells ?? System.Array.Empty<CellData>();
            for (int i = 0; i < interior.Length; i++)
                if (interior[i] != null)
                    count++;

            return count;
        }

        public static IEnumerable<object> InstallColliderBlobs(WorldBootstrapCollisionLoadResult collisionLoad, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Cell collider transfer", "Registering collider blobs", WorldResources.Cells.Count);
            WorldResources.StaticCellColliders.Clear();
            WorldResources.TerrainColliders.Clear();
            WorldResources.StaticCellColliders.EnsureCapacity(WorldResources.Cells.Count);
            WorldResources.TerrainColliders.EnsureCapacity(WorldResources.Cells.Count);
            WorldResources.ColliderBlobs = collisionLoad.Blobs;

            int statCellsWithCol = 0;
            int terrainCellsWithCol = 0;
            int cursor = 0;
            foreach (var kv in WorldResources.Cells)
            {
                k_StatCellBlobs.Begin();
                try
                {
                    var coord = kv.Key;
                    var data = kv.Value;
                    if (data.HasStaticCollider)
                    {
                        WorldResources.StaticCellColliders[coord] = data.StaticColliderBlob;
                        data.StaticColliderBlob = default;
                        statCellsWithCol++;
                    }
                    if (data.HasTerrainCollider)
                    {
                        WorldResources.TerrainColliders[coord] = data.TerrainColliderBlob;
                        data.TerrainColliderBlob = default;
                        terrainCellsWithCol++;
                    }
                }
                finally
                {
                    k_StatCellBlobs.End();
                }

                cursor++;
                if (cursor == WorldResources.Cells.Count || (cursor % MergeBatchSize) == 0)
                {
                    progress?.Report($"Registering collider blobs {cursor}/{WorldResources.Cells.Count}", cursor, WorldResources.Cells.Count);
                    yield return null;
                }
            }
            progress?.CompleteStage("Collider blobs registered");
        }
    }
}
