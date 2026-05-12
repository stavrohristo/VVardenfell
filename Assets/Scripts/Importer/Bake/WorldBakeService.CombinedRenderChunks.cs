using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    internal static partial class WorldBakeService
    {
        const int CombinedCellRenderMaxVertices = 65535;
        const int CombinedCellRenderTileCount = 2;
        const int CombinedCellRenderMinLeaves = 2;
        const int CombinedCellRenderCoalesceMaxSourceVertices = 16384;
        const int CombinedCellRenderPolicyVersion = 2;

        private static List<CombinedCellRenderChunkDef> BuildCombinedCellRenderChunks(
            StagedCellData staged,
            MaterialBakery materials,
            TextureBakery textures,
            Dictionary<uint, int> materialIndexCache,
            Dictionary<string, int> textureIndexCache,
            HashSet<int> materialIndices,
            HashSet<int> textureIndices)
        {
            if (!staged.BakeCombinedCellRenderChunks || staged.WorkItem.IsInterior || staged.PlacedRefs == null || staged.PlacedRefs.Count == 0)
                return new List<CombinedCellRenderChunkDef>();

            var groups = new Dictionary<CombinedCellRenderGroupKey, List<CombinedCellRenderLeaf>>();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float tileMeters = cellMeters / CombinedCellRenderTileCount;
            for (int i = 0; i < staged.PlacedRefs.Count; i++)
            {
                var placed = staged.PlacedRefs[i];
                if (placed.ContentReference.Kind != ContentReferenceKind.Static)
                {
                    staged.CombinedRenderRejectedNonStaticRefCount++;
                    continue;
                }

                if (!placed.CombinedStaticEligible)
                    continue;

                staged.CombinedRenderCandidateRefCount++;
                var nodes = placed.Model.Prefab.Nodes;
                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    var node = nodes[nodeIndex];
                    if (node.Kind != ModelPrefabNodeKind.RenderLeaf || node.RenderLeaf.VertexCount == 0)
                        continue;

                    if (!node.RenderLeaf.HasNormals || !node.RenderLeaf.HasUvs)
                        continue;

                    if (!TryBuildCombinedCellRenderLeaf(placed, nodeIndex, staged.WorkItem.CellOrigin, tileMeters, out var leaf))
                    {
                        staged.CombinedRenderRejectedOversizedLeafCount++;
                        continue;
                    }

                    int materialIndex = GetMaterialIndex(node.MaterialFlags, materials, materialIndexCache);
                    int combinedMaterialIndex = GetCombinedCellRenderMaterialIndex(node.MaterialFlags);
                    int textureIndex = GetTextureIndex(node.TexturePath, textures, textureIndexCache);
                    int textureBucketKey = GetCombinedCellRenderTextureBucketKey(textureIndex, textures);
                    float alphaCutoff = GetCombinedCellRenderAlphaCutoff(node.MaterialFlags);
                    materialIndices.Add(materialIndex);
                    if (textureIndex >= 0)
                        textureIndices.Add(textureIndex);

                    leaf = leaf.WithRenderState(textureIndex, alphaCutoff);
                    var key = new CombinedCellRenderGroupKey(leaf.TileX, leaf.TileY, combinedMaterialIndex, textureBucketKey);
                    if (!groups.TryGetValue(key, out var leaves))
                    {
                        leaves = new List<CombinedCellRenderLeaf>();
                        groups.Add(key, leaves);
                    }

                    leaves.Add(leaf);
                    staged.CombinedRenderCandidateLeafCount++;
                }
            }

            staged.CombinedRenderBucketGroupCount = groups.Count;
            var chunks = new List<CombinedCellRenderChunkDef>();
            var weakGroups = new Dictionary<CombinedCellRenderRescueKey, List<CombinedCellRenderLeaf>>();
            var keys = new List<CombinedCellRenderGroupKey>(groups.Keys);
            keys.Sort(CompareCombinedCellRenderGroupKeys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                AppendCombinedCellRenderGroupChunks(key, groups[key], staged.WorkItem.CellOrigin, chunks, staged, weakGroups);
            }

            AppendCombinedCellRenderRescueChunks(weakGroups, staged.WorkItem.CellOrigin, chunks, staged);
            CoalesceSmallCombinedCellRenderChunks(chunks, staged);

            return chunks;
        }

        static bool IsCombinedCellRenderEligibleStaticGraph(in StagedPlacedRefData placed)
            => IsCombinedCellRenderEligibleStaticGraph(placed.ModelPath, placed.Model);

        static bool IsCombinedCellRenderEligibleStaticGraph(string modelPath, ModelSource model)
        {
            if (model == null || model.Prefab == null || string.IsNullOrEmpty(modelPath))
                return false;

            if (model.HasObjectAnimation || model.HasUnsupportedObjectControllers)
                return false;

            var nodes = model.Prefab.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                var nodeKind = nodes[i].Kind;
                if (nodeKind is not (ModelPrefabNodeKind.SyntheticRoot or ModelPrefabNodeKind.Transform or ModelPrefabNodeKind.RenderLeaf))
                    return false;
            }

            return true;
        }

        static bool TryBuildCombinedCellRenderLeaf(
            in StagedPlacedRefData placed,
            int nodeIndex,
            Vector3 cellOrigin,
            float tileMeters,
            out CombinedCellRenderLeaf leaf)
        {
            var nodes = placed.Model.Prefab.Nodes;
            var mesh = nodes[nodeIndex].RenderLeaf;
            Matrix4x4 nodeToModel = BuildNodeLocalToModel(nodes, nodeIndex);
            Matrix4x4 refToCell = Matrix4x4.TRS(
                placed.Position - cellOrigin,
                placed.Rotation,
                Vector3.one * placed.Scale);
            Matrix4x4 localToCell = refToCell * nodeToModel;

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                Vector3 position = localToCell.MultiplyPoint3x4(mesh.Vertices[v]);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            Vector3 size = max - min;
            if (size.x > tileMeters || size.z > tileMeters)
            {
                leaf = default;
                return false;
            }

            Vector3 center = (min + max) * 0.5f;
            int tileX = Mathf.Clamp((int)Mathf.Floor(center.x / tileMeters), 0, CombinedCellRenderTileCount - 1);
            int tileY = Mathf.Clamp((int)Mathf.Floor(center.z / tileMeters), 0, CombinedCellRenderTileCount - 1);
            leaf = new CombinedCellRenderLeaf(placed, nodeIndex, tileX, tileY, min, max);
            return true;
        }

        static float GetCombinedCellRenderTileMeters()
            => LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters / CombinedCellRenderTileCount;

        static int GetCombinedCellRenderTextureBucketKey(int textureIndex, TextureBakery textures)
        {
            return textureIndex >= 0 && textures != null
                ? textures.GetBucketKey(textureIndex)
                : RefTextureBucketFile.MakeBucketKey(1, 1, TextureFormat.RGBA32, 1);
        }

        static int GetCombinedCellRenderMaterialIndex(uint materialFlags)
            => (materialFlags & CacheFormat.MatFlagAlphaBlend) != 0 ? 1 : 0;

        static float GetCombinedCellRenderAlphaCutoff(uint materialFlags)
            => (materialFlags & CacheFormat.MatFlagAlphaClip) != 0
                ? CacheFormat.UnpackAlphaThreshold(materialFlags) / 255f
                : -1f;

        static void AppendCombinedCellRenderGroupChunks(
            CombinedCellRenderGroupKey key,
            List<CombinedCellRenderLeaf> leaves,
            Vector3 cellOrigin,
            List<CombinedCellRenderChunkDef> chunks,
            StagedCellData staged,
            Dictionary<CombinedCellRenderRescueKey, List<CombinedCellRenderLeaf>> weakGroups)
        {
            leaves.Sort(CompareCombinedCellRenderLeaves);
            int start = 0;
            while (start < leaves.Count)
            {
                int vertexCount = 0;
                int indexCount = 0;
                int end = start;
                for (; end < leaves.Count; end++)
                {
                    var leaf = leaves[end];
                    var mesh = leaf.Placed.Model.Prefab.Nodes[leaf.NodeIndex].RenderLeaf;
                    int meshVertexCount = mesh.VertexCount;
                    if (meshVertexCount > CombinedCellRenderMaxVertices)
                    {
                        throw new InvalidDataException(
                            $"Combined render source '{leaf.Placed.ModelPath}' has render leaf '{mesh.Name}' with {meshVertexCount} vertices, exceeding {CombinedCellRenderMaxVertices}.");
                    }

                    if (vertexCount + meshVertexCount > CombinedCellRenderMaxVertices && end > start)
                    {
                        break;
                    }

                    vertexCount += meshVertexCount;
                    indexCount += mesh.Indices.Length;
                }

                if (!PassesCombinedCellRenderCostGate(leaves, start, end))
                {
                    AddCombinedCellRenderWeakLeaves(key, leaves, start, end, weakGroups);
                    start = end;
                    continue;
                }

                int uniqueTextureCount = CountUniqueTextureIndices(leaves, start, end);
                chunks.Add(BuildCombinedCellRenderChunk(key, leaves, start, end, vertexCount, indexCount, cellOrigin));
                staged.CombinedRenderEmittedChunkCount++;
                staged.CombinedRenderEmittedMemberLeafCount += end - start;
                staged.CombinedRenderEmittedMemberRefCount += CountUniquePlacedRefs(leaves, start, end);
                staged.CombinedRenderEmittedVertexCount += vertexCount;
                staged.CombinedRenderEmittedIndexCount += indexCount;
                staged.CombinedRenderEmittedUniqueTextureCount += uniqueTextureCount;
                if (uniqueTextureCount > 1)
                    staged.CombinedRenderEmittedMultiTextureChunkCount++;
                start = end;
            }
        }

        static void AppendCombinedCellRenderRescueChunks(
            Dictionary<CombinedCellRenderRescueKey, List<CombinedCellRenderLeaf>> weakGroups,
            Vector3 cellOrigin,
            List<CombinedCellRenderChunkDef> chunks,
            StagedCellData staged)
        {
            if (weakGroups == null || weakGroups.Count == 0)
                return;

            var keys = new List<CombinedCellRenderRescueKey>(weakGroups.Keys);
            keys.Sort(CompareCombinedCellRenderRescueKeys);
            for (int i = 0; i < keys.Count; i++)
            {
                var rescueKey = keys[i];
                var leaves = weakGroups[rescueKey];
                if (leaves == null || leaves.Count == 0)
                    continue;

                leaves.Sort(CompareCombinedCellRenderLeaves);
                int start = 0;
                while (start < leaves.Count)
                {
                    int vertexCount = 0;
                    int indexCount = 0;
                    int end = start;
                    for (; end < leaves.Count; end++)
                    {
                        var leaf = leaves[end];
                        var mesh = leaf.Placed.Model.Prefab.Nodes[leaf.NodeIndex].RenderLeaf;
                        int meshVertexCount = mesh.VertexCount;
                        if (meshVertexCount > CombinedCellRenderMaxVertices)
                        {
                            throw new InvalidDataException(
                                $"Combined render source '{leaf.Placed.ModelPath}' has render leaf '{mesh.Name}' with {meshVertexCount} vertices, exceeding {CombinedCellRenderMaxVertices}.");
                        }

                        if (vertexCount + meshVertexCount > CombinedCellRenderMaxVertices && end > start)
                            break;

                        vertexCount += meshVertexCount;
                        indexCount += mesh.Indices.Length;
                    }

                    if (!PassesCombinedCellRenderCostGate(leaves, start, end))
                    {
                        staged.CombinedRenderRejectedWeakGroupCount++;
                        start = end;
                        continue;
                    }

                    var groupKey = new CombinedCellRenderGroupKey(-1, -1, rescueKey.MaterialIndex, rescueKey.TextureBucketKey);
                    int uniqueTextureCount = CountUniqueTextureIndices(leaves, start, end);
                    chunks.Add(BuildCombinedCellRenderChunk(groupKey, leaves, start, end, vertexCount, indexCount, cellOrigin));
                    staged.CombinedRenderEmittedChunkCount++;
                    staged.CombinedRenderCellWideChunkCount++;
                    staged.CombinedRenderRescuedWeakLeafCount += end - start;
                    staged.CombinedRenderEmittedMemberLeafCount += end - start;
                    staged.CombinedRenderEmittedMemberRefCount += CountUniquePlacedRefs(leaves, start, end);
                    staged.CombinedRenderEmittedVertexCount += vertexCount;
                    staged.CombinedRenderEmittedIndexCount += indexCount;
                    staged.CombinedRenderEmittedUniqueTextureCount += uniqueTextureCount;
                    if (uniqueTextureCount > 1)
                        staged.CombinedRenderEmittedMultiTextureChunkCount++;
                    start = end;
                }
            }
        }

        static void AddCombinedCellRenderWeakLeaves(
            CombinedCellRenderGroupKey key,
            List<CombinedCellRenderLeaf> leaves,
            int start,
            int end,
            Dictionary<CombinedCellRenderRescueKey, List<CombinedCellRenderLeaf>> weakGroups)
        {
            var rescueKey = new CombinedCellRenderRescueKey(key.MaterialIndex, key.TextureBucketKey);
            if (!weakGroups.TryGetValue(rescueKey, out var rescuedLeaves))
            {
                rescuedLeaves = new List<CombinedCellRenderLeaf>();
                weakGroups.Add(rescueKey, rescuedLeaves);
            }

            for (int i = start; i < end; i++)
                rescuedLeaves.Add(leaves[i]);
        }

        static void CoalesceSmallCombinedCellRenderChunks(List<CombinedCellRenderChunkDef> chunks, StagedCellData staged)
        {
            if (chunks == null || chunks.Count < 2)
                return;

            var groups = new Dictionary<CombinedCellRenderCoalesceKey, List<int>>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk == null || chunk.TileX < 0 || chunk.TileY < 0 || chunk.VertexCount > CombinedCellRenderCoalesceMaxSourceVertices)
                    continue;

                var key = new CombinedCellRenderCoalesceKey(chunk.MaterialIndex, chunk.TextureBucketKey);
                if (!groups.TryGetValue(key, out var indices))
                {
                    indices = new List<int>();
                    groups.Add(key, indices);
                }

                indices.Add(i);
            }

            if (groups.Count == 0)
                return;

            var remove = new bool[chunks.Count];
            var mergedChunks = new List<CombinedCellRenderChunkDef>();
            int coalescedSourceChunks = 0;
            int coalescedChunks = 0;
            int sourceMultiTextureChunks = 0;
            int mergedMultiTextureChunks = 0;
            int sourceUniqueTextures = 0;
            int mergedUniqueTextures = 0;
            var keys = new List<CombinedCellRenderCoalesceKey>(groups.Keys);
            keys.Sort(CompareCombinedCellRenderCoalesceKeys);
            for (int k = 0; k < keys.Count; k++)
            {
                var indices = groups[keys[k]];
                indices.Sort(CompareCombinedCellRenderChunkIndices(chunks));
                int start = 0;
                while (start < indices.Count)
                {
                    int vertexCount = 0;
                    int indexCount = 0;
                    int end = start;
                    for (; end < indices.Count; end++)
                    {
                        var chunk = chunks[indices[end]];
                        if (vertexCount + chunk.VertexCount > CombinedCellRenderMaxVertices && end > start)
                            break;

                        vertexCount += chunk.VertexCount;
                        indexCount += chunk.IndexCount;
                    }

                    if (end - start < 2)
                    {
                        start = end;
                        continue;
                    }

                    var merged = MergeCombinedCellRenderChunks(chunks, indices, start, end, vertexCount, indexCount);
                    mergedChunks.Add(merged);
                    coalescedSourceChunks += end - start;
                    coalescedChunks++;
                    int mergedTextureCount = CountUniqueTextureSelectors(merged);
                    mergedUniqueTextures += mergedTextureCount;
                    if (mergedTextureCount > 1)
                        mergedMultiTextureChunks++;
                    for (int i = start; i < end; i++)
                    {
                        int sourceTextureCount = CountUniqueTextureSelectors(chunks[indices[i]]);
                        sourceUniqueTextures += sourceTextureCount;
                        if (sourceTextureCount > 1)
                            sourceMultiTextureChunks++;
                        remove[indices[i]] = true;
                    }
                    start = end;
                }
            }

            if (mergedChunks.Count == 0)
                return;

            var retained = new List<CombinedCellRenderChunkDef>(chunks.Count - coalescedSourceChunks + mergedChunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                if (!remove[i])
                    retained.Add(chunks[i]);
            }

            retained.AddRange(mergedChunks);
            chunks.Clear();
            chunks.AddRange(retained);
            staged.CombinedRenderCoalescedSourceChunkCount += coalescedSourceChunks;
            staged.CombinedRenderCoalescedChunkCount += coalescedChunks;
            staged.CombinedRenderEmittedChunkCount -= coalescedSourceChunks - coalescedChunks;
            staged.CombinedRenderCellWideChunkCount += coalescedChunks;
            staged.CombinedRenderEmittedMultiTextureChunkCount += mergedMultiTextureChunks - sourceMultiTextureChunks;
            staged.CombinedRenderEmittedUniqueTextureCount += mergedUniqueTextures - sourceUniqueTextures;
        }

        static Comparison<int> CompareCombinedCellRenderChunkIndices(List<CombinedCellRenderChunkDef> chunks)
            => (a, b) =>
            {
                var ca = chunks[a];
                var cb = chunks[b];
                int cmp = ca.TileX.CompareTo(cb.TileX);
                if (cmp != 0) return cmp;
                cmp = ca.TileY.CompareTo(cb.TileY);
                if (cmp != 0) return cmp;
                cmp = ca.VertexCount.CompareTo(cb.VertexCount);
                return cmp != 0 ? cmp : a.CompareTo(b);
            };

        static CombinedCellRenderChunkDef MergeCombinedCellRenderChunks(
            List<CombinedCellRenderChunkDef> chunks,
            List<int> indices,
            int start,
            int end,
            int vertexCount,
            int indexCount)
        {
            var first = chunks[indices[start]];
            var vertexBytes = new byte[vertexCount * (sizeof(float) * 10)];
            var indexBytes = new byte[indexCount * sizeof(ushort)];
            using var vertexStream = new MemoryStream(vertexBytes);
            using var indexStream = new MemoryStream(indexBytes);
            using var indexWriter = new BinaryWriter(indexStream);
            var members = new List<CombinedCellRenderChunkMemberDef>();
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            int vertexOffset = 0;
            for (int i = start; i < end; i++)
            {
                var chunk = chunks[indices[i]];
                if (chunk.MeshFlags != first.MeshFlags)
                    throw new InvalidDataException("Combined render chunk coalesce encountered incompatible mesh flags.");
                if (chunk.MaterialIndex != first.MaterialIndex || chunk.TextureBucketKey != first.TextureBucketKey)
                    throw new InvalidDataException("Combined render chunk coalesce encountered incompatible render state.");

                vertexStream.Write(chunk.VertexBytes, 0, chunk.VertexBytes.Length);
                using var sourceIndexStream = new MemoryStream(chunk.IndexBytes, writable: false);
                using var indexReader = new BinaryReader(sourceIndexStream);
                for (int idx = 0; idx < chunk.IndexCount; idx++)
                    indexWriter.Write(checked((ushort)(indexReader.ReadUInt16() + vertexOffset)));

                vertexOffset += chunk.VertexCount;
                if (chunk.Members != null)
                    members.AddRange(chunk.Members);

                var chunkMin = new Vector3(
                    chunk.BoundsCenterX - chunk.BoundsExtentsX,
                    chunk.BoundsCenterY - chunk.BoundsExtentsY,
                    chunk.BoundsCenterZ - chunk.BoundsExtentsZ);
                var chunkMax = new Vector3(
                    chunk.BoundsCenterX + chunk.BoundsExtentsX,
                    chunk.BoundsCenterY + chunk.BoundsExtentsY,
                    chunk.BoundsCenterZ + chunk.BoundsExtentsZ);
                min = Vector3.Min(min, chunkMin);
                max = Vector3.Max(max, chunkMax);
            }

            members.Sort(CompareCombinedCellRenderMembers);
            Bounds bounds = new((min + max) * 0.5f, max - min);
            return new CombinedCellRenderChunkDef
            {
                TileX = -1,
                TileY = -1,
                MaterialIndex = first.MaterialIndex,
                TextureBucketKey = first.TextureBucketKey,
                BoundsCenterX = bounds.center.x,
                BoundsCenterY = bounds.center.y,
                BoundsCenterZ = bounds.center.z,
                BoundsExtentsX = bounds.extents.x,
                BoundsExtentsY = bounds.extents.y,
                BoundsExtentsZ = bounds.extents.z,
                VertexCount = vertexCount,
                IndexCount = indexCount,
                MeshFlags = first.MeshFlags,
                VertexBytes = vertexBytes,
                IndexBytes = indexBytes,
                Members = members.ToArray(),
            };
        }

        static bool PassesCombinedCellRenderCostGate(List<CombinedCellRenderLeaf> leaves, int start, int end)
            => end - start >= CombinedCellRenderMinLeaves;

        static int CountUniquePlacedRefs(List<CombinedCellRenderLeaf> leaves, int start, int end)
        {
            var ids = new HashSet<uint>();
            for (int i = start; i < end; i++)
                ids.Add(leaves[i].Placed.PlacedRefId);
            return ids.Count;
        }

        static int CountUniqueTextureIndices(List<CombinedCellRenderLeaf> leaves, int start, int end)
        {
            var ids = new HashSet<int>();
            for (int i = start; i < end; i++)
                ids.Add(leaves[i].TextureIndex);
            return ids.Count;
        }

        static int CountUniqueTextureSelectors(CombinedCellRenderChunkDef chunk)
        {
            const int vertexStride = sizeof(float) * 10;
            const int textureSelectorOffset = sizeof(float) * 8;
            if (chunk == null || chunk.VertexBytes == null || chunk.VertexBytes.Length != chunk.VertexCount * vertexStride)
                throw new InvalidDataException("Combined render chunk has invalid vertex payload length.");

            var ids = new HashSet<int>();
            for (int i = 0; i < chunk.VertexCount; i++)
            {
                int byteOffset = (i * vertexStride) + textureSelectorOffset;
                ids.Add((int)BitConverter.ToSingle(chunk.VertexBytes, byteOffset));
            }

            return ids.Count;
        }

        static CombinedCellRenderChunkDef BuildCombinedCellRenderChunk(
            CombinedCellRenderGroupKey key,
            List<CombinedCellRenderLeaf> leaves,
            int start,
            int end,
            int vertexCount,
            int indexCount,
            Vector3 cellOrigin)
        {
            var vertexBytes = new byte[vertexCount * (sizeof(float) * 10)];
            var indexBytes = new byte[indexCount * sizeof(ushort)];
            using var vertexStream = new MemoryStream(vertexBytes);
            using var indexStream = new MemoryStream(indexBytes);
            using var vertexWriter = new BinaryWriter(vertexStream);
            using var indexWriter = new BinaryWriter(indexStream);
            var members = new List<CombinedCellRenderChunkMemberDef>(end - start);
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            int vertexOffset = 0;
            for (int i = start; i < end; i++)
            {
                var leaf = leaves[i];
                var nodes = leaf.Placed.Model.Prefab.Nodes;
                var node = nodes[leaf.NodeIndex];
                var mesh = node.RenderLeaf;
                Matrix4x4 nodeToModel = BuildNodeLocalToModel(nodes, leaf.NodeIndex);
                Matrix4x4 refToCell = Matrix4x4.TRS(
                    leaf.Placed.Position - cellOrigin,
                    leaf.Placed.Rotation,
                    Vector3.one * leaf.Placed.Scale);
                Matrix4x4 localToCell = refToCell * nodeToModel;

                for (int v = 0; v < mesh.Vertices.Length; v++)
                {
                    Vector3 position = localToCell.MultiplyPoint3x4(mesh.Vertices[v]);
                    Vector3 normal = localToCell.MultiplyVector(mesh.Normals[v]).normalized;
                    Vector2 uv = mesh.Uvs[v];

                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);

                    vertexWriter.Write(position.x);
                    vertexWriter.Write(position.y);
                    vertexWriter.Write(position.z);
                    vertexWriter.Write(normal.x);
                    vertexWriter.Write(normal.y);
                    vertexWriter.Write(normal.z);
                    vertexWriter.Write(uv.x);
                    vertexWriter.Write(uv.y);
                    vertexWriter.Write((float)leaf.TextureIndex);
                    vertexWriter.Write(leaf.AlphaCutoff);
                }

                for (int idx = 0; idx < mesh.Indices.Length; idx++)
                    indexWriter.Write(checked((ushort)(mesh.Indices[idx] + vertexOffset)));

                vertexOffset += mesh.Vertices.Length;
                members.Add(new CombinedCellRenderChunkMemberDef
                {
                    PlacedRefId = leaf.Placed.PlacedRefId,
                    NodeIndex = leaf.NodeIndex,
                });
            }

            Bounds bounds = new((min + max) * 0.5f, max - min);
            members.Sort(CompareCombinedCellRenderMembers);

            return new CombinedCellRenderChunkDef
            {
                TileX = key.TileX,
                TileY = key.TileY,
                MaterialIndex = key.MaterialIndex,
                TextureBucketKey = key.TextureBucketKey,
                BoundsCenterX = bounds.center.x,
                BoundsCenterY = bounds.center.y,
                BoundsCenterZ = bounds.center.z,
                BoundsExtentsX = bounds.extents.x,
                BoundsExtentsY = bounds.extents.y,
                BoundsExtentsZ = bounds.extents.z,
                VertexCount = vertexCount,
                IndexCount = indexCount,
                MeshFlags = CacheFormat.MeshFlagHasNormals | CacheFormat.MeshFlagHasUVs | CacheFormat.MeshFlagHasTextureSelector | CacheFormat.MeshFlagHasAlphaCutoff,
                VertexBytes = vertexBytes,
                IndexBytes = indexBytes,
                Members = members.ToArray(),
            };
        }

        static Matrix4x4 BuildNodeLocalToModel(ModelPrefabSourceNode[] nodes, int nodeIndex)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            int current = nodeIndex;
            while (current >= 0)
            {
                var node = nodes[current];
                matrix = Matrix4x4.TRS(node.LocalPosition, node.LocalRotation, Vector3.one * node.LocalScale) * matrix;
                current = node.ParentIndex;
            }

            return matrix;
        }

        readonly struct CombinedCellRenderGroupKey : IEquatable<CombinedCellRenderGroupKey>
        {
            public readonly int TileX;
            public readonly int TileY;
            public readonly int MaterialIndex;
            public readonly int TextureBucketKey;

            public CombinedCellRenderGroupKey(int tileX, int tileY, int materialIndex, int textureBucketKey)
            {
                TileX = tileX;
                TileY = tileY;
                MaterialIndex = materialIndex;
                TextureBucketKey = textureBucketKey;
            }

            public bool Equals(CombinedCellRenderGroupKey other)
                => TileX == other.TileX && TileY == other.TileY && MaterialIndex == other.MaterialIndex && TextureBucketKey == other.TextureBucketKey;

            public override bool Equals(object obj)
                => obj is CombinedCellRenderGroupKey other && Equals(other);

            public override int GetHashCode()
                => (((TileX * 397) ^ TileY) * 397) ^ (MaterialIndex * 397) ^ TextureBucketKey;
        }

        readonly struct CombinedCellRenderRescueKey : IEquatable<CombinedCellRenderRescueKey>
        {
            public readonly int MaterialIndex;
            public readonly int TextureBucketKey;

            public CombinedCellRenderRescueKey(int materialIndex, int textureBucketKey)
            {
                MaterialIndex = materialIndex;
                TextureBucketKey = textureBucketKey;
            }

            public bool Equals(CombinedCellRenderRescueKey other)
                => MaterialIndex == other.MaterialIndex && TextureBucketKey == other.TextureBucketKey;

            public override bool Equals(object obj)
                => obj is CombinedCellRenderRescueKey other && Equals(other);

            public override int GetHashCode()
                => (MaterialIndex * 397) ^ TextureBucketKey;
        }

        readonly struct CombinedCellRenderCoalesceKey : IEquatable<CombinedCellRenderCoalesceKey>
        {
            public readonly int MaterialIndex;
            public readonly int TextureBucketKey;

            public CombinedCellRenderCoalesceKey(int materialIndex, int textureBucketKey)
            {
                MaterialIndex = materialIndex;
                TextureBucketKey = textureBucketKey;
            }

            public bool Equals(CombinedCellRenderCoalesceKey other)
                => MaterialIndex == other.MaterialIndex && TextureBucketKey == other.TextureBucketKey;

            public override bool Equals(object obj)
                => obj is CombinedCellRenderCoalesceKey other && Equals(other);

            public override int GetHashCode()
                => (MaterialIndex * 397) ^ TextureBucketKey;
        }

        readonly struct CombinedCellRenderLeaf
        {
            public readonly StagedPlacedRefData Placed;
            public readonly int NodeIndex;
            public readonly int TileX;
            public readonly int TileY;
            public readonly int TextureIndex;
            public readonly float AlphaCutoff;
            public readonly Vector3 BoundsMin;
            public readonly Vector3 BoundsMax;

            public CombinedCellRenderLeaf(in StagedPlacedRefData placed, int nodeIndex, int tileX, int tileY, Vector3 boundsMin, Vector3 boundsMax, int textureIndex = -1, float alphaCutoff = -1f)
            {
                Placed = placed;
                NodeIndex = nodeIndex;
                TileX = tileX;
                TileY = tileY;
                TextureIndex = textureIndex;
                AlphaCutoff = alphaCutoff;
                BoundsMin = boundsMin;
                BoundsMax = boundsMax;
            }

            public CombinedCellRenderLeaf WithRenderState(int textureIndex, float alphaCutoff)
                => new CombinedCellRenderLeaf(Placed, NodeIndex, TileX, TileY, BoundsMin, BoundsMax, textureIndex, alphaCutoff);
        }

        static int CompareCombinedCellRenderGroupKeys(CombinedCellRenderGroupKey a, CombinedCellRenderGroupKey b)
        {
            int cmp = a.TileX.CompareTo(b.TileX);
            if (cmp != 0) return cmp;
            cmp = a.TileY.CompareTo(b.TileY);
            if (cmp != 0) return cmp;
            cmp = a.MaterialIndex.CompareTo(b.MaterialIndex);
            return cmp != 0 ? cmp : a.TextureBucketKey.CompareTo(b.TextureBucketKey);
        }

        static int CompareCombinedCellRenderRescueKeys(CombinedCellRenderRescueKey a, CombinedCellRenderRescueKey b)
        {
            int cmp = a.MaterialIndex.CompareTo(b.MaterialIndex);
            return cmp != 0 ? cmp : a.TextureBucketKey.CompareTo(b.TextureBucketKey);
        }

        static int CompareCombinedCellRenderCoalesceKeys(CombinedCellRenderCoalesceKey a, CombinedCellRenderCoalesceKey b)
        {
            int cmp = a.MaterialIndex.CompareTo(b.MaterialIndex);
            return cmp != 0 ? cmp : a.TextureBucketKey.CompareTo(b.TextureBucketKey);
        }

        static int CompareCombinedCellRenderLeaves(CombinedCellRenderLeaf a, CombinedCellRenderLeaf b)
        {
            int cmp = a.Placed.PlacedRefId.CompareTo(b.Placed.PlacedRefId);
            return cmp != 0 ? cmp : a.NodeIndex.CompareTo(b.NodeIndex);
        }

        static int CompareCombinedCellRenderMembers(CombinedCellRenderChunkMemberDef a, CombinedCellRenderChunkMemberDef b)
        {
            int cmp = a.PlacedRefId.CompareTo(b.PlacedRefId);
            return cmp != 0 ? cmp : a.NodeIndex.CompareTo(b.NodeIndex);
        }

        static void LogCombinedCellRenderBakeStats(StagedCellData[] stagedCells)
        {
            if (stagedCells == null)
                return;

            long candidateRefs = 0;
            long candidateLeaves = 0;
            long rejectedNonStaticRefs = 0;
            long rejectedOversizedLeaves = 0;
            long rejectedWeakGroups = 0;
            long bucketGroups = 0;
            long rescuedWeakLeaves = 0;
            long cellWideChunks = 0;
            long coalescedSourceChunks = 0;
            long coalescedChunks = 0;
            long emittedChunks = 0;
            long emittedMemberLeaves = 0;
            long emittedMemberRefs = 0;
            long emittedVertices = 0;
            long emittedIndices = 0;
            long emittedMultiTextureChunks = 0;
            long emittedUniqueTextures = 0;

            for (int i = 0; i < stagedCells.Length; i++)
            {
                var staged = stagedCells[i];
                if (staged == null)
                    continue;

                candidateRefs += staged.CombinedRenderCandidateRefCount;
                candidateLeaves += staged.CombinedRenderCandidateLeafCount;
                rejectedNonStaticRefs += staged.CombinedRenderRejectedNonStaticRefCount;
                rejectedOversizedLeaves += staged.CombinedRenderRejectedOversizedLeafCount;
                rejectedWeakGroups += staged.CombinedRenderRejectedWeakGroupCount;
                bucketGroups += staged.CombinedRenderBucketGroupCount;
                rescuedWeakLeaves += staged.CombinedRenderRescuedWeakLeafCount;
                cellWideChunks += staged.CombinedRenderCellWideChunkCount;
                coalescedSourceChunks += staged.CombinedRenderCoalescedSourceChunkCount;
                coalescedChunks += staged.CombinedRenderCoalescedChunkCount;
                emittedChunks += staged.CombinedRenderEmittedChunkCount;
                emittedMemberLeaves += staged.CombinedRenderEmittedMemberLeafCount;
                emittedMemberRefs += staged.CombinedRenderEmittedMemberRefCount;
                emittedVertices += staged.CombinedRenderEmittedVertexCount;
                emittedIndices += staged.CombinedRenderEmittedIndexCount;
                emittedMultiTextureChunks += staged.CombinedRenderEmittedMultiTextureChunkCount;
                emittedUniqueTextures += staged.CombinedRenderEmittedUniqueTextureCount;
            }

            Debug.Log(
                $"[VVardenfell][CombinedRenderBake] candidates refs={candidateRefs} leaves={candidateLeaves}; " +
                $"rejected nonStaticRefs={rejectedNonStaticRefs} oversizedLeaves={rejectedOversizedLeaves} weakGroups={rejectedWeakGroups}; " +
                $"bucketGroups={bucketGroups} rescuedWeakLeaves={rescuedWeakLeaves}; emitted chunks={emittedChunks} cellWideChunks={cellWideChunks} " +
                $"coalescedChunks={coalescedChunks}/{coalescedSourceChunks} multiTextureChunks={emittedMultiTextureChunks} " +
                $"uniqueTexturesInChunks={emittedUniqueTextures} memberLeaves={emittedMemberLeaves} memberRefs={emittedMemberRefs} vertices={emittedVertices} indices={emittedIndices}");
        }
    }
}
