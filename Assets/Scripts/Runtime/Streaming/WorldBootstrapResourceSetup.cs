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
        static readonly ProfilerMarker k_TerrainAssets = new("VV.Install.TerrainAssetResolve");
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
                if (WorldResources.TerrainTemplate == null && WorldResources.TerrainShader != null)
                {
                    WorldResources.TerrainTemplate = new Material(WorldResources.TerrainShader)
                    {
                        name = "VV:TerrainTemplate",
                        enableInstancing = true,
                    };
                    WorldResources.TerrainTemplate.SetFloat("_TileScale", 16f);
                    WorldResources.TerrainTemplate.SetFloat("_SplatSize", 16f);
                }

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
            WorldResources.ClearPreloadedCells();
            WorldResources.EnsurePreloadedCellCapacity(totalPreloadedCells);
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
                    WorldResources.RegisterExteriorCell(coord, data);
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
                if (!WorldResources.TryRegisterInteriorCell(cellId, data, out string existingId))
                {
                    Debug.LogWarning($"[VVardenfell][Streaming] interior cell hash collision between '{existingId}' and '{cellId}'; keeping the first hash mapping.");
                }
                installed++;
                if (installed == totalPreloadedCells || (installed % MergeBatchSize) == 0)
                {
                    progress?.Report($"Installing preloaded cells {installed}/{totalPreloadedCells}", installed, totalPreloadedCells);
                    yield return null;
                }
            }
            if (installed != totalPreloadedCells)
                throw new System.InvalidOperationException($"[VVardenfell][Streaming] installed {installed} preloaded cells, expected {totalPreloadedCells}.");

            WorldResources.MarkPreloadedCellsComplete();
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
            int exteriorCellCount = WorldResources.ExteriorCellCount;
            progress?.BeginStage("Cell collider transfer", "Registering collider blobs", exteriorCellCount);
            WorldResources.StaticCellColliders.Clear();
            WorldResources.TerrainColliders.Clear();
            WorldResources.StaticCellColliders.EnsureCapacity(exteriorCellCount);
            WorldResources.TerrainColliders.EnsureCapacity(exteriorCellCount);
            WorldResources.ColliderBlobs = collisionLoad.Blobs;

            int statCellsWithCol = 0;
            int terrainCellsWithCol = 0;
            int cursor = 0;
            var exteriorCells = WorldResources.CopyExteriorCellEntries();
            for (int i = 0; i < exteriorCells.Length; i++)
            {
                var kv = exteriorCells[i];
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
                if (cursor == exteriorCellCount || (cursor % MergeBatchSize) == 0)
                {
                    progress?.Report($"Registering collider blobs {cursor}/{exteriorCellCount}", cursor, exteriorCellCount);
                    yield return null;
                }
            }
            progress?.CompleteStage("Collider blobs registered");
        }
    }
}
