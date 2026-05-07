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
                        AssembleFinalCellWriteBuffer(preparedWrite);
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
                AssembleFinalCellWriteBuffer(pending.PreparedWrite);
            }
            finally
            {
                pending.Dispose();
            }
        }


        private static void AssembleFinalCellWriteBuffer(PreparedCellWriteData preparedWrite)
        {
            try
            {
                preparedWrite.FinalBuffer = BuildFinalCellWriteBuffer(preparedWrite);
            }
            finally
            {
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


        private static FinalCellWriteBuffer BuildFinalCellWriteBuffer(PreparedCellWriteData preparedWrite)
        {
            return new FinalCellWriteBuffer
            {
                HeaderBytes = BuildCellHeaderBytes(preparedWrite),
                TerrainHeightBytes = preparedWrite.TerrainHeightBytes,
                TerrainNormalBytes = preparedWrite.TerrainNormalBytes,
                TerrainColliderChunkBytes = ((preparedWrite.Flags & CacheFormat.CellFlagHasTerrain) != 0)
                    ? BuildLengthPrefixedBytes(preparedWrite.BlobData?.TerrainColliderBlobBytes)
                    : null,
                LayerGridBytes = preparedWrite.LayerGridBytes,
                WorldMapBytes = preparedWrite.WorldMapBytes,
                StaticCollisionChunkBytes = ((preparedWrite.Flags & CacheFormat.CellFlagHasStaticCollision) != 0)
                    ? BuildLengthPrefixedBytes(preparedWrite.BlobData?.StaticCollisionBlobBytes)
                    : null,
                RefCountBytes = BuildUInt32Bytes((uint)preparedWrite.RefCount),
                RefBytes = preparedWrite.RefBytes,
                DoorCountBytes = BuildUInt32Bytes((uint)preparedWrite.DoorCount),
                DoorBytes = preparedWrite.DoorBytes,
                CapturedSoulCountBytes = BuildUInt32Bytes((uint)preparedWrite.CapturedSoulCount),
                CapturedSoulBytes = preparedWrite.CapturedSoulBytes,
                LockStateCountBytes = BuildUInt32Bytes((uint)preparedWrite.LockStateCount),
                LockStateBytes = preparedWrite.LockStateBytes,
                CombinedRenderChunkCountBytes = BuildUInt32Bytes((uint)preparedWrite.CombinedRenderChunkCount),
                CombinedRenderChunkBytes = preparedWrite.CombinedRenderChunkBytes,
            };
        }


        private static IEnumerator FlushCellFilesIncremental(
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress)
        {
            var info = new CellWriteProgressInfo { Subphase = "Flushing cell files" };
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;

            int claimed = 0;
            int completed = 0;
            string lastCompletedKey = null;
            Exception flushFailure = null;
            int maxWriters = Math.Max(1, Math.Min(Environment.ProcessorCount, preparedWrites.Length));

            var flushTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWriters];
                for (int worker = 0; worker < maxWriters; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= preparedWrites.Length)
                                break;

                            Interlocked.Increment(ref claimed);
                            try
                            {
                                FlushCellFile(preparedWrites[index]);
                                lastCompletedKey = preparedWrites[index].Key;
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    flushFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!flushTask.IsCompleted)
            {
                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                info.LastCompletedCellKey = lastCompletedKey;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
                yield return null;
            }

            if (flushFailure != null)
                throw flushFailure;

            info.ClaimedCount = preparedWrites.Length;
            info.CompletedCount = preparedWrites.Length;
            info.LastCompletedCellKey = lastCompletedKey;
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;
        }


        private static void FlushCellFile(PreparedCellWriteData preparedWrite)
        {
            try
            {
                using var fs = File.Create(preparedWrite.OutputPath);
                WriteSegment(fs, preparedWrite.FinalBuffer.HeaderBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainHeightBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainNormalBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainColliderChunkBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.LayerGridBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.WorldMapBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.StaticCollisionChunkBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.CapturedSoulCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.CapturedSoulBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.LockStateCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.LockStateBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.CombinedRenderChunkCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.CombinedRenderChunkBytes);

                fs.Flush(flushToDisk: true);

            }
            finally
            {
            }

            if (!TryValidateCellFile(preparedWrite.OutputPath, preparedWrite.IsInterior, preparedWrite.CellId, out string validationError))
            {
                throw new InvalidDataException(
                    $"Wrote invalid cell file '{preparedWrite.OutputPath}' for '{preparedWrite.Key}': {validationError}");
            }
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


        private static byte[] BuildCellHeaderBytes(PreparedCellWriteData preparedWrite)
        {
            using var ms = new MemoryStream(64);
            using var w = new BinaryWriter(ms);
            w.Write(CellBakery.MagicCell);
            w.Write(preparedWrite.GridX);
            w.Write(preparedWrite.GridY);
            w.Write(preparedWrite.Flags);
            if ((preparedWrite.Flags & CacheFormat.CellFlagHasEnvironment) != 0)
            {
                var environment = preparedWrite.Environment;
                w.Write(environment.HasMood);
                w.Write(environment.HasWater);
                w.Write(environment.AmbientColorRgba);
                w.Write(environment.DirectionalColorRgba);
                w.Write(environment.FogColorRgba);
                w.Write(environment.FogDensity);
                w.Write(environment.WaterHeight);
                w.Write(environment.RegionId ?? string.Empty);
            }
            return ms.ToArray();
        }


        private static byte[] BuildLengthPrefixedBytes(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return BuildInt32Bytes(0);

            var bytes = new byte[sizeof(int) + payload.Length];
            int offset = 0;
            WriteInt32(bytes, ref offset, payload.Length);
            Buffer.BlockCopy(payload, 0, bytes, offset, payload.Length);
            return bytes;
        }


        private static byte[] BuildUInt32Bytes(uint value)
        {
            var bytes = new byte[sizeof(uint)];
            int offset = 0;
            WriteUInt32(bytes, ref offset, value);
            return bytes;
        }


        private static byte[] BuildInt32Bytes(int value)
        {
            var bytes = new byte[sizeof(int)];
            int offset = 0;
            WriteInt32(bytes, ref offset, value);
            return bytes;
        }


        private static void WriteSegment(Stream stream, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;
            stream.Write(bytes, 0, bytes.Length);
        }


        private static bool TryValidateCellFile(string path, bool isInterior, string cellId, out string error)
        {
            error = null;

            try
            {
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);

                if (r.ReadUInt32() != CellBakery.MagicCell)
                {
                    error = "bad cell magic";
                    return false;
                }

                r.ReadInt32();
                r.ReadInt32();
                uint flags = r.ReadUInt32();

                bool hasTerrain = (flags & CacheFormat.CellFlagHasTerrain) != 0;
                bool hasNormals = (flags & CacheFormat.CellFlagHasNormals) != 0;
                bool hasVtex = (flags & CacheFormat.CellFlagHasVtex) != 0;
                bool hasStaticCollision = (flags & CacheFormat.CellFlagHasStaticCollision) != 0;
                bool hasEnvironment = (flags & CacheFormat.CellFlagHasEnvironment) != 0;
                bool hasWorldMap = (flags & CacheFormat.CellFlagHasWorldMap) != 0;

                if (hasEnvironment)
                {
                    SkipExact(r, 2L, "cell environment flags");
                    SkipExact(r, 4L * 3L, "cell environment colors");
                    SkipExact(r, sizeof(float) * 2L, "cell environment scalars");
                    r.ReadString();
                }

                if (hasTerrain)
                {
                    SkipExact(r, 65L * 65L * sizeof(float), "terrain heights");
                    if (hasNormals)
                        SkipExact(r, 3L * 65L * 65L, "terrain normals");

                    var terrainBlob = BlobStreamIO.ReadLengthPrefixed<Unity.Physics.Collider>(
                        r,
                        CacheFormat.PhysicsBlobVersion,
                        $"cell file '{path}' terrain collider");
                    if (terrainBlob.IsCreated)
                        terrainBlob.Dispose();

                    if (hasVtex)
                        SkipExact(r, 16L * 16L * sizeof(ushort), "layer grid");
                    if (hasWorldMap)
                        SkipExact(r, 81L, "world map");
                }

                if (hasStaticCollision)
                {
                    var staticBlob = BlobStreamIO.ReadLengthPrefixed<Unity.Physics.Collider>(
                        r,
                        CacheFormat.PhysicsBlobVersion,
                        $"cell file '{path}' static collider");
                    if (staticBlob.IsCreated)
                        staticBlob.Dispose();
                }

                uint refCount = r.ReadUInt32();
                SkipExact(r, checked((long)refCount * 72L), "ref table");

                uint doorCount = r.ReadUInt32();
                for (int i = 0; i < doorCount; i++)
                {
                    SkipExact(r, 36L, "door table entry");
                    r.ReadString();
                }

                uint capturedSoulCount = r.ReadUInt32();
                for (int i = 0; i < capturedSoulCount; i++)
                {
                    SkipExact(r, sizeof(uint), "captured soul table entry placed ref id");
                    r.ReadString();
                }

                uint lockStateCount = r.ReadUInt32();
                for (int i = 0; i < lockStateCount; i++)
                {
                    SkipExact(r, sizeof(uint) + sizeof(int) + sizeof(byte), "lock state table entry fixed fields");
                    r.ReadString();
                    r.ReadString();
                }

                uint combinedRenderChunkCount = r.ReadUInt32();
                for (int i = 0; i < combinedRenderChunkCount; i++)
                {
                    SkipExact(r, sizeof(int) * 4L + sizeof(float) * 6L + sizeof(uint) * 3L, "combined render chunk fixed fields");
                    uint vertexByteCount = r.ReadUInt32();
                    uint indexByteCount = r.ReadUInt32();
                    uint memberCount = r.ReadUInt32();
                    SkipExact(
                        r,
                        checked((long)vertexByteCount + indexByteCount + (long)memberCount * (sizeof(uint) + sizeof(int))),
                        "combined render chunk payload");
                }

                if (fs.Position != fs.Length)
                {
                    error = $"unexpected trailing data at offset {fs.Position}/{fs.Length}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                string cellLabel = isInterior
                    ? $"interior '{cellId ?? string.Empty}'"
                    : $"cell file '{path}'";
                error = $"{cellLabel}: {ex.Message}";
                return false;
            }
        }


        private static void SkipExact(BinaryReader reader, long byteCount, string section)
        {
            if (byteCount < 0)
                throw new InvalidDataException($"Negative byte count while validating {section}.");

            var stream = reader.BaseStream;
            long remaining = stream.Length - stream.Position;
            if (remaining < byteCount)
            {
                throw new InvalidDataException(
                    $"Truncated {section}: expected {byteCount} bytes, only {remaining} remain.");
            }

            stream.Seek(byteCount, SeekOrigin.Current);
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
                    OutputPath = staged.WorkItem.OutputPath,
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
                OutputPath = staged.WorkItem.OutputPath,
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
            Dictionary<int, string> ltexMap,
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
                            w.Write(LtexIndex.ResolveVtex(vtex, ltexMap) ?? string.Empty);
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
