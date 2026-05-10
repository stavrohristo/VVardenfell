using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldInteriorSpawnUtility
    {
        internal static void SpawnInteriorCell(World world, Entity sectionEntity, float3 worldOffset, Entity transitionEntity, ref LogicalRefLookup logicalRefs)
        {
            if (sectionEntity == Entity.Null)
                return;

            var em = world.EntityManager;
            var header = em.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
            var spawnedEntities = new List<Entity>();
            var refs = em.GetBuffer<RuntimeCellSectionRef>(sectionEntity);
            var refArray = refs.Length > 0 ? new RefEntry[refs.Length] : System.Array.Empty<RefEntry>();
            for (int i = 0; i < refs.Length; i++)
                refArray[i] = refs[i].Value;
            var interiorChildEntities = refArray.Length > 0 ? new Entity[refArray.Length] : System.Array.Empty<Entity>();
            var interiorCellId = header.CellId;

            if (em.HasComponent<RuntimeCellSectionStaticCollider>(sectionEntity))
                SpawnInteriorStaticCollider(em, header, em.GetComponentData<RuntimeCellSectionStaticCollider>(sectionEntity), worldOffset, spawnedEntities);

            for (int i = 0; i < refArray.Length; i++)
                interiorChildEntities[i] = WorldRefSpawnUtility.SpawnInteriorRef(em, refArray[i], worldOffset, interiorCellId, spawnedEntities);

            if (refArray.Length > 0)
            {
                var contentBlob = WorldResources.Cache?.ContentBlob ?? default;
                if (!contentBlob.IsCreated)
                    throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Interior spawn requires runtime content blob for logical refs.");
                WorldRefSpawnUtility.BuildLogicalRefs(
                    em,
                    contentBlob,
                    refArray,
                    interiorChildEntities,
                    true,
                    interiorCellId,
                    worldOffset,
                    ref logicalRefs,
                    spawnedEntities,
                    out _);
            }

            EnableInteriorPickColliders(em, spawnedEntities);

            var spawnedBuffer = em.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            for (int i = 0; i < spawnedEntities.Count; i++)
                spawnedBuffer.Add(new InteriorSpawnedEntity { Value = spawnedEntities[i] });
        }

        static void SpawnInteriorStaticCollider(
            EntityManager em,
            RuntimeCellSectionHeader header,
            RuntimeCellSectionStaticCollider collider,
            float3 worldOffset,
            List<Entity> spawnedEntities)
        {
            if (!collider.Blob.IsCreated)
                return;

            var staticEntity = em.CreateEntity();
            em.AddComponentData(staticEntity, LocalTransform.FromPositionRotationScale(worldOffset, quaternion.identity, 1f));
            em.AddComponentData(staticEntity, new LocalToWorld
            {
                Value = float4x4.Translate(worldOffset)
            });
            em.AddComponent<InteriorCellMember>(staticEntity);
            em.AddComponent<Unity.Transforms.Static>(staticEntity);
            RuntimeColliderAttachmentUtility.AttachSource(
                em,
                staticEntity,
                collider.Blob,
                RuntimeColliderKind.StaticCell,
                active: true);
            spawnedEntities.Add(staticEntity);
        }

        static void EnableInteriorPickColliders(EntityManager em, List<Entity> spawnedEntities)
        {
            if (spawnedEntities == null)
                return;

            for (int i = 0; i < spawnedEntities.Count; i++)
            {
                Entity entity = spawnedEntities[i];
                if (entity == Entity.Null || !em.Exists(entity))
                    continue;
                if (!em.HasComponent<InteractionPickSurfaceTag>(entity) || !em.HasComponent<RuntimeColliderSource>(entity))
                    continue;

                var source = em.GetComponentData<RuntimeColliderSource>(entity);
                if (source.Kind != RuntimeColliderKind.InteractionPick)
                    continue;

                if (em.HasComponent<ModelPrefabNodeTag>(entity)
                    && em.HasComponent<LogicalRefParent>(entity)
                    && IsActorLogicalRef(em, em.GetComponentData<LogicalRefParent>(entity).Value))
                {
                    continue;
                }

                RuntimeColliderAttachmentUtility.EnablePhysics(em, entity);
            }
        }

        static bool IsActorLogicalRef(EntityManager em, Entity logicalEntity)
        {
            return logicalEntity != Entity.Null
                   && em.Exists(logicalEntity)
                   && em.HasComponent<PassiveActorPresence>(logicalEntity);
        }
    }
}
