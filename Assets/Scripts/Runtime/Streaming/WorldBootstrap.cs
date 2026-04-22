using System.Collections;
using System.IO;
using System.Threading.Tasks;
using System.Text;
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
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;
using SphereCollider = Unity.Physics.SphereCollider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// One-time setup for the world: fills WorldResources, builds per-bucket ref prefabs,
    /// preloads every cell, spawns every terrain + ref entity, and only then creates the
    /// singleton entities the runtime streaming systems observe.
    /// </summary>
    public static class WorldBootstrap
    {
        const int MergeBatchSize = 512;

        public const int DefaultViewRadius = 8;
        public const int DefaultMaxLoadsPerFrame = 64;
        public const int DefaultMaxUnloadsPerFrame = 64;
        public const bool DefaultGateTerrainByRadius = false;

        static readonly ProfilerMarker k_Install = new("VV.WorldBootstrap.Install");
        static readonly ProfilerMarker k_Managed = new("VV.Install.ManagedResources");
        static readonly ProfilerMarker k_MeshBounds = new("VV.Install.MeshBoundsCache");
        static readonly ProfilerMarker k_TerrainAssets = new("VV.Install.TerrainAssetResolve");
        static readonly ProfilerMarker k_Sentinel = new("VV.Install.SentinelCollider");
        static readonly ProfilerMarker k_RefPrefabs = new("VV.Install.RefPrefabBuild");
        static readonly ProfilerMarker k_CellPreload = new("VV.Install.CellPreload");
        static readonly ProfilerMarker k_InteractableBlobs = new("VV.Install.InteractableColliderLoad");
        static readonly ProfilerMarker k_StatCellBlobs = new("VV.Install.CellColliderTransfer");
        static readonly ProfilerMarker k_Singletons = new("VV.Install.CreateSingletons");

        sealed class PreloadResult
        {
            public CellData[] ExteriorCells;
            public PreloadFailureInfo[] ExteriorFailures;
            public CellData[] InteriorCells;
            public PreloadFailureInfo[] InteriorFailures;
        }

        enum PreloadFailureKind
        {
            MissingFile,
            TruncatedData,
            BlobVersionMismatch,
            BlobPayloadMismatch,
            CorruptData,
            Other,
        }

        sealed class PreloadFailureInfo
        {
            public bool IsInterior;
            public string CellLabel;
            public string Path;
            public PreloadFailureKind Kind;
            public string Message;
        }

        readonly struct CollisionLoadResult
        {
            public CollisionLoadResult(BlobAssetReference<Collider>[] blobs, string error)
            {
                Blobs = blobs;
                Error = error;
            }

            public BlobAssetReference<Collider>[] Blobs { get; }
            public string Error { get; }
        }

        public static void Install(CacheLoader cache)
        {
            using var _ = k_Install.Auto();
            RuntimeCoroutinePump.RunToCompletion(InstallIncremental(cache, new RuntimeLoadProgress()));
        }

        public static IEnumerator InstallIncremental(CacheLoader cache, RuntimeLoadProgress progress)
        {
            var installSw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;

            CachePaths.Warmup();

            NativeHashSet<int2> available = default;
            var loadedMap = default(LoadedCellsMap);
            var loadQueue = default(LoadQueue);
            var unloadList = default(UnloadList);
            bool singletonInstalled = false;

            try
            {
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

                progress?.BeginStage("Sentinel collider", "Creating sentinel collider", 1);
                k_Sentinel.Begin();
                try
                {
                    if (!WorldResources.SentinelCollider.IsCreated)
                    {
                        WorldResources.SentinelCollider = SphereCollider.Create(
                            new SphereGeometry { Center = float3.zero, Radius = 0.01f },
                            CollisionFilter.Zero);
                    }
                }
                finally
                {
                    k_Sentinel.End();
                }
                progress?.Report("Sentinel collider ready", 1, 1);
                progress?.CompleteStage();
                yield return null;

                var rmas = WorldResources.RefsRmas ?? System.Array.Empty<RenderMeshArray>();
                WorldResources.RefPrefabs = new Entity[rmas.Length];

                progress?.BeginStage("Ref prefab build", "Creating ref prefabs", rmas.Length);
                for (int b = 0; b < rmas.Length; b++)
                {
                    k_RefPrefabs.Begin();
                    try
                    {
                        var prefab = em.CreateEntity();
                        em.SetName(prefab, $"VVardenfell.RefPrefab[b{b}]");
                        RenderMeshUtility.AddComponents(
                            prefab, em, WorldResources.Desc, rmas[b],
                            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                        em.AddComponentData(prefab, LocalTransform.Identity);
                        em.AddComponentData(prefab, default(TextureSlice));
                        em.AddComponentData(prefab, new CellLink { Value = int2.zero });
                        em.AddComponent<Unity.Transforms.Static>(prefab);
                        em.AddComponentData(prefab, new PhysicsCollider { Value = WorldResources.SentinelCollider });
                        em.AddSharedComponent(prefab, new PhysicsWorldIndex { Value = 0 });
                        em.AddComponent<Prefab>(prefab);
                        WorldResources.RefPrefabs[b] = prefab;
                    }
                    finally
                    {
                        k_RefPrefabs.End();
                    }

                    int completed = b + 1;
                    progress?.Report($"Creating ref prefabs {completed}/{rmas.Length}", completed, rmas.Length);
                    yield return null;
                }
                progress?.CompleteStage("Ref prefabs ready");

                progress?.BeginStage("Background preload", "Scheduling cell preload and collider load", 2);
                var preloadTask = Task.Run(() => PreloadCells(cache));
                var collisionTask = Task.Run(() =>
                {
                    k_InteractableBlobs.Begin();
                    try
                    {
                        return new CollisionLoadResult(
                            CollisionLoader.LoadAll(CachePaths.Collisions, out var error),
                            error);
                    }
                    finally
                    {
                        k_InteractableBlobs.End();
                    }
                });

                while (!preloadTask.IsCompleted || !collisionTask.IsCompleted)
                {
                    int completed = (preloadTask.IsCompleted ? 1 : 0) + (collisionTask.IsCompleted ? 1 : 0);
                    progress?.Report("Waiting for background cache reads", completed, 2);
                    yield return null;
                }

                var preload = preloadTask.GetAwaiter().GetResult();
                var collisionLoad = collisionTask.GetAwaiter().GetResult();

                var firstPreloadFailure = GetFirstPreloadFailure(preload);
                if (firstPreloadFailure != null)
                {
                    LogPreloadFailureSummary(preload, firstPreloadFailure);
                    throw new InvalidDataException(firstPreloadFailure.Message);
                }

                if (!string.IsNullOrEmpty(collisionLoad.Error))
                    throw new InvalidDataException($"collisions.bin: {collisionLoad.Error}");

                progress?.Report("Background cache reads complete", 2, 2);
                progress?.CompleteStage();
                yield return null;

                int totalPreloadedCells = cache.Manifest.CellGrid.Length + cache.Manifest.InteriorCellCount;
                progress?.BeginStage("Cell preload merge", "Installing preloaded cells", totalPreloadedCells);
                available = new NativeHashSet<int2>(cache.Manifest.CellCount, Allocator.Persistent);
                WorldResources.Cells.Clear();
                WorldResources.Cells.EnsureCapacity(cache.Manifest.CellGrid.Length);
                WorldResources.InteriorCells.Clear();
                WorldResources.InteriorCells.EnsureCapacity(cache.Manifest.InteriorCellCount);
                for (int i = 0; i < cache.Manifest.CellGrid.Length; i++)
                {
                    k_CellPreload.Begin();
                    try
                    {
                        var g = cache.Manifest.CellGrid[i];
                        var coord = new int2(g.Item1, g.Item2);
                        available.Add(coord);
                        WorldResources.Cells[coord] = preload.ExteriorCells[i];
                    }
                    finally
                    {
                        k_CellPreload.End();
                    }

                    int completed = i + 1;
                    if (completed == cache.Manifest.CellGrid.Length || (completed % MergeBatchSize) == 0)
                    {
                        progress?.Report($"Installing preloaded cells {completed}/{totalPreloadedCells}", completed, totalPreloadedCells);
                        yield return null;
                    }
                }
                for (int i = 0; i < cache.Manifest.InteriorCellCount; i++)
                {
                    string cellId = cache.Manifest.InteriorCellIds[i] ?? string.Empty;
                    if (!WorldResources.InteriorCells.ContainsKey(cellId))
                        WorldResources.InteriorCells[cellId] = preload.InteriorCells[i];
                    int completed = cache.Manifest.CellGrid.Length + i + 1;
                    if (completed == totalPreloadedCells || (completed % MergeBatchSize) == 0)
                    {
                        progress?.Report($"Installing preloaded cells {completed}/{totalPreloadedCells}", completed, totalPreloadedCells);
                        yield return null;
                    }
                }
                progress?.CompleteStage("Preloaded cells installed");
                Debug.Log($"[VVardenfell] preloaded {WorldResources.Cells.Count}/{cache.Manifest.CellGrid.Length} exterior cells and {WorldResources.InteriorCells.Count}/{cache.Manifest.InteriorCellCount} interiors");

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
                Debug.Log($"[VVardenfell] collider blobs: {WorldResources.ColliderBlobs.Length} interactable, {statCellsWithCol} STAT-cell, {terrainCellsWithCol} terrain-cell");

                int cellCap = System.Math.Max(cache.Manifest.CellCount, 128);
                loadedMap = new LoadedCellsMap
                {
                    Map = new NativeHashMap<int2, Entity>(cellCap, Allocator.Persistent),
                    Active = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
                };
                loadQueue = new LoadQueue
                {
                    Queue = new NativeQueue<int2>(Allocator.Persistent),
                };
                unloadList = new UnloadList
                {
                    PendingEntityDestroy = new NativeList<int2>(32, Allocator.Persistent),
                };

                yield return WorldSpawner.SpawnAllIncremental(world, cache, loadedMap, DefaultGateTerrainByRadius, progress);

                progress?.BeginStage("Create singleton state", "Publishing streaming singletons", 1);
                k_Singletons.Begin();
                try
                {
                    var singleton = em.CreateEntity();
                    em.SetName(singleton, "VVardenfell.World");
                    em.AddComponentData(singleton, new StreamingConfig
                    {
                        ViewRadius = DefaultViewRadius,
                        MaxLoadsPerFrame = DefaultMaxLoadsPerFrame,
                        MaxUnloadsPerFrame = DefaultMaxUnloadsPerFrame,
                        GateTerrainByRadius = DefaultGateTerrainByRadius,
                        ExteriorStreamingPaused = false,
                        CameraCell = new int2(int.MinValue, int.MinValue),
                    });
                    em.AddComponentData(singleton, new AvailableCells { Set = available });
                    em.AddComponentData(singleton, loadedMap);
                    em.AddComponentData(singleton, loadQueue);
                    em.AddComponentData(singleton, unloadList);
                    singletonInstalled = true;
                }
                finally
                {
                    k_Singletons.End();
                }
                progress?.Report("Streaming singletons ready", 1, 1);
                progress?.CompleteStage();
                yield return null;

                progress?.BeginStage("Game initialization", "Publishing game initialization payload", 1);
                try
                {
                    var initEntity = em.CreateEntity();
                    em.SetName(initEntity, "VVardenfell.GameInitialization");
                    em.AddComponentData(initEntity, new GameInitializationSingleton
                    {
                        PlayerSettings = ResolvePlayerMovementSettings(),
                        PlayerPosition = DefaultPlayerSpawnPosition(),
                        PlayerRotation = quaternion.identity,
                        PlayerPitchDegrees = 0f,
                        HasSerializedSavePayload = false,
                    });
                }
                finally
                {
                }
                progress?.Report("Game initialization queued", 1, 1);
                progress?.CompleteStage();
                yield return null;
            }
            finally
            {
                if (!singletonInstalled)
                {
                    if (available.IsCreated)
                        available.Dispose();
                    if (loadedMap.Map.IsCreated)
                        loadedMap.Map.Dispose();
                    if (loadedMap.Active.IsCreated)
                        loadedMap.Active.Dispose();
                    if (loadQueue.Queue.IsCreated)
                        loadQueue.Queue.Dispose();
                    if (unloadList.PendingEntityDestroy.IsCreated)
                        unloadList.PendingEntityDestroy.Dispose();
                }
            }

            installSw.Stop();
            Debug.Log($"[VVardenfell] world install in {installSw.ElapsedMilliseconds}ms");
        }

        internal static float3 DefaultPlayerSpawnPosition()
        {
            float ox = -2 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = -9 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float half = LandRecordSize.CellUnitsMw * 0.5f * WorldScale.MwUnitsToMeters;
            return new float3(ox + half, 10f, oz + half);
        }

        private static PlayerCharacterComponent ResolvePlayerMovementSettings()
            => BootstrapController.ResolvePlayerMovementSettings();

        public static void Uninstall()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                WorldResources.Reset();
                return;
            }

            var em = world.EntityManager;

            foreach (var e in em.CreateEntityQuery(typeof(PlayerStanceColliders)).ToEntityArray(Allocator.Temp))
            {
                var stance = em.GetComponentData<PlayerStanceColliders>(e);
                if (stance.Standing.IsCreated) stance.Standing.Dispose();
                if (stance.Crouching.IsCreated) stance.Crouching.Dispose();
            }

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

        private static PreloadResult PreloadCells(CacheLoader cache)
        {
            var cellGrid = cache.Manifest.CellGrid;
            var loaded = new CellData[cellGrid.Length];
            var failures = new PreloadFailureInfo[cellGrid.Length];
            var interiorIds = cache.Manifest.InteriorCellIds ?? System.Array.Empty<string>();
            var loadedInteriors = new CellData[interiorIds.Length];
            var interiorFailures = new PreloadFailureInfo[interiorIds.Length];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = System.Math.Min(8, System.Math.Max(1, System.Environment.ProcessorCount / 2)),
            };

            Parallel.For(0, cellGrid.Length, options, i =>
            {
                var g = cellGrid[i];
                string path = CachePaths.CellFile(g.Item1, g.Item2);
                if (!System.IO.File.Exists(path))
                {
                    failures[i] = CreateMissingFileFailure(
                        isInterior: false,
                        cellLabel: $"({g.Item1},{g.Item2})",
                        path: path);
                    return;
                }

                try
                {
                    loaded[i] = CellFile.Read(path);
                }
                catch (System.Exception ex)
                {
                    failures[i] = CreatePreloadFailure(
                        isInterior: false,
                        cellLabel: $"({g.Item1},{g.Item2})",
                        path: path,
                        ex: ex);
                }
            });

            Parallel.For(0, interiorIds.Length, options, i =>
            {
                string cellId = interiorIds[i] ?? string.Empty;
                string path = CachePaths.InteriorCellFile(cellId);
                if (!System.IO.File.Exists(path))
                {
                    interiorFailures[i] = CreateMissingFileFailure(
                        isInterior: true,
                        cellLabel: cellId,
                        path: path);
                    return;
                }

                try
                {
                    loadedInteriors[i] = CellFile.Read(path, isInterior: true, cellId: cellId);
                }
                catch (System.Exception ex)
                {
                    interiorFailures[i] = CreatePreloadFailure(
                        isInterior: true,
                        cellLabel: cellId,
                        path: path,
                        ex: ex);
                }
            });

            return new PreloadResult
            {
                ExteriorCells = loaded,
                ExteriorFailures = failures,
                InteriorCells = loadedInteriors,
                InteriorFailures = interiorFailures,
            };
        }

        private static PreloadFailureInfo CreateMissingFileFailure(bool isInterior, string cellLabel, string path)
        {
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new PreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = PreloadFailureKind.MissingFile,
                Message = $"[VVardenfell] missing baked {target} file at '{path}'",
            };
        }

        private static PreloadFailureInfo CreatePreloadFailure(bool isInterior, string cellLabel, string path, System.Exception ex)
        {
            var kind = ClassifyPreloadFailure(ex);
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new PreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = kind,
                Message = $"[VVardenfell] failed preloading {target} at '{path}': {ex.Message}",
            };
        }

        private static PreloadFailureKind ClassifyPreloadFailure(System.Exception ex)
        {
            string message = FlattenExceptionMessage(ex);
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                return PreloadFailureKind.MissingFile;
            if (message.IndexOf("version mismatch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return PreloadFailureKind.BlobVersionMismatch;
            if (message.IndexOf("deserialize", System.StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("blob payload", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return PreloadFailureKind.BlobPayloadMismatch;
            if (message.IndexOf("truncated", System.StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("beyond the end of the stream", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return PreloadFailureKind.TruncatedData;
            if (ex is InvalidDataException)
                return PreloadFailureKind.CorruptData;
            return PreloadFailureKind.Other;
        }

        private static string FlattenExceptionMessage(System.Exception ex)
        {
            if (ex == null)
                return string.Empty;

            var sb = new StringBuilder(256);
            for (var cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");
                sb.Append(cursor.Message);
            }
            return sb.ToString();
        }

        private static PreloadFailureInfo GetFirstPreloadFailure(PreloadResult result)
        {
            if (result?.ExteriorFailures != null)
            {
                for (int i = 0; i < result.ExteriorFailures.Length; i++)
                {
                    if (result.ExteriorFailures[i] != null)
                        return result.ExteriorFailures[i];
                }
            }

            if (result?.InteriorFailures != null)
            {
                for (int i = 0; i < result.InteriorFailures.Length; i++)
                {
                    if (result.InteriorFailures[i] != null)
                        return result.InteriorFailures[i];
                }
            }

            return null;
        }

        private static void LogPreloadFailureSummary(PreloadResult result, PreloadFailureInfo firstFailure)
        {
            int exteriorFailures = 0;
            int interiorFailures = 0;
            var countsByKind = new int[System.Enum.GetValues(typeof(PreloadFailureKind)).Length];

            AccumulateFailures(result?.ExteriorFailures, ref exteriorFailures, countsByKind);
            AccumulateFailures(result?.InteriorFailures, ref interiorFailures, countsByKind);

            var sb = new StringBuilder(512);
            sb.Append("[VVardenfell] preload failed during background cache reads. First failure: ")
                .Append(firstFailure.IsInterior ? "interior '" : "cell ")
                .Append(firstFailure.CellLabel)
                .Append(firstFailure.IsInterior ? "'" : string.Empty)
                .Append(" [")
                .Append(firstFailure.Kind)
                .Append("] ")
                .Append(firstFailure.Path)
                .AppendLine();
            sb.Append("Summary: ")
                .Append(exteriorFailures)
                .Append(" exterior, ")
                .Append(interiorFailures)
                .Append(" interior failures.");

            bool appendedBreakdown = false;
            for (int i = 0; i < countsByKind.Length; i++)
            {
                if (countsByKind[i] == 0)
                    continue;

                if (!appendedBreakdown)
                {
                    sb.Append(" Breakdown:");
                    appendedBreakdown = true;
                }

                sb.Append(' ')
                    .Append((PreloadFailureKind)i)
                    .Append('=')
                    .Append(countsByKind[i]);
            }

            Debug.LogError(sb.ToString());
        }

        private static void AccumulateFailures(PreloadFailureInfo[] failures, ref int total, int[] countsByKind)
        {
            if (failures == null)
                return;

            for (int i = 0; i < failures.Length; i++)
            {
                var failure = failures[i];
                if (failure == null)
                    continue;

                total++;
                countsByKind[(int)failure.Kind]++;
            }
        }
    }
}
