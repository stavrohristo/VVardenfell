using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Bootstrap
{
    internal static class RuntimeBootstrapUtility
    {
        public static Entity ResolveOrCreate<TPreferred>(
            EntityManager entityManager,
            string entityName = null)
            where TPreferred : unmanaged, IComponentData
        {
            if (TryGetSingletonEntity<TPreferred>(entityManager, out Entity entity))
                return entity;

            entity = entityManager.CreateEntity();
            if (!string.IsNullOrEmpty(entityName))
                entityManager.SetName(entity, entityName);
            return entity;
        }

        public static Entity ResolveOrCreate<TFirst, TSecond>(
            EntityManager entityManager,
            string entityName = null)
            where TFirst : unmanaged, IComponentData
            where TSecond : unmanaged, IComponentData
        {
            if (TryGetSingletonEntity<TFirst>(entityManager, out Entity entity)
                || TryGetSingletonEntity<TSecond>(entityManager, out entity))
            {
                return entity;
            }

            entity = entityManager.CreateEntity();
            if (!string.IsNullOrEmpty(entityName))
                entityManager.SetName(entity, entityName);
            return entity;
        }

        public static Entity ResolveOrCreate<TPreferred>(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            FixedString64Bytes entityName,
            out bool created)
            where TPreferred : unmanaged, IComponentData
        {
            if (TryGetSingletonEntity<TPreferred>(entityManager, out Entity entity))
            {
                created = false;
                return entity;
            }

            created = true;
            entity = ecb.CreateEntity();
            if (!entityName.IsEmpty)
                ecb.SetName(entity, entityName);
            return entity;
        }

        public static Entity ResolveOrCreate<TFirst, TSecond>(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            FixedString64Bytes entityName,
            out bool created)
            where TFirst : unmanaged, IComponentData
            where TSecond : unmanaged, IComponentData
        {
            if (TryGetSingletonEntity<TFirst>(entityManager, out Entity entity)
                || TryGetSingletonEntity<TSecond>(entityManager, out entity))
            {
                created = false;
                return entity;
            }

            created = true;
            entity = ecb.CreateEntity();
            if (!entityName.IsEmpty)
                ecb.SetName(entity, entityName);
            return entity;
        }

        public static void EnsureComponent<T>(EntityManager entityManager, Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!entityManager.HasComponent<T>(entity))
                entityManager.AddComponentData(entity, value);
        }

        public static void SetOrAddComponent<T>(EntityManager entityManager, Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.SetComponentData(entity, value);
            else
                entityManager.AddComponentData(entity, value);
        }

        public static void EnsureBuffer<T>(EntityManager entityManager, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!entityManager.HasBuffer<T>(entity))
                entityManager.AddBuffer<T>(entity);
        }

        public static DynamicBuffer<T> EnsureClearedBuffer<T>(EntityManager entityManager, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            DynamicBuffer<T> buffer = entityManager.HasBuffer<T>(entity)
                ? entityManager.GetBuffer<T>(entity)
                : entityManager.AddBuffer<T>(entity);
            buffer.Clear();
            return buffer;
        }

        public static void EnsureComponent<T>(
            EntityManager entityManager,
            Entity entity,
            T value,
            ref EntityCommandBuffer ecb,
            bool entityCreated)
            where T : unmanaged, IComponentData
        {
            if (entityCreated || !entityManager.HasComponent<T>(entity))
                ecb.AddComponent(entity, value);
        }

        public static void EnsureBuffer<T>(
            EntityManager entityManager,
            Entity entity,
            ref EntityCommandBuffer ecb,
            bool entityCreated)
            where T : unmanaged, IBufferElementData
        {
            if (entityCreated || !entityManager.HasBuffer<T>(entity))
                ecb.AddBuffer<T>(entity);
        }

        static bool TryGetSingletonEntity<T>(EntityManager entityManager, out Entity entity)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = SingletonEntityQueryCache<T>.Get(entityManager);
            if (!query.IsEmptyIgnoreFilter)
            {
                entity = query.GetSingletonEntity();
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        static class SingletonEntityQueryCache<T>
            where T : unmanaged, IComponentData
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
