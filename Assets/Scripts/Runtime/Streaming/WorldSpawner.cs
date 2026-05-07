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
using VVardenfell.Runtime.WorldRefs;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Spawns exterior cell terrain, static colliders, and refs for the streaming pipeline.
    /// Bootstrap can still drive the legacy full-world path, while normal runtime loading
    /// creates cells on demand.
    /// </summary>
    public static class WorldSpawner
    {
        const int StaticBatchSize = 256;
        const int RefGatherBatchSize = 512;
        const int RefSliceSize = 32768;

        static readonly ProfilerMarker k_SpawnAll = new("VV.WorldSpawner.SpawnAll");
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

            var cellEntries = WorldResources.CopyExteriorCellEntries();

            WorldResources.LoadedManaged.EnsureCapacity(WorldResources.ExteriorCellCount);

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

            var combinedChunkEntitiesByCell = new Dictionary<int2, Entity[]>();
            int combinedChunkEntityCount = 0;
            int combinedMultiTextureChunkCount = 0;
            progress?.BeginStage("Spawn combined render chunks", "Creating combined render chunks", System.Math.Max(1, cellEntries.Length));
            for (int i = 0; i < cellEntries.Length; i++)
            {
                var coord = cellEntries[i].Key;
                var chunkEntities = CombinedCellRenderSpawnUtility.SpawnChunks(em, coord, cellEntries[i].Value, active: false, out int multiTextureChunks);
                if (chunkEntities.Length > 0)
                {
                    combinedChunkEntitiesByCell[coord] = chunkEntities;
                    combinedChunkEntityCount += chunkEntities.Length;
                    combinedMultiTextureChunkCount += multiTextureChunks;
                }

                int completed = i + 1;
                if (completed == cellEntries.Length || (completed % RefGatherBatchSize) == 0)
                {
                    progress?.Report($"Creating combined render chunks {completed}/{cellEntries.Length}", completed, cellEntries.Length);
                    yield return null;
                }
            }
            progress?.CompleteStage("Combined render chunks ready");

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
                    cache.ContentBlob,
                    refArr,
                    coordArr,
                    spawnedRefEntities,
                    false,
                    default,
                    float3.zero,
                    ref logicalRefs,
                    progress,
                    out _);
                int combinedSuppressedLeafCount = 0;
                for (int i = 0; i < cellEntries.Length; i++)
                {
                    var coord = cellEntries[i].Key;
                    if (!combinedChunkEntitiesByCell.TryGetValue(coord, out var chunkEntities))
                        continue;

                    combinedSuppressedLeafCount += CombinedCellRenderSpawnUtility.AttachMembershipLinks(
                        em,
                        cellEntries[i].Value.CombinedRenderChunks,
                        chunkEntities,
                        ref logicalRefs);
                }
                if (combinedChunkEntityCount > 0 || combinedSuppressedLeafCount > 0)
                    UnityEngine.Debug.Log($"[VVardenfell][CombinedRender] spawnedChunks={combinedChunkEntityCount} multiTextureChunks={combinedMultiTextureChunkCount} suppressedLeaves={combinedSuppressedLeafCount}.");
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
                    .WithNone<CombinedCellRenderSuppressed>()
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

        public static void SpawnInteriorCell(World world, CellData cell, float3 worldOffset, Entity transitionEntity, ref LogicalRefLookup logicalRefs)
        {
            WorldInteriorSpawnUtility.SpawnInteriorCell(world, cell, worldOffset, transitionEntity, ref logicalRefs);
            ActiveExplicitRefLookupLifecycleUtility.MarkDirty(world.EntityManager);
        }

        public static bool TrySpawnInteriorCellByHash(
            World world,
            ulong interiorCellHash,
            float3 worldOffset,
            Entity transitionEntity,
            ref LogicalRefLookup logicalRefs,
            out FixedString128Bytes interiorCellId)
        {
            interiorCellId = default;
            if (interiorCellHash == 0UL)
                return false;
            if (!WorldResources.TryGetInteriorCell(interiorCellHash, out CellData cell) || cell == null)
                return false;

            interiorCellId = RuntimeFixedStringUtility.ToFixed128OrDefault(cell.CellId);
            if (interiorCellId.IsEmpty)
                interiorCellId = WorldResources.ResolveInteriorCellId(interiorCellHash);
            SpawnInteriorCell(world, cell, worldOffset, transitionEntity, ref logicalRefs);
            return true;
        }

        public static bool TryGetInteriorStaticCollider(
            ulong interiorCellHash,
            out BlobAssetReference<Collider> collider)
        {
            collider = default;
            if (interiorCellHash == 0UL)
                return false;
            if (!WorldResources.TryGetInteriorCell(interiorCellHash, out CellData cell) || cell == null)
                return false;
            if (!cell.StaticColliderBlob.IsCreated)
                return false;

            collider = cell.StaticColliderBlob;
            return true;
        }

        public static bool TrySpawnExteriorCellByCoord(
            World world,
            int2 coord,
            ref LoadedCellsMap loaded,
            ref LogicalRefLookup logicalRefs,
            bool active,
            bool gateTerrainByRadius)
        {
            return WorldResources.TryGetExteriorCell(coord, out CellData cellData)
                   && SpawnExteriorCell(
                       world,
                       coord,
                       cellData,
                       ref loaded,
                       ref logicalRefs,
                       active,
                       gateTerrainByRadius);
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

            Entity[] combinedChunkEntities = System.Array.Empty<Entity>();
            int combinedMultiTextureChunkCount = 0;
            if (!streamableAlreadySpawned)
                combinedChunkEntities = CombinedCellRenderSpawnUtility.SpawnChunks(em, coord, data, active, out combinedMultiTextureChunkCount);

            var refs = data.Refs;
            bool activeExplicitRefsDirty = false;
            if (!streamableAlreadySpawned && refs != null && refs.Length > 0)
            {
                var spawnedRefEntities = new Entity[refs.Length];
                WorldRefSpawnUtility.SpawnExteriorRefs(em, refs, coord, spawnedRefEntities);

                var refArray = new NativeArray<RefEntry>(refs.Length, Allocator.Temp);
                for (int i = 0; i < refs.Length; i++)
                    refArray[i] = refs[i];
                var coordArray = BuildCoordArray(coord, refs.Length);
                var contentBlob = WorldResources.Cache?.ContentBlob ?? default;
                if (!contentBlob.IsCreated)
                    throw new System.InvalidOperationException("[VVardenfell][ContentBlob] WorldSpawner requires runtime content blob for logical refs.");
                WorldRefSpawnUtility.BuildLogicalRefs(
                    em,
                    contentBlob,
                    refArray,
                    coordArray,
                    spawnedRefEntities,
                    false,
                    default,
                    float3.zero,
                    ref logicalRefs,
                    null,
                    out _);
                int combinedSuppressedLeafCount = CombinedCellRenderSpawnUtility.AttachMembershipLinks(
                    em,
                    data.CombinedRenderChunks,
                    combinedChunkEntities,
                    ref logicalRefs);
                if (combinedChunkEntities.Length > 0 || combinedSuppressedLeafCount > 0)
                    UnityEngine.Debug.Log($"[VVardenfell][CombinedRender] cell={coord.x},{coord.y} spawnedChunks={combinedChunkEntities.Length} multiTextureChunks={combinedMultiTextureChunkCount} suppressedLeaves={combinedSuppressedLeafCount}.");
                activeExplicitRefsDirty = true;
                coordArray.Dispose();
                refArray.Dispose();

                if (!active)
                    SetExteriorCellActiveState(em, coord, false, gateTerrainByRadius);
            }
            else if (!active && !streamableAlreadySpawned)
            {
                SetExteriorCellActiveState(em, coord, false, gateTerrainByRadius);
            }

            if (loaded.Map.IsCreated && terrainEntity != Entity.Null)
                loaded.Map[coord] = terrainEntity;
            if (loaded.Streamed.IsCreated)
                loaded.Streamed.Add(coord);
            if (active && loaded.Active.IsCreated)
            {
                if (loaded.Active.Add(coord))
                {
                    loaded.ActiveRevision++;
                    activeExplicitRefsDirty = true;
                }
                WorldExteriorPhysicsUtility.SetCellPhysicsActive(em, coord, true);
            }

            if (activeExplicitRefsDirty)
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(em);

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
