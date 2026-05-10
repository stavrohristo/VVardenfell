using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;

namespace VVardenfell.Importer.Bake
{
    internal static partial class WorldBakeService
    {
        private static void WriteRuntimeCellSection(PreparedCellWriteData preparedWrite)
        {
            string path = preparedWrite.IsInterior
                ? CachePaths.InteriorCellSectionFile(preparedWrite.CellId)
                : CachePaths.ExteriorCellSectionFile(preparedWrite.GridX, preparedWrite.GridY);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

            BlobAssetReference<Collider> terrainCollider = default;
            BlobAssetReference<Collider> staticCollider = default;
            bool terrainColliderCreated = false;
            bool staticColliderCreated = false;

            try
            {
                terrainColliderCreated = TryReadCellBlob(
                    preparedWrite.BlobData?.TerrainColliderBlobBytes,
                    $"{preparedWrite.Key} terrain collider",
                    out terrainCollider);
                staticColliderCreated = TryReadCellBlob(
                    preparedWrite.BlobData?.StaticCollisionBlobBytes,
                    $"{preparedWrite.Key} static collider",
                    out staticCollider);

                using var sectionWorld = new World($"VV.CellSectionBake({preparedWrite.Key})");
                var em = sectionWorld.EntityManager;
                Entity entity = CreateSectionEntity(em, terrainColliderCreated, staticColliderCreated);
                em.SetComponentData(entity, new RuntimeCellSectionHeader
                {
                    PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                    Flags = preparedWrite.Flags,
                    GridX = preparedWrite.GridX,
                    GridY = preparedWrite.GridY,
                    IsInterior = (byte)(preparedWrite.IsInterior ? 1 : 0),
                    CellId = new FixedString128Bytes(preparedWrite.CellId ?? string.Empty),
                    InteriorCellHash = preparedWrite.IsInterior ? RuntimeContentStableHash.HashInteriorCellId(preparedWrite.CellId) : 0UL,
                    Environment = new CellEnvironmentDataBlob
                    {
                        HasMood = preparedWrite.Environment.HasMood,
                        HasWater = preparedWrite.Environment.HasWater,
                        AmbientColorRgba = preparedWrite.Environment.AmbientColorRgba,
                        DirectionalColorRgba = preparedWrite.Environment.DirectionalColorRgba,
                        FogColorRgba = preparedWrite.Environment.FogColorRgba,
                        FogDensity = preparedWrite.Environment.FogDensity,
                        WaterHeight = preparedWrite.Environment.WaterHeight,
                        RegionId = new FixedString128Bytes(preparedWrite.Environment.RegionId ?? string.Empty),
                    },
                });

                if (terrainColliderCreated)
                    em.SetComponentData(entity, new RuntimeCellSectionTerrainCollider { Blob = terrainCollider });
                if (staticColliderCreated)
                    em.SetComponentData(entity, new RuntimeCellSectionStaticCollider { Blob = staticCollider });

                WriteTerrainBuffers(em, entity, preparedWrite);
                WriteRefBuffers(em, entity, preparedWrite);
                WriteCombinedChunkBuffers(em, entity, preparedWrite);

                using (var writer = new MemoryBinaryWriter(em))
                {
                    SerializeUtility.SerializeWorld(em, writer);
                    var bytes = new byte[writer.Length];
                    unsafe
                    {
                        Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
                    }
                    File.WriteAllBytes(path, bytes);
                }
            }
            finally
            {
                if (terrainColliderCreated && terrainCollider.IsCreated)
                    terrainCollider.Dispose();
                if (staticColliderCreated && staticCollider.IsCreated)
                    staticCollider.Dispose();
            }

            if (!TryValidateRuntimeCellSection(path, preparedWrite, out string error))
                throw new InvalidDataException($"Wrote invalid DOTS cell section '{path}' for '{preparedWrite.Key}': {error}");
        }

