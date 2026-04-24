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
    }
}
