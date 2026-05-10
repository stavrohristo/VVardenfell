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
        static readonly ProfilerMarker k_InteractableBlobs = new("VV.Install.InteractableColliderLoad");

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

        public static IEnumerable<object> InstallAvailableCells(
            CacheLoader cache,
            NativeHashSet<int2> available,
            RuntimeLoadProgress progress)
        {
            int totalCells = cache.Manifest.CellGrid?.Length ?? 0;
            progress?.BeginStage("Cell section manifest", "Publishing available cells", totalCells);
            for (int i = 0; i < cache.Manifest.CellGrid.Length; i++)
            {
                var g = cache.Manifest.CellGrid[i];
                available.Add(new int2(g.Item1, g.Item2));
                if (i + 1 == totalCells || ((i + 1) % MergeBatchSize) == 0)
                {
                    progress?.Report($"Publishing available cells {i + 1}/{totalCells}", i + 1, totalCells);
                    yield return null;
                }
            }

            for (int i = 0; i < cache.Manifest.InteriorCellCount; i++)
            {
                string cellId = cache.Manifest.InteriorCellIds[i] ?? string.Empty;
                WorldResources.RegisterInteriorCellId(cellId);
            }
            progress?.CompleteStage("Cell section manifest ready");
        }

        public static IEnumerable<object> InstallColliderBlobs(WorldBootstrapCollisionLoadResult collisionLoad, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Collider blobs", "Registering interactable collider blobs", 1);
            WorldResources.ColliderBlobs = collisionLoad.Blobs;
            progress?.Report("Interactable collider blobs registered", 1, 1);
            progress?.CompleteStage("Collider blobs registered");
            yield return null;
        }
    }
}
