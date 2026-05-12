using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class LogicalRefDestroyUtility
    {
        public static void QueueDestroyLogicalRef(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup,
            bool preserveRuntimeSpawnRegistration = false)
        {
            if (!entityManager.Exists(logicalEntity))
                return;

            if (entityManager.HasComponent<RuntimeCellSectionMember>(logicalEntity))
            {
                QueueRemoveResidentSectionLogicalRef(entityManager, ref ecb, logicalEntity, ref logicalRefLookup);
                return;
            }

            if (entityManager.HasComponent<RuntimeSpawnedRefIdentity>(logicalEntity))
            {
                uint runtimeRefId = entityManager.GetComponentData<RuntimeSpawnedRefIdentity>(logicalEntity).RuntimeRefId;
                if (preserveRuntimeSpawnRegistration)
                {
                    RuntimeSpawnProjectionUtility.MarkUnloaded(entityManager, runtimeRefId);
                }
                else
                {
                    RuntimeSpawnProjectionUtility.MarkDestroyed(entityManager, runtimeRefId);
                }
            }

            if (entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
                LogicalRefLookupUtility.Remove(ref logicalRefLookup, placedRefId);
            }

            var children = LogicalRefChildUtility.SnapshotChildBuffer(entityManager, logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                if (entityManager.Exists(children[i]))
                    ecb.DestroyEntity(children[i]);
            }

            if (entityManager.Exists(logicalEntity))
            {
                ActiveExplicitRefLookupLifecycleUtility.QueueDynamicRemoveIfTrackedOrActive(entityManager, logicalEntity);
                ecb.DestroyEntity(logicalEntity);
            }
        }

        static void QueueRemoveResidentSectionLogicalRef(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup)
        {
            if (!entityManager.HasComponent<PlacedRefRuntimeState>(logicalEntity))
                throw new System.InvalidOperationException("[VVardenfell][WorldRefs] resident section logical ref is missing PlacedRefRuntimeState; rebake required.");

            CombinedCellRenderDecombineUtility.DecombineIfLinked(entityManager, logicalEntity);

            ecb.SetComponent(logicalEntity, new PlacedRefRuntimeState { Disabled = 1 });
            if (entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
                ContentReference content = entityManager.HasComponent<LogicalRefContent>(logicalEntity)
                    ? entityManager.GetComponentData<LogicalRefContent>(logicalEntity).Value
                    : default;
                ScriptVisibleSaveStateUtility.UpsertRemoved(entityManager, placedRefId, content, 1);
                LogicalRefLookupUtility.Remove(ref logicalRefLookup, placedRefId);
            }

            ActiveExplicitRefLookupLifecycleUtility.QueueDynamicRemoveIfTrackedOrActive(entityManager, logicalEntity);
            QueueProjectEntityInactive(entityManager, ref ecb, logicalEntity, isActorRoot: entityManager.HasComponent<ActorSpawnSource>(logicalEntity));

            var children = LogicalRefChildUtility.SnapshotChildBuffer(entityManager, logicalEntity);
            bool isActor = entityManager.HasComponent<ActorSpawnSource>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i];
                if (child == Entity.Null || !entityManager.Exists(child))
                    continue;

                QueueProjectEntityInactive(entityManager, ref ecb, child, isActor);
            }
        }

        static void QueueProjectEntityInactive(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            bool isActorRoot)
        {
            if (entityManager.HasComponent<ActorRenderVisible>(entity))
                ecb.SetComponentEnabled<ActorRenderVisible>(entity, false);

            if (entityManager.HasComponent<ActorShadowCasterVisible>(entity))
                ecb.SetComponentEnabled<ActorShadowCasterVisible>(entity, false);

            if (entityManager.HasComponent<MaterialMeshInfo>(entity) && !isActorRoot)
                ecb.SetComponentEnabled<MaterialMeshInfo>(entity, false);

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                RuntimeColliderAttachmentUtility.QueueDisablePhysics(entityManager, ref ecb, entity);
        }
    }
}
