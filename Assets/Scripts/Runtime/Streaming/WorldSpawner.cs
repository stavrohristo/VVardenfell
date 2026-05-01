using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Eagerly spawns every cell's entities at bootstrap. Work is split across multiple
    /// frames so bootstrap still ends with the entire world resident, but the main thread
    /// no longer pays one monolithic startup stall.
    /// </summary>
    public static class WorldSpawner
    {
        const int TerrainBatchSize = 64;
        const int StaticBatchSize = 256;
        const int RefGatherBatchSize = 512;
        const int RefSliceSize = 32768;

        static readonly ProfilerMarker k_SpawnAll = new("VV.WorldSpawner.SpawnAll");
        static readonly ProfilerMarker k_TerrainMesh = new("VV.Spawn.Terrain.MeshBuild");
        static readonly ProfilerMarker k_TerrainMat = new("VV.Spawn.Terrain.MaterialBuild");
        static readonly ProfilerMarker k_TerrainEntity = new("VV.Spawn.Terrain.EntityCreate");
        static readonly ProfilerMarker k_StatCellEntities = new("VV.Spawn.StatCellEntities");
        static readonly ProfilerMarker k_RefGather = new("VV.Spawn.Refs.Gather");
        static readonly ProfilerMarker k_BulkDisable = new("VV.Spawn.BulkDisableMMI");

        public static void SpawnAll(World world, CacheLoader cache, ref LoadedCellsMap loaded, ref LogicalRefLookup logicalRefs, bool gateTerrainByRadius)
        {
            using var _ = k_SpawnAll.Auto();
            RuntimeCoroutinePump.RunToCompletion(SpawnAllIncremental(world, cache, loaded, logicalRefs, gateTerrainByRadius, null));
        }

        public static IEnumerator SpawnAllIncremental(World world, CacheLoader cache, LoadedCellsMap loaded, LogicalRefLookup logicalRefs, bool gateTerrainByRadius, RuntimeLoadProgress progress)
        {
            var em = world.EntityManager;

            var cellEntries = new KeyValuePair<int2, CellData>[WorldResources.Cells.Count];
            int cellIndex = 0;
            foreach (var kv in WorldResources.Cells)
                cellEntries[cellIndex++] = kv;

            WorldResources.LoadedManaged.EnsureCapacity(WorldResources.Cells.Count);

            progress?.BeginStage("Spawn terrain", "Creating terrain entities", cellEntries.Length);
            int terrainBuilt = 0;
            for (int i = 0; i < cellEntries.Length; i++)
            {
                var coord = cellEntries[i].Key;
                var data = cellEntries[i].Value;
                Entity terrainEntity = Entity.Null;

                if (data.HasTerrain)
                {
                    k_TerrainMesh.Begin();
                    k_TerrainMat.Begin();
                    k_TerrainEntity.Begin();
                    try
                    {
                        var terrainResult = WorldTerrainStaticSpawnUtility.SpawnTerrainCell(em, coord, data, active: false);
                        terrainEntity = terrainResult.Entity;
                        terrainBuilt += terrainResult.BuiltTerrain;
                    }
                    finally
                    {
                        k_TerrainEntity.End();
                        k_TerrainMat.End();
                        k_TerrainMesh.End();
                    }
                }

                loaded.Map[coord] = terrainEntity;

                int completed = i + 1;
                if (completed == cellEntries.Length || (completed % TerrainBatchSize) == 0)
                {
                    progress?.Report($"Creating terrain entities {completed}/{cellEntries.Length}", completed, cellEntries.Length);
                    yield return null;
                }
            }
            progress?.CompleteStage("Terrain entities ready");

            var staticColliderEntries = new KeyValuePair<int2, BlobAssetReference<Collider>>[WorldResources.StaticCellColliders.Count];
            int staticIndex = 0;
            foreach (var kv in WorldResources.StaticCellColliders)
                staticColliderEntries[staticIndex++] = kv;

            progress?.BeginStage("Spawn static colliders", "Creating static collider entities", staticColliderEntries.Length);
            int staticCellsSpawned = 0;
            for (int i = 0; i < staticColliderEntries.Length; i++)
            {
                var coord = staticColliderEntries[i].Key;
                var blob = staticColliderEntries[i].Value;
                if (blob.IsCreated)
                {
                    k_StatCellEntities.Begin();
                    try
                    {
                        if (WorldTerrainStaticSpawnUtility.SpawnStaticCellCollider(em, coord, blob, active: false) != Entity.Null)
                            staticCellsSpawned++;
                    }
                    finally
                    {
                        k_StatCellEntities.End();
                    }
                }

                int completed = i + 1;
                if (completed == staticColliderEntries.Length || (completed % StaticBatchSize) == 0)
                {
                    progress?.Report($"Creating static collider entities {completed}/{staticColliderEntries.Length}", completed, staticColliderEntries.Length);
                    yield return null;
                }
            }
            progress?.CompleteStage("Static colliders ready");

            int totalRefs = 0;
            for (int i = 0; i < cellEntries.Length; i++)
                totalRefs += cellEntries[i].Value.Refs?.Length ?? 0;

            progress?.BeginStage("Spawn refs", "Gathering refs", System.Math.Max(1, cellEntries.Length));
            var refArr = default(NativeArray<RefEntry>);
            var coordArr = default(NativeArray<int2>);
            Entity[] spawnedRefEntities = null;
            if (totalRefs > 0)
            {
                refArr = new NativeArray<RefEntry>(totalRefs, Allocator.Persistent);
                coordArr = new NativeArray<int2>(totalRefs, Allocator.Persistent);
                spawnedRefEntities = new Entity[totalRefs];

                if (logicalRefs.Map.IsCreated && logicalRefs.Map.Capacity < totalRefs)
                    logicalRefs.Map.Capacity = totalRefs;

                int cursor = 0;
                
                for (int i = 0; i < cellEntries.Length; i++)
                {
                    k_RefGather.Begin();
                    try
                    {
                        var coord = cellEntries[i].Key;
                        var cellData = cellEntries[i].Value;
                        var refs = cellData.Refs;
                        if (refs != null)
                        {
                            for (int r = 0; r < refs.Length; r++)
                            {
                                refArr[cursor] = refs[r];
                                coordArr[cursor] = coord;
                                cursor++;
                            }
                        }
                    }
                    finally
                    {
                        k_RefGather.End();
                    }

                    int completed = i + 1;
                    if (completed == cellEntries.Length || (completed % RefGatherBatchSize) == 0)
                    {
                        progress?.Report($"Gathering refs {completed}/{cellEntries.Length}", completed, cellEntries.Length);
                        yield return null;
                    }
                }

                progress?.CompleteStage("Refs gathered");
                progress?.BeginStage("Spawn refs", "Instantiating and applying refs", totalRefs);
                for (int i = 0; i < totalRefs; i++)
                {
                    spawnedRefEntities[i] = WorldRefSpawnUtility.SpawnExteriorRef(em, refArr[i], coordArr[i]);
                    int completed = i + 1;
                    if (completed == totalRefs || (completed % RefGatherBatchSize) == 0)
                    {
                        progress?.Report($"Instantiating and applying refs {completed}/{totalRefs}", completed, totalRefs);
                        yield return null;
                    }
                }

                progress?.CompleteStage("Refs ready");
                yield return null;

                progress?.BeginStage("Spawn logical refs", "Creating logical placed refs", totalRefs);
                WorldRefSpawnUtility.BuildLogicalRefs(
                    em,
                    cache.ContentDatabase,
                    refArr,
                    coordArr,
                    spawnedRefEntities,
                    false,
                    default,
                    float3.zero,
                    ref logicalRefs,
                    progress,
                    out _);
                progress?.CompleteStage("Logical refs ready");
            }
            else
            {
                progress?.Report("No refs to spawn", 1, 1);
                progress?.CompleteStage();
                yield return null;
            }

            if (refArr.IsCreated)
                refArr.Dispose();
            if (coordArr.IsCreated)
                coordArr.Dispose();

            if (loaded.Streamed.IsCreated)
            {
                for (int i = 0; i < cellEntries.Length; i++)
                    loaded.Streamed.Add(cellEntries[i].Key);
            }

            progress?.BeginStage("Finalize render state", "Applying initial visibility gate", 1);
            k_BulkDisable.Begin();
            try
            {
                var disableQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CellLink, MaterialMeshInfo>()
                    .WithNone<CellCoord>();

                var disableQuery = em.CreateEntityQuery(disableQueryBuilder);
                em.SetComponentEnabled<MaterialMeshInfo>(disableQuery, false);
                disableQuery.Dispose();
                disableQueryBuilder.Dispose();
            }
            finally
            {
                k_BulkDisable.End();
            }
            progress?.Report("Initial visibility gate applied", 1, 1);
            progress?.CompleteStage();
            yield return null;
        }

        public static IEnumerator SpawnAllTerrainIncremental(World world, LoadedCellsMap loaded, RuntimeLoadProgress progress)
        {
            var em = world.EntityManager;
            var cellEntries = new KeyValuePair<int2, CellData>[WorldResources.Cells.Count];
            int cellIndex = 0;
            foreach (var kv in WorldResources.Cells)
                cellEntries[cellIndex++] = kv;

            WorldResources.LoadedManaged.EnsureCapacity(WorldResources.Cells.Count);

            progress?.BeginStage("Spawn terrain", "Creating all terrain entities", cellEntries.Length);
            for (int i = 0; i < cellEntries.Length; i++)
            {
                var coord = cellEntries[i].Key;
                var data = cellEntries[i].Value;
                Entity terrainEntity = Entity.Null;

                if (data.HasTerrain)
                {
                    var terrainResult = WorldTerrainStaticSpawnUtility.SpawnTerrainCell(em, coord, data, active: false);
                    terrainEntity = terrainResult.Entity;
                }

                loaded.Map[coord] = terrainEntity;

                int completed = i + 1;
                if (completed == cellEntries.Length || (completed % TerrainBatchSize) == 0)
                {
                    progress?.Report($"Creating terrain entities {completed}/{cellEntries.Length}", completed, cellEntries.Length);
                    yield return null;
                }
            }

            progress?.CompleteStage("Terrain entities ready");
        }

        public static void SpawnInteriorCell(World world, CellData cell, float3 worldOffset, Entity transitionEntity, ref LogicalRefLookup logicalRefs)
        {
            WorldInteriorSpawnUtility.SpawnInteriorCell(world, cell, worldOffset, transitionEntity, ref logicalRefs);
        }

        public static bool SpawnExteriorCell(
            World world,
            int2 coord,
            CellData data,
            ref LoadedCellsMap loaded,
            ref LogicalRefLookup logicalRefs,
            bool active,
            bool gateTerrainByRadius)
        {
            if (world == null || data == null)
                return false;

            var em = world.EntityManager;
            Entity terrainEntity = Entity.Null;

            bool terrainAlreadyResident = loaded.Map.IsCreated && loaded.Map.TryGetValue(coord, out terrainEntity);
            if (data.HasTerrain && !terrainAlreadyResident)
            {
                var terrainResult = WorldTerrainStaticSpawnUtility.SpawnTerrainCell(em, coord, data, active: false);
                terrainEntity = terrainResult.Entity;
            }

            bool streamableAlreadySpawned = loaded.Streamed.IsCreated && loaded.Streamed.Contains(coord);
            if (!streamableAlreadySpawned && WorldResources.TryGetStaticCellCollider(coord, out var staticBlob))
                WorldTerrainStaticSpawnUtility.SpawnStaticCellCollider(em, coord, staticBlob, active);

            var refs = data.Refs;
            if (!streamableAlreadySpawned && refs != null && refs.Length > 0)
            {
                var spawnedRefEntities = new Entity[refs.Length];
                WorldRefSpawnUtility.SpawnExteriorRefs(em, refs, coord, spawnedRefEntities);

                var refArray = new NativeArray<RefEntry>(refs.Length, Allocator.Temp);
                for (int i = 0; i < refs.Length; i++)
                    refArray[i] = refs[i];
                var coordArray = BuildCoordArray(coord, refs.Length);
                WorldRefSpawnUtility.BuildLogicalRefs(
                    em,
                    WorldResources.Cache?.ContentDatabase ?? RuntimeContentDatabase.Active,
                    refArray,
                    coordArray,
                    spawnedRefEntities,
                    false,
                    default,
                    float3.zero,
                    ref logicalRefs,
                    null,
                    out _);
                coordArray.Dispose();
                refArray.Dispose();

                if (!active)
                    SetExteriorCellActiveState(em, coord, false, gateTerrainByRadius);
            }
            else if (!active && !streamableAlreadySpawned)
            {
                SetExteriorCellActiveState(em, coord, false, gateTerrainByRadius);
            }

            if (loaded.Map.IsCreated)
                loaded.Map[coord] = terrainEntity;
            if (loaded.Streamed.IsCreated)
                loaded.Streamed.Add(coord);
            if (active && loaded.Active.IsCreated)
            {
                loaded.Active.Add(coord);
                WorldExteriorPhysicsUtility.SetCellPhysicsActive(em, coord, true);
            }

            return true;
        }

        private static NativeArray<int2> BuildCoordArray(int2 coord, int length)
        {
            var coords = new NativeArray<int2>(length, Allocator.Temp);
            for (int i = 0; i < length; i++)
                coords[i] = coord;
            return coords;
        }

        public static void SetExteriorCellActiveState(EntityManager em, int2 coord, bool active, bool gateTerrainByRadius)
        {
            WorldExteriorVisibilityUtility.SetExteriorCellActiveState(em, coord, active, gateTerrainByRadius);
        }

        public static void HideExteriorVisibility(World world, ref LoadedCellsMap loaded)
        {
            WorldExteriorVisibilityUtility.HideExteriorVisibility(world, ref loaded);
        }

        public static void SyncExteriorVisibility(
            World world,
            in StreamingConfig config,
            in AvailableCells available,
            ref LoadedCellsMap loaded)
        {
            WorldExteriorVisibilityUtility.SyncExteriorVisibility(world, config, available, ref loaded);
        }

    }
}
