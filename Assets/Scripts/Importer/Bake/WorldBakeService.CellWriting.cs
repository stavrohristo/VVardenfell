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
        private static IEnumerator BuildCellColliderBlobsIncremental(
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress,
            float cellMeters)
        {
            var info = new CellWriteProgressInfo { Subphase = "Building collider blobs" };
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;

            using var scratch = new CellWriteScratch();
            var pendingStaticJobs = new List<PendingStaticColliderJob>();
            int nextIndex = 0;
            int claimed = 0;
            int completed = 0;
            int maxInFlightStaticJobs = Math.Max(1, Math.Min(Math.Max(1, Environment.ProcessorCount - 1), preparedWrites.Length));

            while (completed < preparedWrites.Length)
            {
                while (nextIndex < preparedWrites.Length && pendingStaticJobs.Count < maxInFlightStaticJobs)
                {
                    var preparedWrite = preparedWrites[nextIndex++];
                    PrepareTerrainColliderBlob(preparedWrite, cellMeters, scratch);

                    if ((preparedWrite.Flags & CacheFormat.CellFlagHasStaticCollision) != 0)
                    {
                        pendingStaticJobs.Add(ScheduleStaticColliderBlobJob(preparedWrite));
                    }
                    else
                    {
                        claimed++;
                        completed++;
                        info.LastCompletedCellKey = preparedWrite.Key;
                    }
                }

                for (int i = pendingStaticJobs.Count - 1; i >= 0; i--)
                {
                    var pending = pendingStaticJobs[i];
                    if (!pending.Handle.IsCompleted)
                        continue;

                    FinalizeStaticColliderBlobJob(pending);
                    pendingStaticJobs.RemoveAt(i);
                    claimed++;
                    completed++;
                    info.LastCompletedCellKey = pending.PreparedWrite.Key;
                }

                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
                yield return null;
            }

            for (int i = 0; i < pendingStaticJobs.Count; i++)
                pendingStaticJobs[i].Dispose();

            if (pendingStaticJobs.Count == 0 && nextIndex > 0)
            {
                info.ClaimedCount = preparedWrites.Length;
                info.CompletedCount = preparedWrites.Length;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            }

        }


        private static void PrepareTerrainColliderBlob(
            PreparedCellWriteData preparedWrite,
            float cellMeters,
            CellWriteScratch scratch)
        {
            if ((preparedWrite.Flags & CacheFormat.CellFlagHasTerrain) == 0)
                return;

            try
            {
                preparedWrite.BlobData ??= new BuiltCellBlobData();

                const int n = LandRecord.Size;
                for (int i = 0; i < n * n; i++)
                    scratch.TerrainHeights[i] = preparedWrite.Land.Heights[i] * WorldScale.MwUnitsToMeters;

                float spacing = cellMeters / (n - 1);
                var terrainScale = new Unity.Mathematics.float3(spacing, 1f, spacing);
                var terrainBlob = Unity.Physics.TerrainCollider.Create(
                    scratch.TerrainHeights,
                    new Unity.Mathematics.int2(n, n),
                    terrainScale,
                    Unity.Physics.TerrainCollider.CollisionMethod.VertexSamples);
                try
                {
                    preparedWrite.BlobData.TerrainColliderBlobBytes = SerializeCellBlob(terrainBlob);
                }
                finally
                {
                    if (terrainBlob.IsCreated)
                        terrainBlob.Dispose();
                }
            }
            finally
            {
            }
        }


        private static PendingStaticColliderJob ScheduleStaticColliderBlobJob(PreparedCellWriteData preparedWrite)
        {
            try
            {
                int vertexCount = preparedWrite.StaticCollision.Vertices.Length;
                int triangleCount = preparedWrite.StaticCollision.Indices.Length / 3;

                var points = new Unity.Collections.NativeArray<Unity.Mathematics.float3>(vertexCount, Unity.Collections.Allocator.Persistent);
                var triangles = new Unity.Collections.NativeArray<Unity.Mathematics.int3>(triangleCount, Unity.Collections.Allocator.Persistent);
                var output = new Unity.Collections.NativeArray<Unity.Entities.BlobAssetReference<Unity.Physics.Collider>>(1, Unity.Collections.Allocator.Persistent);

                for (int i = 0; i < vertexCount; i++)
                {
                    var vertex = preparedWrite.StaticCollision.Vertices[i];
                    points[i] = new Unity.Mathematics.float3(vertex.x, vertex.y, vertex.z);
                }

                for (int t = 0; t < triangleCount; t++)
                {
                    triangles[t] = new Unity.Mathematics.int3(
                        preparedWrite.StaticCollision.Indices[t * 3 + 0],
                        preparedWrite.StaticCollision.Indices[t * 3 + 1],
                        preparedWrite.StaticCollision.Indices[t * 3 + 2]);
                }

                var pending = new PendingStaticColliderJob
                {
                    PreparedWrite = preparedWrite,
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                };

                pending.Handle = new CreateStaticColliderBlobJob
                {
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                }.Schedule();

                Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                return pending;
            }
            finally
            {
            }
        }


        private static void FinalizeStaticColliderBlobJob(PendingStaticColliderJob pending)
        {
            pending.Handle.Complete();
            try
            {
                pending.PreparedWrite.BlobData ??= new BuiltCellBlobData();
                pending.PreparedWrite.BlobData.StaticCollisionBlobBytes = SerializeCellBlob(pending.Output[0]);
            }
            finally
            {
                pending.Dispose();
            }
        }


        private static byte[] SerializeCellBlob<T>(Unity.Entities.BlobAssetReference<T> blob)
            where T : unmanaged
        {
            try
            {
                return BlobStreamIO.SerializeBlob(blob, CacheFormat.PhysicsBlobVersion);
            }
            finally
            {
            }
        }


        private static IEnumerator FlushCellSectionsIncremental(
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress)
        {
            var info = new CellWriteProgressInfo { Subphase = "Flushing cell sections" };
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;

            int completed = 0;
            string lastCompletedKey = null;

            for (int index = 0; index < preparedWrites.Length; index++)
            {
                info.ClaimedCount = index + 1;
                info.CompletedCount = completed;
                info.LastCompletedCellKey = lastCompletedKey;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
                FlushCellSection(preparedWrites[index]);
                lastCompletedKey = preparedWrites[index].Key;
                completed++;
                if ((index & 3) == 3)
                    yield return null;
            }

            info.ClaimedCount = preparedWrites.Length;
            info.CompletedCount = preparedWrites.Length;
            info.LastCompletedCellKey = lastCompletedKey;
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;
        }


        private static void FlushCellSection(PreparedCellWriteData preparedWrite)
        {
            WriteRuntimeCellSection(preparedWrite);
        }


        private static void UpdateCellWriteProgress(BakeProgress progress, int total, CellWriteProgressInfo info)
        {
            progress.Stage = "Cell Writes";
            progress.Total = total;
            progress.Current = Math.Min(total, info.ClaimedCount);
            progress.Label = total == 0
                ? $"No cells for {info.Subphase?.ToLowerInvariant() ?? "cell writes"}"
                : $"{info.Subphase} {info.CompletedCount}/{total}";
        }


        private static byte[] BuildTerrainHeightBytes(LandRecord land)
        {
            const int n = LandRecord.Size;
            var bytes = new byte[n * n * sizeof(float)];
            int offset = 0;
            for (int i = 0; i < n * n; i++)
            {
                float height = land.Heights[i] * WorldScale.MwUnitsToMeters;
                WriteSingle(bytes, ref offset, height);
            }
            return bytes;
        }


        private static Dictionary<string, ContentReference> BuildGameplayContentLookup(GameplayContentData gameplayContent)
        {
            return GameplayContentReferenceIndex.BuildPlaceableIndex(gameplayContent);
        }


        private static ContentReference ResolveGameplayContentReference(Dictionary<string, ContentReference> lookup, string baseId)
        {
            return GameplayContentReferenceIndex.TryResolvePlaceable(lookup, baseId, out var contentReference)
                ? contentReference
                : default;
        }


        private static byte[] BuildTerrainNormalBytes(LandRecord land)
        {
            var bytes = new byte[land.Normals.Length];
            Buffer.BlockCopy(land.Normals, 0, bytes, 0, bytes.Length);
            return bytes;
        }


        private static byte[] BuildLayerGridBytes(ushort[] layerGrid)
        {
            var bytes = new byte[layerGrid.Length * sizeof(ushort)];
            int offset = 0;
            for (int i = 0; i < layerGrid.Length; i++)
                WriteUInt16(bytes, ref offset, layerGrid[i]);
            return bytes;
        }


        private static byte[] BuildWorldMapBytes(LandRecord land)
        {
            var bytes = new byte[land.WorldMap.Length];
            for (int i = 0; i < land.WorldMap.Length; i++)
                bytes[i] = unchecked((byte)land.WorldMap[i]);
            return bytes;
        }


        private static byte[] BuildRefBytes(IReadOnlyList<CellBakery.BakedRef> refs)
        {
            int count = refs?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream(count * 72);
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var r = refs[i];
                w.Write(r.SpawnModeRaw);
                w.Write(r.ModelPrefabIndex);
                w.Write(r.LocalMeshIndex);
                w.Write(r.LocalMaterialIndex);
                w.Write(r.SliceIndex);
                w.Write(r.CollisionIndex);
                w.Write(r.PlacedRefId);
                w.Write(r.DoorMetaIndex);
                w.Write(r.ContentHandleValue);
                w.Write(r.ContentKind);
                w.Write(r.PositionUnity.x);
                w.Write(r.PositionUnity.y);
                w.Write(r.PositionUnity.z);
                w.Write(r.RotationUnity.x);
                w.Write(r.RotationUnity.y);
                w.Write(r.RotationUnity.z);
                w.Write(r.RotationUnity.w);
                w.Write(r.Scale);
            }
            return ms.ToArray();
        }


        private static byte[] BuildDoorBytes(IReadOnlyList<DoorRefEntry> doors)
        {
            int count = doors?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var door = doors[i];
                w.Write(door.PlacedRefId);
                w.Write(door.Flags);
                w.Write(door.DestPosX);
                w.Write(door.DestPosY);
                w.Write(door.DestPosZ);
                w.Write(door.DestRotX);
                w.Write(door.DestRotY);
                w.Write(door.DestRotZ);
                w.Write(door.DestRotW);
                w.Write(door.DestinationCellId ?? string.Empty);
            }
            return ms.ToArray();
        }

        private static byte[] BuildCapturedSoulBytes(IReadOnlyList<PlacedRefSoulEntry> entries)
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                w.Write(entry.PlacedRefId);
                w.Write(entry.SoulId ?? string.Empty);
            }

            return ms.ToArray();
        }

        private static byte[] BuildLockStateBytes(IReadOnlyList<PlacedRefLockEntry> entries)
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                w.Write(entry.PlacedRefId);
                w.Write(entry.LockLevel);
                w.Write(entry.Locked);
                w.Write(entry.KeyId ?? string.Empty);
                w.Write(entry.TrapId ?? string.Empty);
            }

            return ms.ToArray();
        }

        private static byte[] BuildCombinedRenderChunkBytes(IReadOnlyList<CombinedCellRenderChunkDef> chunks)
        {
            int count = chunks?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var chunk = chunks[i] ?? throw new InvalidDataException($"Combined render chunk {i} is null.");
                var vertexBytes = chunk.VertexBytes ?? Array.Empty<byte>();
                var indexBytes = chunk.IndexBytes ?? Array.Empty<byte>();
                var members = chunk.Members ?? Array.Empty<CombinedCellRenderChunkMemberDef>();

                w.Write(chunk.TileX);
                w.Write(chunk.TileY);
                w.Write(chunk.MaterialIndex);
                w.Write(chunk.TextureBucketKey);
                w.Write(chunk.BoundsCenterX);
                w.Write(chunk.BoundsCenterY);
                w.Write(chunk.BoundsCenterZ);
                w.Write(chunk.BoundsExtentsX);
                w.Write(chunk.BoundsExtentsY);
                w.Write(chunk.BoundsExtentsZ);
                w.Write((uint)chunk.VertexCount);
                w.Write((uint)chunk.IndexCount);
                w.Write(chunk.MeshFlags);
                w.Write((uint)vertexBytes.Length);
                w.Write((uint)indexBytes.Length);
                w.Write((uint)members.Length);
                w.Write(vertexBytes);
                w.Write(indexBytes);
                for (int m = 0; m < members.Length; m++)
                {
                    var member = members[m] ?? throw new InvalidDataException($"Combined render chunk {i} member {m} is null.");
                    w.Write(member.PlacedRefId);
                    w.Write(member.NodeIndex);
                }
            }

            return ms.ToArray();
        }


        private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }


        private static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            WriteUInt32(buffer, ref offset, unchecked((uint)value));
        }


        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }


        private static void WriteSingle(byte[] buffer, ref int offset, float value)
        {
            WriteUInt32(buffer, ref offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }


        private static BakeManifest.BakedCellState BuildCellState(StagedCellData staged)
        {
            if (staged.BakedRefs == null && staged.PreviousState != null)
            {
                return new BakeManifest.BakedCellState
                {
                    Key = staged.WorkItem.Key,
                    SectionPath = BuildCellSectionPath(staged.WorkItem),
                    Fingerprint = staged.Fingerprint,
                    PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                    IsInterior = staged.WorkItem.IsInterior,
                    GridX = staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.Cell.GridY,
                    InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                    MeshIndices = staged.PreviousState.MeshIndices ?? Array.Empty<int>(),
                    MaterialIndices = staged.PreviousState.MaterialIndices ?? Array.Empty<int>(),
                    TextureIndices = staged.PreviousState.TextureIndices ?? Array.Empty<int>(),
                    CollisionIndices = staged.PreviousState.CollisionIndices ?? Array.Empty<int>(),
                    TerrainLayerIndices = staged.PreviousState.TerrainLayerIndices ?? Array.Empty<int>(),
                };
            }

            return new BakeManifest.BakedCellState
            {
                Key = staged.WorkItem.Key,
                SectionPath = BuildCellSectionPath(staged.WorkItem),
                Fingerprint = staged.Fingerprint,
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                IsInterior = staged.WorkItem.IsInterior,
                GridX = staged.WorkItem.Cell.GridX,
                GridY = staged.WorkItem.Cell.GridY,
                InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                MeshIndices = staged.GlobalMeshIndices ?? Array.Empty<int>(),
                MaterialIndices = staged.GlobalMaterialIndices ?? Array.Empty<int>(),
                TextureIndices = staged.GlobalTextureIndices ?? Array.Empty<int>(),
                CollisionIndices = staged.GlobalCollisionIndices ?? Array.Empty<int>(),
                TerrainLayerIndices = staged.GlobalTerrainLayerIndices ?? Array.Empty<int>(),
            };
        }


        private static string BuildCellSectionPath(CellBakeWorkItem workItem)
        {
            return workItem.IsInterior
                ? CachePaths.InteriorCellSectionFile(workItem.Cell.Name ?? string.Empty)
                : CachePaths.ExteriorCellSectionFile(workItem.Cell.GridX, workItem.Cell.GridY);
        }


        private static int[] ToSortedArray(HashSet<int> set)
        {
            var values = new int[set.Count];
            set.CopyTo(values);
            Array.Sort(values);
            return values;
        }


        private static string ComputeFingerprint(
            CellBakeWorkItem workItem,
            List<CellReference> refs,
            LandRecord land,
            RecordIndex recordIndex,
            Dictionary<string, Dictionary<int, string>> ltexMapsBySource,
            bool bakeCombinedCellRenderChunks)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(workItem.IsInterior);
                w.Write(workItem.Cell.Name ?? string.Empty);
                w.Write(workItem.Cell.GridX);
                w.Write(workItem.Cell.GridY);
                w.Write(workItem.Cell.Environment.HasMood);
                w.Write(workItem.Cell.Environment.HasWater);
                w.Write(workItem.Cell.Environment.AmbientColorRgba);
                w.Write(workItem.Cell.Environment.DirectionalColorRgba);
                w.Write(workItem.Cell.Environment.FogColorRgba);
                w.Write(workItem.Cell.Environment.FogDensity);
                w.Write(workItem.Cell.Environment.WaterHeight);
                w.Write(workItem.Cell.Environment.RegionId ?? string.Empty);
                w.Write(bakeCombinedCellRenderChunks);
                w.Write(refs.Count);
                for (int i = 0; i < refs.Count; i++)
                {
                    var reference = refs[i];
                    w.Write(reference.FormId);
                    w.Write(reference.BaseId ?? string.Empty);
                    w.Write(reference.PosX);
                    w.Write(reference.PosY);
                    w.Write(reference.PosZ);
                    w.Write(reference.RotX);
                    w.Write(reference.RotY);
                    w.Write(reference.RotZ);
                    w.Write(reference.Scale);
                    w.Write(reference.Deleted);
                    w.Write(reference.IsDoor);
                    w.Write(reference.DoorDestCell ?? string.Empty);
                    w.Write(reference.DoorDestX);
                    w.Write(reference.DoorDestY);
                    w.Write(reference.DoorDestZ);
                    w.Write(reference.DoorDestRotX);
                    w.Write(reference.DoorDestRotY);
                    w.Write(reference.DoorDestRotZ);
                    w.Write(reference.SoulId ?? string.Empty);
                    if (recordIndex.TryGet(reference.BaseId, out var rec))
                    {
                        w.Write(rec.Tag);
                        w.Write(rec.Model ?? string.Empty);
                    }
                    else
                    {
                        w.Write(0u);
                        w.Write(string.Empty);
                    }
                }

                bool hasLand = land != null && land.HasHeights;
                w.Write(hasLand);
                if (hasLand)
                {
                    w.Write(land.GridX);
                    w.Write(land.GridY);
                    w.Write(land.Heights.Length);
                    for (int i = 0; i < land.Heights.Length; i++)
                        w.Write(land.Heights[i]);

                    bool hasNormals = land.Normals != null;
                    w.Write(hasNormals);
                    if (hasNormals)
                    {
                        w.Write(land.Normals.Length);
                        for (int i = 0; i < land.Normals.Length; i++)
                            w.Write(land.Normals[i]);
                    }

                    bool hasVtex = land.VtexIndices != null;
                    w.Write(hasVtex);
                    if (hasVtex)
                    {
                        w.Write(land.VtexIndices.Length);
                        for (int i = 0; i < land.VtexIndices.Length; i++)
                        {
                            ushort vtex = land.VtexIndices[i];
                            w.Write(vtex);
                            w.Write(LtexIndex.ResolveVtexRequired(
                                vtex,
                                ltexMapsBySource,
                                workItem.LandSourcePath,
                                $"{workItem.Key} terrain VTEX slot {i}") ?? string.Empty);
                        }
                    }

                    bool hasWorldMap = land.WorldMap != null;
                    w.Write(hasWorldMap);
                    if (hasWorldMap)
                    {
                        w.Write(land.WorldMap.Length);
                        for (int i = 0; i < land.WorldMap.Length; i++)
                            w.Write(land.WorldMap[i]);
                    }
                }
            }

            ms.Position = 0;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(ms.ToArray());
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }


        private static void AppendTransformed(
            CollisionPayload src,
            Vector3 pos,
            Quaternion rot,
            float scale,
            Vector3 cellOrigin,
            List<Vector3> outVerts,
            List<int> outIndices)
        {
            int baseVert = outVerts.Count;
            for (int i = 0; i < src.Vertices.Length; i++)
                outVerts.Add(pos + rot * (src.Vertices[i] * scale) - cellOrigin);

            for (int i = 0; i < src.Indices.Length; i++)
                outIndices.Add(baseVert + src.Indices[i]);
        }


        }
    }
