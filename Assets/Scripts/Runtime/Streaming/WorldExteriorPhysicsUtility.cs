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
            var physicsQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink, RuntimeColliderSource>();
            var physicsQuery = em.CreateEntityQuery(physicsQueryBuilder);
            using (var entities = physicsQuery.ToEntityArray(Allocator.Temp))
            using (var links = physicsQuery.ToComponentDataArray<CellLink>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!links[i].Value.Equals(coord))
                        continue;

                    if (active)
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(em, entities[i]);
                    else
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(em, entities[i]);
                }
            }
            physicsQuery.Dispose();
            physicsQueryBuilder.Dispose();
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

            var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink, RuntimeColliderSource>();
            var query = em.CreateEntityQuery(queryBuilder);
            using (var entities = query.ToEntityArray(Allocator.Temp))
            using (var links = query.ToComponentDataArray<CellLink>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    bool shouldBeActive = desired.Contains(links[i].Value);
                    bool isActive = em.HasComponent<PhysicsCollider>(entities[i]);
                    if (shouldBeActive && !isActive)
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(em, entities[i]);
                    else if (!shouldBeActive && isActive)
                        VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(em, entities[i]);
                }
            }
            query.Dispose();
            queryBuilder.Dispose();
        }
    }
}
