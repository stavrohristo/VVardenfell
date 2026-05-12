using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// "Unload" a cell by flipping the <see cref="MaterialMeshInfo"/> enable bit off on
    /// the entities owned by that cell section. BRG skips entities whose MMI is
    /// disabled, so rendering stops immediately. Entities stay in their chunks — no
    /// structural change, no chunk-layout scrambling, no re-Instantiate cost when the
    /// camera returns (just another bit flip to re-enable).
    ///
    /// Trade-off: memory grows with the set of cells ever visited (bounded in MW at
    /// ~1400 cells). This is the deliberate tradeoff for stable chunk layout — eager
    /// mode hits ~2× the frame rate because its entities never move.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellScheduleSystem))]
    public partial struct CellUnloadSystem : ISystem
    {
        EntityQuery _singletonQuery;

        public void OnCreate(ref SystemState state)
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAll<LoadedCellsMap, UnloadList, PendingCellPhysicsUnload, RuntimeSectionRegistry>()
                .Build();
            state.RequireForUpdate(_singletonQuery);

            // WithPresent<MaterialMeshInfo> (not WithAll) — queries with WithAll on an
        }

        public void OnUpdate(ref SystemState state)
        {
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var loadedEntity = _singletonQuery.GetSingletonEntity();
            var unload = _singletonQuery.GetSingleton<UnloadList>();
            var pendingPhysicsUnload = _singletonQuery.GetSingleton<PendingCellPhysicsUnload>();
            var registry = _singletonQuery.GetSingleton<RuntimeSectionRegistry>();
            if (unload.PendingEntityDestroy.Length == 0) return;

            state.EntityManager.CompleteDependencyBeforeRW<MaterialMeshInfo>();
            bool activeChanged = false;
            for (int i = 0; i < unload.PendingEntityDestroy.Length; i++)
            {
                var coord = unload.PendingEntityDestroy[i];

                bool hasSection = TryGetExteriorSection(ref state, registry, coord, out Entity sectionEntity);
                if (hasSection)
                {
                    SetSectionRenderActive(ref state, sectionEntity, active: false);
                    QueueSectionActorActive(ref state, sectionEntity, active: false);
                }

                QueuePhysicsCell(ref pendingPhysicsUnload.Cells, coord);
                if (loaded.Active.Remove(coord))
                {
                    loaded.ActiveRevision++;
                    activeChanged = true;
                    if (!hasSection)
                        throw new System.InvalidOperationException($"[VVardenfell][CellSection] active exterior cell ({coord.x},{coord.y}) has no section root.");
                    ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(state.EntityManager, sectionEntity, false);
                }
                if (loaded.SectionStates.IsCreated
                    && (!loaded.SectionStates.TryGetValue(coord, out byte rawState) || rawState != (byte)CellSectionLoadState.Failed))
                {
                    loaded.SectionStates[coord] = (byte)CellSectionLoadState.LoadedInactive;
                }
                // Managed resources (terrain Mesh/Texture/Material) stay alive across
                // enable/disable cycles — we only toggle render/physics state.
            }

            if (activeChanged)
                state.EntityManager.SetComponentData(loadedEntity, loaded);
            unload.PendingEntityDestroy.Clear();
        }

        static bool TryGetExteriorSection(ref SystemState state, RuntimeSectionRegistry registry, int2 coord, out Entity sectionEntity)
        {
            sectionEntity = default;
            return registry.ExteriorSections.IsCreated
                && registry.ExteriorSections.TryGetValue(coord, out sectionEntity)
                && sectionEntity != Entity.Null
                && state.EntityManager.Exists(sectionEntity);
        }

        static void SetSectionRenderActive(ref SystemState state, Entity sectionEntity, bool active)
        {
            var terrains = RequireSectionBuffer<RuntimeCellSectionTerrainEntity>(ref state, sectionEntity, "terrain entities");
            for (int i = 0; i < terrains.Length; i++)
                SetSingleRenderActive(ref state, terrains[i].Value, active, skipSuppressed: false);

            var renderEntities = RequireSectionBuffer<RuntimeCellSectionRenderEntity>(ref state, sectionEntity, "render entities");
            for (int i = 0; i < renderEntities.Length; i++)
                SetSingleRenderActive(ref state, renderEntities[i].Value, active, skipSuppressed: true);

            var combined = RequireSectionBuffer<RuntimeCellSectionCombinedRenderEntity>(ref state, sectionEntity, "combined render entities");
            for (int i = 0; i < combined.Length; i++)
            {
                Entity entity = RequireSectionEntity(ref state, combined[i].Value, "combined render entity");
                SetMaterialMeshEnabled(ref state, entity, active);
            }
        }

        static void SetSingleRenderActive(ref SystemState state, Entity entity, bool active, bool skipSuppressed)
        {
            entity = RequireSectionEntity(ref state, entity, "render entity");
            if (skipSuppressed && state.EntityManager.HasComponent<CombinedCellRenderSuppressed>(entity))
                return;
            if (active && IsLogicalParentDisabled(ref state, entity))
                active = false;
            SetMaterialMeshEnabled(ref state, entity, active);
        }

        static bool IsLogicalParentDisabled(ref SystemState state, Entity entity)
        {
            if (!state.EntityManager.HasComponent<LogicalRefParent>(entity))
                return false;

            Entity parent = state.EntityManager.GetComponentData<LogicalRefParent>(entity).Value;
            return parent != Entity.Null
                   && state.EntityManager.Exists(parent)
                   && state.EntityManager.HasComponent<PlacedRefRuntimeState>(parent)
                   && state.EntityManager.GetComponentData<PlacedRefRuntimeState>(parent).Disabled != 0;
        }

        static void SetMaterialMeshEnabled(ref SystemState state, Entity entity, bool active)
        {
            if (state.EntityManager.IsComponentEnabled<MaterialMeshInfo>(entity) != active)
                state.EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, active);
        }

        static void QueueSectionActorActive(ref SystemState state, Entity sectionEntity, bool active)
        {
            var actors = RequireSectionBuffer<RuntimeCellSectionActorInitEntity>(ref state, sectionEntity, "actor init entities");
            if (actors.Length == 0)
                return;

            Entity queueEntity = RuntimePhysicsMutationQueueUtility.RequireQueueEntity(state.EntityManager);
            var mutations = state.EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            bool queued = false;
            for (int i = 0; i < actors.Length; i++)
            {
                Entity actor = RequireSectionEntity(ref state, actors[i].Value, "actor init entity");
                bool actorActive = active
                                   && (!state.EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                                       || state.EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled == 0);
                SetActorVisibility(ref state, actor, actorActive);
                QueueRuntimeColliderActive(ref state, actor, actorActive, ref mutations, ref queued);
                if (!state.EntityManager.HasBuffer<LogicalRefChild>(actor))
                    continue;

                var children = state.EntityManager.GetBuffer<LogicalRefChild>(actor);
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                {
                    Entity child = children[childIndex].Value;
                    if (child == Entity.Null || !state.EntityManager.Exists(child))
                        continue;

                    QueueRuntimeColliderActive(ref state, child, actorActive, ref mutations, ref queued);
                }
            }

            if (queued)
                RuntimePhysicsMutationQueueUtility.MarkFlushRequested(state.EntityManager, queueEntity);
        }

        static void SetActorVisibility(ref SystemState state, Entity actor, bool active)
        {
            if (state.EntityManager.HasComponent<ActorRenderVisible>(actor)
                && state.EntityManager.IsComponentEnabled<ActorRenderVisible>(actor) != active)
            {
                state.EntityManager.SetComponentEnabled<ActorRenderVisible>(actor, active);
            }

            if (state.EntityManager.HasComponent<ActorShadowCasterVisible>(actor)
                && state.EntityManager.IsComponentEnabled<ActorShadowCasterVisible>(actor) != active)
            {
                state.EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(actor, active);
            }
        }

        static void QueueRuntimeColliderActive(
            ref SystemState state,
            Entity entity,
            bool active,
            ref DynamicBuffer<RuntimePhysicsMutationRequest> mutations,
            ref bool queued)
        {
            if (!state.EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return;

            bool isActive = state.EntityManager.HasComponent<PhysicsCollider>(entity);
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

        static DynamicBuffer<T> RequireSectionBuffer<T>(ref SystemState state, Entity sectionEntity, string label)
            where T : unmanaged, IBufferElementData
        {
            if (sectionEntity == Entity.Null || !state.EntityManager.Exists(sectionEntity) || !state.EntityManager.HasBuffer<T>(sectionEntity))
                throw new System.InvalidOperationException($"[VVardenfell][CellSection] section root is missing {label}; rebake required.");
            return state.EntityManager.GetBuffer<T>(sectionEntity);
        }

        static Entity RequireSectionEntity(ref SystemState state, Entity entity, string label)
        {
            if (entity == Entity.Null || !state.EntityManager.Exists(entity))
                throw new System.InvalidOperationException($"[VVardenfell][CellSection] section buffer references missing {label}; rebake required.");
            return entity;
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
