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
using VVardenfell.Runtime.Pathfinding;
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

    internal sealed class WorldBootstrapRenderRegistryResult
    {
        public Material TerrainMaterial;
        public BatchMeshID[] RegisteredMeshes;
        public BatchMaterialID[] RegisteredRefMaterials;
        public BatchMaterialID[] RegisteredCombinedMaterials;
        public BatchMaterialID RegisteredTerrainMaterial;
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

            progress?.BeginStage("Install managed resources", "Warming cache paths", 1);
            k_Managed.Begin();
            try
            {
                if (cache == null)
                    throw new System.IO.InvalidDataException("[VVardenfell][Bootstrap] Cache loader is not available.");
            }
            finally
            {
                k_Managed.End();
            }
            progress?.Report("Managed bootstrap paths ready", 1, 1);
            progress?.CompleteStage();
            yield return null;
        }

        public static IEnumerable<object> InstallTerrainAssets(CacheLoader cache, WorldBootstrapRenderRegistryResult registries, RuntimeLoadProgress progress)
        {
            if (registries == null)
                throw new System.ArgumentNullException(nameof(registries));

            progress?.BeginStage("Terrain asset resolve", "Resolving terrain shader and materials", 1);
            k_TerrainAssets.Begin();
            try
            {
                Shader terrainShader = Shader.Find("VVardenfell/MwTerrain");
                if (terrainShader == null)
                    Debug.LogWarning("[VVardenfell] VVardenfell/MwTerrain shader missing; terrain will use URP/Lit fallback.");

                var fallbackShader = Shader.Find("Universal Render Pipeline/Lit");
                Material terrainTemplate = null;
                Material terrainFallbackMat = null;
#if UNITY_EDITOR
                var registry = cache.Registry;
                if (registry != null)
                {
                    if (terrainShader != null)
                        terrainTemplate = registry.GetOrCreateTerrainTemplate(terrainShader);
                    terrainFallbackMat = registry.GetOrCreateTerrainFallback(fallbackShader);
                    UnityEditor.AssetDatabase.SaveAssets();
                }
#endif
                if (terrainTemplate == null && terrainShader != null)
                {
                    terrainTemplate = new Material(terrainShader)
                    {
                        name = "VV:TerrainTemplate",
                        enableInstancing = true,
                    };
                    terrainTemplate.SetFloat("_TileScale", 16f);
                    terrainTemplate.SetFloat("_SplatSize", 16f);
                }

                if (terrainFallbackMat == null)
                {
                    terrainFallbackMat = new Material(fallbackShader)
                    {
                        name = "VV:TerrainFallback",
                        color = new Color(0.35f, 0.42f, 0.30f),
                    };
                }

                InstallGlobalRenderRegistries(cache, registries, terrainTemplate);
            }
            finally
            {
                k_TerrainAssets.End();
            }
            progress?.Report("Terrain assets ready", 1, 1);
            progress?.CompleteStage();
            yield return null;
        }

        static void InstallGlobalRenderRegistries(CacheLoader cache, WorldBootstrapRenderRegistryResult registries, Material terrainTemplate)
        {
            if (cache?.Meshes == null || cache.Meshes.Length == 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Mesh cache is not loaded.");
            if (cache.RefBucketKeys == null || cache.RefBucketKeys.Length == 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Texture bucket registry is not loaded.");
            if (cache.Materials == null || cache.BlendVariantCount <= 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Ref materials are not loaded.");
            if (cache.CombinedMaterials == null || cache.CombinedRenderVariantCount <= 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Combined render materials are not loaded.");
            if (terrainTemplate == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Terrain material template is not loaded.");
            if (cache.TerrainLayers?.Array == null || cache.TerrainLayers.LayerMeta0 == null || cache.TerrainLayers.LayerMeta1 == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Terrain layer atlas is not loaded.");
            if (cache.TerrainSplats == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Terrain splat array is not loaded.");

            terrainTemplate.SetTexture("_LayerArray", cache.TerrainLayers.Array);
            terrainTemplate.SetTexture("_LayerMeta0", cache.TerrainLayers.LayerMeta0);
            terrainTemplate.SetTexture("_LayerMeta1", cache.TerrainLayers.LayerMeta1);
            terrainTemplate.SetTexture("_SplatArray", cache.TerrainSplats);

            registries.TerrainMaterial = terrainTemplate;
        }

        public static IEnumerable<object> InstallRenderAssetRegistrations(CacheLoader cache, WorldBootstrapRenderRegistryResult registries, RuntimeLoadProgress progress)
        {
            if (cache == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Cache loader is not available.");
            if (registries == null || registries.TerrainMaterial == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Render registries are not initialized.");

            var world = World.DefaultGameObjectInjectionWorld;
            var renderer = world?.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (renderer == null)
                throw new System.InvalidOperationException("[VVardenfell][Rendering] EntitiesGraphicsSystem is unavailable; render assets cannot be pre-registered.");

            int meshCount = cache.Meshes?.Length ?? 0;
            int refMaterialCount = cache.Materials?.Length ?? 0;
            int combinedMaterialCount = cache.CombinedMaterials?.Length ?? 0;
            int total = meshCount + refMaterialCount + combinedMaterialCount + 1;
            progress?.BeginStage("Render asset registration", "Pre-registering render meshes and materials", total);

            registries.RegisteredMeshes = new BatchMeshID[meshCount];
            for (int i = 0; i < meshCount; i++)
            {
                var mesh = cache.Meshes[i];
                if (mesh == null)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Mesh cache entry {i} is null.");
                registries.RegisteredMeshes[i] = renderer.RegisterMesh(mesh);
                if (registries.RegisteredMeshes[i].value == 0)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Failed to pre-register mesh {i} '{mesh.name}'.");
                if ((i + 1) % MergeBatchSize == 0)
                {
                    progress?.Report($"Pre-registering meshes {i + 1}/{meshCount}", i + 1, total);
                    yield return null;
                }
            }

            registries.RegisteredRefMaterials = new BatchMaterialID[refMaterialCount];
            for (int i = 0; i < refMaterialCount; i++)
            {
                var material = cache.Materials[i];
                if (material == null)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Ref material cache entry {i} is null.");
                registries.RegisteredRefMaterials[i] = renderer.RegisterMaterial(material);
                if (registries.RegisteredRefMaterials[i].value == 0)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Failed to pre-register ref material {i} '{material.name}'.");
                int completed = meshCount + i + 1;
                if ((i + 1) % MergeBatchSize == 0)
                {
                    progress?.Report($"Pre-registering ref materials {i + 1}/{refMaterialCount}", completed, total);
                    yield return null;
                }
            }

            registries.RegisteredCombinedMaterials = new BatchMaterialID[combinedMaterialCount];
            for (int i = 0; i < combinedMaterialCount; i++)
            {
                var material = cache.CombinedMaterials[i];
                if (material == null)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Combined material cache entry {i} is null.");
                registries.RegisteredCombinedMaterials[i] = renderer.RegisterMaterial(material);
                if (registries.RegisteredCombinedMaterials[i].value == 0)
                    throw new System.IO.InvalidDataException($"[VVardenfell][Rendering] Failed to pre-register combined material {i} '{material.name}'.");
                int completed = meshCount + refMaterialCount + i + 1;
                if ((i + 1) % MergeBatchSize == 0)
                {
                    progress?.Report($"Pre-registering combined materials {i + 1}/{combinedMaterialCount}", completed, total);
                    yield return null;
                }
            }

            registries.RegisteredTerrainMaterial = renderer.RegisterMaterial(registries.TerrainMaterial);
            if (registries.RegisteredTerrainMaterial.value == 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Rendering] Failed to pre-register terrain material.");

            progress?.Report("Render assets pre-registered", total, total);
            progress?.CompleteStage("Render assets pre-registered");
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
            RuntimeSectionRegistry sectionRegistry,
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
                ulong cellHash = InteriorCellIdHash.Hash(cellId);
                if (cellHash != 0UL)
                    sectionRegistry.InteriorCellIdsByHash[cellHash] = new FixedString128Bytes(cellId);
            }
            progress?.CompleteStage("Cell section manifest ready");
        }

        public static IEnumerable<object> InstallPathGridNavigation(CacheLoader cache, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Pathgrid navigation", "Publishing pathgrid navigation resource", 1);
            if (!cache.PathGridNavigation.IsCreated)
                throw new System.IO.InvalidDataException("[VVardenfell][PathGrid] Pathgrid navigation resource is not loaded.");

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][PathGrid] Default ECS world is not ready.");

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimePathGridNavigationResource>());
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();
            var resource = new RuntimePathGridNavigationResource
            {
                Navigation = cache.ReleasePathGridNavigation(),
            };
            if (em.HasComponent<RuntimePathGridNavigationResource>(entity))
                em.SetComponentData(entity, resource);
            else
                em.AddComponentData(entity, resource);

            progress?.Report("Pathgrid navigation resource published", 1, 1);
            progress?.CompleteStage("Pathgrid navigation ready");
            yield return null;
        }

        public static IEnumerable<object> InstallColliderBlobs(WorldBootstrapCollisionLoadResult collisionLoad, RuntimeLoadProgress progress)
        {
            progress?.BeginStage("Collider blobs", "Registering interactable collider blobs", 1);
            progress?.Report("Interactable collider blobs registered", 1, 1);
            progress?.CompleteStage("Collider blobs registered");
            yield return null;
        }

        public static RuntimeMaterializationResources CreateMaterializationResources(
            CacheLoader cache,
            BlobAssetReference<Collider>[] colliderBlobs,
            WorldBootstrapRenderRegistryResult registries)
        {
            if (cache == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Cache loader is not available.");
            if (!cache.ContentBlob.IsCreated)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Runtime content blob is not loaded.");
            if (cache.ModelPrefabCatalog?.Records == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Model prefab catalog is not loaded.");
            if (cache.MaterialRecords == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Material records are not loaded.");
            if (cache.Meshes == null || cache.Meshes.Length == 0)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Mesh cache is not loaded.");
            if (colliderBlobs == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Collider blobs are not loaded.");
            if (registries == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Render registries are not loaded.");
            if (registries.TerrainMaterial == null)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Terrain material is not loaded.");
            if (!cache.TexBucketInfo.IsCreated)
                throw new System.IO.InvalidDataException("[VVardenfell][Materialization] Texture bucket info is not loaded.");

            return new RuntimeMaterializationResources
            {
                Cache = cache,
                ContentBlob = cache.ContentBlob,
                ModelPrefabRecords = cache.ModelPrefabCatalog.Records,
                MaterialRecords = cache.MaterialRecords,
                Meshes = cache.Meshes,
                TerrainMaterial = registries.TerrainMaterial,
                RegisteredMeshes = registries.RegisteredMeshes,
                RegisteredRefMaterials = registries.RegisteredRefMaterials,
                RegisteredCombinedMaterials = registries.RegisteredCombinedMaterials,
                RegisteredTerrainMaterial = registries.RegisteredTerrainMaterial,
                TerrainSplats = cache.TerrainSplats,
                TexBucketInfo = cache.TexBucketInfo,
                FallbackBucketSlice = cache.FallbackBucketSlice,
                RefBucketKeys = cache.RefBucketKeys,
                RefBucketIndexByKey = cache.RefBucketIndexByKey,
                BlendVariantCount = cache.BlendVariantCount,
                CombinedRenderVariantCount = cache.CombinedRenderVariantCount,
                ColliderBlobs = colliderBlobs,
            };
        }
    }
}
