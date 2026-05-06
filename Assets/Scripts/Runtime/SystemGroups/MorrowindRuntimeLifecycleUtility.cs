using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Systems
{
    internal static class MorrowindRuntimeLifecycleUtility
    {
        public static void EnsureActive(EntityManager entityManager, EntityQuery query)
        {
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "VVardenfell.RuntimeActive");
            entityManager.AddComponentData(entity, new MorrowindRuntimeActive());
        }

        public static void EnsurePaused(EntityManager entityManager, EntityQuery query)
        {
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "VVardenfell.RuntimePaused");
            entityManager.AddComponentData(entity, new MorrowindRuntimePaused());
        }

        public static void RemoveActive(EntityManager entityManager, EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }

        public static void RemovePaused(EntityManager entityManager, EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }

        public static void RemoveRuntimeLifecycle(EntityManager entityManager, EntityQuery pausedQuery, EntityQuery activeQuery)
        {
            RemovePaused(entityManager, pausedQuery);
            RemoveActive(entityManager, activeQuery);
        }
    }
}
