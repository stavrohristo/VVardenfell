using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldExteriorPhysicsUtility
    {
        static readonly ProfilerMarker k_PhysicsSync = new("VV.Spawn.PhysicsSync");

        internal static void SetCellPhysicsActive(EntityManager em, int2 coord, bool active)
        {
            SetRegisteredCellPhysicsActive(em, coord, active);
        }

        internal static void DisableExteriorPhysics(EntityManager em)
        {
            using var _ = k_PhysicsSync.Auto();

            var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink, RuntimeColliderSource, PhysicsCollider>();
            var query = em.CreateEntityQuery(queryBuilder);
            em.RemoveComponent<PhysicsCollider>(query);
            query.Dispose();
            queryBuilder.Dispose();
        }

        internal static void SyncExteriorPhysics(EntityManager em, NativeHashSet<int2> desired)
        {
            using var _ = k_PhysicsSync.Auto();

            SyncRegisteredExteriorPhysics(em, desired);
            SyncLinkedExteriorPhysics(em, desired);
        }

        static void SetRegisteredCellPhysicsActive(EntityManager em, int2 coord, bool active)
        {
            if (!WorldResources.ExteriorCellEntities.TryGetValue(coord, out var entities))
                return;

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                Entity entity = entities[i];
                if (entity == Entity.Null || !em.Exists(entity))
                {
                    entities.RemoveAt(i);
                    continue;
                }

                if (!em.HasComponent<RuntimeColliderSource>(entity))
                    continue;

                if (active)
                    VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(em, entity);
                else
                    VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(em, entity);
            }
        }

        static void SyncRegisteredExteriorPhysics(EntityManager em, NativeHashSet<int2> desired)
        {
            foreach (var pair in WorldResources.ExteriorCellEntities)
            {
                bool shouldBeActive = desired.Contains(pair.Key);
                var entities = pair.Value;
                for (int i = entities.Count - 1; i >= 0; i--)
                {
                    Entity entity = entities[i];
                    if (entity == Entity.Null || !em.Exists(entity))
                    {
                        entities.RemoveAt(i);
                        continue;
                    }

                    if (!em.HasComponent<RuntimeColliderSource>(entity))
                        continue;

                    bool isActive = em.HasComponent<PhysicsCollider>(entity);
                    if (shouldBeActive && !isActive)
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(em, entity);
                    else if (!shouldBeActive && isActive)
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(em, entity);
                }
            }
        }

        static void SyncLinkedExteriorPhysics(EntityManager em, NativeHashSet<int2> desired)
        {
            var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink, RuntimeColliderSource>();
            var query = em.CreateEntityQuery(queryBuilder);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var links = query.ToComponentDataArray<CellLink>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entity == Entity.Null || !em.Exists(entity))
                    continue;

                bool disabled = em.HasComponent<PlacedRefRuntimeState>(entity)
                    && em.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0;
                bool shouldBeActive = !disabled && desired.Contains(links[i].Value);
                bool isActive = em.HasComponent<PhysicsCollider>(entity);
                if (shouldBeActive && !isActive)
                    VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(em, entity);
                else if (!shouldBeActive && isActive)
                    VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(em, entity);
            }

            query.Dispose();
            queryBuilder.Dispose();
        }
    }
}
