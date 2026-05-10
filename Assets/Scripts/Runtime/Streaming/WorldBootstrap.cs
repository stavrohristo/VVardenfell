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
using VVardenfell.Runtime.Physics;
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
            WorldBootstrapPreloadResult preload = null;
            Task<WorldBootstrapCollisionLoadResult> collisionTask = null;
            bool singletonInstalled = false;

            try
            {
                WorldResources.RuntimeMode = options.Mode;
                WorldBootstrapStateUtility.PublishRuntimeMode(em, options.Mode);
                RuntimeBootstrapRequestUtility.PublishAll(em);
                foreach (var step in WorldBootstrapResourceSetup.InstallManagedResources(cache, progress))
                    yield return step;

                foreach (var step in WorldBootstrapResourceSetup.InstallTerrainAssets(cache, progress))
                    yield return step;

                collisionTask = Task.Run(WorldBootstrapResourceSetup.LoadCollisionBlobs);

                progress?.BeginStage("Cell section validation", "Validating DOTS cell section cache", 1);
                preload = options.RequiresFullCellPreload
                    ? WorldBootstrapPreloadUtility.PreloadCells(cache)
                    : WorldBootstrapPreloadUtility.PreloadSandboxCells(cache, options.SandboxProfile ?? SandboxWorldFixtures.Active);
                progress?.Report("Cell sections validated", 1, 1);
                progress?.CompleteStage();
                yield return null;

                progress?.BeginStage("Background preload", "Waiting for collider load", 1);
                while (!collisionTask.IsCompleted)
                {
                    progress?.Report("Waiting for background cache reads", 0, 1);
                    yield return null;
                }

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
                    SandboxWorldFixtureApplier.Apply(cache, options.SandboxProfile ?? SandboxWorldFixtures.Active);

                progress?.Report("Background cache reads complete", 1, 1);
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

                foreach (var step in WorldBootstrapResourceSetup.InstallAvailableCells(cache, available, progress))
                    yield return step;

                foreach (var step in WorldBootstrapResourceSetup.InstallColliderBlobs(collisionLoad, progress))
                    yield return step;

                progress?.BeginStage("Spawn prefabs", "Loading runtime spawn prefab cache", 1);
                RuntimeSpawnPrefabMaterializer.LoadAndMaterialize(em, cache);
                progress?.Report("Runtime spawn prefabs ready", 1, 1);
                progress?.CompleteStage();
                yield return null;

                EnsurePhysicsMutationQueueReadyForDirectCellSpawn(world, em);

                var defaultSpawn = options.PlayerStartPosition;
                var defaultCameraCell = WorldPositionToCell(defaultSpawn);
                if (options.SpawnInitialExteriorCell && WorldSpawner.TrySpawnExteriorCellByCoord(
                        world,
                        defaultCameraCell,
                        ref loadedMap,
                        ref logicalRefLookup,
                        active: true,
                        gateTerrainByRadius: DefaultGateTerrainByRadius))
                {
                    progress?.BeginStage("Initial cell", $"Spawning start cell {defaultCameraCell.x},{defaultCameraCell.y}", 1);
                    progress?.Report("Start cell ready", 1, 1);
                    progress?.CompleteStage();
                    yield return null;
                }
                else if (options.SpawnInitialExteriorCell)
                {
                    UnityEngine.Debug.LogWarning($"[VVardenfell] default start cell {defaultCameraCell.x},{defaultCameraCell.y} is missing from the cache; player may spawn before terrain is available.");
                }

                if (options.QueueInitialExteriorCells)
                {
                    int queueRadius = options.SandboxProfile != null
                        ? math.max(0, options.SandboxProfile.PreloadExteriorCellRadius)
                        : DefaultViewRadius;
                    int maxQueuedCells = math.max(1, (queueRadius * 2 + 1) * (queueRadius * 2 + 1));
                    WorldBootstrapStateUtility.QueueInitialExteriorCells(
                        loadQueue,
                        available,
                        defaultCameraCell,
                        queueRadius,
                        maxQueuedCells);
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

        static void EnsurePhysicsMutationQueueReadyForDirectCellSpawn(World world, EntityManager em)
        {
            EntityQuery query = PhysicsMutationQueueQueryCache.Get(em);
            if (!query.IsEmptyIgnoreFilter)
                return;

            RuntimeBootstrapRequestUtility.Publish<RuntimePhysicsMutationBootstrapRequest>(
                em,
                "VVardenfell.RuntimePhysicsMutationBootstrapRequest");
            SystemHandle bootstrapSystem = world.Unmanaged.GetExistingUnmanagedSystem<RuntimePhysicsMutationBootstrapSystem>();
            if (bootstrapSystem.Equals(SystemHandle.Null))
                throw new System.InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation bootstrap system is unavailable.");
            bootstrapSystem.Update(world.Unmanaged);
            if (query.IsEmptyIgnoreFilter)
                throw new System.InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation bootstrap did not create the mutation queue.");
        }

        public static void Uninstall()
        {
            WorldBootstrapStateUtility.Uninstall();
        }

        static class PhysicsMutationQueueQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                    ComponentType.ReadWrite<RuntimePhysicsMutationRequest>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
