using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.WorldRefs;

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
            RuntimeBootstrapRequestUtility.PublishAll(em);
            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            var contentBlob = RequireRuntimeContentBlob();
            ref RuntimeContentBlob content = ref contentBlob.Value;
            ResolveInitialPlayerData(
                ref content,
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
                PlayerCrime = PlayerCrimeState.Default,
                PlayerPosition = options.PlayerStartPosition,
                PlayerRotation = options.PlayerStartRotation,
                PlayerPitchDegrees = 0f,
                RuntimeMode = (byte)options.Mode,
                SpawnLocalPlayer = (byte)(options.SpawnLocalPlayer ? 1 : 0),
                HasSerializedSavePayload = hasSerializedSavePayload,
                SerializedSavePayloadStatus = new FixedString128Bytes(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
            });
            PopulateInitializationSpellbook(em.AddBuffer<ActorKnownSpell>(initEntity), knownSpells);
            PopulateInitializationInventory(em.AddBuffer<PlayerInitialInventoryItem>(initEntity), initialInventory);
            em.AddBuffer<ActorActiveMagicEffect>(initEntity);
            MorrowindCombatSettingsBridge.PublishPersisted(em);
        }

        public static void PublishSandboxStartRequest(EntityManager em)
        {
            RuntimeBootstrapRequestUtility.PublishAll(em);
            EntityQuery requestQuery = NewGameInitializationQueryCache.Get(em);
            if (!requestQuery.IsEmptyIgnoreFilter)
                return;

            var requestEntity = em.CreateEntity();
            em.SetName(requestEntity, "VVardenfell.SandboxInitialization");
            em.AddComponentData(requestEntity, new NewGameInitializationSingleton());
        }

        static void ResolveInitialPlayerData(
            ref RuntimeContentBlob content,
            out ActorRuntimeStatSeed stats,
            out ActorIdentitySet identity,
            out ActorKnownSpell[] knownSpells,
            out PlayerInitialInventoryItem[] initialInventory)
        {
            if (MorrowindActorMovementStats.TryCreatePlayerSeedFromContent(
                    ref content,
                    out stats,
                    out identity,
                    out knownSpells,
                    out initialInventory))
            {
                return;
            }

            stats = MorrowindActorMovementStats.CreateDefaultPlayerSeed(ref content);
            identity = ActorIdentitySet.DefaultPlayer();
            knownSpells = System.Array.Empty<ActorKnownSpell>();
            initialInventory = System.Array.Empty<PlayerInitialInventoryItem>();
        }

        static BlobAssetReference<RuntimeContentBlob> RequireRuntimeContentBlob()
        {
            var blob = WorldResources.Cache?.ContentBlob ?? default;
            if (!blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] World bootstrap initialization requires runtime content blob.");
            return blob;
        }

        static void PopulateInitializationSpellbook(DynamicBuffer<ActorKnownSpell> buffer, ActorKnownSpell[] knownSpells)
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

            using (var entities = PlayerStanceCollidersQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var stance = em.GetComponentData<PlayerStanceColliders>(entities[i]);
                    if (stance.Standing.IsCreated) stance.Standing.Dispose();
                    if (stance.Crouching.IsCreated) stance.Crouching.Dispose();
                }
            }

            using (var entities = AvailableCellsQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    em.GetComponentData<AvailableCells>(entities[i]).Set.Dispose();
            }

            using (var entities = LoadedCellsMapQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var lc = em.GetComponentData<LoadedCellsMap>(entities[i]);
                    if (lc.Map.IsCreated) lc.Map.Dispose();
                    if (lc.Streamed.IsCreated) lc.Streamed.Dispose();
                    if (lc.Active.IsCreated) lc.Active.Dispose();
                }
            }

            using (var entities = LogicalRefLookupQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var lookup = em.GetComponentData<LogicalRefLookup>(entities[i]);
                    if (lookup.Map.IsCreated)
                        lookup.Map.Dispose();
                }
            }

            using (var entities = PlacedRefRuntimeStateLookupQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var lookup = em.GetComponentData<PlacedRefRuntimeStateLookup>(entities[i]);
                    if (lookup.DisabledByPlacedRef.IsCreated)
                        lookup.DisabledByPlacedRef.Dispose();
                }
            }

            ActiveExplicitRefLookupLifecycleUtility.DisposeAll(em);

            using (var entities = RuntimeWorldCellBlobReferenceQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var reference = em.GetComponentData<RuntimeWorldCellBlobReference>(entities[i]);
                    if (reference.Blob.IsCreated)
                        reference.Blob.Dispose();
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = MorrowindScriptRuntimeCatalogQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var catalog = em.GetComponentData<MorrowindScriptRuntimeCatalog>(entities[i]);
                    catalog.Dispose();
                    em.SetComponentData(entities[i], default(MorrowindScriptRuntimeCatalog));
                }
            }

            using (var entities = LoadQueueQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    em.GetComponentData<LoadQueue>(entities[i]).Queue.Dispose();
            }

            using (var entities = UnloadListQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var u = em.GetComponentData<UnloadList>(entities[i]);
                    u.PendingEntityDestroy.Dispose();
                }
            }

            using (var entities = PendingCellPhysicsLoadQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var pending = em.GetComponentData<PendingCellPhysicsLoad>(entities[i]);
                    pending.Cells.Dispose();
                }
            }

            using (var entities = PendingCellPhysicsUnloadQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var pending = em.GetComponentData<PendingCellPhysicsUnload>(entities[i]);
                    pending.Cells.Dispose();
                }
            }

            WorldResources.Reset();
        }

        static PlayerCharacterComponent ResolvePlayerMovementSettings()
            => BootstrapController.ResolvePlayerMovementSettings();

        static class NewGameInitializationQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<NewGameInitializationSingleton>());
        }

        static class PlayerStanceCollidersQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlayerStanceColliders>());
        }

        static class AvailableCellsQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<AvailableCells>());
        }

        static class LoadedCellsMapQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LoadedCellsMap>());
        }

        static class LogicalRefLookupQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LogicalRefLookup>());
        }

        static class PlacedRefRuntimeStateLookupQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlacedRefRuntimeStateLookup>());
        }

        static class MorrowindScriptRuntimeCatalogQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<MorrowindScriptRuntimeCatalog>());
        }

        static class RuntimeWorldCellBlobReferenceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
        }

        static class LoadQueueQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LoadQueue>());
        }

        static class UnloadListQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<UnloadList>());
        }

        static class PendingCellPhysicsLoadQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PendingCellPhysicsLoad>());
        }

        static class PendingCellPhysicsUnloadQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PendingCellPhysicsUnload>());
        }

        static EntityQuery GetQuery(
            EntityManager entityManager,
            ref World worldCache,
            ref EntityQuery queryCache,
            ref bool queryCreated,
            params ComponentType[] componentTypes)
        {
            World world = entityManager.World;
            if (queryCreated && worldCache == world)
                return queryCache;

            worldCache = world;
            queryCache = entityManager.CreateEntityQuery(componentTypes);
            queryCreated = true;
            return queryCache;
        }
    }
}
