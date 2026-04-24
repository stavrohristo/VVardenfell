using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldExteriorVisibilityUtility
    {
        public static void SetExteriorCellActiveState(EntityManager em, int2 coord, bool active, bool gateTerrainByRadius)
        {
            var targetQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>();
            if (!gateTerrainByRadius)
                targetQueryBuilder = targetQueryBuilder.WithNone<CellCoord>();

            var targetQuery = em.CreateEntityQuery(targetQueryBuilder);
            using (var entities = targetQuery.ToEntityArray(Allocator.Temp))
            using (var links = targetQuery.ToComponentDataArray<CellLink>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (links[i].Value.Equals(coord))
                        em.SetComponentEnabled<MaterialMeshInfo>(entities[i], active);
                }
            }
            targetQuery.Dispose();
            targetQueryBuilder.Dispose();

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
            loaded.Active.Clear();
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
                    if (loaded.Map.ContainsKey(coord))
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

            var refsQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>();
            if (!config.GateTerrainByRadius)
                refsQueryBuilder = refsQueryBuilder.WithNone<CellCoord>();

            var refsQuery = em.CreateEntityQuery(refsQueryBuilder);
            using var refEntities = refsQuery.ToEntityArray(Allocator.Temp);
            using var refLinks = refsQuery.ToComponentDataArray<CellLink>(Allocator.Temp);
            for (int i = 0; i < refEntities.Length; i++)
                em.SetComponentEnabled<MaterialMeshInfo>(refEntities[i], desired.Contains(refLinks[i].Value));

            refsQuery.Dispose();
            refsQueryBuilder.Dispose();

            if (!config.GateTerrainByRadius)
            {
                var terrainQuery = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CellCoord>()
                    .WithPresent<MaterialMeshInfo>();
                var builtTerrainQuery = em.CreateEntityQuery(terrainQuery);
                em.SetComponentEnabled<MaterialMeshInfo>(builtTerrainQuery, true);
                builtTerrainQuery.Dispose();
                terrainQuery.Dispose();
            }

            WorldExteriorPhysicsUtility.SyncExteriorPhysics(em, desired);
            loaded.Active.Clear();
            var desiredEnumerator = desired.GetEnumerator();
            while (desiredEnumerator.MoveNext())
                loaded.Active.Add(desiredEnumerator.Current);
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
    }
}
