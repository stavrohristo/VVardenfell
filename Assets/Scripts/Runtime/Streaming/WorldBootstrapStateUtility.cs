using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
            out PlacedRefRuntimeStateLookup placedRefRuntimeStateLookup,
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
                Streamed = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
                Active = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
            };
            logicalRefLookup = new LogicalRefLookup
            {
                Map = new NativeParallelHashMap<uint, Entity>(System.Math.Max(cellCap * 8, 1024), Allocator.Persistent),
            };
            placedRefRuntimeStateLookup = new PlacedRefRuntimeStateLookup
            {
                DisabledByPlacedRef = new NativeParallelHashMap<uint, byte>(System.Math.Max(cellCap * 8, 1024), Allocator.Persistent),
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
            PlacedRefRuntimeStateLookup placedRefRuntimeStateLookup,
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
            em.AddComponentData(singleton, placedRefRuntimeStateLookup);
            em.AddComponentData(singleton, loadQueue);
            em.AddComponentData(singleton, unloadList);
            em.AddComponentData(singleton, pendingPhysicsLoad);
            em.AddComponentData(singleton, pendingPhysicsUnload);
        }

        public static void PublishGameInitialization(EntityManager em)
            => PublishGameInitialization(em, WorldBootstrapOptions.Vanilla);

        public static void PublishGameInitialization(EntityManager em, WorldBootstrapOptions options)
        {
            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            ResolveInitialPlayerData(
                RuntimeContentDatabase.Active,
                out var playerStats,
                out var playerIdentity,
                out var knownSpells,
                out var initialInventory);
            var initEntity = em.CreateEntity();
            em.SetName(initEntity, "VVardenfell.GameInitialization");
            em.AddComponentData(initEntity, new GameInitializationSingleton
            {
                PlayerSettings = ResolvePlayerMovementSettings(),
                PlayerActorStats = playerStats,
                PlayerIdentity = playerIdentity,
                PlayerPosition = options.PlayerStartPosition,
                PlayerRotation = options.PlayerStartRotation,
                PlayerPitchDegrees = 0f,
                RuntimeMode = (byte)options.Mode,
                SpawnLocalPlayer = (byte)(options.SpawnLocalPlayer ? 1 : 0),
                HasSerializedSavePayload = hasSerializedSavePayload,
                SerializedSavePayloadStatus = new FixedString128Bytes(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
            });
            PopulateInitializationSpellbook(em.AddBuffer<PlayerKnownSpell>(initEntity), knownSpells);
            PopulateInitializationInventory(em.AddBuffer<PlayerInitialInventoryItem>(initEntity), initialInventory);
            em.AddBuffer<ActorActiveMagicEffect>(initEntity);
        }

        public static void PublishSandboxStartRequest(EntityManager em)
        {
            using var requestQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NewGameInitializationSingleton>());
            if (!requestQuery.IsEmptyIgnoreFilter)
                return;

            var requestEntity = em.CreateEntity();
            em.SetName(requestEntity, "VVardenfell.SandboxInitialization");
            em.AddComponentData(requestEntity, new NewGameInitializationSingleton());
        }

        static void ResolveInitialPlayerData(
            RuntimeContentDatabase contentDb,
            out ActorRuntimeStatSeed stats,
            out ActorIdentitySet identity,
            out PlayerKnownSpell[] knownSpells,
            out PlayerInitialInventoryItem[] initialInventory)
        {
            if (MorrowindActorMovementStats.TryCreatePlayerSeedFromContent(
                    contentDb,
                    out stats,
                    out identity,
                    out knownSpells,
                    out initialInventory))
            {
                return;
            }

            stats = MorrowindActorMovementStats.CreateDefaultPlayerSeed();
            identity = ActorIdentitySet.DefaultPlayer();
            knownSpells = System.Array.Empty<PlayerKnownSpell>();
            initialInventory = System.Array.Empty<PlayerInitialInventoryItem>();
        }

        static void PopulateInitializationSpellbook(DynamicBuffer<PlayerKnownSpell> buffer, PlayerKnownSpell[] knownSpells)
        {
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationInventory(DynamicBuffer<PlayerInitialInventoryItem> buffer, PlayerInitialInventoryItem[] inventory)
        {
            if (inventory == null)
                return;

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Count > 0 && inventory[i].Content.IsValid)
                    buffer.Add(inventory[i]);
            }
        }

        public static void DisposeUnpublishedState(
            NativeHashSet<int2> available,
            LoadedCellsMap loadedMap,
            LogicalRefLookup logicalRefLookup,
            PlacedRefRuntimeStateLookup placedRefRuntimeStateLookup,
            LoadQueue loadQueue,
            UnloadList unloadList,
            PendingCellPhysicsLoad pendingPhysicsLoad,
            PendingCellPhysicsUnload pendingPhysicsUnload)
        {
            if (available.IsCreated)
                available.Dispose();
            if (loadedMap.Map.IsCreated)
                loadedMap.Map.Dispose();
            if (loadedMap.Streamed.IsCreated)
                loadedMap.Streamed.Dispose();
            if (loadedMap.Active.IsCreated)
                loadedMap.Active.Dispose();
            if (logicalRefLookup.Map.IsCreated)
                logicalRefLookup.Map.Dispose();
            if (placedRefRuntimeStateLookup.DisabledByPlacedRef.IsCreated)
                placedRefRuntimeStateLookup.DisabledByPlacedRef.Dispose();
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
                if (lc.Map.IsCreated) lc.Map.Dispose();
                if (lc.Streamed.IsCreated) lc.Streamed.Dispose();
                if (lc.Active.IsCreated) lc.Active.Dispose();
            }

            foreach (var e in em.CreateEntityQuery(typeof(LogicalRefLookup)).ToEntityArray(Allocator.Temp))
            {
                var lookup = em.GetComponentData<LogicalRefLookup>(e);
                if (lookup.Map.IsCreated)
                    lookup.Map.Dispose();
            }

            foreach (var e in em.CreateEntityQuery(typeof(PlacedRefRuntimeStateLookup)).ToEntityArray(Allocator.Temp))
            {
                var lookup = em.GetComponentData<PlacedRefRuntimeStateLookup>(e);
                if (lookup.DisabledByPlacedRef.IsCreated)
                    lookup.DisabledByPlacedRef.Dispose();
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