        static Entity CreateSectionEntity(EntityManager em, bool hasTerrainCollider, bool hasStaticCollider)
        {
            int componentCount = 13 + (hasTerrainCollider ? 1 : 0) + (hasStaticCollider ? 1 : 0);
            var components = new ComponentType[componentCount];
            int i = 0;
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionHeader>();
            if (hasTerrainCollider)
                components[i++] = ComponentType.ReadWrite<RuntimeCellSectionTerrainCollider>();
            if (hasStaticCollider)
                components[i++] = ComponentType.ReadWrite<RuntimeCellSectionStaticCollider>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionTerrainHeight>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionTerrainNormal>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionTerrainLayer>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionWorldMapSample>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionRef>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionDoor>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionCapturedSoul>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionLockState>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionCombinedChunk>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionCombinedVertexByte>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionCombinedIndexByte>();
            components[i++] = ComponentType.ReadWrite<RuntimeCellSectionCombinedMember>();
            var archetype = em.CreateArchetype(components);
            return em.CreateEntity(archetype);
        }

        static bool TryReadCellBlob(byte[] bytes, string context, out BlobAssetReference<Collider> blob)
        {
            blob = default;
            if (bytes == null || bytes.Length == 0)
                return false;

            if (!BlobStreamIO.TryDeserializeBlob(bytes, CacheFormat.PhysicsBlobVersion, out blob))
                throw new InvalidDataException($"{context} blob version mismatch. Expected {CacheFormat.PhysicsBlobVersion}.");

            return blob.IsCreated;
        }

        static void WriteTerrainBuffers(EntityManager em, Entity entity, PreparedCellWriteData preparedWrite)
        {
            if ((preparedWrite.Flags & CacheFormat.CellFlagHasTerrain) == 0)
                return;

            var heights = em.GetBuffer<RuntimeCellSectionTerrainHeight>(entity);
            AppendFloats(heights, preparedWrite.TerrainHeightBytes, LandRecord.Size * LandRecord.Size, "terrain heights");

            if ((preparedWrite.Flags & CacheFormat.CellFlagHasNormals) != 0)
            {
                var normals = em.GetBuffer<RuntimeCellSectionTerrainNormal>(entity);
                AppendSBytes(normals, preparedWrite.TerrainNormalBytes, 3 * LandRecord.Size * LandRecord.Size, "terrain normals");
            }

            if ((preparedWrite.Flags & CacheFormat.CellFlagHasVtex) != 0)
            {
                var layers = em.GetBuffer<RuntimeCellSectionTerrainLayer>(entity);
                AppendUInt16(layers, preparedWrite.LayerGridBytes, LandRecord.NumTextures, "terrain layer grid");
            }

            if ((preparedWrite.Flags & CacheFormat.CellFlagHasWorldMap) != 0)
            {
                var worldMap = em.GetBuffer<RuntimeCellSectionWorldMapSample>(entity);
                AppendSBytes(worldMap, preparedWrite.WorldMapBytes, 81, "world map");
            }
        }

        static void WriteRefBuffers(EntityManager em, Entity entity, PreparedCellWriteData preparedWrite)
        {
            var refs = em.GetBuffer<RuntimeCellSectionRef>(entity);
            refs.ResizeUninitialized(preparedWrite.RefCount);
            using (var reader = NewReader(preparedWrite.RefBytes))
            {
                for (int i = 0; i < preparedWrite.RefCount; i++)
                    refs[i] = new RuntimeCellSectionRef { Value = ReadRefEntry(reader) };
            }

            var doors = em.GetBuffer<RuntimeCellSectionDoor>(entity);
            doors.ResizeUninitialized(preparedWrite.DoorCount);
            using (var reader = NewReader(preparedWrite.DoorBytes))
            {
                for (int i = 0; i < preparedWrite.DoorCount; i++)
                    doors[i] = ReadDoor(reader);
            }

            var souls = em.GetBuffer<RuntimeCellSectionCapturedSoul>(entity);
            souls.ResizeUninitialized(preparedWrite.CapturedSoulCount);
            using (var reader = NewReader(preparedWrite.CapturedSoulBytes))
            {
                for (int i = 0; i < preparedWrite.CapturedSoulCount; i++)
                {
                    souls[i] = new RuntimeCellSectionCapturedSoul
                    {
                        PlacedRefId = reader.ReadUInt32(),
                        SoulId = new FixedString64Bytes(reader.ReadString()),
                    };
                }
            }

            var locks = em.GetBuffer<RuntimeCellSectionLockState>(entity);
            locks.ResizeUninitialized(preparedWrite.LockStateCount);
            using (var reader = NewReader(preparedWrite.LockStateBytes))
            {
                for (int i = 0; i < preparedWrite.LockStateCount; i++)
                {
                    locks[i] = new RuntimeCellSectionLockState
                    {
                        PlacedRefId = reader.ReadUInt32(),
                        LockLevel = reader.ReadInt32(),
                        Locked = reader.ReadByte(),
                        KeyId = new FixedString64Bytes(reader.ReadString()),
                        TrapId = new FixedString64Bytes(reader.ReadString()),
                    };
                }
            }
        }

