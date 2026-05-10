using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal static class RuntimeWorldCellBlobBuilder
    {
        public static BlobAssetReference<RuntimeWorldCellBlob> Build(CacheLoader cache)
        {
            if (cache?.Manifest == null)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] cannot build without a cache manifest.");

            var sources = LoadSectionSources(cache);
            return BuildBlob(sources);
        }

        static List<CellSource> LoadSectionSources(CacheLoader cache)
        {
            var result = new List<CellSource>((cache.Manifest.CellGrid?.Length ?? 0) + (cache.Manifest.InteriorCellIds?.Length ?? 0));
            var states = BuildCellStateLookup(cache.Manifest.CellStates);
            var exterior = cache.Manifest.CellGrid ?? Array.Empty<(int X, int Y)>();
            for (int i = 0; i < exterior.Length; i++)
            {
                var coord = new int2(exterior[i].X, exterior[i].Y);
                string path = ResolveCellSectionPath(ResolveCellState(states, false, coord.x, coord.y, null), false, coord.x, coord.y, null);
                result.Add(ReadSection(path, false, string.Empty));
            }

            var interiors = cache.Manifest.InteriorCellIds ?? Array.Empty<string>();
            for (int i = 0; i < interiors.Length; i++)
            {
                string cellId = interiors[i] ?? string.Empty;
                string path = ResolveCellSectionPath(ResolveCellState(states, true, 0, 0, cellId), true, 0, 0, cellId);
                result.Add(ReadSection(path, true, cellId));
            }

            return result;
        }

        static CellSource ReadSection(string path, bool isInterior, string cellId)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] missing runtime cell section '{path}'; rebake required.");

            using var world = new World($"VV.WorldCellBlobRead({Path.GetFileName(path)})");
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

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' must contain exactly one header.");
            Entity entity = query.GetSingletonEntity();
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' pipeline {header.PipelineVersion} does not match {CacheFormat.WorldBakePipelineVersion}; rebake required.");
            if ((header.IsInterior != 0) != isInterior)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' interior flag mismatch.");

            return new CellSource
            {
                Header = header,
                Refs = CopyRefs(em, entity),
                Doors = CopyDoors(em, entity),
                LockStates = CopyLockStates(em, entity),
                CapturedSouls = CopyCapturedSouls(em, entity),
                TerrainHeights = CopyTerrainHeights(em, entity, header.Flags),
                WorldMapSamples = CopyWorldMapSamples(em, entity, header.Flags),
            };
        }

        static BlobAssetReference<RuntimeWorldCellBlob> BuildBlob(List<CellSource> sources)
        {
            int refCount = 0;
            int doorCount = 0;
            int lockStateCount = 0;
            int capturedSoulCount = 0;
            int terrainHeightCount = 0;
            int worldMapSampleCount = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                refCount += sources[i].Refs.Length;
                doorCount += sources[i].Doors.Length;
                lockStateCount += sources[i].LockStates.Length;
                capturedSoulCount += sources[i].CapturedSouls.Length;
                terrainHeightCount += sources[i].TerrainHeights.Length;
                worldMapSampleCount += sources[i].WorldMapSamples.Length;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref RuntimeWorldCellBlob root = ref builder.ConstructRoot<RuntimeWorldCellBlob>();
                var cells = builder.Allocate(ref root.Cells, sources.Count);
                var refs = builder.Allocate(ref root.Refs, refCount);
                var doors = builder.Allocate(ref root.Doors, doorCount);
                var lockStates = builder.Allocate(ref root.LockStates, lockStateCount);
                var capturedSouls = builder.Allocate(ref root.CapturedSouls, capturedSoulCount);
                var terrainHeights = builder.Allocate(ref root.TerrainHeights, terrainHeightCount);
                var worldMapSamples = builder.Allocate(ref root.WorldMapSamples, worldMapSampleCount);
                var exteriorLookup = new List<RuntimeWorldCellExteriorLookupBlob>();
                var interiorLookup = new List<RuntimeContentHashLookupBlob>();

                int refCursor = 0;
                int doorCursor = 0;
                int lockCursor = 0;
                int soulCursor = 0;
                int heightCursor = 0;
                int mapCursor = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    int firstRef = refCursor;
                    int firstDoor = doorCursor;
                    int firstLock = lockCursor;
                    int firstSoul = soulCursor;
                    int firstHeight = heightCursor;
                    int firstMap = mapCursor;

                    for (int r = 0; r < source.Refs.Length; r++)
                        refs[refCursor++] = source.Refs[r];
                    for (int d = 0; d < source.Doors.Length; d++)
                        doors[doorCursor++] = source.Doors[d];
                    for (int l = 0; l < source.LockStates.Length; l++)
                        lockStates[lockCursor++] = source.LockStates[l];
                    for (int s = 0; s < source.CapturedSouls.Length; s++)
                        capturedSouls[soulCursor++] = source.CapturedSouls[s];
                    for (int h = 0; h < source.TerrainHeights.Length; h++)
                        terrainHeights[heightCursor++] = source.TerrainHeights[h];
                    for (int m = 0; m < source.WorldMapSamples.Length; m++)
                        worldMapSamples[mapCursor++] = source.WorldMapSamples[m];

                    bool isInterior = source.Header.IsInterior != 0;
                    var coord = new int2(source.Header.GridX, source.Header.GridY);
                    cells[i] = new RuntimeWorldCellDefBlob
                    {
                        ExteriorCoord = coord,
                        CellId = source.Header.CellId,
                        InteriorCellId = isInterior ? source.Header.CellId : default,
                        InteriorCellHash = source.Header.InteriorCellHash,
                        IsInterior = (byte)(isInterior ? 1 : 0),
                        HasTerrain = (byte)((source.Header.Flags & CacheFormat.CellFlagHasTerrain) != 0 ? 1 : 0),
                        HasWorldMap = (byte)((source.Header.Flags & CacheFormat.CellFlagHasWorldMap) != 0 ? 1 : 0),
                        HasStaticCollider = (byte)((source.Header.Flags & CacheFormat.CellFlagHasStaticCollision) != 0 ? 1 : 0),
                        HasTerrainCollider = (byte)((source.Header.Flags & CacheFormat.CellFlagHasTerrain) != 0 ? 1 : 0),
                        FirstRefIndex = firstRef,
                        RefCount = source.Refs.Length,
                        FirstDoorIndex = firstDoor,
                        DoorCount = source.Doors.Length,
                        FirstLockStateIndex = firstLock,
                        LockStateCount = source.LockStates.Length,
                        FirstCapturedSoulIndex = firstSoul,
                        CapturedSoulCount = source.CapturedSouls.Length,
                        FirstTerrainHeightIndex = firstHeight,
                        TerrainHeightCount = source.TerrainHeights.Length,
                        FirstWorldMapSampleIndex = firstMap,
                        WorldMapSampleCount = source.WorldMapSamples.Length,
                        Environment = CopyEnvironment(source.Header.Environment),
                    };

                    if (isInterior)
                        interiorLookup.Add(new RuntimeContentHashLookupBlob { Hash = source.Header.InteriorCellHash, HandleValue = i });
                    else
                        exteriorLookup.Add(new RuntimeWorldCellExteriorLookupBlob { Coord = coord, CellIndex = i });
                }

                CopyExteriorLookup(ref builder, ref root.ExteriorCellLookup, exteriorLookup);
                CopyInteriorLookup(ref builder, ref root.InteriorCellHashLookup, interiorLookup);
                return builder.CreateBlobAssetReference<RuntimeWorldCellBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        static RefEntry[] CopyRefs(EntityManager em, Entity entity)
        {
            var buffer = em.GetBuffer<RuntimeCellSectionRef>(entity);
            var result = new RefEntry[buffer.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = buffer[i].Value;
            return result;
        }

        static RuntimeWorldDoorRefDefBlob[] CopyDoors(EntityManager em, Entity entity)
        {
            var buffer = em.GetBuffer<RuntimeCellSectionDoor>(entity);
            var result = new RuntimeWorldDoorRefDefBlob[buffer.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var door = buffer[i];
                result[i] = new RuntimeWorldDoorRefDefBlob
                {
                    PlacedRefId = door.PlacedRefId,
                    Flags = door.Flags,
                    DestPosX = door.DestinationPosition.x,
                    DestPosY = door.DestinationPosition.y,
                    DestPosZ = door.DestinationPosition.z,
                    DestRotX = door.DestinationRotation.value.x,
                    DestRotY = door.DestinationRotation.value.y,
                    DestRotZ = door.DestinationRotation.value.z,
                    DestRotW = door.DestinationRotation.value.w,
                    DestinationCellId = door.DestinationCellId,
                    DestinationCellHash = string.IsNullOrWhiteSpace(door.DestinationCellId.ToString()) ? 0UL : InteriorCellIdHash.Hash(door.DestinationCellId.ToString()),
                };
            }
            return result;
        }

        static RuntimeWorldPlacedRefLockStateBlob[] CopyLockStates(EntityManager em, Entity entity)
        {
            var buffer = em.GetBuffer<RuntimeCellSectionLockState>(entity);
            var result = new RuntimeWorldPlacedRefLockStateBlob[buffer.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var item = buffer[i];
                result[i] = new RuntimeWorldPlacedRefLockStateBlob
                {
                    PlacedRefId = item.PlacedRefId,
                    LockLevel = item.LockLevel,
                    Locked = item.Locked,
                    KeyId = item.KeyId,
                    TrapId = item.TrapId,
                };
            }
            return result;
        }

        static RuntimeWorldPlacedRefCapturedSoulBlob[] CopyCapturedSouls(EntityManager em, Entity entity)
        {
            var buffer = em.GetBuffer<RuntimeCellSectionCapturedSoul>(entity);
            var result = new RuntimeWorldPlacedRefCapturedSoulBlob[buffer.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var item = buffer[i];
                result[i] = new RuntimeWorldPlacedRefCapturedSoulBlob
                {
                    PlacedRefId = item.PlacedRefId,
                    SoulId = item.SoulId,
                    SoulIdHash = RuntimeContentStableHash.HashId(item.SoulId.ToString()),
                };
            }
            return result;
        }

        static float[] CopyTerrainHeights(EntityManager em, Entity entity, uint flags)
        {
            if ((flags & CacheFormat.CellFlagHasTerrain) == 0)
                return Array.Empty<float>();
            var buffer = em.GetBuffer<RuntimeCellSectionTerrainHeight>(entity);
            var result = new float[buffer.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = buffer[i].Value;
            return result;
        }

        static sbyte[] CopyWorldMapSamples(EntityManager em, Entity entity, uint flags)
        {
            if ((flags & CacheFormat.CellFlagHasWorldMap) == 0)
                return Array.Empty<sbyte>();
            var buffer = em.GetBuffer<RuntimeCellSectionWorldMapSample>(entity);
            var result = new sbyte[buffer.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = buffer[i].Value;
            return result;
        }

        static RuntimeWorldCellEnvironmentDefBlob CopyEnvironment(in CellEnvironmentDataBlob source)
            => new RuntimeWorldCellEnvironmentDefBlob
            {
                HasMood = source.HasMood,
                HasWater = source.HasWater,
                AmbientColorRgba = source.AmbientColorRgba,
                DirectionalColorRgba = source.DirectionalColorRgba,
                FogColorRgba = source.FogColorRgba,
                FogDensity = source.FogDensity,
                WaterHeight = source.WaterHeight,
                RegionIdHash = RuntimeContentStableHash.HashId(source.RegionId.ToString()),
            };

        static Dictionary<string, BakeManifest.BakedCellState> BuildCellStateLookup(BakeManifest.BakedCellState[] states)
        {
            var lookup = new Dictionary<string, BakeManifest.BakedCellState>(StringComparer.OrdinalIgnoreCase);
            if (states == null)
                return lookup;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null)
                    continue;
                lookup[state.IsInterior ? BuildInteriorCellStateKey(state.InteriorId) : BuildExteriorCellStateKey(state.GridX, state.GridY)] = state;
            }
            return lookup;
        }

        static BakeManifest.BakedCellState ResolveCellState(Dictionary<string, BakeManifest.BakedCellState> stateByKey, bool isInterior, int gridX, int gridY, string interiorId)
        {
            string key = isInterior ? BuildInteriorCellStateKey(interiorId) : BuildExteriorCellStateKey(gridX, gridY);
            return stateByKey.TryGetValue(key, out var state) ? state : null;
        }

        static string ResolveCellSectionPath(BakeManifest.BakedCellState state, bool isInterior, int gridX, int gridY, string interiorId)
            => !string.IsNullOrWhiteSpace(state?.SectionPath)
                ? state.SectionPath
                : isInterior
                    ? CachePaths.InteriorCellSectionFile(interiorId ?? string.Empty)
                    : CachePaths.ExteriorCellSectionFile(gridX, gridY);

        static string BuildExteriorCellStateKey(int gridX, int gridY) => $"ext:{gridX},{gridY}";
        static string BuildInteriorCellStateKey(string interiorId) => $"int:{(interiorId ?? string.Empty).Trim().ToLowerInvariant()}";

        static void CopyExteriorLookup(ref BlobBuilder builder, ref BlobArray<RuntimeWorldCellExteriorLookupBlob> destination, List<RuntimeWorldCellExteriorLookupBlob> source)
        {
            source.Sort((a, b) => PackCoord(a.Coord).CompareTo(PackCoord(b.Coord)));
            var dst = builder.Allocate(ref destination, source.Count);
            long previous = long.MinValue;
            for (int i = 0; i < source.Count; i++)
            {
                long key = PackCoord(source[i].Coord);
                if (i > 0 && key == previous)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Duplicate exterior cell coord {source[i].Coord.x},{source[i].Coord.y}.");
                previous = key;
                dst[i] = source[i];
            }
        }

        static void CopyInteriorLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentHashLookupBlob> destination, List<RuntimeContentHashLookupBlob> source)
        {
            source.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            var dst = builder.Allocate(ref destination, source.Count);
            ulong previous = 0UL;
            for (int i = 0; i < source.Count; i++)
            {
                if (i > 0 && source[i].Hash == previous)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Duplicate interior cell hash 0x{source[i].Hash:X16}.");
                previous = source[i].Hash;
                dst[i] = source[i];
            }
        }

        static long PackCoord(int2 coord)
            => ((long)coord.x << 32) ^ (uint)coord.y;

        struct CellSource
        {
            public RuntimeCellSectionHeader Header;
            public RefEntry[] Refs;
            public RuntimeWorldDoorRefDefBlob[] Doors;
            public RuntimeWorldPlacedRefLockStateBlob[] LockStates;
            public RuntimeWorldPlacedRefCapturedSoulBlob[] CapturedSouls;
            public float[] TerrainHeights;
            public sbyte[] WorldMapSamples;
        }
    }
}
