using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{

    internal static partial class WorldBakeService
    {
        private static IEnumerator AssignRenderShardIndicesIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            MeshBakery meshes,
            TextureBakery textures,
            RenderShardBakery renderShards)
        {

            progress.Stage = "Assigning render shards";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to shard" : $"Assigning render shards 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            int maxWorkers = Math.Max(1, Environment.ProcessorCount);
            int fallbackBucketKey = RenderShardBakery.PackBucketKey(1, 1);

            string[] familyKeysByMeshIndex = Array.Empty<string>();
            if (meshes.Count > 0)
            {
                familyKeysByMeshIndex = new string[meshes.Count];
                int familyCompleted = 0;
                var familyTask = Task.Run(() =>
                {
                    try
                    {
                        Parallel.For(
                            0,
                            familyKeysByMeshIndex.Length,
                            new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                            meshIndex =>
                            {
                                string sourceLabel = meshes.GetSourceLabel(meshIndex);
                                familyKeysByMeshIndex[meshIndex] = RenderShardBakery.NormalizeFamilyKey(sourceLabel);
                                Interlocked.Increment(ref familyCompleted);
                            });
                    }
                    finally
                    {
                    }
                });

                while (!familyTask.IsCompleted)
                {
                    progress.Total = familyKeysByMeshIndex.Length;
                    progress.Current = familyCompleted;
                    progress.Label = $"Precomputing shard families {familyCompleted}/{familyKeysByMeshIndex.Length}";
                    yield return null;
                }

                familyTask.GetAwaiter().GetResult();

                progress.Total = familyKeysByMeshIndex.Length;
                progress.Current = familyKeysByMeshIndex.Length;
                progress.Label = $"Precomputing shard families {familyKeysByMeshIndex.Length}/{familyKeysByMeshIndex.Length}";
                yield return null;
            }

            int[] bucketKeysByTextureIndex = Array.Empty<int>();
            if (textures.Count > 0)
            {
                bucketKeysByTextureIndex = new int[textures.Count];
                for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
                    bucketKeysByTextureIndex[textureIndex] = GetTextureBucketKey(textures, textureIndex);
            }

            var localRequestSets = new ConcurrentBag<HashSet<RenderShardRequestKey>>();
            int requestBuildCompleted = 0;
            var requestBuildTask = Task.Run(() =>
            {
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, dirtyCells.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                        () => new HashSet<RenderShardRequestKey>(),
                        (range, _, localSet) =>
                        {
                            for (int cellIndex = range.Item1; cellIndex < range.Item2; cellIndex++)
                            {
                                var staged = dirtyCells[cellIndex];
                                var bakedRefs = staged.BakedRefs;
                                if (bakedRefs != null)
                                {
                                    for (int r = 0; r < bakedRefs.Count; r++)
                                    {
                                        var baked = bakedRefs[r];
                                        if ((RefSpawnMode)baked.SpawnModeRaw != RefSpawnMode.RenderShard)
                                            continue;
                                        int globalMeshIndex = baked.LocalMeshIndex;
                                        if (globalMeshIndex < 0)
                                            continue;

                                        int textureIndex = baked.SliceIndex;
                                        int bucketKey = (uint)textureIndex < (uint)bucketKeysByTextureIndex.Length
                                            ? bucketKeysByTextureIndex[textureIndex]
                                            : fallbackBucketKey;
                                        string familyKey = (uint)globalMeshIndex < (uint)familyKeysByMeshIndex.Length
                                            ? familyKeysByMeshIndex[globalMeshIndex]
                                            : "__root__";
                                        localSet.Add(new RenderShardRequestKey(bucketKey, familyKey, globalMeshIndex));
                                    }
                                }

                                Interlocked.Increment(ref requestBuildCompleted);
                            }

                            return localSet;
                        },
                        localSet => localRequestSets.Add(localSet));
                }
                finally
                {
                }
            });

            while (!requestBuildTask.IsCompleted)
            {
                progress.Total = dirtyCells.Count;
                progress.Current = requestBuildCompleted;
                progress.Label = $"Building shard requests {requestBuildCompleted}/{dirtyCells.Count}";
                yield return null;
            }

            requestBuildTask.GetAwaiter().GetResult();

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Building shard requests {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            List<RenderShardRequestKey> sortedRequests;
            {
                var uniqueRequests = new HashSet<RenderShardRequestKey>();
                foreach (var localSet in localRequestSets)
                {
                    if (localSet == null)
                        continue;

                    foreach (var request in localSet)
                        uniqueRequests.Add(request);
                }

                sortedRequests = new List<RenderShardRequestKey>(uniqueRequests);
                sortedRequests.Sort(RenderShardRequestKeyComparer.Instance);
            }

            progress.Total = Math.Max(1, sortedRequests.Count);
            progress.Current = 0;
            progress.Label = sortedRequests.Count == 0
                ? "Resolving shard assignments 0/0"
                : $"Resolving shard assignments 0/{sortedRequests.Count}";
            yield return null;

            int suspiciousShardCount = 0;
            var resolvedAssignments = new Dictionary<RenderShardRequestKey, RenderShardResolvedAssignment>(sortedRequests.Count);
            {
                for (int i = 0; i < sortedRequests.Count; i++)
                {
                    var request = sortedRequests[i];
                    var assignment = renderShards.GetOrAddAssignment(request.BucketKey, request.FamilyKey, request.GlobalMeshIndex);
                    if (assignment.LocalMeshIndex == 0 && assignment.RenderShardIndex >= renderShards.Count - 1)
                        suspiciousShardCount++;

                    resolvedAssignments[request] = new RenderShardResolvedAssignment(
                        assignment.RenderShardIndex,
                        assignment.LocalMeshIndex);

                    if (((i + 1) & 255) == 0 || i + 1 == sortedRequests.Count)
                    {
                        progress.Total = Math.Max(1, sortedRequests.Count);
                        progress.Current = i + 1;
                        progress.Label = $"Resolving shard assignments {i + 1}/{sortedRequests.Count}";
                        yield return null;
                    }
                }
            }

            int applyCompleted = 0;
            var applyTask = Task.Run(() =>
            {
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, dirtyCells.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                        range =>
                        {
                            for (int cellIndex = range.Item1; cellIndex < range.Item2; cellIndex++)
                            {
                                var staged = dirtyCells[cellIndex];
                                var bakedRefs = staged.BakedRefs;
                                if (bakedRefs != null)
                                {
                                    for (int r = 0; r < bakedRefs.Count; r++)
                                    {
                                        var baked = bakedRefs[r];
                                        if ((RefSpawnMode)baked.SpawnModeRaw != RefSpawnMode.RenderShard)
                                            continue;
                                        int globalMeshIndex = baked.LocalMeshIndex;
                                        if (globalMeshIndex < 0)
                                            continue;

                                        int textureIndex = baked.SliceIndex;
                                        int bucketKey = (uint)textureIndex < (uint)bucketKeysByTextureIndex.Length
                                            ? bucketKeysByTextureIndex[textureIndex]
                                            : fallbackBucketKey;
                                        string familyKey = (uint)globalMeshIndex < (uint)familyKeysByMeshIndex.Length
                                            ? familyKeysByMeshIndex[globalMeshIndex]
                                            : "__root__";
                                        var request = new RenderShardRequestKey(bucketKey, familyKey, globalMeshIndex);
                                        var resolved = resolvedAssignments[request];

                                        bakedRefs[r] = new CellBakery.BakedRef(
                                            RefSpawnMode.RenderShard,
                                            resolved.RenderShardIndex,
                                            resolved.LocalMeshIndex,
                                            baked.LocalMaterialIndex,
                                            baked.SliceIndex,
                                            baked.CollisionIndex,
                                            baked.PlacedRefId,
                                            baked.DoorMetaIndex,
                                            baked.ContentHandleValue,
                                            baked.ContentKind,
                                            baked.PositionUnity,
                                            baked.RotationUnity,
                                            baked.Scale);
                                    }
                                }

                                Interlocked.Increment(ref applyCompleted);
                            }
                        });
                }
                finally
                {
                }
            });

            while (!applyTask.IsCompleted)
            {
                progress.Total = dirtyCells.Count;
                progress.Current = applyCompleted;
                progress.Label = $"Applying shard assignments {applyCompleted}/{dirtyCells.Count}";
                yield return null;
            }

            applyTask.GetAwaiter().GetResult();

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Applying shard assignments {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Assigning render shards {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            var catalog = renderShards.BuildCatalog();
            RenderShardFile.LogShardStats(catalog, "bake");
            if (suspiciousShardCount > 0)
            {
            }
        }


        private static int GetTextureBucketKey(TextureBakery textures, int textureIndex)
        {
            if (textureIndex < 0)
                return RenderShardBakery.PackBucketKey(1, 1);

            int2 dims = textures.GetBucketDimensions(textureIndex);
            return RenderShardBakery.PackBucketKey(dims.x, dims.y);
        }


        private static void ResolveDirtyCellIndices(
            StagedCellData staged,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            CollisionBakery collisions,
            ModelPrefabBakery modelPrefabs,
            Dictionary<uint, int> materialIndexCache,
            Dictionary<string, int> textureIndexCache,
            Dictionary<int, ushort> terrainLayerCache)
        {
            try
            {
            if (staged.PreparedRefs == null && staged.PlacedRefs == null)
                return;

            if (staged.TerrainTexturePaths != null)
            {
                var layerGrid = new ushort[staged.TerrainTexturePaths.Length];
                var layerByPath = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < staged.TerrainTexturePaths.Length; i++)
                {
                    string path = staged.TerrainTexturePaths[i];
                    if (!layerByPath.TryGetValue(path, out ushort layer))
                    {
                        int texIdx = GetTextureIndex(path, textures, textureIndexCache);
                        layer = GetTerrainLayerIndex(texIdx, terrainLayers, terrainLayerCache);
                        layerByPath[path] = layer;
                    }
                    layerGrid[i] = layer;
                }
                staged.LayerGrid = layerGrid;
            }

            int preparedCount = staged.PreparedRefs?.Count ?? 0;
            int placedCount = staged.PlacedRefs?.Count ?? 0;
            var result = new List<CellBakery.BakedRef>(preparedCount + placedCount);
            var meshIndices = new HashSet<int>();
            var materialIndices = new HashSet<int>();
            var textureIndices = new HashSet<int>();
            var collisionIndices = new HashSet<int>();
            for (int i = 0; i < preparedCount; i++)
            {
                var prepared = staged.PreparedRefs[i];
                int meshIndex = meshes.AddOrGet(prepared.MeshSourceLabel, prepared.Built);
                int materialIndex = GetMaterialIndex(prepared.MaterialFlags, materials, materialIndexCache);
                int sliceIndex = GetTextureIndex(prepared.TexturePath, textures, textureIndexCache);
                int collisionIndex = prepared.Collision.IsEmpty
                    ? -1
                    : collisions.AddOrGet(prepared.Collision);
                meshIndices.Add(meshIndex);
                materialIndices.Add(materialIndex);
                if (sliceIndex >= 0)
                    textureIndices.Add(sliceIndex);
                if (collisionIndex >= 0)
                    collisionIndices.Add(collisionIndex);
                result.Add(new CellBakery.BakedRef(
                    RefSpawnMode.RenderShard,
                    -1,
                    meshIndex,
                    materialIndex,
                    sliceIndex,
                    collisionIndex,
                    prepared.PlacedRefId,
                    prepared.DoorMetaIndex,
                    prepared.ContentReference.HandleValue,
                    (int)prepared.ContentReference.Kind,
                    prepared.Position,
                    prepared.Rotation,
                    prepared.Scale));
            }

            for (int i = 0; i < placedCount; i++)
            {
                var placed = staged.PlacedRefs[i];
                if (string.IsNullOrEmpty(placed.ModelPath))
                {
                    result.Add(new CellBakery.BakedRef(
                        RefSpawnMode.RenderShard,
                        -1,
                        -1,
                        -1,
                        -1,
                        -1,
                        placed.PlacedRefId,
                        placed.DoorMetaIndex,
                        placed.ContentReference.HandleValue,
                        (int)placed.ContentReference.Kind,
                        placed.Position,
                        placed.Rotation,
                        placed.Scale));
                    continue;
                }

                if (!modelPrefabs.TryGetAssignment(placed.ModelPath, out var assignment))
                {
                    RecordDroppedBakeRef(
                        staged.WorkItem,
                        placed.PlacedRefId,
                        string.Empty,
                        placed.ModelPath,
                        placed.ContentReference,
                        "model-prefab assignment was not found");
                    continue;
                }

                if (assignment.CollisionIndex >= 0)
                    collisionIndices.Add(assignment.CollisionIndex);

                result.Add(new CellBakery.BakedRef(
                    RefSpawnMode.ModelPrefab,
                    assignment.ModelPrefabIndex,
                    -1,
                    -1,
                    -1,
                    assignment.CollisionIndex,
                    placed.PlacedRefId,
                    placed.DoorMetaIndex,
                    placed.ContentReference.HandleValue,
                    (int)placed.ContentReference.Kind,
                    placed.Position,
                    placed.Rotation,
                    placed.Scale));
            }

            staged.BakedRefs = result;
            staged.GlobalMeshIndices = ToSortedArray(meshIndices);
            staged.GlobalMaterialIndices = ToSortedArray(materialIndices);
            staged.GlobalTextureIndices = ToSortedArray(textureIndices);
            staged.GlobalCollisionIndices = ToSortedArray(collisionIndices);
            if (staged.LayerGrid != null)
            {
                var terrainLayerIndices = new HashSet<int>();
                for (int i = 0; i < staged.LayerGrid.Length; i++)
                    terrainLayerIndices.Add(staged.LayerGrid[i]);
                staged.GlobalTerrainLayerIndices = ToSortedArray(terrainLayerIndices);
            }
            else
            {
                staged.GlobalTerrainLayerIndices = Array.Empty<int>();
            }
            }
            finally
            {
            }
        }


        private static int GetMaterialIndex(uint flags, MaterialBakery materials, Dictionary<uint, int> materialIndexCache)
        {
            if (!materialIndexCache.TryGetValue(flags, out int materialIndex))
            {
                materialIndex = materials.AddOrGet(flags);
                materialIndexCache[flags] = materialIndex;
            }
            return materialIndex;
        }


        private static int GetTextureIndex(string texturePath, TextureBakery textures, Dictionary<string, int> textureIndexCache)
        {
            texturePath ??= string.Empty;
            if (!textureIndexCache.TryGetValue(texturePath, out int textureIndex))
            {
                textureIndex = textures.AddOrGet(texturePath);
                textureIndexCache[texturePath] = textureIndex;
            }
            return textureIndex;
        }


        private static ushort GetTerrainLayerIndex(int textureIndex, TerrainLayerBakery terrainLayers, Dictionary<int, ushort> terrainLayerCache)
        {
            if (!terrainLayerCache.TryGetValue(textureIndex, out ushort layerIndex))
            {
                layerIndex = terrainLayers.AddOrGet(textureIndex);
                terrainLayerCache[textureIndex] = layerIndex;
            }
            return layerIndex;
        }


        private static List<PreparedRefData> BuildPreparedCellRefs(StagedCellData staged)
        {
            try
            {
            var result = new List<PreparedRefData>(staged.PendingRefs.Count);
            staged.PendingRefCount = staged.PendingRefs.Count;
            if (staged.PendingRefs.Count == 0)
            {
                staged.PreparedWorkItemCount = 0;
                staged.SourcePreparedMeshCount = 0;
                staged.StaticGroupCount = 0;
                staged.CombinedGroupCount = 0;
                staged.InteractableWorkItemCount = 0;
                staged.CombinedPreparedPayloadBytes = 0L;
                return result;
            }

            var workItems = new List<PreparedWorkItem>(staged.PendingRefs.Count);
            int interactableWorkItems = 0;
            int sourcePreparedMeshCount = 0;
            try
            {
                for (int i = 0; i < staged.PendingRefs.Count; i++)
                {
                    var pending = staged.PendingRefs[i];
                    if (pending.IsInteractable)
                    {
                        workItems.Add(new PreparedWorkItem(
                            true,
                            pending.Built,
                            pending.Collision,
                            pending.MaterialFlags,
                            pending.TexturePath,
                            pending.PlacedRefId,
                            pending.DoorMetaIndex,
                            pending.ContentReference,
                            pending.Position,
                            pending.Rotation,
                            pending.Scale,
                            pending.MeshSourceLabel));
                        interactableWorkItems++;
                        continue;
                    }

                    workItems.Add(new PreparedWorkItem(
                        false,
                        pending.Built,
                        default,
                        pending.MaterialFlags,
                        pending.TexturePath,
                        0u,
                        -1,
                        default,
                        pending.Position,
                        pending.Rotation,
                        pending.Scale,
                        pending.MeshSourceLabel));
                    sourcePreparedMeshCount++;
                }
            }
            finally
            {
            }

            int combinedGroupCount = 0;
            try
            {
                // Storage-first pass: keep non-interactable refs as reusable source meshes
                // instead of baking cell-specific combined meshes.
            }
            finally
            {
            }

            if (workItems.Count == 0)
            {
                staged.PreparedWorkItemCount = 0;
                staged.SourcePreparedMeshCount = 0;
                staged.StaticGroupCount = 0;
                staged.CombinedGroupCount = combinedGroupCount;
                staged.InteractableWorkItemCount = interactableWorkItems;
                staged.CombinedPreparedPayloadBytes = 0L;
                return result;
            }

            var prepared = new PreparedRefData[workItems.Count];
            try
            {
                for (int i = 0; i < workItems.Count; i++)
                    prepared[i] = BuildPreparedRefData(workItems[i]);
            }
            finally
            {
            }

            result.AddRange(prepared);
            staged.PreparedWorkItemCount = workItems.Count;
            staged.SourcePreparedMeshCount = sourcePreparedMeshCount;
            staged.StaticGroupCount = sourcePreparedMeshCount;
            staged.CombinedGroupCount = combinedGroupCount;
            staged.InteractableWorkItemCount = interactableWorkItems;
            staged.CombinedPreparedPayloadBytes = 0L;
            return result;
            }
            finally
            {
            }
        }


        private static PreparedRefData BuildPreparedRefData(in PreparedWorkItem workItem)
        {
            try
            {
            return new PreparedRefData(
                workItem.Built,
                workItem.Collision,
                workItem.MeshSourceLabel,
                workItem.MaterialFlags,
                workItem.TexturePath,
                workItem.PlacedRefId,
                workItem.DoorMetaIndex,
                workItem.ContentReference,
                workItem.Position,
                workItem.Rotation,
                workItem.Scale);
            }
            finally
            {
            }
        }


        private static NifMeshBuilder.RawBuiltMesh CombineGroupMesh(List<StagedRefData> group, Vector3 cellOrigin, string name)
        {
            try
            {
            int totalVerts = 0;
            int totalIndices = 0;
            bool hasNormals = true;
            bool hasUvs = true;

            for (int i = 0; i < group.Count; i++)
            {
                totalVerts += group[i].Built.VertexCount;
                totalIndices += group[i].Built.Indices.Length;
                hasNormals &= group[i].Built.HasNormals;
                hasUvs &= group[i].Built.HasUvs;
            }

            var verts = new Vector3[totalVerts];
            Vector3[] normals = hasNormals ? new Vector3[totalVerts] : null;
            Vector2[] uvs = hasUvs ? new Vector2[totalVerts] : null;
            var indices = new int[totalIndices];

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            int vertexOffset = 0;
            int indexOffset = 0;
            for (int i = 0; i < group.Count; i++)
            {
                var pending = group[i];
                for (int v = 0; v < pending.Built.Vertices.Length; v++)
                {
                    var transformed = pending.Position + pending.Rotation * (pending.Built.Vertices[v] * pending.Scale) - cellOrigin;
                    verts[vertexOffset + v] = transformed;

                    if (transformed.x < min.x) min.x = transformed.x;
                    if (transformed.y < min.y) min.y = transformed.y;
                    if (transformed.z < min.z) min.z = transformed.z;
                    if (transformed.x > max.x) max.x = transformed.x;
                    if (transformed.y > max.y) max.y = transformed.y;
                    if (transformed.z > max.z) max.z = transformed.z;

                    if (normals != null)
                        normals[vertexOffset + v] = pending.Rotation * pending.Built.Normals[v];
                    if (uvs != null)
                        uvs[vertexOffset + v] = pending.Built.Uvs[v];
                }

                for (int t = 0; t < pending.Built.Indices.Length; t++)
                    indices[indexOffset + t] = pending.Built.Indices[t] + vertexOffset;

                vertexOffset += pending.Built.Vertices.Length;
                indexOffset += pending.Built.Indices.Length;
            }

            return new NifMeshBuilder.RawBuiltMesh(
                verts,
                normals,
                uvs,
                indices,
                group[0].TexturePath,
                name,
                new Bounds((min + max) * 0.5f, max - min),
                group[0].Built.AlphaFlags,
                group[0].Built.AlphaThreshold);
            }
            finally
            {
            }
        }


        private static IEnumerator WriteDirtyCellsIncremental(List<StagedCellData> dirtyCells, BakeProgress progress, float cellMeters)
        {
            progress.Stage = "Cell Writes";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to write" : $"Preparing cell write payloads 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            var preparedWrites = new PreparedCellWriteData[dirtyCells.Count];
            yield return PrepareCellWritePayloadsIncremental(dirtyCells, preparedWrites, progress);
            yield return BuildCellColliderBlobsIncremental(preparedWrites, progress, cellMeters);
            yield return FlushCellFilesIncremental(preparedWrites, progress);
        }


        private static IEnumerator PrepareCellWritePayloadsIncremental(
            List<StagedCellData> dirtyCells,
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress)
        {
            var info = new CellWriteProgressInfo { Subphase = "Preparing cell write payloads" };
            UpdateCellWriteProgress(progress, dirtyCells.Count, info);
            yield return null;

            int claimed = 0;
            int completed = 0;
            string lastCompletedKey = null;
            Exception prepareFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var prepareTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWorkers];
                for (int worker = 0; worker < maxWorkers; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= dirtyCells.Count)
                                break;

                            Interlocked.Increment(ref claimed);
                            try
                            {
                                preparedWrites[index] = PrepareCellWritePayload(dirtyCells[index]);
                                lastCompletedKey = dirtyCells[index].WorkItem.Key;
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    prepareFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!prepareTask.IsCompleted)
            {
                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                info.LastCompletedCellKey = lastCompletedKey;
                UpdateCellWriteProgress(progress, dirtyCells.Count, info);
                yield return null;
            }

            if (prepareFailure != null)
                throw prepareFailure;

            info.ClaimedCount = dirtyCells.Count;
            info.CompletedCount = dirtyCells.Count;
            info.LastCompletedCellKey = lastCompletedKey;
            UpdateCellWriteProgress(progress, dirtyCells.Count, info);
            yield return null;
        }


        private static PreparedCellWriteData PrepareCellWritePayload(StagedCellData staged)
        {
            try
            {
                uint flags = 0;
                bool hasTerrain = staged.Land != null && staged.Land.HasHeights;
                bool hasNormals = hasTerrain && staged.Land.Normals != null;
                bool hasVtex = hasTerrain && staged.LayerGrid != null && staged.LayerGrid.Length == LandRecord.NumTextures;
                bool hasWorldMap = hasTerrain && staged.Land.WorldMap != null && staged.Land.WorldMap.Length == 81;
                bool hasStaticCollision = !staged.StaticCollision.IsEmpty;
                bool hasEnvironment = staged.Environment.HasAnyData;
                if (hasTerrain) flags |= CacheFormat.CellFlagHasTerrain;
                if (hasNormals) flags |= CacheFormat.CellFlagHasNormals;
                if (hasVtex) flags |= CacheFormat.CellFlagHasVtex;
                if (hasStaticCollision) flags |= CacheFormat.CellFlagHasStaticCollision;
                if (hasEnvironment) flags |= CacheFormat.CellFlagHasEnvironment;
                if (hasWorldMap) flags |= CacheFormat.CellFlagHasWorldMap;

                return new PreparedCellWriteData
                {
                    Key = staged.WorkItem.Key,
                    OutputPath = staged.WorkItem.OutputPath,
                    IsInterior = staged.WorkItem.IsInterior,
                    CellId = staged.WorkItem.Cell.Name ?? string.Empty,
                    GridX = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridY,
                    Flags = flags,
                    Environment = staged.Environment,
                    Land = staged.Land,
                    StaticCollision = staged.StaticCollision,
                    TerrainHeightBytes = hasTerrain ? BuildTerrainHeightBytes(staged.Land) : null,
                    TerrainNormalBytes = hasNormals ? BuildTerrainNormalBytes(staged.Land) : null,
                    LayerGridBytes = hasVtex ? BuildLayerGridBytes(staged.LayerGrid) : null,
                    WorldMapBytes = hasWorldMap ? BuildWorldMapBytes(staged.Land) : null,
                    RefBytes = BuildRefBytes(staged.BakedRefs),
                    DoorBytes = BuildDoorBytes(staged.DoorEntries),
                    RefCount = staged.BakedRefs?.Count ?? 0,
                    DoorCount = staged.DoorEntries?.Count ?? 0,
                };
            }
            finally
            {
            }
        }


        }
    }
