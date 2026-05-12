using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

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
            Entity entity = query.IsEmptyIgnoreFilter ? Create(entityManager) : query.GetSingletonEntity();
            EnsureContainers(entityManager, entity);
            EnsureEventComponents(entityManager, entity);
            QueueFullRebuild(entityManager, entity);
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
                if (entityManager.Exists(entity))
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
            if (lookup.ActiveEntriesByContentKey.IsCreated)
                lookup.ActiveEntriesByContentKey.Dispose();
            if (lookup.ActiveDynamicEntriesByEntity.IsCreated)
                lookup.ActiveDynamicEntriesByEntity.Dispose();
            if (lookup.ActiveExteriorCells.IsCreated)
                lookup.ActiveExteriorCells.Dispose();
            entityManager.SetComponentData(entity, default(ActiveExplicitRefLookup));
        }

        public static void MarkDirty(EntityManager entityManager)
            => QueueFullRebuild(entityManager);

        public static void QueueFullRebuild(EntityManager entityManager)
        {
            if (!TryGetLookupEntity(entityManager, out Entity entity))
                return;

            QueueFullRebuild(entityManager, entity);
        }

        public static void QueueSectionChange(EntityManager entityManager, Entity section, bool activate)
        {
            if (section == Entity.Null)
                throw new InvalidOperationException("[VVardenfell][WorldRefs] cannot queue active explicit-ref section change for a null section.");
            if (!entityManager.Exists(section))
                throw new InvalidOperationException("[VVardenfell][WorldRefs] cannot queue active explicit-ref section change for a missing section.");
            if (!entityManager.HasBuffer<RuntimeCellSectionExplicitRefEntry>(section))
                throw new InvalidOperationException("[VVardenfell][WorldRefs] section root is missing RuntimeCellSectionExplicitRefEntry; rebake required.");
            if (!TryGetLookupEntity(entityManager, out Entity lookupEntity))
                return;

            EnsureEventComponents(entityManager, lookupEntity);
            entityManager.GetBuffer<ActiveExplicitSectionChange>(lookupEntity).Add(new ActiveExplicitSectionChange
            {
                Section = section,
                Activate = (byte)(activate ? 1 : 0),
            });
            MarkWorkPending(entityManager, lookupEntity);
        }

        public static void QueueDynamicAddIfActive(
            EntityManager entityManager,
            Entity entity,
            in LoadedCellsMap loadedCells,
            in InteriorTransitionState transition)
        {
            if (!TryGetDynamicRef(entityManager, entity, out int contentKey, out uint placedRefId))
                return;

            bool isActive = IsEntityActive(entityManager, entity, loadedCells, transition);
            if (!isActive)
                return;

            QueueDynamicChange(
                entityManager,
                entity,
                contentKey,
                placedRefId,
                ActiveExplicitDynamicRefOperation.Add,
                wasActive: false,
                isActive: true);
        }

        public static void QueueDynamicRemoveIfTrackedOrActive(
            EntityManager entityManager,
            Entity entity)
        {
            TryGetSingleton(entityManager, out LoadedCellsMap loadedCells);
            TryGetSingleton(entityManager, out InteriorTransitionState transition);
            QueueDynamicRemoveIfTrackedOrActive(entityManager, entity, loadedCells, transition);
        }

        public static void QueueDynamicRemoveIfTrackedOrActive(
            EntityManager entityManager,
            Entity entity,
            in LoadedCellsMap loadedCells,
            in InteriorTransitionState transition)
        {
            if (!TryGetDynamicRef(entityManager, entity, out int contentKey, out uint placedRefId))
                return;

            bool wasActive = IsEntityActive(entityManager, entity, loadedCells, transition);
            if (!wasActive && TryGetLookupEntity(entityManager, out Entity lookupEntity))
            {
                var lookup = entityManager.GetComponentData<ActiveExplicitRefLookup>(lookupEntity);
                wasActive = lookup.ActiveDynamicEntriesByEntity.IsCreated && lookup.ActiveDynamicEntriesByEntity.ContainsKey(entity);
            }
            if (!wasActive)
                return;

            QueueDynamicChange(
                entityManager,
                entity,
                contentKey,
                placedRefId,
                ActiveExplicitDynamicRefOperation.Remove,
                wasActive: true,
                isActive: false);
        }

        public static void QueueDynamicMove(
            EntityManager entityManager,
            Entity entity,
            in LogicalRefLocation previousLocation,
            in LoadedCellsMap loadedCells,
            in InteriorTransitionState transition)
        {
            if (!TryGetDynamicRef(entityManager, entity, out int contentKey, out uint placedRefId))
                return;
            if (!entityManager.HasComponent<LogicalRefLocation>(entity))
                return;

            bool wasActive = IsLocationActive(previousLocation, loadedCells, transition);
            var currentLocation = entityManager.GetComponentData<LogicalRefLocation>(entity);
            bool isActive = IsLocationActive(currentLocation, loadedCells, transition);
            if (!wasActive && !isActive)
                return;

            QueueDynamicChange(
                entityManager,
                entity,
                contentKey,
                placedRefId,
                ActiveExplicitDynamicRefOperation.Move,
                wasActive,
                isActive);
        }

        static void QueueFullRebuild(EntityManager entityManager, Entity entity)
        {
            EnsureEventComponents(entityManager, entity);
            entityManager.GetBuffer<ActiveExplicitSectionChange>(entity).Clear();
            entityManager.GetBuffer<ActiveExplicitDynamicRefChange>(entity).Clear();
            entityManager.SetComponentEnabled<ActiveExplicitRefLookupFullRebuild>(entity, true);
            MarkWorkPending(entityManager, entity);
            if (entityManager.HasComponent<ActiveExplicitRefLookupDirty>(entity))
                entityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(entity, true);
        }

        static void QueueDynamicChange(
            EntityManager entityManager,
            Entity entity,
            int contentKey,
            uint placedRefId,
            ActiveExplicitDynamicRefOperation operation,
            bool wasActive,
            bool isActive)
        {
            if (contentKey == 0 || placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][WorldRefs] cannot queue active explicit-ref dynamic change for an invalid content key or placed ref id.");
            if (!TryGetLookupEntity(entityManager, out Entity lookupEntity))
                return;

            EnsureEventComponents(entityManager, lookupEntity);
            entityManager.GetBuffer<ActiveExplicitDynamicRefChange>(lookupEntity).Add(new ActiveExplicitDynamicRefChange
            {
                Entity = entity,
                ContentKey = contentKey,
                PlacedRefId = placedRefId,
                Operation = (byte)operation,
                WasActive = (byte)(wasActive ? 1 : 0),
                IsActive = (byte)(isActive ? 1 : 0),
            });
            MarkWorkPending(entityManager, lookupEntity);
        }

        static bool TryGetDynamicRef(EntityManager entityManager, Entity entity, out int contentKey, out uint placedRefId)
        {
            contentKey = 0;
            placedRefId = 0u;
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return false;
            if (entityManager.HasComponent<RuntimeCellSectionMember>(entity))
                return false;
            if (!entityManager.HasComponent<LogicalRefTag>(entity)
                || !entityManager.HasComponent<LogicalRefContent>(entity)
                || !entityManager.HasComponent<PlacedRefIdentity>(entity))
            {
                return false;
            }

            var content = entityManager.GetComponentData<LogicalRefContent>(entity).Value;
            placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
            if (!content.IsValid || placedRefId == 0u)
                return false;
            contentKey = ActiveExplicitRefLookupUtility.Pack(content);
            return contentKey != 0;
        }

        static bool IsEntityActive(
            EntityManager entityManager,
            Entity entity,
            in LoadedCellsMap loadedCells,
            in InteriorTransitionState transition)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<LogicalRefLocation>(entity))
                return false;
            return IsLocationActive(entityManager.GetComponentData<LogicalRefLocation>(entity), loadedCells, transition);
        }

        static bool IsLocationActive(
            in LogicalRefLocation location,
            in LoadedCellsMap loadedCells,
            in InteriorTransitionState transition)
        {
            if (transition.InteriorActive != 0)
                return location.IsInterior != 0 && location.InteriorCellHash == transition.ActiveInteriorCellHash;

            return location.IsInterior == 0 && loadedCells.Active.IsCreated && loadedCells.Active.Contains(location.ExteriorCell);
        }

        static void EnsureContainers(EntityManager entityManager, Entity entity)
        {
            var lookup = entityManager.GetComponentData<ActiveExplicitRefLookup>(entity);
            if (!lookup.ByContentKey.IsCreated)
                lookup.ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent);
            if (!lookup.AllByContentKey.IsCreated)
                lookup.AllByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent);
            if (!lookup.ActiveEntriesByContentKey.IsCreated)
                lookup.ActiveEntriesByContentKey = new NativeParallelMultiHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent);
            if (!lookup.ActiveDynamicEntriesByEntity.IsCreated)
                lookup.ActiveDynamicEntriesByEntity = new NativeParallelHashMap<Entity, ActiveExplicitDynamicRefEntry>(InitialCapacity, Allocator.Persistent);
            if (!lookup.ActiveExteriorCells.IsCreated)
                lookup.ActiveExteriorCells = new NativeParallelHashMap<int2, byte>(InitialCapacity, Allocator.Persistent);
            entityManager.SetComponentData(entity, lookup);
        }

        static void EnsureEventComponents(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<ActiveExplicitRefLookupDirty>(entity))
            {
                entityManager.AddComponent<ActiveExplicitRefLookupDirty>(entity);
                entityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(entity, false);
            }
            if (!entityManager.HasComponent<ActiveExplicitRefLookupWorkPending>(entity))
            {
                entityManager.AddComponent<ActiveExplicitRefLookupWorkPending>(entity);
                entityManager.SetComponentEnabled<ActiveExplicitRefLookupWorkPending>(entity, false);
            }
            if (!entityManager.HasComponent<ActiveExplicitRefLookupFullRebuild>(entity))
            {
                entityManager.AddComponent<ActiveExplicitRefLookupFullRebuild>(entity);
                entityManager.SetComponentEnabled<ActiveExplicitRefLookupFullRebuild>(entity, false);
            }
            if (!entityManager.HasComponent<ActiveExplicitRefLookupBuildState>(entity))
                entityManager.AddComponentData(entity, new ActiveExplicitRefLookupBuildState());
            if (!entityManager.HasBuffer<ActiveExplicitSectionChange>(entity))
                entityManager.AddBuffer<ActiveExplicitSectionChange>(entity);
            if (!entityManager.HasBuffer<ActiveExplicitDynamicRefChange>(entity))
                entityManager.AddBuffer<ActiveExplicitDynamicRefChange>(entity);
            if (!entityManager.HasComponent<SessionTeardown>(entity))
            {
                entityManager.AddComponent<SessionTeardown>(entity);
                entityManager.SetComponentEnabled<SessionTeardown>(entity, false);
            }
        }

        static void MarkWorkPending(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<ActiveExplicitRefLookupWorkPending>(entity))
                throw new InvalidOperationException("[VVardenfell][WorldRefs] active explicit-ref lookup exists without its work-pending marker.");
            entityManager.SetComponentEnabled<ActiveExplicitRefLookupWorkPending>(entity, true);
        }

        static bool TryGetLookupEntity(EntityManager entityManager, out Entity entity)
        {
            EntityQuery query = GetLookupQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                entity = Entity.Null;
                return false;
            }

            entity = query.GetSingletonEntity();
            return true;
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

        static bool TryGetSingleton<T>(EntityManager entityManager, out T value)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = ComponentQueryCache<T>.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                value = default;
                return false;
            }

            value = query.GetSingleton<T>();
            return true;
        }

        static Entity Create(EntityManager entityManager)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ActiveExplicitRefLookup),
                typeof(ActiveExplicitRefLookupDirty),
                typeof(ActiveExplicitRefLookupWorkPending),
                typeof(ActiveExplicitRefLookupFullRebuild),
                typeof(ActiveExplicitRefLookupBuildState),
                typeof(ActiveExplicitSectionChange),
                typeof(ActiveExplicitDynamicRefChange),
                typeof(SessionTeardown));
            entityManager.SetComponentData(entity, new ActiveExplicitRefLookup
            {
                ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent),
                AllByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent),
                ActiveEntriesByContentKey = new NativeParallelMultiHashMap<int, ActiveExplicitRefTarget>(InitialCapacity, Allocator.Persistent),
                ActiveDynamicEntriesByEntity = new NativeParallelHashMap<Entity, ActiveExplicitDynamicRefEntry>(InitialCapacity, Allocator.Persistent),
                ActiveExteriorCells = new NativeParallelHashMap<int2, byte>(InitialCapacity, Allocator.Persistent),
            });
            entityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(entity, false);
            entityManager.SetComponentEnabled<ActiveExplicitRefLookupWorkPending>(entity, false);
            entityManager.SetComponentEnabled<ActiveExplicitRefLookupFullRebuild>(entity, false);
            entityManager.SetComponentEnabled<SessionTeardown>(entity, false);
            return entity;
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
    }
}
