using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        static readonly ProfilerMarker k_Terrain = new("VV.Spawn.TerrainCells");
        static readonly ProfilerMarker k_TerrainMesh = new("VV.Spawn.Terrain.MeshBuild");
        static readonly ProfilerMarker k_TerrainMat = new("VV.Spawn.Terrain.MaterialBuild");
        static readonly ProfilerMarker k_TerrainEntity = new("VV.Spawn.Terrain.EntityCreate");
        static readonly ProfilerMarker k_StatCellEntities = new("VV.Spawn.StatCellEntities");
        static readonly ProfilerMarker k_RefGather = new("VV.Spawn.Refs.Gather");
        static readonly ProfilerMarker k_RefSort = new("VV.Spawn.Refs.Sort");
        static readonly ProfilerMarker k_RefInstantiate = new("VV.Spawn.Refs.Instantiate");
        static readonly ProfilerMarker k_RefBuildJob = new("VV.Spawn.Refs.BuildDataJob");
        static readonly ProfilerMarker k_RefApplyJob = new("VV.Spawn.Refs.ApplyJob");
        static readonly ProfilerMarker k_RefEcbPlayback = new("VV.Spawn.Refs.EcbPlayback");
        static readonly ProfilerMarker k_BulkDisable = new("VV.Spawn.BulkDisableMMI");

        public static void SpawnAll(World world, CacheLoader cache, ref LoadedCellsMap loaded, bool gateTerrainByRadius)
        {
            using var _ = k_SpawnAll.Auto();
            RuntimeCoroutinePump.RunToCompletion(SpawnAllIncremental(world, cache, loaded, gateTerrainByRadius, null));
        }

        public static IEnumerator SpawnAllIncremental(World world, CacheLoader cache, LoadedCellsMap loaded, bool gateTerrainByRadius, RuntimeLoadProgress progress)
        {
            var em = world.EntityManager;
            var sw = Stopwatch.StartNew();

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
                    var managed = new WorldResources.PerCellManaged();

                    k_TerrainMesh.Begin();
                    try
                    {
                        managed.TerrainMesh = BuildTerrainMesh(data);
                    }
                    finally
                    {
                        k_TerrainMesh.End();
                    }

                    k_TerrainMat.Begin();
                    try
                    {
                        managed.TerrainMat = BuildTerrainMaterial(data);
                        managed.SplatMap = (managed.TerrainMat != null && managed.TerrainMat != WorldResources.TerrainFallbackMat)
                            ? managed.TerrainMat.GetTexture("_Splat") as Texture2D
                            : null;
                        managed.TerrainRma = new RenderMeshArray(
                            new Material[] { managed.TerrainMat },
                            new Mesh[] { managed.TerrainMesh });
                    }
                    finally
                    {
                        k_TerrainMat.End();
                    }

                    k_TerrainEntity.Begin();
                    try
                    {
                        terrainEntity = em.CreateEntity();
                        em.SetName(terrainEntity, $"Terrain({coord.x},{coord.y})");
                        RenderMeshUtility.AddComponents(
                            terrainEntity, em, WorldResources.Desc, managed.TerrainRma,
                            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

                        float ox = coord.x * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                        float oz = coord.y * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                        em.AddComponentData(terrainEntity, LocalTransform.FromPositionRotationScale(
                            new float3(ox, 0, oz), quaternion.identity, 1f));

                        float cellHalf = LandRecordSize.CellUnitsMw * 0.5f * WorldScale.MwUnitsToMeters;
                        em.SetComponentData(terrainEntity, new RenderBounds
                        {
                            Value = new AABB
                            {
                                Center = new float3(cellHalf, 0f, cellHalf),
                                Extents = new float3(cellHalf, 1000f, cellHalf),
                            }
                        });

                        em.AddComponentData(terrainEntity, new CellCoord { Value = coord });
                        em.AddComponentData(terrainEntity, new CellLink { Value = coord });
                        em.AddComponent<Unity.Transforms.Static>(terrainEntity);
                        em.AddSharedComponent(terrainEntity, new PhysicsWorldIndex { Value = 0 });
                        if (WorldResources.TerrainColliders.TryGetValue(coord, out var terrBlob) && terrBlob.IsCreated)
                            em.AddComponentData(terrainEntity, new PhysicsCollider { Value = terrBlob });
                    }
                    finally
                    {
                        k_TerrainEntity.End();
                    }

                    WorldResources.LoadedManaged[coord] = managed;
                    terrainBuilt++;
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
            long terrainMs = sw.ElapsedMilliseconds;

            var staticColliderEntries = new KeyValuePair<int2, BlobAssetReference<Collider>>[WorldResources.StaticCellColliders.Count];
            int staticIndex = 0;
            foreach (var kv in WorldResources.StaticCellColliders)
                staticColliderEntries[staticIndex++] = kv;

            progress?.BeginStage("Spawn static colliders", "Creating static collider entities", staticColliderEntries.Length);
            int staticCellsSpawned = 0;
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            for (int i = 0; i < staticColliderEntries.Length; i++)
            {
                var coord = staticColliderEntries[i].Key;
                var blob = staticColliderEntries[i].Value;
                if (blob.IsCreated)
                {
                    k_StatCellEntities.Begin();
                    try
                    {
                        var e = em.CreateEntity();
                        em.SetName(e, $"CellStatic({coord.x},{coord.y})");
                        em.AddComponentData(e, LocalTransform.FromPositionRotationScale(
                            new float3(coord.x * cellMeters, 0f, coord.y * cellMeters),
                            quaternion.identity, 1f));
                        em.AddComponentData(e, new LocalToWorld
                        {
                            Value = float4x4.Translate(new float3(coord.x * cellMeters, 0f, coord.y * cellMeters))
                        });
                        em.AddComponentData(e, new PhysicsCollider { Value = blob });
                        em.AddComponent<Unity.Transforms.Static>(e);
                        em.AddSharedComponent(e, new PhysicsWorldIndex { Value = 0 });
                        em.AddComponentData(e, new CellLink { Value = coord });
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
            long staticMs = sw.ElapsedMilliseconds;

            int totalRefs = 0;
            for (int i = 0; i < cellEntries.Length; i++)
                totalRefs += cellEntries[i].Value.Refs?.Length ?? 0;

            progress?.BeginStage("Spawn refs", "Gathering refs", System.Math.Max(1, cellEntries.Length));
            var refArr = default(NativeArray<RefEntry>);
            var coordArr = default(NativeArray<int2>);
            if (totalRefs > 0 && WorldResources.RefPrefabs != null)
            {
                refArr = new NativeArray<RefEntry>(totalRefs, Allocator.TempJob);
                coordArr = new NativeArray<int2>(totalRefs, Allocator.TempJob);

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

                progress?.BeginStage("Sort refs", "Sorting gathered refs", totalRefs);
                k_RefSort.Begin();
                try
                {
                    var paired = new NativeArray<PairedRef>(totalRefs, Allocator.TempJob);
                    for (int i = 0; i < totalRefs; i++)
                        paired[i] = new PairedRef { Ref = refArr[i], Coord = coordArr[i] };

                    paired.Sort(new PairedBucketComparer
                    {
                        TexBucketInfo = WorldResources.TexBucketInfo,
                        FallbackBucket = WorldResources.FallbackBucketSlice.x,
                    });

                    for (int i = 0; i < totalRefs; i++)
                    {
                        refArr[i] = paired[i].Ref;
                        coordArr[i] = paired[i].Coord;
                    }

                    paired.Dispose();
                }
                finally
                {
                    k_RefSort.End();
                }

                progress?.Report("Refs sorted", totalRefs, totalRefs);
                progress?.CompleteStage();
                yield return null;

                progress?.BeginStage("Spawn refs", "Instantiating and applying refs", totalRefs);
                var blobTable = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Collider>>();
                var blobs = new NativeArray<BlobAssetReference<Collider>>(
                    blobTable.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < blobTable.Length; i++)
                    blobs[i] = blobTable[i];

                try
                {
                    for (int start = 0; start < totalRefs; start += RefSliceSize)
                    {
                        int chunkLen = System.Math.Min(RefSliceSize, totalRefs - start);
                        var chunkRefs = new NativeArray<RefEntry>(chunkLen, Allocator.TempJob);
                        var chunkCoords = new NativeArray<int2>(chunkLen, Allocator.TempJob);
                        var entities = new NativeArray<Entity>(chunkLen, Allocator.TempJob);
                        var spawnData = new NativeArray<RefSpawnData>(chunkLen, Allocator.TempJob);

                        try
                        {
                            for (int i = 0; i < chunkLen; i++)
                            {
                                chunkRefs[i] = refArr[start + i];
                                chunkCoords[i] = coordArr[start + i];
                            }

                            k_RefInstantiate.Begin();
                            try
                            {
                                int fallbackBucket = WorldResources.FallbackBucketSlice.x;
                                var texInfo = WorldResources.TexBucketInfo;
                                var prefabs = WorldResources.RefPrefabs;
                                int runStart = 0;
                                int runBucket = GetBucket(chunkRefs[0], texInfo, fallbackBucket);
                                for (int i = 1; i <= chunkLen; i++)
                                {
                                    int bucket = i < chunkLen ? GetBucket(chunkRefs[i], texInfo, fallbackBucket) : -1;
                                    if (bucket != runBucket)
                                    {
                                        var slice = entities.GetSubArray(runStart, i - runStart);
                                        em.Instantiate(prefabs[runBucket], slice);
                                        runStart = i;
                                        runBucket = bucket;
                                    }
                                }
                            }
                            finally
                            {
                                k_RefInstantiate.End();
                            }

                            k_RefBuildJob.Begin();
                            try
                            {
                                new BuildRefSpawnDataJob
                                {
                                    Refs = chunkRefs,
                                    Coords = chunkCoords,
                                    TexBucketInfo = WorldResources.TexBucketInfo,
                                    FallbackBucketSlice = WorldResources.FallbackBucketSlice,
                                    MeshBounds = WorldResources.MeshBounds,
                                    MeshCount = WorldResources.MeshBounds.Length,
                                    ColliderBlobs = blobs,
                                    Sentinel = WorldResources.SentinelCollider,
                                    Output = spawnData,
                                }.Schedule(chunkLen, 128).Complete();
                            }
                            finally
                            {
                                k_RefBuildJob.End();
                            }

                            var ecb = new EntityCommandBuffer(Allocator.TempJob);
                            try
                            {
                                k_RefApplyJob.Begin();
                                try
                                {
                                    new ApplyRefSpawnDataJob
                                    {
                                        Entities = entities,
                                        SpawnData = spawnData,
                                        CommandBuf = ecb.AsParallelWriter(),
                                    }.Schedule(chunkLen, 128).Complete();
                                }
                                finally
                                {
                                    k_RefApplyJob.End();
                                }

                                k_RefEcbPlayback.Begin();
                                try
                                {
                                    ecb.Playback(em);
                                    ApplyRefGameplayMetadata(em, chunkRefs, chunkCoords, entities);
                                }
                                finally
                                {
                                    k_RefEcbPlayback.End();
                                }
                            }
                            finally
                            {
                                ecb.Dispose();
                            }
                        }
                        finally
                        {
                            if (chunkRefs.IsCreated)
                                chunkRefs.Dispose();
                            if (chunkCoords.IsCreated)
                                chunkCoords.Dispose();
                            if (entities.IsCreated)
                                entities.Dispose();
                            if (spawnData.IsCreated)
                                spawnData.Dispose();
                        }

                        int completed = start + chunkLen;
                        progress?.Report($"Instantiating and applying refs {completed}/{totalRefs}", completed, totalRefs);
                        yield return null;
                    }
                }
                finally
                {
                    blobs.Dispose();
                }

                progress?.CompleteStage("Refs ready");
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

            progress?.BeginStage("Finalize render state", "Applying initial visibility gate", 1);
            k_BulkDisable.Begin();
            try
            {
                var disableQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CellLink, MaterialMeshInfo>();
                if (!gateTerrainByRadius)
                    disableQueryBuilder = disableQueryBuilder.WithNone<CellCoord>();

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

            sw.Stop();
            Debug.Log($"[VVardenfell] eager-spawn: {loaded.Map.Count} cells ({terrainBuilt} w/ terrain, {staticCellsSpawned} w/ STAT collision), {totalRefs} refs - terrain {terrainMs}ms, static {staticMs - terrainMs}ms, total {sw.ElapsedMilliseconds}ms");
        }

        public static void SpawnInteriorCell(World world, CellData cell, float3 worldOffset, Entity transitionEntity)
        {
            if (cell == null)
                return;

            var em = world.EntityManager;
            var spawnedEntities = new System.Collections.Generic.List<Entity>(cell.Refs?.Length + (cell.HasStaticCollider ? 1 : 0) ?? 1);
            int fallbackBucket = WorldResources.FallbackBucketSlice.x;
            var texInfo = WorldResources.TexBucketInfo;
            var meshBounds = WorldResources.MeshBounds;

            if (cell.HasStaticCollider)
            {
                var staticEntity = em.CreateEntity();
                em.SetName(staticEntity, $"InteriorStatic({cell.CellId})");
                em.AddComponentData(staticEntity, LocalTransform.FromPositionRotationScale(worldOffset, quaternion.identity, 1f));
                em.AddComponentData(staticEntity, new LocalToWorld
                {
                    Value = float4x4.Translate(worldOffset)
                });
                em.AddComponentData(staticEntity, new PhysicsCollider { Value = cell.StaticColliderBlob });
                em.AddComponent<InteriorCellMember>(staticEntity);
                em.AddComponent<Unity.Transforms.Static>(staticEntity);
                em.AddSharedComponent(staticEntity, new PhysicsWorldIndex { Value = 0 });
                spawnedEntities.Add(staticEntity);
            }

            var prefabs = WorldResources.RefPrefabs;
            var colliderBlobs = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Collider>>();
            if (cell.Refs == null)
                return;

            for (int i = 0; i < cell.Refs.Length; i++)
            {
                var entry = cell.Refs[i];
                int bucket = GetBucket(entry, texInfo, fallbackBucket);
                var entity = em.Instantiate(prefabs[bucket]);
                em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                    new float3(entry.PosX, entry.PosY, entry.PosZ) + worldOffset,
                    new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                    entry.Scale));
                em.SetComponentData(entity, MaterialMeshInfo.FromRenderMeshArrayIndices(entry.MaterialIndex, entry.MeshIndex));

                int textureSlice = entry.SliceIndex < 0 ? WorldResources.FallbackBucketSlice.y : texInfo[entry.SliceIndex].y;
                em.SetComponentData(entity, new TextureSlice { Value = textureSlice });
                var aabb = (uint)entry.MeshIndex < (uint)meshBounds.Length
                    ? meshBounds[entry.MeshIndex]
                    : new AABB { Center = float3.zero, Extents = new float3(1f) };
                em.SetComponentData(entity, new RenderBounds { Value = aabb });

                var blob = WorldResources.SentinelCollider;
                if ((uint)entry.CollisionIndex < (uint)colliderBlobs.Length && colliderBlobs[entry.CollisionIndex].IsCreated)
                    blob = colliderBlobs[entry.CollisionIndex];
                em.SetComponentData(entity, new PhysicsCollider { Value = blob });

                if (em.HasComponent<CellLink>(entity))
                    em.RemoveComponent<CellLink>(entity);

                em.AddComponent<InteriorCellMember>(entity);
                if (entry.PlacedRefId != 0u)
                {
                    em.AddComponentData(entity, new PlacedRefIdentity
                    {
                        Value = entry.PlacedRefId
                    });
                }

                if (entry.DoorMetaIndex >= 0 && cell.Doors != null && entry.DoorMetaIndex < cell.Doors.Length)
                {
                    var door = cell.Doors[entry.DoorMetaIndex];
                    em.AddComponentData(entity, new DoorInteractable
                    {
                        IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                        DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                        DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                        DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
                    });
                }

                spawnedEntities.Add(entity);
            }

            var spawnedBuffer = em.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            for (int i = 0; i < spawnedEntities.Count; i++)
                spawnedBuffer.Add(new InteriorSpawnedEntity { Value = spawnedEntities[i] });
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

            loaded.Active.Clear();
            var desiredEnumerator = desired.GetEnumerator();
            while (desiredEnumerator.MoveNext())
                loaded.Active.Add(desiredEnumerator.Current);
            desired.Dispose();
        }

        private static void ApplyRefGameplayMetadata(
            EntityManager em,
            NativeArray<RefEntry> refs,
            NativeArray<int2> coords,
            NativeArray<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                RefEntry entry = refs[i];
                if (entry.PlacedRefId != 0u && !em.HasComponent<PlacedRefIdentity>(entity))
                {
                    em.AddComponentData(entity, new PlacedRefIdentity
                    {
                        Value = entry.PlacedRefId
                    });
                }

                if (TryResolveDoorInteractable(coords[i], entry, out var door) && !em.HasComponent<DoorInteractable>(entity))
                    em.AddComponentData(entity, door);
            }
        }

        private static bool TryResolveDoorInteractable(int2 coord, RefEntry entry, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (entry.DoorMetaIndex < 0)
                return false;
            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell?.Doors == null || entry.DoorMetaIndex >= cell.Doors.Length)
                return false;

            var door = cell.Doors[entry.DoorMetaIndex];
            doorInteractable = new DoorInteractable
            {
                IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
            };
            return true;
        }

        private static int GetBucket(RefEntry entry, NativeArray<int2> texInfo, int fallbackBucket)
            => entry.SliceIndex < 0 ? fallbackBucket : texInfo[entry.SliceIndex].x;

        private struct PairedRef
        {
            public RefEntry Ref;
            public int2 Coord;
        }

        private struct RefSpawnData
        {
            public LocalTransform Transform;
            public MaterialMeshInfo MaterialMeshInfo;
            public TextureSlice TextureSlice;
            public CellLink CellLink;
            public RenderBounds RenderBounds;
            public PhysicsCollider Collider;
        }

        [BurstCompile]
        private struct BuildRefSpawnDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RefEntry> Refs;
            [ReadOnly] public NativeArray<int2> Coords;
            [ReadOnly] public NativeArray<int2> TexBucketInfo;
            [ReadOnly] public NativeArray<AABB> MeshBounds;
            [ReadOnly] public NativeArray<BlobAssetReference<Collider>> ColliderBlobs;
            [ReadOnly] public int2 FallbackBucketSlice;
            [ReadOnly] public int MeshCount;
            [ReadOnly] public BlobAssetReference<Collider> Sentinel;

            [WriteOnly] public NativeArray<RefSpawnData> Output;

            public void Execute(int index)
            {
                var r = Refs[index];
                int2 bucketSlice = r.SliceIndex < 0 ? FallbackBucketSlice : TexBucketInfo[r.SliceIndex];
                var aabb = (uint)r.MeshIndex < (uint)MeshCount
                    ? MeshBounds[r.MeshIndex]
                    : new AABB { Center = float3.zero, Extents = new float3(1f) };

                BlobAssetReference<Collider> blob = Sentinel;
                if ((uint)r.CollisionIndex < (uint)ColliderBlobs.Length)
                {
                    var candidate = ColliderBlobs[r.CollisionIndex];
                    if (candidate.IsCreated)
                        blob = candidate;
                }

                Output[index] = new RefSpawnData
                {
                    Transform = LocalTransform.FromPositionRotationScale(
                        new float3(r.PosX, r.PosY, r.PosZ),
                        new quaternion(r.RotX, r.RotY, r.RotZ, r.RotW),
                        r.Scale),
                    MaterialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(r.MaterialIndex, r.MeshIndex),
                    TextureSlice = new TextureSlice { Value = bucketSlice.y },
                    CellLink = new CellLink { Value = Coords[index] },
                    RenderBounds = new RenderBounds { Value = aabb },
                    Collider = new PhysicsCollider { Value = blob },
                };
            }
        }

        [BurstCompile]
        private struct ApplyRefSpawnDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<RefSpawnData> SpawnData;
            public EntityCommandBuffer.ParallelWriter CommandBuf;

            public void Execute(int index)
            {
                var entity = Entities[index];
                var data = SpawnData[index];
                CommandBuf.SetComponent(index, entity, data.Transform);
                CommandBuf.SetComponent(index, entity, data.MaterialMeshInfo);
                CommandBuf.SetComponent(index, entity, data.TextureSlice);
                CommandBuf.SetComponent(index, entity, data.CellLink);
                CommandBuf.SetComponent(index, entity, data.RenderBounds);
                CommandBuf.SetComponent(index, entity, data.Collider);
            }
        }

        private struct PairedBucketComparer : IComparer<PairedRef>
        {
            [ReadOnly] public NativeArray<int2> TexBucketInfo;
            public int FallbackBucket;

            public int Compare(PairedRef a, PairedRef b)
            {
                int ba = a.Ref.SliceIndex < 0 ? FallbackBucket : TexBucketInfo[a.Ref.SliceIndex].x;
                int bb = b.Ref.SliceIndex < 0 ? FallbackBucket : TexBucketInfo[b.Ref.SliceIndex].x;
                if (ba != bb)
                    return ba.CompareTo(bb);
                long ka = ((long)a.Ref.MaterialIndex << 32) | (uint)a.Ref.MeshIndex;
                long kb = ((long)b.Ref.MaterialIndex << 32) | (uint)b.Ref.MeshIndex;
                return ka.CompareTo(kb);
            }
        }

        private static Material BuildTerrainMaterial(CellData data)
        {
            if (WorldResources.TerrainShader == null
                || WorldResources.TerrainTemplate == null
                || WorldResources.Cache?.TerrainLayers == null
                || WorldResources.Cache.TerrainLayers.Array == null
                || data.LayerGrid == null)
            {
                return WorldResources.TerrainFallbackMat;
            }

            var mat = new Material(WorldResources.TerrainTemplate)
            {
                name = $"VV:Terrain({data.GridX},{data.GridY})",
            };
            mat.SetTexture("_LayerArray", WorldResources.Cache.TerrainLayers.Array);

            var splat = new Texture2D(16, 16, TextureFormat.R16, mipChain: false, linear: true)
            {
                name = $"VV:Splat({data.GridX},{data.GridY})",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            splat.SetPixelData(data.LayerGrid, 0);
            splat.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            mat.SetTexture("_Splat", splat);
            return mat;
        }

        private static Mesh BuildTerrainMesh(CellData data)
        {
            const int N = 65;
            float spacingMw = LandRecordSize.CellUnitsMw / (float)(N - 1);
            float spacingU = spacingMw * WorldScale.MwUnitsToMeters;

            var verts = new Vector3[N * N];
            var uvs = new Vector2[N * N];
            var normals = new Vector3[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    verts[i] = new Vector3(x * spacingU, data.Heights[i], y * spacingU);
                    uvs[i] = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
                    if (data.Normals != null)
                    {
                        float nx = data.Normals[i * 3 + 0] / 127f;
                        float ny = data.Normals[i * 3 + 1] / 127f;
                        float nz = data.Normals[i * 3 + 2] / 127f;
                        normals[i] = new Vector3(nx, nz, ny).normalized;
                    }
                }
            }

            var tris = new int[(N - 1) * (N - 1) * 6];
            int t = 0;
            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    int v00 = y * N + x;
                    int v10 = y * N + x + 1;
                    int v01 = (y + 1) * N + x;
                    int v11 = (y + 1) * N + x + 1;
                    tris[t++] = v00; tris[t++] = v01; tris[t++] = v10;
                    tris[t++] = v10; tris[t++] = v01; tris[t++] = v11;
                }
            }

            var mesh = new Mesh { name = $"Terrain({data.GridX},{data.GridY})" };
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            if (data.Normals != null)
                mesh.SetNormals(normals);
            else
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
