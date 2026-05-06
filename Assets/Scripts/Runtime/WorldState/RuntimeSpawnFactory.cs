using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    static class RuntimeSpawnFactory
    {
        public static bool QueueActorSpawn(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            ref RuntimeContentBlob content,
            ContentReference contentReference,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            byte persistencePolicy)
        {
            if (contentReference.Kind != ContentReferenceKind.Actor)
                    return false;

            Entity logicalEntity = LogicalRefEntityFactory.QueueCreate(
                entityManager,
                ref ecb,
                ref content,
                new LogicalRefEntityDescriptor
                {
                    ContentReference = contentReference,
                    PlacedRefId = runtimeRefId,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                    IsInterior = isInterior,
                    ExteriorCell = exteriorCell,
                    InteriorCellId = interiorCellId,
                    AddRuntimeSpawnIdentity = true,
                    RuntimeSpawnPersistencePolicy = persistencePolicy,
                });

            return true;
        }

        public static bool QueueSpawn(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            ref RuntimeContentBlob content,
            in WorldResources.RuntimeSpawnPrefabDescriptor descriptor,
            ContentReference contentReference,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            bool exteriorActive,
            ref LogicalRefLookup logicalRefLookup,
            Entity interiorTransitionEntity,
            byte persistencePolicy)
        {
            if (WorldResources.ModelPrefabs == null
                || (uint)descriptor.ModelPrefabIndex >= (uint)WorldResources.ModelPrefabs.Length)
                return false;

            if (!WorldBootstrap.EnsureModelPrefabBuilt(entityManager, descriptor.ModelPrefabIndex))
                return false;

            Entity renderPrefab = WorldResources.ModelPrefabs[descriptor.ModelPrefabIndex];
            Entity renderRoot = ecb.Instantiate(renderPrefab);
            QueueRenderRootMetadata(
                entityManager,
                ref ecb,
                renderPrefab,
                renderRoot,
                descriptor,
                runtimeRefId,
                position,
                rotation,
                scale,
                isInterior,
                exteriorCell,
                exteriorActive);

            Entity logicalEntity = LogicalRefEntityFactory.QueueCreate(
                entityManager,
                ref ecb,
                ref content,
                new LogicalRefEntityDescriptor
                {
                    ContentReference = contentReference,
                    PlacedRefId = runtimeRefId,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                    IsInterior = isInterior,
                    ExteriorCell = exteriorCell,
                    InteriorCellId = interiorCellId,
                    AddRuntimeSpawnIdentity = true,
                    RuntimeSpawnPersistencePolicy = persistencePolicy,
                });

            return true;
        }

        public static Entity QueueMaterializeSpawn(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            uint runtimeRefId,
            bool isInterior,
            int2 exteriorCell,
            bool exteriorActive,
            ref LogicalRefLookup logicalRefLookup,
            Entity interiorTransitionEntity)
        {
            Entity logicalEntity = FindLogicalRefByPlacedRef(entityManager, runtimeRefId);
            Entity renderRoot = FindRuntimeSpawnRenderRoot(entityManager, runtimeRefId);
            if (logicalEntity == Entity.Null)
                return Entity.Null;

            LogicalRefLookupUtility.Replace(ref logicalRefLookup, runtimeRefId, logicalEntity);
            if (renderRoot != Entity.Null)
            {
                LogicalRefChildUtility.QueueAppendLinkedEntityGroup(entityManager, ref ecb, logicalEntity, renderRoot);
                QueueLinkedRenderMetadata(entityManager, ref ecb, renderRoot, isInterior, exteriorCell);
            }

            if (!isInterior && !exteriorActive)
                QueueExteriorActiveState(entityManager, ref ecb, logicalEntity, false);

            if (isInterior)
                entityManager.GetBuffer<InteriorSpawnedEntity>(interiorTransitionEntity).Add(new InteriorSpawnedEntity { Value = logicalEntity });

            return logicalEntity;
        }

        static void QueueRenderRootMetadata(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity renderPrefab,
            Entity renderRoot,
            in WorldResources.RuntimeSpawnPrefabDescriptor descriptor,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            bool exteriorActive)
        {
            ecb.SetComponent(renderRoot, LocalTransform.FromPositionRotationScale(position, rotation, scale));
            ecb.SetComponent(renderRoot, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(scale)),
            });

            ecb.AddComponent(renderRoot, new PlacedRefIdentity { Value = runtimeRefId });
            ecb.AddComponent<RuntimeSpawnRenderRootTag>(renderRoot);
            if (isInterior)
            {
                if (!entityManager.HasComponent<InteriorCellMember>(renderPrefab))
                    ecb.AddComponent<InteriorCellMember>(renderRoot);
                if (entityManager.HasComponent<CellLink>(renderPrefab))
                    ecb.RemoveComponent<CellLink>(renderRoot);
            }
            else
            {
                if (entityManager.HasComponent<CellLink>(renderPrefab))
                    ecb.SetComponent(renderRoot, new CellLink { Value = exteriorCell });
                else
                    ecb.AddComponent(renderRoot, new CellLink { Value = exteriorCell });
            }

            var colliderBlobs = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Unity.Physics.Collider>>();
            if ((uint)descriptor.CollisionIndex < (uint)colliderBlobs.Length && colliderBlobs[descriptor.CollisionIndex].IsCreated)
            {
                var colliderBlob = colliderBlobs[descriptor.CollisionIndex];
                RuntimeColliderAttachmentUtility.QueueAttachInstantiatedSource(
                    entityManager,
                    ref ecb,
                    renderPrefab,
                    renderRoot,
                    colliderBlob,
                    RuntimeColliderKind.RuntimeSpawn,
                    active: isInterior || exteriorActive);
            }
        }

        static void QueueLinkedRenderMetadata(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity renderRoot,
            bool isInterior,
            int2 exteriorCell)
        {
            if (!entityManager.HasBuffer<LinkedEntityGroup>(renderRoot))
                return;

            if (!isInterior)
                WorldResources.RegisterExteriorCellEntity(exteriorCell, renderRoot);

            var linked = entityManager.GetBuffer<LinkedEntityGroup>(renderRoot);
            var linkedEntities = new NativeArray<Entity>(linked.Length, Allocator.Temp);
            for (int i = 0; i < linked.Length; i++)
                linkedEntities[i] = linked[i].Value;

            for (int i = 0; i < linkedEntities.Length; i++)
            {
                Entity child = linkedEntities[i];
                if (child == renderRoot || !entityManager.Exists(child))
                    continue;

                if (isInterior)
                {
                    if (!entityManager.HasComponent<InteriorCellMember>(child))
                        ecb.AddComponent<InteriorCellMember>(child);
                    if (entityManager.HasComponent<CellLink>(child))
                        ecb.RemoveComponent<CellLink>(child);
                }
                else
                {
                    if (entityManager.HasComponent<CellLink>(child))
                        ecb.SetComponent(child, new CellLink { Value = exteriorCell });
                    else
                        ecb.AddComponent(child, new CellLink { Value = exteriorCell });
                    WorldResources.RegisterExteriorCellEntity(exteriorCell, child);
                }
            }

            linkedEntities.Dispose();
        }

        public static void QueueExteriorActiveState(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity, bool active)
        {
            if (logicalEntity == Entity.Null || !entityManager.Exists(logicalEntity))
                return;

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            var childEntities = LogicalRefChildUtility.SnapshotChildBuffer(entityManager, logicalEntity);

            for (int i = 0; i < childEntities.Length; i++)
            {
                Entity child = childEntities[i];
                if (child == Entity.Null || !entityManager.Exists(child))
                    continue;

                if (entityManager.HasComponent<MaterialMeshInfo>(child))
                    entityManager.SetComponentEnabled<MaterialMeshInfo>(child, active);

                if (entityManager.HasComponent<RuntimeColliderSource>(child))
                {
                    if (active)
                        RuntimeColliderAttachmentUtility.QueueEnablePhysics(entityManager, ref ecb, child);
                    else
                        RuntimeColliderAttachmentUtility.QueueDisablePhysics(entityManager, ref ecb, child);
                }
            }

        }

        static Entity FindLogicalRefByPlacedRef(EntityManager entityManager, uint placedRefId)
        {
            EntityQuery query = LogicalRefIdentityQueryCache.Get(entityManager);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (identities[i].Value == placedRefId)
                    return entities[i];
            }

            return Entity.Null;
        }

        static Entity FindRuntimeSpawnRenderRoot(EntityManager entityManager, uint runtimeRefId)
        {
            EntityQuery query = RuntimeSpawnRenderRootQueryCache.Get(entityManager);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (identities[i].Value == runtimeRefId)
                    return entities[i];
            }

            return Entity.Null;
        }

        static class LogicalRefIdentityQueryCache
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
                    ComponentType.ReadOnly<LogicalRefTag>(),
                    ComponentType.ReadOnly<PlacedRefIdentity>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class RuntimeSpawnRenderRootQueryCache
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
                    ComponentType.ReadOnly<RuntimeSpawnRenderRootTag>(),
                    ComponentType.ReadOnly<PlacedRefIdentity>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
