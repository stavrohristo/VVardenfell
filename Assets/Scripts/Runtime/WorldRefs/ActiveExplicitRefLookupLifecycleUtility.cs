using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class ActiveExplicitRefLookupLifecycleUtility
    {
        const int InitialCapacity = 1024;
        static World s_QueryWorld;
        static EntityQuery s_LookupQuery;
        static bool s_LookupQueryCreated;

        public static Entity CreateOrRepairForBootstrap(EntityManager entityManager)
        {
            EntityQuery query = GetLookupQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
                return Create(entityManager);

            Entity entity = query.GetSingletonEntity();
            var lookup = entityManager.GetComponentData<ActiveExplicitRefLookup>(entity);
            if (!lookup.ByContentKey.IsCreated)
                lookup.ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent);
            if (!lookup.AllByContentKey.IsCreated)
                lookup.AllByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent);
            entityManager.SetComponentData(entity, lookup);

            if (!entityManager.HasComponent<ActiveExplicitRefLookupDirty>(entity))
                entityManager.AddComponent<ActiveExplicitRefLookupDirty>(entity);
            if (!entityManager.HasComponent<ActiveExplicitRefLookupBuildState>(entity))
                entityManager.AddComponentData(entity, new ActiveExplicitRefLookupBuildState());
            if (!entityManager.HasComponent<SessionTeardown>(entity))
            {
                entityManager.AddComponent<SessionTeardown>(entity);
                entityManager.SetComponentEnabled<SessionTeardown>(entity, false);
            }

            entityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(entity, true);
            entityManager.SetComponentData(entity, new ActiveExplicitRefLookupBuildState());
            return entity;
        }

        public static void DisposeAll(EntityManager entityManager)
        {
            EntityQuery query = GetLookupQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!entityManager.Exists(entity))
                    continue;

                Dispose(entityManager, entity);
            }
        }

        public static void Dispose(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;

            var lookup = entityManager.GetComponentData<ActiveExplicitRefLookup>(entity);
            if (lookup.ByContentKey.IsCreated)
                lookup.ByContentKey.Dispose();
            if (lookup.AllByContentKey.IsCreated)
                lookup.AllByContentKey.Dispose();
            entityManager.SetComponentData(entity, default(ActiveExplicitRefLookup));
        }

        public static void MarkDirty(EntityManager entityManager)
        {
            EntityQuery query = GetLookupQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
                return;

            Entity entity = query.GetSingletonEntity();
            if (!entityManager.HasComponent<ActiveExplicitRefLookupDirty>(entity))
                throw new InvalidOperationException("[VVardenfell][WorldRefs] active explicit-ref lookup exists without its dirty marker.");

            entityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(entity, true);
        }

        static EntityQuery GetLookupQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_LookupQueryCreated && s_QueryWorld == world)
                return s_LookupQuery;

            s_QueryWorld = world;
            s_LookupQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ActiveExplicitRefLookup>());
            s_LookupQueryCreated = true;
            return s_LookupQuery;
        }

        static Entity Create(EntityManager entityManager)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ActiveExplicitRefLookup),
                typeof(ActiveExplicitRefLookupDirty),
                typeof(ActiveExplicitRefLookupBuildState),
                typeof(SessionTeardown));
            entityManager.SetName(entity, "VVardenfell.ActiveExplicitRefs");
            entityManager.SetComponentData(entity, new ActiveExplicitRefLookup
            {
                ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent),
                AllByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent),
            });
            entityManager.SetComponentEnabled<SessionTeardown>(entity, false);
            return entity;
        }
    }
}
