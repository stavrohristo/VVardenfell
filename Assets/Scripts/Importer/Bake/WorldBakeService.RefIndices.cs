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
        private static void ResolveDirtyCellIndices(
            StagedCellData staged,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            TerrainSplatBakery terrainSplats,
            CollisionBakery collisions,
            ModelPrefabBakery modelPrefabs,
            Dictionary<uint, int> materialIndexCache,
            Dictionary<string, int> textureIndexCache,
            Dictionary<int, ushort> terrainLayerCache)
        {
            var meshIndices = new HashSet<int>();
            var materialIndices = new HashSet<int>();
            var textureIndices = new HashSet<int>();
            var collisionIndices = new HashSet<int>();

            if (staged.TerrainTexturePaths != null)
            {
                var layerGrid = new ushort[staged.TerrainTexturePaths.Length];
                var layerByPath = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < staged.TerrainTexturePaths.Length; i++)
                {
                    string path = staged.TerrainTexturePaths[i];
                    if (!layerByPath.TryGetValue(path, out ushort layer))
                    {
                        int texIdx = GetTerrainTextureIndex(path, textures, textureIndexCache, $"{staged.WorkItem.Key} terrain VTEX slot {i}");
                        layer = GetTerrainLayerIndex(texIdx, terrainLayers, terrainLayerCache);
                        layerByPath[path] = layer;
                    }
                    layerGrid[i] = layer;
                }
                staged.LayerGrid = layerGrid;
            }

            if (staged.Land != null && staged.Land.HasHeights)
            {
                staged.TerrainMeshIndex = AddTerrainMesh(staged, meshes);
                meshIndices.Add(staged.TerrainMeshIndex);
                if (staged.LayerGrid == null)
                    throw new InvalidDataException($"{staged.WorkItem.Key} terrain has no resolved layer grid.");
                staged.TerrainSplatSlice = terrainSplats.AddOrGet(staged.LayerGrid);
            }

            int preparedCount = staged.PreparedRefs?.Count ?? 0;
            int placedCount = staged.PlacedRefs?.Count ?? 0;
            var result = new List<CellBakery.BakedRef>(preparedCount + placedCount);
            if (preparedCount != 0)
                throw new InvalidOperationException($"{staged.WorkItem.Key} produced {preparedCount} deprecated prepared refs after model-prefab ref conversion.");

            for (int i = 0; i < placedCount; i++)
            {
                var placed = staged.PlacedRefs[i];
                if (string.IsNullOrEmpty(placed.ModelPath))
                {
                    result.Add(new CellBakery.BakedRef(
                        RefSpawnMode.LogicalOnly,
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

                int collisionIndex = placed.AttachModelCollision ? assignment.CollisionIndex : -1;
                if (collisionIndex >= 0)
                    collisionIndices.Add(collisionIndex);

                result.Add(new CellBakery.BakedRef(
                    RefSpawnMode.ModelPrefab,
                    assignment.ModelPrefabIndex,
                    -1,
                    -1,
                    -1,
                    collisionIndex,
                    placed.PlacedRefId,
                    placed.DoorMetaIndex,
                    placed.ContentReference.HandleValue,
                    (int)placed.ContentReference.Kind,
                    placed.Position,
                    placed.Rotation,
                    placed.Scale));
            }

            staged.BakedRefs = result;
            staged.CombinedRenderChunks = BuildCombinedCellRenderChunks(
                staged,
                materials,
                textures,
                materialIndexCache,
                textureIndexCache,
                materialIndices,
                textureIndices);
            BakeCombinedRenderChunkMeshes(staged, meshes, textures, meshIndices);
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


        private static int AddTerrainMesh(StagedCellData staged, MeshBakery meshes)
        {
            const int sampleCount = LandRecord.Size;
            var land = staged.Land;
            if (land?.Heights == null || land.Heights.Length != sampleCount * sampleCount)
                throw new InvalidDataException($"{staged.WorkItem.Key} terrain height count mismatch.");

            bool hasNormals = land.Normals != null && land.Normals.Length == 3 * sampleCount * sampleCount;
            float spacingMw = LandRecordSize.CellUnitsMw / (float)(sampleCount - 1);
            float spacingU = spacingMw * WorldScale.MwUnitsToMeters;
            var vertices = new Vector3[sampleCount * sampleCount];
            var normals = hasNormals ? new Vector3[vertices.Length] : null;
            var uv0 = new Vector2[vertices.Length];
            var indices = new int[(sampleCount - 1) * (sampleCount - 1) * 6];
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int y = 0; y < sampleCount; y++)
            {
                for (int x = 0; x < sampleCount; x++)
                {
                    int index = y * sampleCount + x;
                    var vertex = new Vector3(
                        x * spacingU,
                        land.Heights[index] * WorldScale.MwUnitsToMeters,
                        y * spacingU);
                    vertices[index] = vertex;
                    uv0[index] = new Vector2(x / (float)(sampleCount - 1), y / (float)(sampleCount - 1));
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                    if (hasNormals)
                    {
                        float nx = land.Normals[index * 3 + 0] / 127f;
                        float ny = land.Normals[index * 3 + 1] / 127f;
                        float nz = land.Normals[index * 3 + 2] / 127f;
                        normals[index] = new Vector3(nx, nz, ny).normalized;
                    }
                }
            }

            int t = 0;
            for (int y = 0; y < sampleCount - 1; y++)
            {
                for (int x = 0; x < sampleCount - 1; x++)
                {
                    int v00 = y * sampleCount + x;
                    int v10 = y * sampleCount + x + 1;
                    int v01 = (y + 1) * sampleCount + x;
                    int v11 = (y + 1) * sampleCount + x + 1;
                    indices[t++] = v00; indices[t++] = v01; indices[t++] = v10;
                    indices[t++] = v10; indices[t++] = v01; indices[t++] = v11;
                }
            }

            return meshes.AddOrGetRaw(
                $"terrain:{staged.WorkItem.Key}",
                vertices,
                normals,
                uv0,
                null,
                indices,
                new Bounds((min + max) * 0.5f, max - min));
        }

        private static void BakeCombinedRenderChunkMeshes(
            StagedCellData staged,
            MeshBakery meshes,
            TextureBakery textures,
            HashSet<int> meshIndices)
        {
            var chunks = staged.CombinedRenderChunks;
            var textureSliceCache = new Dictionary<long, int>();
            for (int i = 0; i < (chunks?.Count ?? 0); i++)
            {
                var chunk = chunks[i] ?? throw new InvalidDataException($"{staged.WorkItem.Key} combined render chunk {i} is null.");
                int meshIndex = AddCombinedRenderMesh(staged.WorkItem.Key, i, chunk, meshes, textures, textureSliceCache);
                chunk.GlobalMeshIndex = meshIndex;
                chunk.VertexBytes = null;
                chunk.IndexBytes = null;
                meshIndices.Add(meshIndex);
            }
        }

        private static int AddCombinedRenderMesh(
            string cellKey,
            int chunkIndex,
            CombinedCellRenderChunkDef chunk,
            MeshBakery meshes,
            TextureBakery textures,
            Dictionary<long, int> textureSliceCache)
        {
            if (chunk.VertexBytes == null || chunk.IndexBytes == null)
                throw new InvalidDataException($"{cellKey} combined render chunk {chunkIndex} missing baked vertex/index bytes.");
            if ((chunk.MeshFlags & CacheFormat.MeshFlagHasTextureSelector) == 0
                || (chunk.MeshFlags & CacheFormat.MeshFlagHasAlphaCutoff) == 0)
                throw new InvalidDataException($"{cellKey} combined render chunk {chunkIndex} is missing texture selector or alpha cutoff payload.");

            const int sourceStride = 10 * sizeof(float);
            if (chunk.VertexCount <= 0 || chunk.IndexCount <= 0)
                throw new InvalidDataException($"{cellKey} combined render chunk {chunkIndex} has empty buffers.");
            if (chunk.VertexBytes.Length != chunk.VertexCount * sourceStride)
                throw new InvalidDataException($"{cellKey} combined render chunk {chunkIndex} vertex bytes mismatch.");
            if (chunk.IndexBytes.Length != chunk.IndexCount * sizeof(ushort))
                throw new InvalidDataException($"{cellKey} combined render chunk {chunkIndex} index bytes mismatch.");

            var vertices = new Vector3[chunk.VertexCount];
            var normals = new Vector3[chunk.VertexCount];
            var uv0 = new Vector2[chunk.VertexCount];
            var uv1 = new Vector2[chunk.VertexCount];
            using (var ms = new MemoryStream(chunk.VertexBytes, writable: false))
            using (var r = new BinaryReader(ms))
            {
                for (int v = 0; v < chunk.VertexCount; v++)
                {
                    vertices[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    normals[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    uv0[v] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    int textureIndex = (int)Math.Round(r.ReadSingle());
                    float alphaCutoff = r.ReadSingle();
                    long sliceKey = ((long)textureIndex << 32) ^ (uint)chunk.TextureBucketKey;
                    if (!textureSliceCache.TryGetValue(sliceKey, out int textureSlice))
                    {
                        textureSlice = textures.GetBucketSliceOrFallback(textureIndex, chunk.TextureBucketKey);
                        textureSliceCache.Add(sliceKey, textureSlice);
                    }
                    uv1[v] = new Vector2(textureSlice, alphaCutoff);
                }
            }

            var indices = new int[chunk.IndexCount];
            using (var ms = new MemoryStream(chunk.IndexBytes, writable: false))
            using (var r = new BinaryReader(ms))
            {
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = r.ReadUInt16();
            }

            return meshes.AddOrGetRaw(
                $"combined:{cellKey}:{chunkIndex}",
                vertices,
                normals,
                uv0,
                uv1,
                indices,
                new Bounds(
                    new Vector3(chunk.BoundsCenterX, chunk.BoundsCenterY, chunk.BoundsCenterZ),
                    new Vector3(chunk.BoundsExtentsX, chunk.BoundsExtentsY, chunk.BoundsExtentsZ) * 2f));
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


        private static int GetTerrainTextureIndex(string texturePath, TextureBakery textures, Dictionary<string, int> textureIndexCache, string context)
        {
            texturePath ??= string.Empty;
            if (!textureIndexCache.TryGetValue(texturePath, out int textureIndex))
            {
                textureIndex = textures.AddOrGetRequired(texturePath, context);
                textureIndexCache[texturePath] = textureIndex;
            }
            if (textureIndex < 0)
                throw new InvalidDataException($"{context} texture '{texturePath}' could not be resolved in configured data roots or archives.");
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


        private static IEnumerator WriteDirtyCellsIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            float cellMeters,
            ModelPrefabCatalogData modelPrefabCatalog,
            TextureBakery textures,
            MaterialBakery materials,
            GameplayContentData gameplayContent,
            CollisionBakery collisions)
        {
            progress.Stage = "Cell Writes";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to write" : $"Authoring cell sections 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            yield return AuthorCellSectionsIncremental(dirtyCells, progress, cellMeters, modelPrefabCatalog, textures, materials, gameplayContent, collisions);
        }


        }
    }
