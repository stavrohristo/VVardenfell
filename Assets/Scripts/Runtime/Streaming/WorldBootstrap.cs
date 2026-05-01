using System.Collections;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// One-time setup for the world: fills WorldResources, preloads every cell, creates
    /// startup entities, and publishes the singleton entities the runtime observes.
    /// </summary>
    public static class WorldBootstrap
    {
        public const int DefaultViewRadius = 1;
        public const int DefaultMaxLoadsPerFrame = 1;
        public const int DefaultMaxUnloadsPerFrame = 64;
        public const bool DefaultGateTerrainByRadius = false;

        static readonly ProfilerMarker k_Install = new("VV.WorldBootstrap.Install");

        public static void Install(CacheLoader cache)
        {
            using var _ = k_Install.Auto();
            RuntimeCoroutinePump.RunToCompletion(InstallIncremental(cache, new RuntimeLoadProgress(), WorldBootstrapOptions.Vanilla));
        }

        public static IEnumerator InstallIncremental(CacheLoader cache, RuntimeLoadProgress progress)
            => InstallIncremental(cache, progress, WorldBootstrapOptions.Vanilla);

        public static IEnumerator InstallIncremental(CacheLoader cache, RuntimeLoadProgress progress, WorldBootstrapOptions options)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;

            NativeHashSet<int2> available = default;
            var loadedMap = default(LoadedCellsMap);
            var logicalRefLookup = default(LogicalRefLookup);
            var placedRefRuntimeStateLookup = default(PlacedRefRuntimeStateLookup);
            var loadQueue = default(LoadQueue);
            var unloadList = default(UnloadList);
            var pendingPhysicsLoad = default(PendingCellPhysicsLoad);
            var pendingPhysicsUnload = default(PendingCellPhysicsUnload);
            Task<WorldBootstrapPreloadResult> preloadTask = null;
            Task<WorldBootstrapCollisionLoadResult> collisionTask = null;
            bool singletonInstalled = false;

            try
            {
                WorldResources.RuntimeMode = options.Mode;
                foreach (var step in WorldBootstrapResourceSetup.InstallManagedResources(cache, progress))
                    yield return step;

                foreach (var step in WorldBootstrapResourceSetup.InstallTerrainAssets(cache, progress))
                    yield return step;

                preloadTask = Task.Run(() => options.RequiresFullCellPreload
                    ? WorldBootstrapPreloadUtility.PreloadCells(cache)
                    : WorldBootstrapPreloadUtility.PreloadSandboxCells(cache, options.SandboxProfile ?? SandboxWorldFixtures.Active));
                collisionTask = Task.Run(WorldBootstrapResourceSetup.LoadCollisionBlobs);

                progress?.BeginStage("Background preload", "Waiting for cell preload and collider load", 2);
                while (!preloadTask.IsCompleted || !collisionTask.IsCompleted)
                {
                    int completed = (preloadTask.IsCompleted ? 1 : 0) + (collisionTask.IsCompleted ? 1 : 0);
                    progress?.Report("Waiting for background cache reads", completed, 2);
                    yield return null;
                }

                var preload = preloadTask.GetAwaiter().GetResult();
                var collisionLoad = collisionTask.GetAwaiter().GetResult();

                var firstPreloadFailure = WorldBootstrapPreloadUtility.GetFirstPreloadFailure(preload);
                if (firstPreloadFailure != null)
                {
                    WorldBootstrapPreloadUtility.LogPreloadFailureSummary(preload, firstPreloadFailure);
                    throw new System.IO.InvalidDataException(firstPreloadFailure.Message);
                }

                if (!string.IsNullOrEmpty(collisionLoad.Error))
                    throw new System.IO.InvalidDataException($"collisions.bin: {collisionLoad.Error}");

                if (options.IsSandbox)
                    SandboxWorldFixtureApplier.Apply(cache, preload, options.SandboxProfile ?? SandboxWorldFixtures.Active);

                progress?.Report("Background cache reads complete", 2, 2);
                progress?.CompleteStage();
                yield return null;

                WorldBootstrapStateUtility.AllocateStreamingState(
                    cache,
                    out available,
                    out loadedMap,
                    out logicalRefLookup,
                    out placedRefRuntimeStateLookup,
                    out loadQueue,
                    out unloadList,
                    out pendingPhysicsLoad,
                    out pendingPhysicsUnload);

                foreach (var step in WorldBootstrapResourceSetup.InstallPreloadedCells(cache, preload, available, progress))
                    yield return step;

                var modelDefs = cache.ModelPrefabCatalog?.Records ?? System.Array.Empty<ModelPrefabDef>();
                WorldResources.ModelPrefabs = new Entity[modelDefs.Length];
                WorldModelPrefabUtility.BuildRuntimeSpawnPrefabLookups(cache);

                foreach (var step in WorldBootstrapResourceSetup.InstallColliderBlobs(collisionLoad, progress))
                    yield return step;

                var terrainSpawn = WorldSpawner.SpawnAllTerrainIncremental(world, loadedMap, progress);
                while (terrainSpawn.MoveNext())
                    yield return terrainSpawn.Current;

                var defaultSpawn = options.PlayerStartPosition;
                var defaultCameraCell = WorldPositionToCell(defaultSpawn);
                if (WorldResources.Cells.TryGetValue(defaultCameraCell, out var startCell) && startCell != null)
                {
                    progress?.BeginStage("Initial cell", $"Spawning start cell {defaultCameraCell.x},{defaultCameraCell.y}", 1);
                    WorldSpawner.SpawnExteriorCell(
                        world,
                        defaultCameraCell,
                        startCell,
                        ref loadedMap,
                        ref logicalRefLookup,
                        active: true,
                        gateTerrainByRadius: DefaultGateTerrainByRadius);
                    progress?.Report("Start cell ready", 1, 1);
                    progress?.CompleteStage();
                    yield return null;
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[VVardenfell] default start cell {defaultCameraCell.x},{defaultCameraCell.y} is missing from the cache; player may spawn before terrain is available.");
                }

                if (options.QueueInitialExteriorCells)
                {
                    WorldBootstrapStateUtility.QueueInitialExteriorCells(
                        loadQueue,
                        available,
                        defaultCameraCell,
                        DefaultViewRadius,
                        DefaultMaxLoadsPerFrame);
                }

                progress?.BeginStage("Create singleton state", "Publishing streaming singletons", 1);
                WorldBootstrapStateUtility.PublishStreamingSingleton(
                    em,
                    available,
                    loadedMap,
                    logicalRefLookup,
                    placedRefRuntimeStateLookup,
                    loadQueue,
                    unloadList,
                    pendingPhysicsLoad,
                    pendingPhysicsUnload,
                    defaultCameraCell);
                singletonInstalled = true;
                progress?.Report("Streaming singletons ready", 1, 1);
                progress?.CompleteStage();
                yield return null;

                progress?.BeginStage("Game initialization", "Publishing game initialization payload", 1);
                WorldBootstrapStateUtility.PublishGameInitialization(em, options);
                if (options.IsSandbox)
                    WorldBootstrapStateUtility.PublishSandboxStartRequest(em);
                progress?.Report("Game initialization queued", 1, 1);
                progress?.CompleteStage();
                yield return null;
            }
            finally
            {
                if (!singletonInstalled)
                {
                    WorldBootstrapStateUtility.DisposeUnpublishedState(
                        available,
                        loadedMap,
                        logicalRefLookup,
                        placedRefRuntimeStateLookup,
                        loadQueue,
                        unloadList,
                        pendingPhysicsLoad,
                        pendingPhysicsUnload);
                }
            }
        }

        internal static float3 DefaultPlayerSpawnPosition()
        {
            float ox = -2 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = -9 * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float half = LandRecordSize.CellUnitsMw * 0.5f * WorldScale.MwUnitsToMeters;
            return new float3(ox + half, 10f, oz + half);
        }

        internal static int2 WorldPositionToCell(float3 position)
        {
            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            return new int2(
                (int)math.floor(position.x / cellM),
                (int)math.floor(position.z / cellM));
        }

        public static void Uninstall()
        {
            WorldBootstrapStateUtility.Uninstall();
        }

        internal static bool EnsureModelPrefabBuilt(EntityManager em, CacheLoader cache, int modelPrefabIndex)
        {
            return WorldModelPrefabUtility.EnsureModelPrefabBuilt(em, cache, modelPrefabIndex);
        }
    }
}