        static void WriteCombinedChunkBuffers(EntityManager em, Entity entity, PreparedCellWriteData preparedWrite)
        {
            var chunks = em.GetBuffer<RuntimeCellSectionCombinedChunk>(entity);
            var vertexBytes = em.GetBuffer<RuntimeCellSectionCombinedVertexByte>(entity);
            var indexBytes = em.GetBuffer<RuntimeCellSectionCombinedIndexByte>(entity);
            var members = em.GetBuffer<RuntimeCellSectionCombinedMember>(entity);

            chunks.ResizeUninitialized(preparedWrite.CombinedRenderChunkCount);
            using var reader = NewReader(preparedWrite.CombinedRenderChunkBytes);
            for (int i = 0; i < preparedWrite.CombinedRenderChunkCount; i++)
            {
                var chunk = new RuntimeCellSectionCombinedChunk
                {
                    TileX = reader.ReadInt32(),
                    TileY = reader.ReadInt32(),
                    MaterialIndex = reader.ReadInt32(),
                    TextureBucketKey = reader.ReadInt32(),
                    BoundsCenter = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    BoundsExtents = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    VertexCount = checked((int)reader.ReadUInt32()),
                    IndexCount = checked((int)reader.ReadUInt32()),
                    MeshFlags = reader.ReadUInt32(),
                };

                uint vertexByteCount = reader.ReadUInt32();
                uint indexByteCount = reader.ReadUInt32();
                uint memberCount = reader.ReadUInt32();
                chunk.FirstVertexByte = vertexBytes.Length;
                chunk.VertexByteCount = checked((int)vertexByteCount);
                chunk.FirstIndexByte = indexBytes.Length;
                chunk.IndexByteCount = checked((int)indexByteCount);
                chunk.FirstMember = members.Length;
                chunk.MemberCount = checked((int)memberCount);

                AppendBytes(vertexBytes, reader.ReadBytes(chunk.VertexByteCount), "combined render vertex bytes");
                AppendBytes(indexBytes, reader.ReadBytes(chunk.IndexByteCount), "combined render index bytes");
                for (int m = 0; m < chunk.MemberCount; m++)
                {
                    members.Add(new RuntimeCellSectionCombinedMember
                    {
                        PlacedRefId = reader.ReadUInt32(),
                        NodeIndex = reader.ReadInt32(),
                    });
                }

                chunks[i] = chunk;
            }
        }

        static BinaryReader NewReader(byte[] bytes)
            => new BinaryReader(new MemoryStream(bytes ?? Array.Empty<byte>(), writable: false));

        static RefEntry ReadRefEntry(BinaryReader reader)
            => new RefEntry
            {
                SpawnModeRaw = reader.ReadInt32(),
                ModelPrefabIndex = reader.ReadInt32(),
                LocalMeshIndex = reader.ReadInt32(),
                LocalMaterialIndex = reader.ReadInt32(),
                SliceIndex = reader.ReadInt32(),
                CollisionIndex = reader.ReadInt32(),
                PlacedRefId = reader.ReadUInt32(),
                DoorMetaIndex = reader.ReadInt32(),
                ContentHandleValue = reader.ReadInt32(),
                ContentKind = reader.ReadInt32(),
                PosX = reader.ReadSingle(),
                PosY = reader.ReadSingle(),
                PosZ = reader.ReadSingle(),
                RotX = reader.ReadSingle(),
                RotY = reader.ReadSingle(),
                RotZ = reader.ReadSingle(),
                RotW = reader.ReadSingle(),
                Scale = reader.ReadSingle(),
            };

        static RuntimeCellSectionDoor ReadDoor(BinaryReader reader)
            => new RuntimeCellSectionDoor
            {
                PlacedRefId = reader.ReadUInt32(),
                Flags = reader.ReadUInt32(),
                DestinationPosition = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                DestinationRotation = new quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                DestinationCellId = new FixedString128Bytes(reader.ReadString()),
            };

