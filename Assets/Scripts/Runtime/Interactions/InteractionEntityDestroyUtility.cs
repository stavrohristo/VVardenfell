using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionEntityDestroyUtility
    {
        public static void DestroyLogicalRef(
            EntityManager entityManager,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup,
            bool preserveRuntimeSpawnRegistration = false)
        {
            LogicalRefDestroyUtility.DestroyLogicalRef(
                entityManager,
                logicalEntity,
                ref logicalRefLookup,
                preserveRuntimeSpawnRegistration);
        }
    }
}
