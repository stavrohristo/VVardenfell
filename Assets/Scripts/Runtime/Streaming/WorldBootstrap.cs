using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// One-time setup for the world: fills <see cref="WorldResources"/>, builds per-bucket
    /// ref prefabs, preloads every cell, spawns every terrain + ref entity in a disabled
    /// state (via <see cref="WorldSpawner.SpawnAll"/>), and creates the singleton entities
    /// the visibility-gating systems query for. Call once from
    /// <see cref="Bootstrap.BootstrapController.DoLoad"/> after the cache has loaded.
    ///
    /// After this runs the entire world is resident in ECS. Per-frame systems only flip
    /// <see cref="MaterialMeshInfo"/> enable bits to gate visibility by camera view radius —
    /// zero Instantiate/Destroy ever happens at runtime, so chunk layout stays frozen.
    /// </summary>
    public static class WorldBootstrap
    {
        public const int DefaultViewRadius = 8;
        public const int DefaultMaxLoadsPerFrame = 64;
        public const int DefaultMaxUnloadsPerFrame = 64;
        /// <summary>When false, terrain stays visible for every cell regardless of view radius.</summary>
        public const bool DefaultGateTerrainByRadius = false;

        public static void Install(CacheLoader cache)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;

            // Cache Unity's persistent-data root on the main thread before any worker
            // resolves cell file paths during parallel preload.
            CachePaths.Warmup();

            // --- Managed resources ---
            WorldResources.Cache = cache;
            // RefsRmas populated by CacheLoader (one RMA per texture-dimension bucket).
            WorldResources.Desc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

            // Cache each mesh's local AABB so the Burst ref-spawn job can patch RenderBounds
            // per ref (Unity.Mathematics.AABB uses center+extents; UnityEngine.Bounds uses
            // center+size, hence the ×0.5 on extents below).
            WorldResources.MeshBounds = new NativeArray<AABB>(cache.Meshes.Length, Allocator.Persistent);
            for (int i = 0; i < cache.Meshes.Length; i++)
            {
                var b = cache.Meshes[i].bounds;
                WorldResources.MeshBounds[i] = new AABB { Center = b.center, Extents = b.extents };
            }

            WorldResources.TerrainShader = Shader.Find("VVardenfell/MwTerrain");
            if (WorldResources.TerrainShader == null)
                Debug.LogWarning("[VVardenfell] VVardenfell/MwTerrain shader missing; terrain will use URP/Lit fallback.");

            var fallbackShader = Shader.Find("Universal Render Pipeline/Lit");

#if UNITY_EDITOR
            // Resolve terrain template + fallback via the registry so user tweaks in
            // the inspector persist across Play-mode runs. Creates the assets on first
            // boot.
            var registry = cache.Registry; // populated by CacheLoader in editor
            if (registry != null)
            {
                if (WorldResources.TerrainShader != null)
                    WorldResources.TerrainTemplate = registry.GetOrCreateTerrainTemplate(WorldResources.TerrainShader);
                WorldResources.TerrainFallbackMat = registry.GetOrCreateTerrainFallback(fallbackShader);
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif

            // Standalone builds (or editor without registry) fall back to ephemeral mats.
            if (WorldResources.TerrainFallbackMat == null)
            {
                WorldResources.TerrainFallbackMat = new Material(fallbackShader)
                {
                    name = "VV:TerrainFallback",
                    color = new Color(0.35f, 0.42f, 0.30f),
                };
            }

            // --- Ref Prefabs (one per texture-dimension bucket) ---
            // Each prefab references its bucket's RenderMeshArray. Since RMA is a managed
            // ISharedComponentData, different RMAs put clones into different ECS chunks —
            // refs with 128² textures never share a chunk with refs using 16² textures,
            // so BRG can issue proper per-bucket draw batches.
            var rmas = WorldResources.RefsRmas;
            WorldResources.RefPrefabs = new Entity[rmas.Length];
            for (int b = 0; b < rmas.Length; b++)
            {
                var prefab = em.CreateEntity();
                em.SetName(prefab, $"VVardenfell.RefPrefab[b{b}]");
                RenderMeshUtility.AddComponents(
                    prefab, em, WorldResources.Desc, rmas[b],
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                em.AddComponentData(prefab, LocalTransform.Identity);
                // Per-instance bucket-local slice, overwritten per-ref during spawn.
                em.AddComponentData(prefab, default(TextureSlice));
                // Regular IComponentData — see CellLink's docstring for why.
                em.AddComponentData(prefab, new CellLink { Value = int2.zero });
                em.AddComponent<Prefab>(prefab);
                WorldResources.RefPrefabs[b] = prefab;
            }

            // --- Singletons ---
            var singleton = em.CreateEntity();
            em.SetName(singleton, "VVardenfell.World");

            // Preload every baked cell into RAM. Disk reads + binary decode are independent,
            // so fan them out across a small worker pool and merge back in manifest order to
            // keep the final dictionary deterministic.
            var available = new NativeHashSet<int2>(cache.Manifest.CellCount, Allocator.Persistent);
            WorldResources.Cells.Clear();
            WorldResources.Cells.EnsureCapacity(cache.Manifest.CellGrid.Length);
            var preloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var preloadedCells = PreloadCells(cache);
            for (int i = 0; i < cache.Manifest.CellGrid.Length; i++)
            {
                var g = cache.Manifest.CellGrid[i];
                var coord = new int2(g.Item1, g.Item2);
                available.Add(coord);
                var cell = preloadedCells[i];
                if (cell != null)
                    WorldResources.Cells[coord] = cell;
            }
            preloadStopwatch.Stop();
            Debug.Log($"[VVardenfell] preloaded {WorldResources.Cells.Count}/{cache.Manifest.CellGrid.Length} baked cells in {preloadStopwatch.ElapsedMilliseconds}ms");

            em.AddComponentData(singleton, new StreamingConfig
            {
                ViewRadius = DefaultViewRadius,
                MaxLoadsPerFrame = DefaultMaxLoadsPerFrame,
                MaxUnloadsPerFrame = DefaultMaxUnloadsPerFrame,
                CameraCell = new int2(int.MinValue, int.MinValue), 
            });
            em.AddComponentData(singleton, new AvailableCells { Set = available });
            // Sized for the whole world since every cell gets spawned eagerly below.
            int cellCap = System.Math.Max(cache.Manifest.CellCount, 128);
            var loadedMap = new LoadedCellsMap
            {
                Map    = new NativeHashMap<int2, Entity>(cellCap, Allocator.Persistent),
                Active = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
            };
            em.AddComponentData(singleton, loadedMap);
            em.AddComponentData(singleton, new LoadQueue
            {
                Queue = new NativeQueue<int2>(Allocator.Persistent),
            });
            em.AddComponentData(singleton, new UnloadList
            {
                PendingEntityDestroy = new NativeList<int2>(32, Allocator.Persistent),
            });

            // Eager-spawn every cell's terrain + refs. When GateTerrainByRadius is false
            // only refs start disabled — terrain stays visible so the player can see
            // distant hills even for cells outside the view radius. Streaming systems
            // only flip MaterialMeshInfo enable bits from here on.
            // NOTE: loadedMap's Map/Active are NativeHashMap/Set — internally pointer-backed,
            // so mutations through this local copy are visible via the ECS-stored copy too.
            WorldSpawner.SpawnAll(world, cache, ref loadedMap, DefaultGateTerrainByRadius);

            PointCameraAtSeydaNeen();
        }

        /// <summary>Dispose native collections; call from the world's teardown path.</summary>
        public static void Uninstall()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                WorldResources.Reset();
                return;
            }
            var em = world.EntityManager;

            foreach (var e in em.CreateEntityQuery(typeof(AvailableCells)).ToEntityArray(Allocator.Temp))
                em.GetComponentData<AvailableCells>(e).Set.Dispose();
            foreach (var e in em.CreateEntityQuery(typeof(LoadedCellsMap)).ToEntityArray(Allocator.Temp))
            {
                var lc = em.GetComponentData<LoadedCellsMap>(e);
                lc.Map.Dispose();
                lc.Active.Dispose();
            }
            foreach (var e in em.CreateEntityQuery(typeof(LoadQueue)).ToEntityArray(Allocator.Temp))
                em.GetComponentData<LoadQueue>(e).Queue.Dispose();
            foreach (var e in em.CreateEntityQuery(typeof(UnloadList)).ToEntityArray(Allocator.Temp))
            {
                var u = em.GetComponentData<UnloadList>(e);
                u.PendingEntityDestroy.Dispose();
            }

            WorldResources.Reset();
        }

        private static void PointCameraAtSeydaNeen()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float ox = -2 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = -9 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float half = LandRecordSize.CellUnitsMw * 0.5f * WorldScale.MwUnitsToMeters;
            var center = new Vector3(ox + half, 5f, oz + half);
            cam.transform.position = center + new Vector3(0, 40f, -40f);
            cam.transform.LookAt(center);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4000f);
        }

        private static CellData[] PreloadCells(CacheLoader cache)
        {
            var cellGrid = cache.Manifest.CellGrid;
            var loaded = new CellData[cellGrid.Length];
            var errors = new string[cellGrid.Length];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = System.Math.Min(8, System.Math.Max(1, System.Environment.ProcessorCount / 2)),
            };

            Parallel.For(0, cellGrid.Length, options, i =>
            {
                var g = cellGrid[i];
                string path = CachePaths.CellFile(g.Item1, g.Item2);
                if (!System.IO.File.Exists(path))
                    return;

                try
                {
                    loaded[i] = CellFile.Read(path);
                }
                catch (System.Exception ex)
                {
                    errors[i] = $"[VVardenfell] failed preloading cell ({g.Item1},{g.Item2}): {ex.Message}";
                }
            });

            for (int i = 0; i < errors.Length; i++)
            {
                if (!string.IsNullOrEmpty(errors[i]))
                    Debug.LogWarning(errors[i]);
            }

            return loaded;
        }
    }
}