        static void AppendFloats(DynamicBuffer<RuntimeCellSectionTerrainHeight> buffer, byte[] bytes, int expectedCount, string label)
        {
            if ((bytes?.Length ?? 0) != expectedCount * sizeof(float))
                throw new InvalidDataException($"{label} byte count mismatch.");
            buffer.ResizeUninitialized(expectedCount);
            for (int i = 0; i < expectedCount; i++)
                buffer[i] = new RuntimeCellSectionTerrainHeight { Value = BitConverter.ToSingle(bytes, i * sizeof(float)) };
        }

        static void AppendSBytes(DynamicBuffer<RuntimeCellSectionTerrainNormal> buffer, byte[] bytes, int expectedCount, string label)
        {
            if ((bytes?.Length ?? 0) != expectedCount)
                throw new InvalidDataException($"{label} byte count mismatch.");
            buffer.ResizeUninitialized(expectedCount);
            for (int i = 0; i < expectedCount; i++)
                buffer[i] = new RuntimeCellSectionTerrainNormal { Value = unchecked((sbyte)bytes[i]) };
        }

        static void AppendSBytes(DynamicBuffer<RuntimeCellSectionWorldMapSample> buffer, byte[] bytes, int expectedCount, string label)
        {
            if ((bytes?.Length ?? 0) != expectedCount)
                throw new InvalidDataException($"{label} byte count mismatch.");
            buffer.ResizeUninitialized(expectedCount);
            for (int i = 0; i < expectedCount; i++)
                buffer[i] = new RuntimeCellSectionWorldMapSample { Value = unchecked((sbyte)bytes[i]) };
        }

        static void AppendUInt16(DynamicBuffer<RuntimeCellSectionTerrainLayer> buffer, byte[] bytes, int expectedCount, string label)
        {
            if ((bytes?.Length ?? 0) != expectedCount * sizeof(ushort))
                throw new InvalidDataException($"{label} byte count mismatch.");
            buffer.ResizeUninitialized(expectedCount);
            for (int i = 0; i < expectedCount; i++)
                buffer[i] = new RuntimeCellSectionTerrainLayer { Value = BitConverter.ToUInt16(bytes, i * sizeof(ushort)) };
        }

        static void AppendBytes(DynamicBuffer<RuntimeCellSectionCombinedVertexByte> buffer, byte[] bytes, string label)
        {
            if (bytes == null)
                throw new InvalidDataException($"{label} payload is null.");
            for (int i = 0; i < bytes.Length; i++)
                buffer.Add(new RuntimeCellSectionCombinedVertexByte { Value = bytes[i] });
        }

        static void AppendBytes(DynamicBuffer<RuntimeCellSectionCombinedIndexByte> buffer, byte[] bytes, string label)
        {
            if (bytes == null)
                throw new InvalidDataException($"{label} payload is null.");
            for (int i = 0; i < bytes.Length; i++)
                buffer.Add(new RuntimeCellSectionCombinedIndexByte { Value = bytes[i] });
        }

        static bool TryValidateRuntimeCellSection(string path, PreparedCellWriteData preparedWrite, out string error)
        {
            error = null;
            try
            {
                using var world = new World($"VV.CellSectionValidate({preparedWrite.Key})");
                byte[] bytes = File.ReadAllBytes(path);
                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                        var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                        SerializeUtility.DeserializeWorld(tx, reader);
                        world.EntityManager.EndExclusiveEntityTransaction();
                    }
                }

                using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
                if (query.CalculateEntityCount() != 1)
                {
                    error = "section must contain exactly one RuntimeCellSectionHeader entity";
                    return false;
                }

                Entity entity = query.GetSingletonEntity();
                var header = world.EntityManager.GetComponentData<RuntimeCellSectionHeader>(entity);
                if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                {
                    error = $"pipeline version {header.PipelineVersion} does not match {CacheFormat.WorldBakePipelineVersion}";
                    return false;
                }
                if (header.IsInterior != (preparedWrite.IsInterior ? (byte)1 : (byte)0)
                    || header.GridX != preparedWrite.GridX
                    || header.GridY != preparedWrite.GridY)
                {
                    error = "section identity mismatch";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
