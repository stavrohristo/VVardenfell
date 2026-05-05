using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Player
{
    static class LocalPlayerPresentationLifecycleUtility
    {
        public static void QueueDestroyLocalPlayerVisuals(EntityManager entityManager, ref EntityCommandBuffer ecb)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LocalPlayerVisual>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    ecb.DestroyEntity(entities[i]);
            }
        }
    }
}
