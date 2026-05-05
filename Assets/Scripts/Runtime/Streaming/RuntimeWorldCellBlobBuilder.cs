using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal static class RuntimeWorldCellBlobBuilder
    {
        public static BlobAssetReference<RuntimeWorldCellBlob> Build()
        {
            int exteriorCount = WorldResources.ExteriorCellCount;
            int interiorCount = WorldResources.InteriorCellHashCount;
            var cellSources = new List<CellSource>(exteriorCount + interiorCount);

            var exteriorCells = WorldResources.CopyExteriorCellEntries();
            for (int i = 0; i < exteriorCells.Length; i++)
            {
                var kv = exteriorCells[i];
                if (kv.Value == null)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Exterior cell {kv.Key.x},{kv.Key.y} is null.");
                cellSources.Add(new CellSource { Cell = kv.Value, Coord = kv.Key, IsInterior = false });
            }

            var interiorCells = WorldResources.CopyInteriorCellHashEntries();
            for (int i = 0; i < interiorCells.Length; i++)
            {
                var kv = interiorCells[i];
                if (kv.Value == null)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Interior cell hash 0x{kv.Key:X16} is null.");
                cellSources.Add(new CellSource
                {
                    Cell = kv.Value,
                    Coord = default,
                    IsInterior = true,
                    InteriorCellHash = kv.Key,
                    InteriorCellId = WorldResources.ResolveInteriorCellId(kv.Key),
                });
            }

            int refCount = 0;
            int doorCount = 0;
            int lockStateCount = 0;
            int capturedSoulCount = 0;
            int terrainHeightCount = 0;
            int worldMapSampleCount = 0;
            for (int i = 0; i < cellSources.Count; i++)
            {
                refCount += cellSources[i].Cell.Refs?.Length ?? 0;
                doorCount += cellSources[i].Cell.Doors?.Length ?? 0;
                lockStateCount += cellSources[i].Cell.LockStates?.Length ?? 0;
                capturedSoulCount += cellSources[i].Cell.CapturedSouls?.Length ?? 0;
                terrainHeightCount += cellSources[i].Cell.Heights?.Length ?? 0;
                worldMapSampleCount += cellSources[i].Cell.WorldMap?.Length ?? 0;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref RuntimeWorldCellBlob root = ref builder.ConstructRoot<RuntimeWorldCellBlob>();
                var cells = builder.Allocate(ref root.Cells, cellSources.Count);
                var refs = builder.Allocate(ref root.Refs, refCount);
                var doors = builder.Allocate(ref root.Doors, doorCount);
                var lockStates = builder.Allocate(ref root.LockStates, lockStateCount);
                var capturedSouls = builder.Allocate(ref root.CapturedSouls, capturedSoulCount);
                var terrainHeights = builder.Allocate(ref root.TerrainHeights, terrainHeightCount);
                var worldMapSamples = builder.Allocate(ref root.WorldMapSamples, worldMapSampleCount);
                var exteriorLookup = new List<RuntimeWorldCellExteriorLookupBlob>(exteriorCount);
                var interiorLookup = new List<RuntimeContentHashLookupBlob>(interiorCount);

                int refCursor = 0;
                int doorCursor = 0;
                int lockStateCursor = 0;
                int capturedSoulCursor = 0;
                int terrainHeightCursor = 0;
                int worldMapSampleCursor = 0;
                for (int i = 0; i < cellSources.Count; i++)
                {
                    var source = cellSources[i];
                    var data = source.Cell;
                    var sourceRefs = data.Refs ?? Array.Empty<RefEntry>();
                    var sourceDoors = data.Doors ?? Array.Empty<DoorRefEntry>();
                    var sourceLockStates = data.LockStates ?? Array.Empty<PlacedRefLockEntry>();
                    var sourceCapturedSouls = data.CapturedSouls ?? Array.Empty<PlacedRefSoulEntry>();
                    var sourceTerrainHeights = data.Heights ?? Array.Empty<float>();
                    var sourceWorldMapSamples = data.WorldMap ?? Array.Empty<sbyte>();
                    int firstRef = refCursor;
                    int firstDoor = doorCursor;
                    int firstLockState = lockStateCursor;
                    int firstCapturedSoul = capturedSoulCursor;
                    int firstTerrainHeight = terrainHeightCursor;
                    int firstWorldMapSample = worldMapSampleCursor;

                    for (int r = 0; r < sourceRefs.Length; r++)
                        refs[refCursor++] = sourceRefs[r];
                    for (int d = 0; d < sourceDoors.Length; d++)
                        doors[doorCursor++] = CopyDoor(sourceDoors[d]);
                    for (int l = 0; l < sourceLockStates.Length; l++)
                        lockStates[lockStateCursor++] = CopyLockState(sourceLockStates[l]);
                    for (int s = 0; s < sourceCapturedSouls.Length; s++)
                        capturedSouls[capturedSoulCursor++] = CopyCapturedSoul(sourceCapturedSouls[s]);
                    for (int h = 0; h < sourceTerrainHeights.Length; h++)
                        terrainHeights[terrainHeightCursor++] = sourceTerrainHeights[h];
                    for (int m = 0; m < sourceWorldMapSamples.Length; m++)
                        worldMapSamples[worldMapSampleCursor++] = sourceWorldMapSamples[m];

                    cells[i] = new RuntimeWorldCellDefBlob
                    {
                        ExteriorCoord = source.Coord,
                        CellId = RuntimeFixedStringUtility.ToFixed128OrDefault(data.CellId),
                        InteriorCellId = RuntimeFixedStringUtility.ToFixed128OrDefault(source.InteriorCellId),
                        InteriorCellHash = source.InteriorCellHash,
                        IsInterior = (byte)(source.IsInterior ? 1 : 0),
                        HasTerrain = (byte)(data.HasTerrain ? 1 : 0),
                        HasWorldMap = (byte)(data.WorldMap != null ? 1 : 0),
                        HasStaticCollider = (byte)(data.HasStaticCollider || WorldResources.TryGetStaticCellCollider(source.Coord, out _) ? 1 : 0),
                        HasTerrainCollider = (byte)(data.HasTerrainCollider || WorldResources.TryGetTerrainCollider(source.Coord, out _) ? 1 : 0),
                        FirstRefIndex = firstRef,
                        RefCount = sourceRefs.Length,
                        FirstDoorIndex = firstDoor,
                        DoorCount = sourceDoors.Length,
                        FirstLockStateIndex = firstLockState,
                        LockStateCount = sourceLockStates.Length,
                        FirstCapturedSoulIndex = firstCapturedSoul,
                        CapturedSoulCount = sourceCapturedSouls.Length,
                        FirstTerrainHeightIndex = firstTerrainHeight,
                        TerrainHeightCount = sourceTerrainHeights.Length,
                        FirstWorldMapSampleIndex = firstWorldMapSample,
                        WorldMapSampleCount = sourceWorldMapSamples.Length,
                        Environment = CopyEnvironment(data.Environment),
                    };

                    if (source.IsInterior)
                    {
                        if (source.InteriorCellHash == 0UL)
                            throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Interior cell '{source.InteriorCellId}' has no hash.");
                        interiorLookup.Add(new RuntimeContentHashLookupBlob { Hash = source.InteriorCellHash, HandleValue = i });
                    }
                    else
                    {
                        exteriorLookup.Add(new RuntimeWorldCellExteriorLookupBlob { Coord = source.Coord, CellIndex = i });
                    }
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

        static RuntimeWorldDoorRefDefBlob CopyDoor(in DoorRefEntry source)
        {
            ulong destinationHash = string.IsNullOrWhiteSpace(source.DestinationCellId)
                ? 0UL
                : InteriorCellIdHash.Hash(source.DestinationCellId);
            return new RuntimeWorldDoorRefDefBlob
            {
                PlacedRefId = source.PlacedRefId,
                Flags = source.Flags,
                DestPosX = source.DestPosX,
                DestPosY = source.DestPosY,
                DestPosZ = source.DestPosZ,
                DestRotX = source.DestRotX,
                DestRotY = source.DestRotY,
                DestRotZ = source.DestRotZ,
                DestRotW = source.DestRotW,
                DestinationCellId = RuntimeFixedStringUtility.ToFixed128OrDefault(source.DestinationCellId),
                DestinationCellHash = destinationHash,
            };
        }

        static RuntimeWorldPlacedRefLockStateBlob CopyLockState(in PlacedRefLockEntry source)
            => new RuntimeWorldPlacedRefLockStateBlob
            {
                PlacedRefId = source.PlacedRefId,
                LockLevel = source.LockLevel,
                Locked = source.Locked,
                KeyId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(source.KeyId),
                TrapId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(source.TrapId),
            };

        static RuntimeWorldPlacedRefCapturedSoulBlob CopyCapturedSoul(in PlacedRefSoulEntry source)
            => new RuntimeWorldPlacedRefCapturedSoulBlob
            {
                PlacedRefId = source.PlacedRefId,
                SoulId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(source.SoulId),
                SoulIdHash = RuntimeContentStableHash.HashId(source.SoulId),
            };

        static RuntimeWorldCellEnvironmentDefBlob CopyEnvironment(in CellEnvironmentData source)
            => new RuntimeWorldCellEnvironmentDefBlob
            {
                HasMood = source.HasMood,
                HasWater = source.HasWater,
                AmbientColorRgba = source.AmbientColorRgba,
                DirectionalColorRgba = source.DirectionalColorRgba,
                FogColorRgba = source.FogColorRgba,
                FogDensity = source.FogDensity,
                WaterHeight = source.WaterHeight,
                RegionIdHash = RuntimeContentStableHash.HashId(source.RegionId),
            };

        static void CopyExteriorLookup(
            ref BlobBuilder builder,
            ref BlobArray<RuntimeWorldCellExteriorLookupBlob> destination,
            List<RuntimeWorldCellExteriorLookupBlob> source)
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

        static void CopyInteriorLookup(
            ref BlobBuilder builder,
            ref BlobArray<RuntimeContentHashLookupBlob> destination,
            List<RuntimeContentHashLookupBlob> source)
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
            public CellData Cell;
            public int2 Coord;
            public bool IsInterior;
            public string InteriorCellId;
            public ulong InteriorCellHash;
        }
    }
}
