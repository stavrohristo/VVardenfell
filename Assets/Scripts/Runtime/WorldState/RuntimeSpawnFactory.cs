using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    static class RuntimeSpawnFactory
    {
        public static Entity Spawn(
            EntityManager entityManager,
            RuntimeContentDatabase contentDb,
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
                return Entity.Null;

            if (!WorldBootstrap.EnsureModelPrefabBuilt(entityManager, WorldResources.Cache, descriptor.ModelPrefabIndex))
                return Entity.Null;

            Entity renderRoot = entityManager.Instantiate(WorldResources.ModelPrefabs[descriptor.ModelPrefabIndex]);
            ApplyRenderRootMetadata(
                entityManager,
                renderRoot,
                descriptor,
                runtimeRefId,
                position,
                rotation,
                scale,
                isInterior,
                exteriorCell,
                exteriorActive);

            Entity logicalEntity = LogicalRefEntityFactory.Create(
                entityManager,
                contentDb,
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

            LogicalRefChildUtility.AppendLinkedEntityGroup(entityManager, logicalEntity, renderRoot);
            LogicalRefEntityFactory.EnsureInteractionProxyQueued(entityManager, logicalEntity);
            LogicalRefLookupUtility.Replace(ref logicalRefLookup, runtimeRefId, logicalEntity);

            if (!isInterior && !exteriorActive)
                SetExteriorActiveState(entityManager, logicalEntity, false);

            if (isInterior)
                entityManager.GetBuffer<InteriorSpawnedEntity>(interiorTransitionEntity).Add(new InteriorSpawnedEntity { Value = logicalEntity });

            return logicalEntity;
        }

        static void ApplyRenderRootMetadata(
            EntityManager entityManager,
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
            entityManager.SetComponentData(renderRoot, LocalTransform.FromPositionRotationScale(position, rotation, scale));
            entityManager.SetComponentData(renderRoot, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(scale)),
            });

            entityManager.AddComponentData(renderRoot, new PlacedRefIdentity { Value = runtimeRefId });
            if (isInterior)
            {
                if (!entityManager.HasComponent<InteriorCellMember>(renderRoot))
                    entityManager.AddComponent<InteriorCellMember>(renderRoot);
                if (entityManager.HasComponent<CellLink>(renderRoot))
                    entityManager.RemoveComponent<CellLink>(renderRoot);
            }
            else
            {
                if (entityManager.HasComponent<CellLink>(renderRoot))
                    entityManager.SetComponentData(renderRoot, new CellLink { Value = exteriorCell });
                else
                    entityManager.AddComponentData(renderRoot, new CellLink { Value = exteriorCell });
            }

            var colliderBlobs = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Unity.Physics.Collider>>();
            if ((uint)descriptor.CollisionIndex < (uint)colliderBlobs.Length && colliderBlobs[descriptor.CollisionIndex].IsCreated)
            {
                var colliderBlob = colliderBlobs[descriptor.CollisionIndex];
                RuntimeColliderAttachmentUtility.AttachSource(
                    entityManager,
                    renderRoot,
                    colliderBlob,
                    RuntimeColliderKind.RuntimeSpawn,
                    active: isInterior || exteriorActive);
            }

            if (!entityManager.HasBuffer<LinkedEntityGroup>(renderRoot))
                return;

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
                        entityManager.AddComponent<InteriorCellMember>(child);
                    if (entityManager.HasComponent<CellLink>(child))
                        entityManager.RemoveComponent<CellLink>(child);
                }
                else
                {
                    if (entityManager.HasComponent<CellLink>(child))
                        entityManager.SetComponentData(child, new CellLink { Value = exteriorCell });
                    else
                        entityManager.AddComponentData(child, new CellLink { Value = exteriorCell });
                }
            }

            linkedEntities.Dispose();
        }

        public static void SetExteriorActiveState(EntityManager entityManager, Entity logicalEntity, bool active)
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
                        RuntimeColliderAttachmentUtility.EnablePhysics(entityManager, child);
                    else
                        RuntimeColliderAttachmentUtility.DisablePhysics(entityManager, child);
                }
            }

        }
    }
}
