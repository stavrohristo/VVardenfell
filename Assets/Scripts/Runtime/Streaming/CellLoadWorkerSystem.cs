using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldRefs;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellUnloadSystem))]
    public partial class CellLoadWorkerSystem : SystemBase
    {
        EntityQuery _singletonQuery;

        protected override void OnCreate()
        {
            _singletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<StreamingConfig>(),
                ComponentType.ReadWrite<LoadQueue>(),
                ComponentType.ReadWrite<LoadedCellsMap>(),
                ComponentType.ReadWrite<RuntimeSectionRegistry>(),
                ComponentType.ReadWrite<LogicalRefLookup>(),
                ComponentType.ReadWrite<PendingCellPhysicsLoad>());
            RequireForUpdate(_singletonQuery);

        }

        protected override void OnUpdate()
        {
            var queue = _singletonQuery.GetSingleton<LoadQueue>();
            if (queue.Queue.Count == 0)
                return;

            var cfg = _singletonQuery.GetSingleton<StreamingConfig>();
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var loadedEntity = _singletonQuery.GetSingletonEntity();
            bool loadedStateChanged = false;
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            var logicalRefs = _singletonQuery.GetSingleton<LogicalRefLookup>();

            CompleteDependency();
            EntityManager.CompleteDependencyBeforeRW<MaterialMeshInfo>();

            int budget = cfg.MaxLoadsPerFrame;
            bool materializedCellThisFrame = false;
            while (budget-- > 0 && queue.Queue.TryDequeue(out var coord))
            {
                if (loaded.Active.Contains(coord) && loaded.Streamed.Contains(coord))
                    continue;
                if (loaded.SectionStates.IsCreated
                    && loaded.SectionStates.TryGetValue(coord, out byte rawState)
                    && rawState == (byte)CellSectionLoadState.Failed)
                {
                    continue;
                }

                if (!loaded.Streamed.Contains(coord))
                {
                    if (loaded.SectionStates.IsCreated)
                    {
                        loaded.SectionStates[coord] = (byte)CellSectionLoadState.Loading;
                        loadedStateChanged = true;
                    }

                    bool materialized = false;
                    try
                    {
                        materialized = LoadAndMaterializeExterior(
                            coord,
                            ref loaded,
                            ref registry,
                            ref logicalRefs,
                            active: true,
                            gateTerrainByRadius: cfg.GateTerrainByRadius);
                    }
                    catch (System.Exception ex)
                    {
                        if (loaded.SectionStates.IsCreated)
                            loaded.SectionStates[coord] = (byte)CellSectionLoadState.Failed;
                        loadedStateChanged = true;
                        UnityEngine.Debug.LogError($"[VVardenfell][CellStreaming] failed loading exterior cell ({coord.x},{coord.y}); cell marked failed and will not retry until reload: {ex}");
                        continue;
                    }

                    if (materialized)
                    {
                        if (loaded.SectionStates.IsCreated)
                            loaded.SectionStates[coord] = (byte)CellSectionLoadState.Active;
                        loadedStateChanged = true;
                        materializedCellThisFrame = true;
                    }
                    else if (loaded.SectionStates.IsCreated)
                    {
                        loaded.SectionStates[coord] = (byte)CellSectionLoadState.Unloaded;
                        loadedStateChanged = true;
                    }

                    if (materializedCellThisFrame)
                        break;

                    continue;
                }

                SetExteriorCellActiveState(coord, active: true, cfg.GateTerrainByRadius);
                if (loaded.Active.Add(coord))
                {
                    loaded.ActiveRevision++;
                    QueueExteriorActiveRefSectionChange(registry, coord, true);
                    loadedStateChanged = true;
                }
                if (loaded.SectionStates.IsCreated)
                {
                    loaded.SectionStates[coord] = (byte)CellSectionLoadState.Active;
                    loadedStateChanged = true;
                }
            }

            if (loadedStateChanged)
                EntityManager.SetComponentData(loadedEntity, loaded);
        }

        public bool LoadAndMaterializeExterior(
            int2 coord,
            ref LoadedCellsMap loaded,
            ref RuntimeSectionRegistry registry,
            ref LogicalRefLookup logicalRefs,
            bool active,
            bool gateTerrainByRadius)
        {
            if (registry.ExteriorSections.TryGetValue(coord, out Entity sectionEntity)
                && sectionEntity != Entity.Null
                && EntityManager.Exists(sectionEntity))
            {
                var header = EntityManager.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
                return MaterializeExteriorSection(
                    new RuntimeCellSectionLoadResult(sectionEntity, header),
                    ref loaded,
                    ref registry,
                    ref logicalRefs,
                    active,
                    gateTerrainByRadius);
            }

            string path = CachePaths.ExteriorCellSectionFile(coord.x, coord.y);
            var result = RuntimeCellSectionFile.LoadIntoWorld(EntityManager, path, isInterior: false);
            return MaterializeExteriorSection(
                result,
                ref loaded,
                ref registry,
                ref logicalRefs,
                active,
                gateTerrainByRadius);
        }

        public bool LoadAndMaterializeExteriorTerrainOnly(
            int2 coord,
            ref LoadedCellsMap loaded,
            ref RuntimeSectionRegistry registry,
            bool active)
        {
            if (registry.ExteriorSections.TryGetValue(coord, out Entity sectionEntity)
                && sectionEntity != Entity.Null
                && EntityManager.Exists(sectionEntity))
            {
                var header = EntityManager.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
                return MaterializeExteriorTerrainOnlySection(
                    new RuntimeCellSectionLoadResult(sectionEntity, header),
                    ref loaded,
                    ref registry,
                    active);
            }

            string path = CachePaths.ExteriorCellSectionFile(coord.x, coord.y);
            var result = RuntimeCellSectionFile.LoadIntoWorld(EntityManager, path, isInterior: false);
            return MaterializeExteriorTerrainOnlySection(
                result,
                ref loaded,
                ref registry,
                active);
        }

        public bool TryGetExteriorStaticCollider(int2 coord, out BlobAssetReference<Collider> collider)
        {
            collider = default;
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            if (!registry.ExteriorSections.TryGetValue(coord, out Entity sectionEntity))
                return false;
            return TryGetSectionStaticCollider(sectionEntity, out collider);
        }

        public bool TryGetExteriorTerrainCollider(int2 coord, out BlobAssetReference<Collider> collider)
        {
            collider = default;
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            if (!registry.ExteriorSections.TryGetValue(coord, out Entity sectionEntity))
                return false;
            return TryGetSectionTerrainCollider(sectionEntity, out collider);
        }

        public void HideExteriorVisibility(ref LoadedCellsMap loaded)
        {
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            using var activeCells = loaded.Active.ToNativeArray(Allocator.Temp);
            for (int i = 0; i < activeCells.Length; i++)
            {
                if (TryGetExteriorSection(registry, activeCells[i], out Entity sectionEntity))
                {
                    SetSectionActiveState(sectionEntity, active: false);
                    ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(EntityManager, sectionEntity, false);
                }
            }
            if (loaded.Active.Count > 0)
            {
                loaded.Active.Clear();
                loaded.ActiveRevision++;
            }
        }

        public void SyncExteriorVisibility(
            in StreamingConfig config,
            in AvailableCells available,
            ref LoadedCellsMap loaded)
        {
            int r = config.ViewRadius;
            var desired = new NativeHashSet<int2>((2 * r + 1) * (2 * r + 1), Allocator.Temp);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    var coord = new int2(config.CameraCell.x + dx, config.CameraCell.y + dy);
                    if (available.Set.Contains(coord))
                        desired.Add(coord);
                }
            }

            var activeDesired = new NativeHashSet<int2>(desired.Count, Allocator.Temp);
            var desiredEnumeratorForActive = desired.GetEnumerator();
            while (desiredEnumeratorForActive.MoveNext())
            {
                var coord = desiredEnumeratorForActive.Current;
                if (loaded.Streamed.Contains(coord))
                    activeDesired.Add(coord);
            }

            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            using var previousActive = loaded.Active.ToNativeArray(Allocator.Temp);
            for (int i = 0; i < previousActive.Length; i++)
            {
                int2 coord = previousActive[i];
                if (!activeDesired.Contains(coord) && TryGetExteriorSection(registry, coord, out Entity sectionEntity))
                {
                    SetSectionActiveState(sectionEntity, active: false);
                    ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(EntityManager, sectionEntity, false);
                }
            }

            var activateEnumerator = activeDesired.GetEnumerator();
            while (activateEnumerator.MoveNext())
            {
                int2 coord = activateEnumerator.Current;
                if (!loaded.Active.Contains(coord) && TryGetExteriorSection(registry, coord, out Entity sectionEntity))
                {
                    SetSectionActiveState(sectionEntity, active: true);
                    ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(EntityManager, sectionEntity, true);
                }
            }

            bool changed = loaded.Active.Count != activeDesired.Count;
            if (!changed)
            {
                var activeEnumerator = loaded.Active.GetEnumerator();
                while (activeEnumerator.MoveNext())
                {
                    if (!activeDesired.Contains(activeEnumerator.Current))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            loaded.Active.Clear();
            var activeDesiredEnumerator = activeDesired.GetEnumerator();
            while (activeDesiredEnumerator.MoveNext())
                loaded.Active.Add(activeDesiredEnumerator.Current);
            if (changed)
                loaded.ActiveRevision++;
            activeDesired.Dispose();
            desired.Dispose();
        }

        public void SetExteriorCellActiveState(int2 coord, bool active, bool gateTerrainByRadius)
        {
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            if (TryGetExteriorSection(registry, coord, out Entity sectionEntity))
                SetSectionActiveState(sectionEntity, active);
        }

        void QueueExteriorActiveRefSectionChange(RuntimeSectionRegistry registry, int2 coord, bool active)
        {
            if (!TryGetExteriorSection(registry, coord, out Entity sectionEntity))
                throw new System.InvalidOperationException($"[VVardenfell][CellSection] active exterior cell ({coord.x},{coord.y}) has no section root.");
            ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(EntityManager, sectionEntity, active);
        }

        bool TryGetExteriorSection(RuntimeSectionRegistry registry, int2 coord, out Entity sectionEntity)
        {
            sectionEntity = default;
            return registry.ExteriorSections.IsCreated
                && registry.ExteriorSections.TryGetValue(coord, out sectionEntity)
                && sectionEntity != Entity.Null
                && EntityManager.Exists(sectionEntity);
        }

        void SetSectionActiveState(Entity sectionEntity, bool active)
        {
            SetSectionRenderActive(sectionEntity, active);
            QueueSectionActorActive(sectionEntity, active);
            QueueSectionPhysicsActive(sectionEntity, active);
        }

        void SetSectionRenderActive(Entity sectionEntity, bool active)
        {
            var terrains = RequireSectionBuffer<RuntimeCellSectionTerrainEntity>(sectionEntity, "terrain entities");
            for (int i = 0; i < terrains.Length; i++)
                SetSingleRenderActive(terrains[i].Value, active, skipSuppressed: false);

            var renderEntities = RequireSectionBuffer<RuntimeCellSectionRenderEntity>(sectionEntity, "render entities");
            for (int i = 0; i < renderEntities.Length; i++)
                SetSingleRenderActive(renderEntities[i].Value, active, skipSuppressed: true);

            var combined = RequireSectionBuffer<RuntimeCellSectionCombinedRenderEntity>(sectionEntity, "combined render entities");
            for (int i = 0; i < combined.Length; i++)
            {
                Entity entity = RequireSectionEntity(combined[i].Value, "combined render entity");
                var chunk = EntityManager.GetComponentData<CombinedCellRenderChunk>(entity);
                SetMaterialMeshEnabled(entity, active && chunk.Disabled == 0);
            }
        }

        void SetSingleRenderActive(Entity entity, bool active, bool skipSuppressed)
        {
            entity = RequireSectionEntity(entity, "render entity");
            if (skipSuppressed && EntityManager.HasComponent<CombinedCellRenderSuppressed>(entity))
                return;
            if (active && IsLogicalParentDisabled(entity))
                active = false;
            SetMaterialMeshEnabled(entity, active);
        }

        bool IsLogicalParentDisabled(Entity entity)
        {
            if (!EntityManager.HasComponent<LogicalRefParent>(entity))
                return false;

            Entity parent = EntityManager.GetComponentData<LogicalRefParent>(entity).Value;
            return parent != Entity.Null
                   && EntityManager.Exists(parent)
                   && EntityManager.HasComponent<PlacedRefRuntimeState>(parent)
                   && EntityManager.GetComponentData<PlacedRefRuntimeState>(parent).Disabled != 0;
        }

        void SetMaterialMeshEnabled(Entity entity, bool active)
        {
            if (EntityManager.IsComponentEnabled<MaterialMeshInfo>(entity) != active)
                EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, active);
        }

        void QueueSectionPhysicsActive(Entity sectionEntity, bool active)
        {
            var colliders = RequireSectionBuffer<RuntimeCellSectionColliderEntity>(sectionEntity, "collider entities");
            Entity queueEntity = RuntimePhysicsMutationQueueUtility.RequireQueueEntity(EntityManager);
            var mutations = EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            bool queued = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Entity entity = RequireSectionEntity(colliders[i].Value, "collider entity");
                if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                    throw new System.InvalidOperationException("[VVardenfell][CellSection] collider entity is missing RuntimeColliderSource; resource binding must run first.");
                bool isActive = EntityManager.HasComponent<PhysicsCollider>(entity);
                if (active && !isActive)
                {
                    RuntimePhysicsMutationQueueUtility.EnqueueEnable(ref mutations, entity);
                    queued = true;
                }
                else if (!active && isActive)
                {
                    RuntimePhysicsMutationQueueUtility.EnqueueDisable(ref mutations, entity);
                    queued = true;
                }
            }

            if (queued)
                RuntimePhysicsMutationQueueUtility.MarkFlushRequested(EntityManager, queueEntity);
        }

        void QueueSectionActorActive(Entity sectionEntity, bool active)
        {
            var actors = RequireSectionBuffer<RuntimeCellSectionActorInitEntity>(sectionEntity, "actor init entities");
            if (actors.Length == 0)
                return;

            Entity queueEntity = RuntimePhysicsMutationQueueUtility.RequireQueueEntity(EntityManager);
            var mutations = EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            bool queued = false;
            for (int i = 0; i < actors.Length; i++)
            {
                Entity actor = RequireSectionEntity(actors[i].Value, "actor init entity");
                bool actorActive = active
                                   && (!EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                                       || EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled == 0);
                SetActorVisibility(actor, actorActive);
                QueueRuntimeColliderActive(actor, actorActive, ref mutations, ref queued);
                if (!EntityManager.HasBuffer<LogicalRefChild>(actor))
                    continue;

                var children = EntityManager.GetBuffer<LogicalRefChild>(actor);
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                {
                    Entity child = children[childIndex].Value;
                    if (child == Entity.Null || !EntityManager.Exists(child))
                        continue;

                    QueueRuntimeColliderActive(child, actorActive, ref mutations, ref queued);
                }
            }

            if (queued)
                RuntimePhysicsMutationQueueUtility.MarkFlushRequested(EntityManager, queueEntity);
        }

        void SetActorVisibility(Entity actor, bool active)
        {
            if (EntityManager.HasComponent<ActorRenderVisible>(actor)
                && EntityManager.IsComponentEnabled<ActorRenderVisible>(actor) != active)
            {
                EntityManager.SetComponentEnabled<ActorRenderVisible>(actor, active);
            }

            if (EntityManager.HasComponent<ActorShadowCasterVisible>(actor)
                && EntityManager.IsComponentEnabled<ActorShadowCasterVisible>(actor) != active)
            {
                EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(actor, active);
            }
        }

        void QueueRuntimeColliderActive(
            Entity entity,
            bool active,
            ref DynamicBuffer<RuntimePhysicsMutationRequest> mutations,
            ref bool queued)
        {
            if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return;

            bool isActive = EntityManager.HasComponent<PhysicsCollider>(entity);
            if (active && !isActive)
            {
                RuntimePhysicsMutationQueueUtility.EnqueueEnable(ref mutations, entity);
                queued = true;
            }
            else if (!active && isActive)
            {
                RuntimePhysicsMutationQueueUtility.EnqueueDisable(ref mutations, entity);
                queued = true;
            }
        }

        DynamicBuffer<T> RequireSectionBuffer<T>(Entity sectionEntity, string label)
            where T : unmanaged, IBufferElementData
        {
            if (sectionEntity == Entity.Null || !EntityManager.Exists(sectionEntity) || !EntityManager.HasBuffer<T>(sectionEntity))
                throw new System.InvalidOperationException($"[VVardenfell][CellSection] section root is missing {label}; rebake required.");
            return EntityManager.GetBuffer<T>(sectionEntity);
        }

        Entity RequireSectionEntity(Entity entity, string label)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                throw new System.InvalidOperationException($"[VVardenfell][CellSection] section buffer references missing {label}; rebake required.");
            return entity;
        }

        bool TryGetSectionStaticCollider(Entity sectionEntity, out BlobAssetReference<Collider> collider)
        {
            collider = default;
            if (sectionEntity == Entity.Null || !EntityManager.Exists(sectionEntity))
                return false;
            if (!EntityManager.HasBuffer<RuntimeCellSectionColliderEntity>(sectionEntity))
                return false;
            var colliders = EntityManager.GetBuffer<RuntimeCellSectionColliderEntity>(sectionEntity);
            for (int i = 0; i < colliders.Length; i++)
            {
                Entity entity = colliders[i].Value;
                if (entity != Entity.Null
                    && EntityManager.Exists(entity)
                    && EntityManager.HasComponent<RuntimeCellSectionStaticCollider>(entity))
                {
                    collider = EntityManager.GetComponentData<RuntimeCellSectionStaticCollider>(entity).Blob;
                    return collider.IsCreated;
                }
            }
            return false;
        }

        bool TryGetSectionTerrainCollider(Entity sectionEntity, out BlobAssetReference<Collider> collider)
        {
            collider = default;
            if (sectionEntity == Entity.Null || !EntityManager.Exists(sectionEntity))
                return false;
            if (!EntityManager.HasBuffer<RuntimeCellSectionTerrainEntity>(sectionEntity))
                return false;
            var terrains = EntityManager.GetBuffer<RuntimeCellSectionTerrainEntity>(sectionEntity);
            for (int i = 0; i < terrains.Length; i++)
            {
                Entity entity = terrains[i].Value;
                if (entity != Entity.Null
                    && EntityManager.Exists(entity)
                    && EntityManager.HasComponent<RuntimeCellSectionTerrainCollider>(entity))
                {
                    collider = EntityManager.GetComponentData<RuntimeCellSectionTerrainCollider>(entity).Blob;
                    return collider.IsCreated;
                }
            }
            return false;
        }

        static void QueuePhysicsCell(ref NativeList<int2> pendingCells, int2 coord)
        {
            for (int i = 0; i < pendingCells.Length; i++)
            {
                if (pendingCells[i].Equals(coord))
                    return;
            }

            pendingCells.Add(coord);
        }

    }
}
