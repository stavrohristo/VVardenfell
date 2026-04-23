using System.Collections;
using System.Collections.Generic;
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
using Stopwatch = System.Diagnostics.Stopwatch;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldState;

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

        public const int DefaultViewRadius = 1;
        public const int DefaultMaxLoadsPerFrame = 1;
        public const int DefaultMaxUnloadsPerFrame = 64;
        public const bool DefaultGateTerrainByRadius = true;

        static readonly ProfilerMarker k_Install = new("VV.WorldBootstrap.Install");
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
            PipelineMismatch,
            UnsupportedSpawnMode,
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
            var logicalRefLookup = default(LogicalRefLookup);
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

                var rmas = WorldResources.RefsRmas ?? System.Array.Empty<RenderMeshArray>();
                WorldResources.RefPrefabs = new Entity[rmas.Length];

                progress?.BeginStage("Ref prefab build", "Creating ref prefabs", rmas.Length);
                var prefabBuildSw = Stopwatch.StartNew();
                for (int b = 0; b < rmas.Length; b++)
                {
                    k_RefPrefabs.Begin();
                    try
                    {
                        Entity prefab;
                        k_RefPrefabCreateEntity.Begin();
                        try
                        {
                            prefab = em.CreateEntity();
                        }
                        finally
                        {
                            k_RefPrefabCreateEntity.End();
                        }

                        em.SetName(prefab, $"VVardenfell.RefPrefab[b{b}]");

                        k_RefPrefabAddRenderMesh.Begin();
                        try
                        {
                            RenderMeshUtility.AddComponents(
                                prefab, em, WorldResources.Desc, rmas[b],
                                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                        }
                        finally
                        {
                            k_RefPrefabAddRenderMesh.End();
                        }

                        k_RefPrefabSetup.Begin();
                        try
                        {
                            em.AddComponentData(prefab, LocalTransform.Identity);
                            em.AddComponentData(prefab, default(TextureSlice));
                            em.AddComponentData(prefab, new CellLink { Value = int2.zero });
                            em.AddComponent<Unity.Transforms.Static>(prefab);
                            em.AddSharedComponent(prefab, new PhysicsWorldIndex { Value = 0 });
                            em.AddComponent<Prefab>(prefab);
                        }
                        finally
                        {
                            k_RefPrefabSetup.End();
                        }

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
                prefabBuildSw.Stop();
                progress?.CompleteStage("Ref prefabs ready");
                if (rmas.Length > 0)
                {
                    double averageMs = prefabBuildSw.Elapsed.TotalMilliseconds / rmas.Length;
                    Debug.Log($"[VVardenfell] ref prefab build: prefabs={rmas.Length}, elapsedMs={prefabBuildSw.Elapsed.TotalMilliseconds:F2}, avgMsPerPrefab={averageMs:F3}");
                }
                else
                {
                    Debug.Log("[VVardenfell] ref prefab build: prefabs=0, elapsedMs=0.00, avgMsPerPrefab=0.000");
                }

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

                var modelDefs = cache.ModelPrefabCatalog?.Records ?? System.Array.Empty<ModelPrefabDef>();
                WorldResources.ModelPrefabs = new Entity[modelDefs.Length];
                BuildRuntimeSpawnPrefabLookups(cache);
                Debug.Log($"[VVardenfell] model prefab build deferred: catalog={modelDefs.Length}");

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
                logicalRefLookup = new LogicalRefLookup
                {
                    Map = new NativeParallelHashMap<uint, Entity>(System.Math.Max(cellCap * 8, 1024), Allocator.Persistent),
                };
                loadQueue = new LoadQueue
                {
                    Queue = new NativeQueue<int2>(Allocator.Persistent),
                };
                unloadList = new UnloadList
                {
                    PendingEntityDestroy = new NativeList<int2>(32, Allocator.Persistent),
                };

                var defaultSpawn = DefaultPlayerSpawnPosition();
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
                    Debug.LogWarning($"[VVardenfell] default start cell {defaultCameraCell.x},{defaultCameraCell.y} is missing from the cache; player may spawn before terrain is available.");
                }

                QueueInitialExteriorCells(
                    ref loadQueue,
                    available,
                    defaultCameraCell,
                    DefaultViewRadius,
                    DefaultMaxLoadsPerFrame);

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
                        ExteriorStreamingPaused = true,
                        CameraCell = defaultCameraCell,
                    });
                    em.AddComponentData(singleton, new AvailableCells { Set = available });
                    em.AddComponentData(singleton, loadedMap);
                    em.AddComponentData(singleton, logicalRefLookup);
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
                    bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
                    var initEntity = em.CreateEntity();
                    em.SetName(initEntity, "VVardenfell.GameInitialization");
                    em.AddComponentData(initEntity, new GameInitializationSingleton
                    {
                        PlayerSettings = ResolvePlayerMovementSettings(),
                        PlayerPosition = DefaultPlayerSpawnPosition(),
                        PlayerRotation = quaternion.identity,
                        PlayerPitchDegrees = 0f,
                        HasSerializedSavePayload = hasSerializedSavePayload,
                        SerializedSavePayloadStatus = new FixedString128Bytes(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
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
                    if (logicalRefLookup.Map.IsCreated)
                        logicalRefLookup.Map.Dispose();
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

        internal static int2 WorldPositionToCell(float3 position)
        {
            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            return new int2(
                (int)math.floor(position.x / cellM),
                (int)math.floor(position.z / cellM));
        }

        private static void QueueInitialExteriorCells(
            ref LoadQueue loadQueue,
            NativeHashSet<int2> available,
            int2 cameraCell,
            int radius,
            int maxCells)
        {
            if (!loadQueue.Queue.IsCreated || !available.IsCreated)
                return;

            int queued = 0;
            for (int dy = -radius; dy <= radius && queued < maxCells; dy++)
            {
                for (int dx = -radius; dx <= radius && queued < maxCells; dx++)
                {
                    var coord = new int2(cameraCell.x + dx, cameraCell.y + dy);
                    if (!available.Contains(coord))
                        continue;

                    loadQueue.Queue.Enqueue(coord);
                    queued++;
                }
            }
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

            foreach (var e in em.CreateEntityQuery(typeof(LogicalRefLookup)).ToEntityArray(Allocator.Temp))
            {
                var lookup = em.GetComponentData<LogicalRefLookup>(e);
                lookup.Map.Dispose();
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

        private static Entity BuildModelPrefabEntityGraph(
            EntityManager em,
            CacheLoader cache,
            ModelPrefabDef def,
            int modelPrefabIndex,
            Dictionary<long, RenderMeshArray> renderArrayCache)
        {
            if (def == null || def.Nodes == null || def.Nodes.Length == 0)
                return Entity.Null;

            var entities = new Entity[def.Nodes.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = em.CreateEntity();
                em.AddComponent<Prefab>(entities[i]);
                em.AddComponent<ModelPrefabNodeTag>(entities[i]);
                em.AddComponentData(entities[i], LocalTransform.FromPositionRotationScale(
                    new float3(def.Nodes[i].PosX, def.Nodes[i].PosY, def.Nodes[i].PosZ),
                    new quaternion(def.Nodes[i].RotX, def.Nodes[i].RotY, def.Nodes[i].RotZ, def.Nodes[i].RotW),
                    def.Nodes[i].Scale));
                em.AddComponentData(entities[i], new LocalToWorld { Value = float4x4.identity });
            }

            int rootIndex = math.clamp(def.RootNodeIndex, 0, entities.Length - 1);
            Entity root = entities[rootIndex];
            em.SetName(root, $"VVardenfell.ModelPrefab[{modelPrefabIndex}]");
            em.AddComponentData(root, new ModelPrefabRoot { ModelPrefabIndex = modelPrefabIndex });
            var linkedGroup = em.AddBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < entities.Length; i++)
                linkedGroup.Add(new LinkedEntityGroup { Value = entities[i] });

            for (int i = 0; i < entities.Length; i++)
            {
                var node = def.Nodes[i];
                var entity = entities[i];

                if (i != rootIndex && node.ParentIndex >= 0 && node.ParentIndex < entities.Length)
                    em.AddComponentData(entity, new Parent { Value = entities[node.ParentIndex] });

                if (node.Kind == ModelPrefabNodeKind.RenderLeaf && node.GlobalMeshIndex >= 0)
                {
                    int bucketIndex = node.TextureIndex >= 0 && node.TextureIndex < WorldResources.TexBucketInfo.Length
                        ? WorldResources.TexBucketInfo[node.TextureIndex].x
                        : WorldResources.FallbackBucketSlice.x;
                    int textureSlice = node.TextureIndex >= 0 && node.TextureIndex < WorldResources.TexBucketInfo.Length
                        ? WorldResources.TexBucketInfo[node.TextureIndex].y
                        : WorldResources.FallbackBucketSlice.y;

                    var rma = GetOrCreateLeafRenderMeshArray(cache, renderArrayCache, bucketIndex, node.GlobalMeshIndex);
                    RenderMeshUtility.AddComponents(
                        entity,
                        em,
                        WorldResources.Desc,
                        rma,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(math.max(0, node.MaterialIndex), 0));
                    em.AddComponentData(entity, new TextureSlice { Value = textureSlice });
                    em.AddComponentData(entity, new RenderBounds
                    {
                        Value = new AABB
                        {
                            Center = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                            Extents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                        }
                    });
                    em.AddComponentData(entity, new ModelPrefabRenderLeaf
                    {
                        MeshIndex = node.GlobalMeshIndex,
                        MaterialIndex = node.MaterialIndex,
                        TextureIndex = node.TextureIndex,
                    });
                }

                if (node.Kind == ModelPrefabNodeKind.Billboard)
                {
                    em.AddComponent<ModelBillboardTag>(entity);
                    em.AddComponentData(entity, new ModelBillboardState
                    {
                        BaseLocalRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                    });
                }
            }

            return root;
        }

        internal static bool EnsureModelPrefabBuilt(EntityManager em, CacheLoader cache, int modelPrefabIndex)
        {
            if (cache?.ModelPrefabCatalog?.Records == null)
                return false;

            var modelDefs = cache.ModelPrefabCatalog.Records;
            if ((uint)modelPrefabIndex >= (uint)modelDefs.Length)
                return false;

            if (WorldResources.ModelPrefabs == null || WorldResources.ModelPrefabs.Length != modelDefs.Length)
                WorldResources.ModelPrefabs = new Entity[modelDefs.Length];

            Entity existing = WorldResources.ModelPrefabs[modelPrefabIndex];
            if (existing != Entity.Null && em.Exists(existing))
                return true;

            var localRenderArrayCache = new Dictionary<long, RenderMeshArray>();
            Entity built = BuildModelPrefabEntityGraph(em, cache, modelDefs[modelPrefabIndex], modelPrefabIndex, localRenderArrayCache);
            WorldResources.ModelPrefabs[modelPrefabIndex] = built;
            return built != Entity.Null;
        }

        private static RenderMeshArray GetOrCreateLeafRenderMeshArray(
            CacheLoader cache,
            Dictionary<long, RenderMeshArray> renderArrayCache,
            int bucketIndex,
            int meshIndex)
        {
            long key = ((long)bucketIndex << 32) ^ (uint)meshIndex;
            if (renderArrayCache.TryGetValue(key, out var existing))
                return existing;

            var materials = new Material[WorldResources.BlendVariantCount];
            int materialStart = bucketIndex * WorldResources.BlendVariantCount;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = cache.Materials[materialStart + i];

            var rma = new RenderMeshArray(materials, new[] { cache.Meshes[meshIndex] });
            renderArrayCache[key] = rma;
            return rma;
        }

        private static void BuildRuntimeSpawnPrefabLookups(CacheLoader cache)
        {
            var modelDefs = cache?.ModelPrefabCatalog?.Records ?? System.Array.Empty<ModelPrefabDef>();
            var modelLookup = new Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor>(modelDefs.Length, System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < modelDefs.Length; i++)
            {
                var def = modelDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.ModelPath))
                    continue;

                modelLookup[def.ModelPath] = new WorldResources.RuntimeSpawnPrefabDescriptor
                {
                    ModelPrefabIndex = i,
                    CollisionIndex = def.CollisionIndex,
                    Supported = 1,
                };
            }

            var contentDb = cache?.ContentDatabase;
            if (contentDb == null)
            {
                WorldResources.SpawnableCreaturePrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableItemPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableLightPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                return;
            }

            var creatures = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.ActorCount];
            for (int i = 0; i < creatures.Length; i++)
            {
                ref readonly var actor = ref contentDb.Get(ActorDefHandle.FromIndex(i));
                if (actor.Kind != ActorDefKind.Creature)
                    continue;

                creatures[i] = ResolveSpawnDescriptor(modelLookup, actor.Model);
            }

            var items = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.ItemCount];
            for (int i = 0; i < items.Length; i++)
            {
                ref readonly var item = ref contentDb.Get(ItemDefHandle.FromIndex(i));
                items[i] = ResolveSpawnDescriptor(modelLookup, item.Model);
            }

            var lights = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.LightCount];
            for (int i = 0; i < lights.Length; i++)
            {
                ref readonly var light = ref contentDb.Get(LightDefHandle.FromIndex(i));
                lights[i] = ResolveSpawnDescriptor(modelLookup, light.Model);
            }

            WorldResources.SpawnableCreaturePrefabs = creatures;
            WorldResources.SpawnableItemPrefabs = items;
            WorldResources.SpawnableLightPrefabs = lights;
        }

        private static WorldResources.RuntimeSpawnPrefabDescriptor ResolveSpawnDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return default;

            string normalizedPath = NormalizeContentModelPath(modelPath);
            return modelLookup.TryGetValue(normalizedPath, out var descriptor) ? descriptor : default;
        }

        private static string NormalizeContentModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim();
            if (trimmed.StartsWith("meshes\\", System.StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return $"meshes\\{trimmed}";
        }

        private static bool[] CollectRequiredBootModelPrefabs(int modelPrefabCount)
        {
            if (modelPrefabCount <= 0)
                return System.Array.Empty<bool>();

            var required = new bool[modelPrefabCount];
            MarkRequiredModelPrefabs(required, WorldResources.Cells.Values);
            MarkRequiredModelPrefabs(required, WorldResources.InteriorCells.Values);
            return required;
        }

        private static void MarkRequiredModelPrefabs(bool[] required, Dictionary<int2, CellData>.ValueCollection cells)
        {
            foreach (var cell in cells)
                MarkRequiredModelPrefabs(required, cell);
        }

        private static void MarkRequiredModelPrefabs(bool[] required, Dictionary<string, CellData>.ValueCollection cells)
        {
            foreach (var cell in cells)
                MarkRequiredModelPrefabs(required, cell);
        }

        private static void MarkRequiredModelPrefabs(bool[] required, CellData cell)
        {
            var refs = cell?.Refs;
            if (refs == null)
                return;

            for (int i = 0; i < refs.Length; i++)
            {
                if ((RefSpawnMode)refs[i].SpawnModeRaw != RefSpawnMode.ModelPrefab)
                    continue;

                int modelPrefabIndex = refs[i].ModelPrefabIndex;
                if ((uint)modelPrefabIndex < (uint)required.Length)
                    required[modelPrefabIndex] = true;
            }
        }

        private static int CountRequiredModelPrefabs(bool[] required)
        {
            if (required == null)
                return 0;

            int count = 0;
            for (int i = 0; i < required.Length; i++)
            {
                if (required[i])
                    count++;
            }

            return count;
        }

        private static PreloadResult PreloadCells(CacheLoader cache)
        {
            var cellGrid = cache.Manifest.CellGrid;
            var loaded = new CellData[cellGrid.Length];
            var failures = new PreloadFailureInfo[cellGrid.Length];
            var interiorIds = cache.Manifest.InteriorCellIds ?? System.Array.Empty<string>();
            var loadedInteriors = new CellData[interiorIds.Length];
            var interiorFailures = new PreloadFailureInfo[interiorIds.Length];
            var stateByKey = BuildCellStateLookup(cache.Manifest.CellStates);
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
                    failures[i] = ValidatePreloadedCell(
                        loaded[i],
                        ResolveCellState(stateByKey, false, g.Item1, g.Item2, null),
                        false,
                        $"({g.Item1},{g.Item2})",
                        path);
                    if (failures[i] != null)
                    {
                        loaded[i] = null;
                        return;
                    }
                    TryAttachPlacementAudit(loaded[i], CachePaths.CellPlacementAuditFile(g.Item1, g.Item2));
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
                    interiorFailures[i] = ValidatePreloadedCell(
                        loadedInteriors[i],
                        ResolveCellState(stateByKey, true, 0, 0, cellId),
                        true,
                        cellId,
                        path);
                    if (interiorFailures[i] != null)
                    {
                        loadedInteriors[i] = null;
                        return;
                    }
                    TryAttachPlacementAudit(loadedInteriors[i], CachePaths.InteriorCellPlacementAuditFile(cellId));
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

        private static Dictionary<string, BakeManifest.BakedCellState> BuildCellStateLookup(BakeManifest.BakedCellState[] states)
        {
            var lookup = new Dictionary<string, BakeManifest.BakedCellState>(System.StringComparer.OrdinalIgnoreCase);
            if (states == null)
                return lookup;

            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null)
                    continue;

                string key = state.IsInterior
                    ? BuildInteriorCellStateKey(state.InteriorId)
                    : BuildExteriorCellStateKey(state.GridX, state.GridY);
                lookup[key] = state;
            }

            return lookup;
        }

        private static BakeManifest.BakedCellState ResolveCellState(
            Dictionary<string, BakeManifest.BakedCellState> stateByKey,
            bool isInterior,
            int gridX,
            int gridY,
            string interiorId)
        {
            if (stateByKey == null)
                return null;

            string key = isInterior
                ? BuildInteriorCellStateKey(interiorId)
                : BuildExteriorCellStateKey(gridX, gridY);
            return stateByKey.TryGetValue(key, out var state) ? state : null;
        }

        private static string BuildExteriorCellStateKey(int gridX, int gridY) => $"ext:{gridX},{gridY}";

        private static string BuildInteriorCellStateKey(string interiorId) => $"int:{(interiorId ?? string.Empty).Trim().ToLowerInvariant()}";

        private static PreloadFailureInfo ValidatePreloadedCell(
            CellData cell,
            BakeManifest.BakedCellState state,
            bool isInterior,
            string cellLabel,
            string path)
        {
            if (state == null)
            {
                return CreateValidationFailure(
                    isInterior,
                    cellLabel,
                    path,
                    PreloadFailureKind.PipelineMismatch,
                    "missing manifest cell state; rebuild the world cache");
            }

            if (state.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
            {
                return CreateValidationFailure(
                    isInterior,
                    cellLabel,
                    path,
                    PreloadFailureKind.PipelineMismatch,
                    $"manifest pipeline {state.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebuild the world cache");
            }

            var refs = cell?.Refs ?? System.Array.Empty<RefEntry>();
            for (int i = 0; i < refs.Length; i++)
            {
                int raw = refs[i].SpawnModeRaw;
                if (raw != (int)RefSpawnMode.RenderShard)
                {
                    string mode = System.Enum.IsDefined(typeof(RefSpawnMode), raw)
                        ? ((RefSpawnMode)raw).ToString()
                        : $"unknown({raw})";
                    return CreateValidationFailure(
                        isInterior,
                        cellLabel,
                        path,
                        PreloadFailureKind.UnsupportedSpawnMode,
                        $"ref {i} uses spawn mode {mode}; normal world cells must be baked as {RefSpawnMode.RenderShard}");
                }
            }

            return null;
        }

        private static void TryAttachPlacementAudit(CellData cell, string auditPath)
        {
            if (cell == null || string.IsNullOrEmpty(auditPath) || !System.IO.File.Exists(auditPath))
                return;

            try
            {
                cell.PlacementAudit = RefPlacementAuditFile.Read(auditPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VVardenfell] failed reading placement audit '{auditPath}': {ex.Message}");
            }
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

        private static PreloadFailureInfo CreateValidationFailure(
            bool isInterior,
            string cellLabel,
            string path,
            PreloadFailureKind kind,
            string detail)
        {
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new PreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = kind,
                Message = $"[VVardenfell] invalid baked {target} at '{path}': {detail}",
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
