using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionActivationProxyBuildUtility
    {
        public static bool QueueEnsureQueued(ref RuntimeContentBlob contentBlob, EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            if (!entityManager.Exists(logicalEntity))
                return false;

            if (!entityManager.HasComponent<LogicalRefTag>(logicalEntity) || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                return false;

            if (entityManager.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity))
                return false;

            if (HasLiveProxy(entityManager, logicalEntity))
                return false;

            if (entityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
            {
                Entity existingProxy = entityManager.GetComponentData<InteractionActivationProxyState>(logicalEntity).ProxyEntity;
                if (existingProxy == Entity.Null || !entityManager.Exists(existingProxy))
                    ecb.RemoveComponent<InteractionActivationProxyState>(logicalEntity);
                else
                    return false;
            }

            if (!InteractionTargetResolver.TryResolveSupportedKind(ref contentBlob, entityManager, logicalEntity, out _))
                return false;

            ecb.AddComponent<InteractionActivationProxyBuildPending>(logicalEntity);
            return true;
        }

        public static void QueuePendingCleared(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            if (entityManager.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity))
                ecb.RemoveComponent<InteractionActivationProxyBuildPending>(logicalEntity);
        }

        public static bool HasLiveProxy(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
                return false;

            Entity proxyEntity = entityManager.GetComponentData<InteractionActivationProxyState>(logicalEntity).ProxyEntity;
            return proxyEntity != Entity.Null && entityManager.Exists(proxyEntity);
        }
    }
}
