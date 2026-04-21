using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using Unity.Burst;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Eagerly spawns every cell's entities at bootstrap in a DISABLED state, so the
    /// streaming pipeline at runtime only has to flip MaterialMeshInfo enable bits. No
    /// Instantiate / Destroy ever happens outside this one-time boot pass → chunk layout
    /// is frozen (matches eager-mode perf) + streaming preserves its visibility semantics
    /// (cells outside the view radius don't render).
    ///
    /// Trade: ~10-15 s of extra boot time (terrain Mesh/Texture2D/Material build ×1400
    /// cells + one batched Instantiate per bucket for ~330k refs) and peak memory of all
    /// baked content resident at once (~500-700 MB). Acceptable for a fixed-size world.
    /// </summary>
    public static class WorldSpawner
    {
        /// <summary>
        /// Populate <paramref name="loaded"/>'s <c>Map</c> with every preloaded cell,
        /// spawn terrain + refs for each, and leave them with MMI disabled so the streaming
        /// systems can toggle visibility per cell. <c>Active</c> stays empty — streaming
        /// will enable cells as the camera approaches.
        ///
        /// When <paramref name="gateTerrainByRadius"/> is false, terrain entities skip the
        /// bulk-disable pass — they're visible from boot and stay visible regardless of
        /// the player's position (useful for always-on distant-hills rendering).
        /// </summary>
        public static void SpawnAll(World world, CacheLoader cache, ref LoadedCellsMap loaded, bool gateTerrainByRadius)
        {
            var em = world.EntityManager;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // ---- Terrain entities ----
            // Build per-cell mesh/material/splat + create terrain entity. This is the
            // expensive phase (managed Unity APIs). 1400 cells × 5-10ms each.
            int terrainBuilt = 0;
            foreach (var kv in WorldResources.Cells)
            {
                var coord = kv.Key;
                var data  = kv.Value;
                Entity terrainEntity = Entity.Null;

                if (data.HasTerrain)
                {
                    var managed = new WorldResources.PerCellManaged();
                    managed.TerrainMesh = BuildTerrainMesh(data);
                    managed.TerrainMat  = BuildTerrainMaterial(data);
                    managed.SplatMap    = (managed.TerrainMat != null && managed.TerrainMat != WorldResources.TerrainFallbackMat)
                        ? managed.TerrainMat.GetTexture("_Splat") as Texture2D
                        : null;
                    managed.TerrainRma  = new RenderMeshArray(
                        new Material[] { managed.TerrainMat },
                        new Mesh[]     { managed.TerrainMesh });

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
                            Center  = new float3(cellHalf, 0f, cellHalf),
                            Extents = new float3(cellHalf, 1000f, cellHalf),
                        }
                    });

                    em.AddComponentData(terrainEntity, new CellCoord { Value = coord });
                    em.AddComponentData(terrainEntity, new CellLink { Value = coord });
                    // Enable bit handled in bulk below alongside refs.

                    WorldResources.LoadedManaged[coord] = managed;
                    terrainBuilt++;
                }

                loaded.Map[coord] = terrainEntity;
            }
            long terrainMs = sw.ElapsedMilliseconds;

            // ---- Ref entities ----
            // One sorted global ref array. Primary key: bucket (so we can do one Instantiate
            // per bucket from the matching prefab). Secondary: (matIdx, meshIdx) so BRG merges
            // same-MMI neighbours inside each chunk. Cell coord travels alongside so the fill
            // job can write per-ref CellLink.
            int totalRefs = 0;
            foreach (var kv in WorldResources.Cells) totalRefs += kv.Value.Refs?.Length ?? 0;

            if (totalRefs > 0 && WorldResources.RefPrefabs != null)
            {
                var refArr   = new NativeArray<RefEntry>(totalRefs, Allocator.TempJob);
                var coordArr = new NativeArray<int2>(totalRefs, Allocator.TempJob);
                int cursor = 0;
                foreach (var kv in WorldResources.Cells)
                {
                    var coord = kv.Key;
                    var refs = kv.Value.Refs;
                    if (refs == null || refs.Length == 0) continue;
                    for (int i = 0; i < refs.Length; i++)
                    {
                        refArr[cursor]   = refs[i];
                        coordArr[cursor] = coord;
                        cursor++;
                    }
                }

                // Sort (paired) by bucket then (mat, mesh). Since NativeArray<T>.Sort with an
                // IComparer can't pair two arrays, we pack both into a temporary struct array,
                // sort that, then unpack back. Cheap for 330k elements.
                var paired = new NativeArray<PairedRef>(totalRefs, Allocator.TempJob);
                for (int i = 0; i < totalRefs; i++)
                    paired[i] = new PairedRef { Ref = refArr[i], Coord = coordArr[i] };

                paired.Sort(new PairedBucketComparer
                {
                    TexBucketInfo  = WorldResources.TexBucketInfo,
                    FallbackBucket = WorldResources.FallbackBucketSlice.x,
                });

                for (int i = 0; i < totalRefs; i++)
                {
                    refArr[i]   = paired[i].Ref;
                    coordArr[i] = paired[i].Coord;
                }
                paired.Dispose();

                // Instantiate in bucket runs.
                var entities = new NativeArray<Entity>(totalRefs, Allocator.TempJob);
                var texInfo        = WorldResources.TexBucketInfo;
                int fallbackBucket = WorldResources.FallbackBucketSlice.x;
                var prefabs        = WorldResources.RefPrefabs;

                int runStart  = 0;
                int runBucket = refArr[0].SliceIndex < 0 ? fallbackBucket : texInfo[refArr[0].SliceIndex].x;
                for (int i = 1; i <= totalRefs; i++)
                {
                    int b = (i < totalRefs)
                        ? (refArr[i].SliceIndex < 0 ? fallbackBucket : texInfo[refArr[i].SliceIndex].x)
                        : -1;
                    if (b != runBucket)
                    {
                        int runLen = i - runStart;
                        var slice = entities.GetSubArray(runStart, runLen);
                        em.Instantiate(prefabs[runBucket], slice);
                        runStart = i;
                        runBucket = b;
                    }
                }

                // Build the per-ref payloads in Burst, then record the component writes via
                // a parallel ECB. Playback is still deterministic, but the expensive
                // coordinate/material/bounds derivation no longer runs in one large serial
                // loop on the main thread.
                var fallbackBucketSlice = WorldResources.FallbackBucketSlice;
                var meshBounds          = WorldResources.MeshBounds;
                int meshCount           = meshBounds.Length;
                var spawnData = new NativeArray<RefSpawnData>(totalRefs, Allocator.TempJob);
                new BuildRefSpawnDataJob
                {
                    Refs                = refArr,
                    Coords              = coordArr,
                    TexBucketInfo       = texInfo,
                    FallbackBucketSlice = fallbackBucketSlice,
                    MeshBounds          = meshBounds,
                    MeshCount           = meshCount,
                    Output              = spawnData,
                }.Schedule(totalRefs, 128).Complete();

                var ecb = new EntityCommandBuffer(Allocator.TempJob);
                new ApplyRefSpawnDataJob
                {
                    Entities   = entities,
                    SpawnData  = spawnData,
                    CommandBuf = ecb.AsParallelWriter(),
                }.Schedule(totalRefs, 128).Complete();

                ecb.Playback(em);
                ecb.Dispose();
                spawnData.Dispose();

                entities.Dispose();
                refArr.Dispose();
                coordArr.Dispose();
            }

            // Bulk-disable rendering on every CellLink-bearing entity in one shot — an
            // O(chunks) enable-bit flip rather than O(entities) per-entity calls. Streaming
            // systems re-enable per cell as it enters view.
            //
            // When gateTerrainByRadius is false we EXCLUDE terrain (WithNone<CellCoord> —
            // only terrain has CellCoord) so it starts and stays visible regardless of
            // camera position.
            var disableQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CellLink, MaterialMeshInfo>();
            if (!gateTerrainByRadius)
                disableQueryBuilder = disableQueryBuilder.WithNone<CellCoord>();
            var disableQuery = em.CreateEntityQuery(disableQueryBuilder);
            em.SetComponentEnabled<MaterialMeshInfo>(disableQuery, false);
            disableQuery.Dispose();
            disableQueryBuilder.Dispose();

            sw.Stop();
            Debug.Log($"[VVardenfell] eager-spawn: {loaded.Map.Count} cells ({terrainBuilt} w/ terrain), {totalRefs} refs — terrain {terrainMs}ms, total {sw.ElapsedMilliseconds}ms");
        }

        // ----------- helpers -----------

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
        }

        [BurstCompile]
        private struct BuildRefSpawnDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RefEntry> Refs;
            [ReadOnly] public NativeArray<int2> Coords;
            [ReadOnly] public NativeArray<int2> TexBucketInfo;
            [ReadOnly] public NativeArray<AABB> MeshBounds;
            [ReadOnly] public int2 FallbackBucketSlice;
            [ReadOnly] public int MeshCount;

            [WriteOnly] public NativeArray<RefSpawnData> Output;

            public void Execute(int index)
            {
                var r = Refs[index];
                int2 bucketSlice = r.SliceIndex < 0 ? FallbackBucketSlice : TexBucketInfo[r.SliceIndex];
                var aabb = (uint)r.MeshIndex < (uint)MeshCount
                    ? MeshBounds[r.MeshIndex]
                    : new AABB { Center = float3.zero, Extents = new float3(1f) };

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
                if (ba != bb) return ba.CompareTo(bb);
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

            var verts   = new Vector3[N * N];
            var uvs     = new Vector2[N * N];
            var normals = new Vector3[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    verts[i] = new Vector3(x * spacingU, data.Heights[i], y * spacingU);
                    uvs[i]   = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
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
            if (data.Normals != null) mesh.SetNormals(normals);
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
