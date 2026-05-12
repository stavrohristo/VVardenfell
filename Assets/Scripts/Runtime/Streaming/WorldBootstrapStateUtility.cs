using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Vfx;
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
            out RuntimeSectionRegistry sectionRegistry,
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
                Streamed = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
                Active = new NativeHashSet<int2>(cellCap, Allocator.Persistent),
                SectionStates = new NativeHashMap<int2, byte>(cellCap, Allocator.Persistent),
            };
            sectionRegistry = new RuntimeSectionRegistry
            {
                ExteriorSections = new NativeHashMap<int2, Entity>(cellCap, Allocator.Persistent),
                InteriorSectionsByHash = new NativeHashMap<ulong, Entity>(System.Math.Max(cache.Manifest.InteriorCellCount, 16), Allocator.Persistent),
                InteriorCellIdsByHash = new NativeHashMap<ulong, FixedString128Bytes>(System.Math.Max(cache.Manifest.InteriorCellCount, 16), Allocator.Persistent),
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
            RuntimeSectionRegistry sectionRegistry,
            LogicalRefLookup logicalRefLookup,
            PlacedRefRuntimeStateLookup placedRefRuntimeStateLookup,
            LoadQueue loadQueue,
            UnloadList unloadList,
            PendingCellPhysicsLoad pendingPhysicsLoad,
            PendingCellPhysicsUnload pendingPhysicsUnload,
            int2 defaultCameraCell)
        {
            var singleton = em.CreateEntity();
            em.AddComponentData(singleton, new StreamingConfig
            {
                ViewRadius = WorldBootstrap.DefaultViewRadius,
                DistantTerrainRadius = WorldBootstrap.DefaultDistantTerrainRadius,
                MaxLoadsPerFrame = WorldBootstrap.DefaultMaxLoadsPerFrame,
                MaxUnloadsPerFrame = WorldBootstrap.DefaultMaxUnloadsPerFrame,
                GateTerrainByRadius = WorldBootstrap.DefaultGateTerrainByRadius,
                ExteriorStreamingPaused = true,
                CameraCell = defaultCameraCell,
            });
            em.AddComponentData(singleton, new AvailableCells { Set = available });
            em.AddComponentData(singleton, loadedMap);
            em.AddComponentData(singleton, sectionRegistry);
            em.AddComponentData(singleton, logicalRefLookup);
            em.AddComponentData(singleton, placedRefRuntimeStateLookup);
            em.AddComponentData(singleton, loadQueue);
            em.AddComponentData(singleton, unloadList);
            em.AddComponentData(singleton, pendingPhysicsLoad);
            em.AddComponentData(singleton, pendingPhysicsUnload);
        }

        public static void PublishMaterializationResources(EntityManager em, RuntimeMaterializationResources resources)
        {
            if (resources == null)
                throw new System.InvalidOperationException("[VVardenfell][Materialization] cannot publish null materialization resources.");

            EntityQuery query = RuntimeMaterializationResourcesQueryCache.Get(em);
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();

            if (em.HasComponent<RuntimeMaterializationResources>(entity))
                em.SetComponentData(entity, resources);
            else
                em.AddComponentData(entity, resources);
        }

        public static void PublishActorPresentationResources(EntityManager em)
        {
            EntityQuery query = RuntimeActorPresentationResourcesQueryCache.Get(em);
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();

            var resources = new RuntimeActorPresentationResources
            {
                GpuAnimation = new ActorGpuAnimationResources(),
                EntitiesGraphicsRenderer = new ActorEntitiesGraphicsRenderResources(),
                ShadowCasterDistance = 64f,
                ShadowCasterPadding = 8f,
                MaxActorShadowCasters = 128,
            };

            if (em.HasComponent<RuntimeActorPresentationResources>(entity))
            {
                em.GetComponentData<RuntimeActorPresentationResources>(entity)?.Dispose(em);
                em.SetComponentData(entity, resources);
            }
            else
            {
                em.AddComponentData(entity, resources);
            }
        }

        public static void PublishVfxPresentationResources(EntityManager em)
        {
            EntityQuery query = RuntimeVfxPresentationResourcesQueryCache.Get(em);
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();

            var resources = new RuntimeVfxPresentationResources();
            if (em.HasComponent<RuntimeVfxPresentationResources>(entity))
            {
                em.GetComponentData<RuntimeVfxPresentationResources>(entity)?.Dispose();
                em.SetComponentData(entity, resources);
            }
            else
            {
                em.AddComponentData(entity, resources);
            }
        }

        public static void PublishActorColliderResource(EntityManager em)
        {
            EntityQuery query = RuntimeActorColliderResourceQueryCache.Get(em);
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();

            var resource = RuntimeActorColliderResource.Create();
            if (em.HasComponent<RuntimeActorColliderResource>(entity))
            {
                var previous = em.GetComponentData<RuntimeActorColliderResource>(entity);
                previous.Dispose();
                em.SetComponentData(entity, resource);
            }
            else
            {
                em.AddComponentData(entity, resource);
            }
        }

        public static void PublishGameInitialization(EntityManager em)
            => PublishGameInitialization(em, WorldBootstrapOptions.Vanilla);

        public static void PublishRuntimeMode(EntityManager em, BootstrapRuntimeMode mode)
        {
            EntityQuery query = RuntimeBootstrapModeStateQueryCache.Get(em);
            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();

            var modeState = new RuntimeBootstrapModeState
            {
                Mode = (byte)mode,
            };
            if (em.HasComponent<RuntimeBootstrapModeState>(entity))
                em.SetComponentData(entity, modeState);
            else
                em.AddComponentData(entity, modeState);

            PublishPresentationGate(em, mode);
        }

        static void PublishPresentationGate(EntityManager em, BootstrapRuntimeMode mode)
        {
            EntityQuery enabledQuery = RuntimePresentationEnabledQueryCache.Get(em);
            if (!enabledQuery.IsEmptyIgnoreFilter)
                em.DestroyEntity(enabledQuery);
            EntityQuery disabledQuery = RuntimePresentationDisabledQueryCache.Get(em);
            if (!disabledQuery.IsEmptyIgnoreFilter)
                em.DestroyEntity(disabledQuery);

            Entity entity = em.CreateEntity();
            if (BootstrapRuntimeModeUtility.IsSandboxMode(mode))
                em.AddComponentData(entity, new RuntimePresentationDisabled());
            else
                em.AddComponentData(entity, new RuntimePresentationEnabled());
        }

        public static void PublishGameInitialization(EntityManager em, WorldBootstrapOptions options)
        {
            PublishRuntimeMode(em, options.Mode);
            PublishBattleSimulatorBootState(em, options);
            RuntimeBootstrapRequestUtility.PublishAll(em);
            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            var contentBlob = RequireRuntimeContentBlob(em);
            ref RuntimeContentBlob content = ref contentBlob.Value;
            ResolveInitialPlayerData(
                ref content,
                out var playerStats,
                out var playerIdentity,
                out var knownSpells,
                out var initialInventory);
            var initEntity = em.CreateEntity();
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

        static void PublishBattleSimulatorBootState(EntityManager em, WorldBootstrapOptions options)
        {
            EntityQuery query = BattleSimulatorBootStateQueryCache.Get(em);
            if (options.Mode != BootstrapRuntimeMode.CombatSandbox)
            {
                if (!query.IsEmptyIgnoreFilter)
                    em.DestroyEntity(query);
                return;
            }

            var profile = options.SandboxProfile;
            if (profile == null)
                throw new System.InvalidOperationException("[VVardenfell][CombatSandbox] missing sandbox profile for battle simulator boot state.");

            Entity entity = query.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : query.GetSingletonEntity();
            var state = new BattleSimulatorBootState
            {
                BattlegroundCell = profile.CombatExteriorCell,
            };
            if (em.HasComponent<BattleSimulatorBootState>(entity))
                em.SetComponentData(entity, state);
            else
                em.AddComponentData(entity, state);

            var simulatorState = new BattleSimulatorState
            {
                BattlegroundCell = profile.CombatExteriorCell,
                Phase = (byte)BattleSimulatorPhase.Setup,
                Status = new FixedString128Bytes("Build two battle groups, then press Ready."),
            };
            if (em.HasComponent<BattleSimulatorState>(entity))
                em.SetComponentData(entity, simulatorState);
            else
                em.AddComponentData(entity, simulatorState);

            if (!em.HasBuffer<BattleSimulatorSpawnRequest>(entity))
                em.AddBuffer<BattleSimulatorSpawnRequest>(entity);
            if (!em.HasComponent<BattleSimulatorResetRequest>(entity))
            {
                em.AddComponent<BattleSimulatorResetRequest>(entity);
                em.SetComponentEnabled<BattleSimulatorResetRequest>(entity, false);
            }
        }

        public static void PublishSandboxStartRequest(EntityManager em)
        {
            RuntimeBootstrapRequestUtility.PublishAll(em);
            EntityQuery requestQuery = NewGameInitializationQueryCache.Get(em);
            if (!requestQuery.IsEmptyIgnoreFilter)
                return;

            var requestEntity = em.CreateEntity();
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

        static BlobAssetReference<RuntimeContentBlob> RequireRuntimeContentBlob(EntityManager em)
        {
            var blob = RuntimeMaterializationResources.Require(em).ContentBlob;
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
            RuntimeSectionRegistry sectionRegistry,
            LogicalRefLookup logicalRefLookup,
            PlacedRefRuntimeStateLookup placedRefRuntimeStateLookup,
            LoadQueue loadQueue,
            UnloadList unloadList,
            PendingCellPhysicsLoad pendingPhysicsLoad,
            PendingCellPhysicsUnload pendingPhysicsUnload)
        {
            if (available.IsCreated)
                available.Dispose();
            if (loadedMap.Streamed.IsCreated)
                loadedMap.Streamed.Dispose();
            if (loadedMap.Active.IsCreated)
                loadedMap.Active.Dispose();
            if (loadedMap.SectionStates.IsCreated)
                loadedMap.SectionStates.Dispose();
            if (sectionRegistry.ExteriorSections.IsCreated)
                sectionRegistry.ExteriorSections.Dispose();
            if (sectionRegistry.InteriorSectionsByHash.IsCreated)
                sectionRegistry.InteriorSectionsByHash.Dispose();
            if (sectionRegistry.InteriorCellIdsByHash.IsCreated)
                sectionRegistry.InteriorCellIdsByHash.Dispose();
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
                return;
            }

            var em = world.EntityManager;

            DestroyLogicalRefs(em);

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
                    if (lc.Streamed.IsCreated) lc.Streamed.Dispose();
                    if (lc.Active.IsCreated) lc.Active.Dispose();
                    if (lc.SectionStates.IsCreated) lc.SectionStates.Dispose();
                }
            }

            using (var entities = RuntimeSectionRegistryQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var registry = em.GetComponentData<RuntimeSectionRegistry>(entities[i]);
                    if (registry.ExteriorSections.IsCreated) registry.ExteriorSections.Dispose();
                    if (registry.InteriorSectionsByHash.IsCreated) registry.InteriorSectionsByHash.Dispose();
                    if (registry.InteriorCellIdsByHash.IsCreated) registry.InteriorCellIdsByHash.Dispose();
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

            using (var entities = RuntimeActorPresentationResourcesQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var resources = em.GetComponentData<RuntimeActorPresentationResources>(entities[i]);
                    resources?.Dispose(em);
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = RuntimeVfxPresentationResourcesQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var resources = em.GetComponentData<RuntimeVfxPresentationResources>(entities[i]);
                    resources?.Dispose();
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = RuntimeActorColliderResourceQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var resource = em.GetComponentData<RuntimeActorColliderResource>(entities[i]);
                    resource.Dispose();
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = RuntimeMaterializationResourcesQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var resources = em.GetComponentData<RuntimeMaterializationResources>(entities[i]);
                    DisposeMaterializationResources(resources);
                    resources?.ClearReferences();
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = RuntimePathGridNavigationResourceQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var resource = em.GetComponentData<RuntimePathGridNavigationResource>(entities[i]);
                    if (resource.Navigation.IsCreated)
                        resource.Navigation.Dispose();
                    em.DestroyEntity(entities[i]);
                }
            }

            using (var entities = RuntimeContentBlobReferenceQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    em.DestroyEntity(entities[i]);
            }

            using (var entities = RuntimeModelPrefabBlobReferenceQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var reference = em.GetComponentData<RuntimeModelPrefabBlobReference>(entities[i]);
                    if (reference.Blob.IsCreated)
                        reference.Blob.Dispose();
                    em.DestroyEntity(entities[i]);
                }
            }

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
        }

        static void DisposeMaterializationResources(RuntimeMaterializationResources resources)
        {
            if (resources == null)
                return;

            UnregisterRenderAssets(resources);

            if (resources.ColliderBlobs == null)
                return;

            for (int i = 0; i < resources.ColliderBlobs.Length; i++)
            {
                if (resources.ColliderBlobs[i].IsCreated)
                    resources.ColliderBlobs[i].Dispose();
            }
        }

        static void UnregisterRenderAssets(RuntimeMaterializationResources resources)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var renderer = world?.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (renderer == null)
                return;

            if (resources.RegisteredMeshes != null)
            {
                for (int i = 0; i < resources.RegisteredMeshes.Length; i++)
                {
                    var id = resources.RegisteredMeshes[i];
                    if (id.value != 0)
                        renderer.UnregisterMesh(id);
                }
            }

            if (resources.RegisteredRefMaterials != null)
            {
                for (int i = 0; i < resources.RegisteredRefMaterials.Length; i++)
                {
                    var id = resources.RegisteredRefMaterials[i];
                    if (id.value != 0)
                        renderer.UnregisterMaterial(id);
                }
            }

            if (resources.RegisteredCombinedMaterials != null)
            {
                for (int i = 0; i < resources.RegisteredCombinedMaterials.Length; i++)
                {
                    var id = resources.RegisteredCombinedMaterials[i];
                    if (id.value != 0)
                        renderer.UnregisterMaterial(id);
                }
            }

            if (resources.RegisteredTerrainMaterial.value != 0)
                renderer.UnregisterMaterial(resources.RegisteredTerrainMaterial);
        }

        static void DestroyLogicalRefs(EntityManager em)
        {
            using (var logicalRefs = LogicalRefTagQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < logicalRefs.Length; i++)
                {
                    Entity logicalRef = logicalRefs[i];
                    if (!em.Exists(logicalRef))
                        continue;

                    if (em.HasBuffer<LogicalRefChild>(logicalRef))
                    {
                        var children = em.GetBuffer<LogicalRefChild>(logicalRef);
                        for (int childIndex = 0; childIndex < children.Length; childIndex++)
                        {
                            Entity child = children[childIndex].Value;
                            if (child != Entity.Null && em.Exists(child))
                                ecb.DestroyEntity(child);
                        }
                    }

                    ecb.DestroyEntity(logicalRef);
                }

                ecb.Playback(em);
                ecb.Dispose();
            }

            using (var orphanChildren = LogicalRefParentQueryCache.Get(em).ToEntityArray(Allocator.Temp))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < orphanChildren.Length; i++)
                {
                    Entity child = orphanChildren[i];
                    if (child != Entity.Null && em.Exists(child))
                        ecb.DestroyEntity(child);
                }

                ecb.Playback(em);
                ecb.Dispose();
            }
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

        static class RuntimeSectionRegistryQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeSectionRegistry>());
        }

        static class RuntimeMaterializationResourcesQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeMaterializationResources>());
        }

        static class RuntimeActorPresentationResourcesQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeActorPresentationResources>());
        }

        static class RuntimeVfxPresentationResourcesQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeVfxPresentationResources>());
        }

        static class RuntimeActorColliderResourceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeActorColliderResource>());
        }

        static class RuntimePathGridNavigationResourceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimePathGridNavigationResource>());
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

        static class LogicalRefTagQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LogicalRefTag>());
        }

        static class LogicalRefParentQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LogicalRefParent>());
        }

        static class RuntimeWorldCellBlobReferenceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
        }

        static class RuntimeContentBlobReferenceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeContentBlobReference>());
        }

        static class RuntimeModelPrefabBlobReferenceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimeModelPrefabBlobReference>());
        }

        static class RuntimeBootstrapModeStateQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadWrite<RuntimeBootstrapModeState>());
        }

        static class BattleSimulatorBootStateQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadWrite<BattleSimulatorBootState>());
        }

        static class RuntimePresentationEnabledQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimePresentationEnabled>());
        }

        static class RuntimePresentationDisabledQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<RuntimePresentationDisabled>());
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
