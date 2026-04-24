using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldBootstrapStateUtility
    {
        public static void AllocateStreamingState(
            CacheLoader cache,
            out NativeHashSet<int2> available,
            out LoadedCellsMap loadedMap,
            out LogicalRefLookup logicalRefLookup,
            out LoadQueue loadQueue,
            out UnloadList unloadList,
            out PendingCellPhysicsLoad pendingPhysicsLoad,
            out PendingCellPhysicsUnload pendingPhysicsUnload)
        {
            int cellCap = System.Math.Max(cache.Manifest.CellCount, 128);
            available = new NativeHashSet<int2>(cache.Manifest.CellCount, Allocator.Persistent);
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
            pendingPhysicsLoad = new PendingCellPhysicsLoad
            {
                Cells = new NativeList<int2>(32, Allocator.Persistent),
            };
            pendingPhysicsUnload = new PendingCellPhysicsUnload
            {
                Cells = new NativeList<int2>(32, Allocator.Persistent),
            };
        }

        public static void QueueInitialExteriorCells(
            LoadQueue loadQueue,
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

        public static void PublishStreamingSingleton(
            EntityManager em,
            NativeHashSet<int2> available,
            LoadedCellsMap loadedMap,
            LogicalRefLookup logicalRefLookup,
            LoadQueue loadQueue,
            UnloadList unloadList,
            PendingCellPhysicsLoad pendingPhysicsLoad,
            PendingCellPhysicsUnload pendingPhysicsUnload,
            int2 defaultCameraCell)
        {
            var singleton = em.CreateEntity();
            em.SetName(singleton, "VVardenfell.World");
            em.AddComponentData(singleton, new StreamingConfig
            {
                ViewRadius = WorldBootstrap.DefaultViewRadius,
                MaxLoadsPerFrame = WorldBootstrap.DefaultMaxLoadsPerFrame,
                MaxUnloadsPerFrame = WorldBootstrap.DefaultMaxUnloadsPerFrame,
                GateTerrainByRadius = WorldBootstrap.DefaultGateTerrainByRadius,
                ExteriorStreamingPaused = true,
                CameraCell = defaultCameraCell,
            });
            em.AddComponentData(singleton, new AvailableCells { Set = available });
            em.AddComponentData(singleton, loadedMap);
            em.AddComponentData(singleton, logicalRefLookup);
            em.AddComponentData(singleton, loadQueue);
            em.AddComponentData(singleton, unloadList);
            em.AddComponentData(singleton, pendingPhysicsLoad);
            em.AddComponentData(singleton, pendingPhysicsUnload);
        }

        public static void PublishGameInitialization(EntityManager em)
        {
            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            var initEntity = em.CreateEntity();
            em.SetName(initEntity, "VVardenfell.GameInitialization");
            em.AddComponentData(initEntity, new GameInitializationSingleton
            {
                PlayerSettings = ResolvePlayerMovementSettings(),
                PlayerActorStats = MorrowindActorMovementStats.CreateDefaultPlayerSeed(),
                PlayerIdentity = ActorIdentitySet.DefaultPlayer(),
                PlayerPosition = WorldBootstrap.DefaultPlayerSpawnPosition(),
                PlayerRotation = quaternion.identity,
                PlayerPitchDegrees = 0f,
                HasSerializedSavePayload = hasSerializedSavePayload,
                SerializedSavePayloadStatus = new FixedString128Bytes(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
            });
            em.AddBuffer<PlayerKnownSpell>(initEntity);
        }

        public static void DisposeUnpublishedState(
            NativeHashSet<int2> available,
            LoadedCellsMap loadedMap,
            LogicalRefLookup logicalRefLookup,
            LoadQueue loadQueue,
            UnloadList unloadList,
            PendingCellPhysicsLoad pendingPhysicsLoad,
            PendingCellPhysicsUnload pendingPhysicsUnload)
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
            if (pendingPhysicsLoad.Cells.IsCreated)
                pendingPhysicsLoad.Cells.Dispose();
            if (pendingPhysicsUnload.Cells.IsCreated)
                pendingPhysicsUnload.Cells.Dispose();
        }

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

            foreach (var e in em.CreateEntityQuery(typeof(PendingCellPhysicsLoad)).ToEntityArray(Allocator.Temp))
            {
                var pending = em.GetComponentData<PendingCellPhysicsLoad>(e);
                pending.Cells.Dispose();
            }

            foreach (var e in em.CreateEntityQuery(typeof(PendingCellPhysicsUnload)).ToEntityArray(Allocator.Temp))
            {
                var pending = em.GetComponentData<PendingCellPhysicsUnload>(e);
                pending.Cells.Dispose();
            }

            WorldResources.Reset();
        }

        static PlayerCharacterComponent ResolvePlayerMovementSettings()
            => BootstrapController.ResolvePlayerMovementSettings();
    }
}
