using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldExteriorVisibilityUtility
    {
        public static void SetExteriorCellActiveState(EntityManager em, int2 coord, bool active, bool gateTerrainByRadius)
        {
            SetRegisteredCellRenderState(em, coord, active, gateTerrainByRadius);
            WorldExteriorPhysicsUtility.SetCellPhysicsActive(em, coord, active);
        }

        public static void HideExteriorVisibility(World world, ref LoadedCellsMap loaded)
        {
            var em = world.EntityManager;
            var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>();
            var query = em.CreateEntityQuery(queryBuilder);
            em.SetComponentEnabled<MaterialMeshInfo>(query, false);
            query.Dispose();
            queryBuilder.Dispose();
            WorldExteriorPhysicsUtility.DisableExteriorPhysics(em);
            if (loaded.Active.Count > 0)
            {
                loaded.Active.Clear();
                loaded.ActiveRevision++;
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(em);
            }
        }

        public static void SyncExteriorVisibility(
            World world,
            in StreamingConfig config,
            in AvailableCells available,
            ref LoadedCellsMap loaded)
        {
            var em = world.EntityManager;
            int r = config.ViewRadius;
            var desired = new NativeHashSet<int2>((2 * r + 1) * (2 * r + 1), Allocator.Temp);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    var coord = new int2(config.CameraCell.x + dx, config.CameraCell.y + dy);
                    if (available.Set.Contains(coord))
                        desired.Add(coord);
                }
            }

            Entity streamingEntity = TryGetStreamingEntity(em);
            bool spawnedMissingCells = false;
            if (streamingEntity != Entity.Null && em.HasComponent<LogicalRefLookup>(streamingEntity))
            {
                var logicalRefs = em.GetComponentData<LogicalRefLookup>(streamingEntity);
                var desiredEnumeratorForSpawn = desired.GetEnumerator();
                while (desiredEnumeratorForSpawn.MoveNext())
                {
                    var coord = desiredEnumeratorForSpawn.Current;
                    if (loaded.Streamed.Contains(coord))
                        continue;

                    if (!WorldResources.Cells.TryGetValue(coord, out var cellData) || cellData == null)
                        continue;

                    WorldSpawner.SpawnExteriorCell(
                        world,
                        coord,
                        cellData,
                        ref loaded,
                        ref logicalRefs,
                        active: true,
                        gateTerrainByRadius: config.GateTerrainByRadius);
                    spawnedMissingCells = true;
                }

                if (spawnedMissingCells)
                    em.SetComponentData(streamingEntity, logicalRefs);
            }

            SyncRegisteredRenderState(em, desired, config.GateTerrainByRadius);

            WorldExteriorPhysicsUtility.SyncExteriorPhysics(em, desired);
            bool changed = loaded.Active.Count != desired.Count;
            if (!changed)
            {
                var activeEnumerator = loaded.Active.GetEnumerator();
                while (activeEnumerator.MoveNext())
                {
                    if (!desired.Contains(activeEnumerator.Current))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            loaded.Active.Clear();
            var desiredEnumerator = desired.GetEnumerator();
            while (desiredEnumerator.MoveNext())
                loaded.Active.Add(desiredEnumerator.Current);
            if (changed)
            {
                loaded.ActiveRevision++;
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(em);
            }
            else if (spawnedMissingCells)
            {
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(em);
            }
            desired.Dispose();
        }

        static Entity TryGetStreamingEntity(EntityManager em)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<StreamingConfig>(),
                ComponentType.ReadOnly<LoadedCellsMap>(),
                ComponentType.ReadOnly<LogicalRefLookup>());
            return query.CalculateEntityCount() > 0 ? query.GetSingletonEntity() : Entity.Null;
        }

        static void SetRegisteredCellRenderState(EntityManager em, int2 coord, bool active, bool gateTerrainByRadius)
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

                if (!em.HasComponent<MaterialMeshInfo>(entity))
                    continue;
                if (em.HasComponent<CellCoord>(entity))
                    continue;

                em.SetComponentEnabled<MaterialMeshInfo>(entity, active);
            }
        }

        static void SyncRegisteredRenderState(EntityManager em, NativeHashSet<int2> desired, bool gateTerrainByRadius)
        {
            foreach (var pair in WorldResources.ExteriorCellEntities)
            {
                bool active = desired.Contains(pair.Key);
                var entities = pair.Value;
                for (int i = entities.Count - 1; i >= 0; i--)
                {
                    Entity entity = entities[i];
                    if (entity == Entity.Null || !em.Exists(entity))
                    {
                        entities.RemoveAt(i);
                        continue;
                    }

                    if (!em.HasComponent<MaterialMeshInfo>(entity))
                        continue;

                    if (em.HasComponent<CellCoord>(entity))
                        continue;

                    em.SetComponentEnabled<MaterialMeshInfo>(entity, active);
                }
            }
        }
    }
}
