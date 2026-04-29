using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Systems
{
    internal static class MorrowindRuntimeLifecycleUtility
    {
        public static void EnsureActive(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimeActive>());
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "VVardenfell.RuntimeActive");
            entityManager.AddComponentData(entity, new MorrowindRuntimeActive());
        }

        public static void EnsurePaused(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "VVardenfell.RuntimePaused");
            entityManager.AddComponentData(entity, new MorrowindRuntimePaused());
        }

        public static void RemoveActive(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimeActive>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }

        public static void RemovePaused(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }

        public static void RemoveRuntimeLifecycle(EntityManager entityManager)
        {
            RemovePaused(entityManager);
            RemoveActive(entityManager);
        }
    }
}
