using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class LogicalRefDestroyUtility
    {
        public static void DestroyLogicalRef(
            EntityManager entityManager,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup,
            bool preserveRuntimeSpawnRegistration = false)
        {
            if (!entityManager.Exists(logicalEntity))
                return;

            if (entityManager.HasComponent<RuntimeSpawnedRefIdentity>(logicalEntity))
            {
                uint runtimeRefId = entityManager.GetComponentData<RuntimeSpawnedRefIdentity>(logicalEntity).RuntimeRefId;
                if (preserveRuntimeSpawnRegistration)
                {
                    RuntimeSpawnProjectionUtility.MarkUnloaded(entityManager, runtimeRefId);
                }
                else if (RuntimeSpawnProjectionUtility.MarkDestroyed(entityManager, runtimeRefId))
                {
                    WorldJournalUtility.AppendRuntimeDestroyed(entityManager, runtimeRefId);
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
                Entity child = children[i];
                if (entityManager.Exists(child)
                    && entityManager.HasComponent<InteractionActivationProxyTag>(child)
                    && entityManager.HasComponent<RuntimeColliderSource>(child))
                {
                    var source = entityManager.GetComponentData<RuntimeColliderSource>(child);
                    if (source.Temporary != 0)
                        RuntimeColliderBlobLifetime.DeferGeneratedBlobDisposal(entityManager, source.Value);
                }

                if (entityManager.Exists(child))
                    entityManager.DestroyEntity(child);
            }

            if (entityManager.Exists(logicalEntity))
                entityManager.DestroyEntity(logicalEntity);
        }
    }
}
