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
    public partial class InteriorSectionLifecycleSystem : SystemBase
    {
        EntityQuery _registryQuery;

        protected override void OnCreate()
        {
            _registryQuery = GetEntityQuery(
                ComponentType.ReadWrite<RuntimeSectionRegistry>());
        }

        protected override void OnUpdate()
        {
        }

        public bool LoadAndActivateByHash(
            ulong interiorCellHash,
            float3 worldOffset,
            ref RuntimeSectionRegistry registry,
            ref LogicalRefLookup logicalRefs,
            out FixedString128Bytes interiorCellId)
        {
            interiorCellId = default;
            if (interiorCellHash == 0UL)
                return false;

            if (!registry.InteriorCellIdsByHash.TryGetValue(interiorCellHash, out var cellId) || cellId.IsEmpty)
                return false;

            if (!registry.InteriorSectionsByHash.TryGetValue(interiorCellHash, out Entity sectionEntity)
                || sectionEntity == Entity.Null
                || !EntityManager.Exists(sectionEntity))
            {
                string path = CachePaths.InteriorCellSectionFile(cellId.ToString());
                var loaded = RuntimeCellSectionFile.LoadIntoWorld(EntityManager, path, isInterior: true, cellId: cellId.ToString());
                return MaterializeInteriorSection(
                    loaded,
                    worldOffset,
                    ref registry,
                    ref logicalRefs,
                    SetInteriorCellActiveState,
                    out interiorCellId);
            }

            var header = EntityManager.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
            return MaterializeInteriorSection(
                new RuntimeCellSectionLoadResult(sectionEntity, header),
                worldOffset,
                ref registry,
                ref logicalRefs,
                SetInteriorCellActiveState,
                out interiorCellId);
        }

        public void DeactivateInteriorCellByHash(ulong interiorCellHash)
        {
            if (interiorCellHash == 0UL)
                return;
            SetInteriorCellActiveState(interiorCellHash, active: false);
        }

        public void DeactivateActiveInterior(in InteriorTransitionState transition)
        {
            if (transition.InteriorActive == 0 || transition.ActiveInteriorCellHash == 0UL)
                return;
            DeactivateInteriorCellByHash(transition.ActiveInteriorCellHash);
        }

        public bool TryGetInteriorStaticCollider(ulong interiorCellHash, out BlobAssetReference<Collider> collider)
        {
            collider = default;
            if (interiorCellHash == 0UL || _registryQuery.CalculateEntityCount() != 1)
                return false;
            var registry = _registryQuery.GetSingleton<RuntimeSectionRegistry>();
            if (!registry.InteriorSectionsByHash.TryGetValue(interiorCellHash, out Entity sectionEntity))
                return false;
            return TryGetSectionStaticCollider(sectionEntity, out collider);
        }

        public void SetInteriorCellActiveState(ulong interiorCellHash, bool active)
        {
            if (interiorCellHash == 0UL)
                return;

            if (_registryQuery.CalculateEntityCount() != 1)
                throw new System.InvalidOperationException("[VVardenfell][CellSection] RuntimeSectionRegistry is unavailable.");
            var registry = _registryQuery.GetSingleton<RuntimeSectionRegistry>();
            if (!registry.InteriorSectionsByHash.TryGetValue(interiorCellHash, out Entity sectionEntity)
                || sectionEntity == Entity.Null
                || !EntityManager.Exists(sectionEntity))
            {
                return;
            }

            SetSectionActiveState(sectionEntity, active);
            ActiveExplicitRefLookupLifecycleUtility.QueueSectionChange(EntityManager, sectionEntity, active);
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

    }
}
