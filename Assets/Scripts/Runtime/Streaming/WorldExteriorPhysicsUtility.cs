using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using UnityEngine;
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

            if (Debug.isDebugBuild)
                LogActivePhysicsSummary(em, "hide-exterior");
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

            if (Debug.isDebugBuild)
                LogActivePhysicsSummary(em, "sync-exterior");
        }

        internal static void LogActivePhysicsSummary(EntityManager em, string contextLabel)
        {
            var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RuntimeColliderSource, PhysicsCollider>();
            var query = em.CreateEntityQuery(queryBuilder);
            int activeTerrain = 0;
            int activeStatic = 0;
            int activeRefs = 0;
            int activeProxies = 0;
            using (var sources = query.ToComponentDataArray<RuntimeColliderSource>(Allocator.Temp))
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    switch (sources[i].Kind)
                    {
                        case RuntimeColliderKind.TerrainCell:
                            activeTerrain++;
                            break;
                        case RuntimeColliderKind.StaticCell:
                            activeStatic++;
                            break;
                        case RuntimeColliderKind.PlacedRef:
                        case RuntimeColliderKind.RuntimeSpawn:
                            activeRefs++;
                            break;
                        case RuntimeColliderKind.ActivationProxy:
                            activeProxies++;
                            break;
                    }
                }
            }
            query.Dispose();
            queryBuilder.Dispose();

        }
    }
}
