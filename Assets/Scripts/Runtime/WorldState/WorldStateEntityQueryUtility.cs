using Unity.Entities;

namespace VVardenfell.Runtime.WorldState
{
    static class WorldStateEntityQueryUtility
    {
        public static Entity GetSingletonEntity<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = ComponentQueryCache<T>.Get(entityManager);
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        public static Entity GetSingletonBufferOwner<T>(EntityManager entityManager)
            where T : unmanaged, IBufferElementData
        {
            EntityQuery query = BufferQueryCache<T>.Get(entityManager);
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        public static bool HasExactlyOne<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = ComponentQueryCache<T>.Get(entityManager);
            return query.CalculateEntityCount() == 1;
        }

        public static bool TryGetExactlyOne<T>(
            EntityManager entityManager,
            out Entity entity,
            out string error,
            string label,
            string notReadySuffix = null)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = ComponentQueryCache<T>.Get(entityManager);
            return TryGetExactlyOne(query, out entity, out error, label, notReadySuffix);
        }

        public static bool TryGetExactlyOneBufferOwner<T>(
            EntityManager entityManager,
            out Entity entity,
            out string error,
            string label,
            string notReadySuffix = null)
            where T : unmanaged, IBufferElementData
        {
            EntityQuery query = BufferQueryCache<T>.Get(entityManager);
            return TryGetExactlyOne(query, out entity, out error, label, notReadySuffix);
        }

        public static bool TryGetExactlyOne(
            EntityQuery query,
            out Entity entity,
            out string error,
            string label,
            string notReadySuffix)
        {
            entity = Entity.Null;
            int count = query.CalculateEntityCount();
            if (count != 1)
            {
                error = count == 0
                    ? $"Runtime {label} state is not ready{FormatNotReadySuffix(notReadySuffix)}"
                    : $"Runtime {label} state has {count} entities; expected exactly one.";
                return false;
            }

            entity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static string FormatNotReadySuffix(string suffix)
        {
            return string.IsNullOrEmpty(suffix) ? "." : $" {suffix}.";
        }

        static class ComponentQueryCache<T>
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

        static class BufferQueryCache<T>
            where T : unmanaged, IBufferElementData
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
