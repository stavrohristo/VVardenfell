using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionEntityDestroyUtility
    {
        public static void QueueDestroyLogicalRef(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup,
            bool preserveRuntimeSpawnRegistration = false)
        {
            LogicalRefDestroyUtility.QueueDestroyLogicalRef(
                entityManager,
                ref ecb,
                logicalEntity,
                ref logicalRefLookup,
                preserveRuntimeSpawnRegistration);
        }
    }
}
