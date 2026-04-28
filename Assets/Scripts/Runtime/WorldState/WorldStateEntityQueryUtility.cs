using Unity.Entities;

namespace VVardenfell.Runtime.WorldState
{
    static class WorldStateEntityQueryUtility
    {
        public static Entity GetSingletonEntity<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        public static Entity GetSingletonBufferOwner<T>(EntityManager entityManager)
            where T : unmanaged, IBufferElementData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        public static bool HasExactlyOne<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
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
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
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
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return TryGetExactlyOne(query, out entity, out error, label, notReadySuffix);
        }

        public static bool TryGetExactlyOne(
            EntityManager entityManager,
            out Entity entity,
            out string error,
            string label,
            string notReadySuffix,
            params ComponentType[] requiredTypes)
        {
            using var query = entityManager.CreateEntityQuery(requiredTypes);
            return TryGetExactlyOne(query, out entity, out error, label, notReadySuffix);
        }

        static bool TryGetExactlyOne(
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
    }
}
