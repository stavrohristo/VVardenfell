using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Cache
{
    public readonly struct RuntimeCellSectionLoadResult
    {
        public readonly Entity SectionEntity;
        public readonly RuntimeCellSectionHeader Header;

        public RuntimeCellSectionLoadResult(Entity sectionEntity, RuntimeCellSectionHeader header)
        {
            SectionEntity = sectionEntity;
            Header = header;
        }
    }

    public static class RuntimeCellSectionFile
    {
        public static RuntimeCellSectionLoadResult LoadIntoWorld(EntityManager target, string path, bool isInterior, string cellId = null)
        {
            if (TryFindResident(target, isInterior, path, cellId, out var existing))
                return existing;

            using (var sectionWorld = DeserializeToTempWorld(path))
            {
                var sourceEntity = ValidateSingleSection(sectionWorld.EntityManager, path, isInterior, cellId);
                ValidateSectionPayload(sectionWorld.EntityManager, sourceEntity, path);
                target.MoveEntitiesFrom(sectionWorld.EntityManager);
            }

            if (!TryFindUnclaimed(target, isInterior, path, cellId, out var loaded))
                throw new InvalidDataException($"Runtime cell section '{path}' was deserialized but no matching unclaimed section entity was found.");

            target.AddComponentData(loaded.SectionEntity, new RuntimeCellSectionResident
            {
                ExteriorCoord = new int2(loaded.Header.GridX, loaded.Header.GridY),
                InteriorCellHash = loaded.Header.InteriorCellHash,
                IsInterior = loaded.Header.IsInterior,
            });
            return loaded;
        }

        public static void ValidateFile(string path, bool isInterior, string cellId = null)
        {
            using var sectionWorld = DeserializeToTempWorld(path);
            var entity = ValidateSingleSection(sectionWorld.EntityManager, path, isInterior, cellId);
            ValidateSectionPayload(sectionWorld.EntityManager, entity, path);
        }

        static World DeserializeToTempWorld(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Runtime cell section path is empty.");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Runtime cell section '{path}' does not exist.", path);

            var world = new World($"VV.CellSectionLoad({Path.GetFileName(path)})");
            try
            {
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

                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        static Entity ValidateSingleSection(EntityManager em, string path, bool isInterior, string cellId)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException($"Runtime cell section '{path}' must contain exactly one cell header entity.");

            Entity entity = query.GetSingletonEntity();
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            ValidateHeader(header, path, isInterior, cellId);
            return entity;
        }

        static void ValidateHeader(RuntimeCellSectionHeader header, string path, bool isInterior, string cellId)
        {
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"Runtime cell section '{path}' pipeline {header.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebake required.");

            bool headerInterior = header.IsInterior != 0;
            if (headerInterior != isInterior)
                throw new InvalidDataException($"Runtime cell section '{path}' interior flag mismatch.");

            if (isInterior)
            {
                string expected = cellId ?? string.Empty;
                string actual = header.CellId.ToString();
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Runtime cell section '{path}' interior id mismatch: found '{actual}', expected '{expected}'.");
            }
        }

        static void ValidateSectionPayload(EntityManager em, Entity entity, string path)
        {
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            uint flags = header.Flags;
            if ((flags & CacheFormat.CellFlagHasTerrain) != 0)
            {
                RequireBufferLength<RuntimeCellSectionTerrainHeight>(em, entity, 65 * 65, path, "terrain heights");
                if ((flags & CacheFormat.CellFlagHasNormals) != 0)
                    RequireBufferLength<RuntimeCellSectionTerrainNormal>(em, entity, 3 * 65 * 65, path, "terrain normals");
                if ((flags & CacheFormat.CellFlagHasVtex) != 0)
                    RequireBufferLength<RuntimeCellSectionTerrainLayer>(em, entity, 16 * 16, path, "terrain layer grid");
                if ((flags & CacheFormat.CellFlagHasWorldMap) != 0)
                    RequireBufferLength<RuntimeCellSectionWorldMapSample>(em, entity, 81, path, "world map");
            }

            var refs = em.GetBuffer<RuntimeCellSectionRef>(entity);
            var doors = em.GetBuffer<RuntimeCellSectionDoor>(entity);
            for (int i = 0; i < refs.Length; i++)
            {
                RefEntry entry = refs[i].Value;
                if (entry.SpawnModeRaw != (int)RefSpawnMode.LogicalOnly && entry.SpawnModeRaw != (int)RefSpawnMode.ModelPrefab)
                {
                    string mode = Enum.IsDefined(typeof(RefSpawnMode), entry.SpawnModeRaw)
                        ? ((RefSpawnMode)entry.SpawnModeRaw).ToString()
                        : $"unknown({entry.SpawnModeRaw})";
                    throw new InvalidDataException($"Runtime cell section '{path}' ref {i} uses unsupported spawn mode {mode}.");
                }

                if ((ContentReferenceKind)entry.ContentKind != ContentReferenceKind.Door)
                    continue;
                if (entry.DoorMetaIndex < 0 || entry.DoorMetaIndex >= doors.Length)
                    throw new InvalidDataException($"Runtime cell section '{path}' door ref 0x{entry.PlacedRefId:X8} has invalid door metadata index {entry.DoorMetaIndex}/{doors.Length}.");
                if (doors[entry.DoorMetaIndex].PlacedRefId != entry.PlacedRefId)
                    throw new InvalidDataException($"Runtime cell section '{path}' door ref 0x{entry.PlacedRefId:X8} points at mismatched door metadata 0x{doors[entry.DoorMetaIndex].PlacedRefId:X8}.");
            }

            ValidateCombinedChunks(em, entity, path);
        }

        static void ValidateCombinedChunks(EntityManager em, Entity entity, string path)
        {
            var chunks = em.GetBuffer<RuntimeCellSectionCombinedChunk>(entity);
            var vertexBytes = em.GetBuffer<RuntimeCellSectionCombinedVertexByte>(entity);
            var indexBytes = em.GetBuffer<RuntimeCellSectionCombinedIndexByte>(entity);
            var members = em.GetBuffer<RuntimeCellSectionCombinedMember>(entity);
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                ValidateRange(chunk.FirstVertexByte, chunk.VertexByteCount, vertexBytes.Length, path, "combined vertex bytes");
                ValidateRange(chunk.FirstIndexByte, chunk.IndexByteCount, indexBytes.Length, path, "combined index bytes");
                ValidateRange(chunk.FirstMember, chunk.MemberCount, members.Length, path, "combined members");
            }
        }

        static void RequireBufferLength<T>(EntityManager em, Entity entity, int expected, string path, string label)
            where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(entity))
                throw new InvalidDataException($"Runtime cell section '{path}' is missing {label}.");
            int length = em.GetBuffer<T>(entity).Length;
            if (length != expected)
                throw new InvalidDataException($"Runtime cell section '{path}' has {length} {label}; expected {expected}.");
        }

        static bool TryFindResident(EntityManager em, bool isInterior, string path, string cellId, out RuntimeCellSectionLoadResult result)
            => TryFind(em, isInterior, path, cellId, requireResident: true, out result);

        static bool TryFindUnclaimed(EntityManager em, bool isInterior, string path, string cellId, out RuntimeCellSectionLoadResult result)
            => TryFind(em, isInterior, path, cellId, requireResident: false, out result);

        static bool TryFind(EntityManager em, bool isInterior, string path, string cellId, bool requireResident, out RuntimeCellSectionLoadResult result)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                bool hasResident = em.HasComponent<RuntimeCellSectionResident>(entity);
                if (hasResident != requireResident)
                    continue;

                var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
                if (!Matches(header, isInterior, cellId, path))
                    continue;

                ValidateHeader(header, path, isInterior, cellId);
                result = new RuntimeCellSectionLoadResult(entity, header);
                return true;
            }

            result = default;
            return false;
        }

        static bool Matches(RuntimeCellSectionHeader header, bool isInterior, string cellId, string path)
        {
            if ((header.IsInterior != 0) != isInterior)
                return false;
            if (!isInterior)
            {
                if (!TryParseExteriorCoord(path, out int2 coord))
                    return true;
                return header.GridX == coord.x && header.GridY == coord.y;
            }

            return string.Equals(header.CellId.ToString(), cellId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryParseExteriorCoord(string path, out int2 coord)
        {
            coord = default;
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            int split = name.IndexOf('_');
            if (split <= 0 || split >= name.Length - 1)
                return false;
            if (!int.TryParse(name.Substring(0, split), out int x) || !int.TryParse(name.Substring(split + 1), out int y))
                return false;
            coord = new int2(x, y);
            return true;
        }

        static void ValidateRange(int start, int count, int total, string path, string label)
        {
            if (start < 0 || count < 0 || start > total || count > total - start)
                throw new InvalidDataException($"Runtime cell section '{path}' has invalid {label} range {start}+{count}/{total}.");
        }
    }
}
