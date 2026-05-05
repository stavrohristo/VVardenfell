using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeWorldCellBlobUtility
    {
        public static ref RuntimeWorldCellDefBlob RequireCell(ref RuntimeWorldCellBlob blob, int cellIndex)
        {
            if ((uint)cellIndex >= (uint)blob.Cells.Length)
                throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Invalid cell index {cellIndex}; length {blob.Cells.Length}.");
            return ref blob.Cells[cellIndex];
        }

        public static ref RuntimeWorldCellDefBlob RequireExteriorCell(ref RuntimeWorldCellBlob blob, int2 coord)
        {
            if (!TryGetExteriorCellIndex(ref blob, coord, out int cellIndex))
                throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Missing exterior cell {coord.x},{coord.y}.");
            return ref RequireCell(ref blob, cellIndex);
        }

        public static ref RuntimeWorldCellDefBlob RequireInteriorCell(ref RuntimeWorldCellBlob blob, ulong cellHash)
        {
            if (!TryGetInteriorCellIndex(ref blob, cellHash, out int cellIndex))
                throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Missing interior cell hash 0x{cellHash:X16}.");
            return ref RequireCell(ref blob, cellIndex);
        }

        public static bool TryGetExteriorCellIndex(ref RuntimeWorldCellBlob blob, int2 coord, out int cellIndex)
        {
            int lo = 0;
            int hi = blob.ExteriorCellLookup.Length - 1;
            long key = PackCoord(coord);
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                long candidate = PackCoord(blob.ExteriorCellLookup[mid].Coord);
                if (candidate == key)
                {
                    cellIndex = blob.ExteriorCellLookup[mid].CellIndex;
                    return true;
                }
                if (candidate < key)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            cellIndex = -1;
            return false;
        }

        public static bool TryGetInteriorCellIndex(ref RuntimeWorldCellBlob blob, ulong cellHash, out int cellIndex)
        {
            cellIndex = -1;
            if (cellHash == 0UL)
                return false;

            int lo = 0;
            int hi = blob.InteriorCellHashLookup.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ulong candidate = blob.InteriorCellHashLookup[mid].Hash;
                if (candidate == cellHash)
                {
                    cellIndex = blob.InteriorCellHashLookup[mid].HandleValue;
                    return true;
                }
                if (candidate < cellHash)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            return false;
        }

        public static ref BlobArray<RefEntry> GetRefs(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstRefIndex;
            count = cell.RefCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.Refs.Length, "world cell ref");
            return ref blob.Refs;
        }

        public static ref BlobArray<RuntimeWorldDoorRefDefBlob> GetDoors(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstDoorIndex;
            count = cell.DoorCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.Doors.Length, "world cell door");
            return ref blob.Doors;
        }

        public static ref BlobArray<RuntimeWorldPlacedRefLockStateBlob> GetLockStates(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstLockStateIndex;
            count = cell.LockStateCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.LockStates.Length, "world cell lock state");
            return ref blob.LockStates;
        }

        public static ref BlobArray<RuntimeWorldPlacedRefCapturedSoulBlob> GetCapturedSouls(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstCapturedSoulIndex;
            count = cell.CapturedSoulCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.CapturedSouls.Length, "world cell captured soul");
            return ref blob.CapturedSouls;
        }

        public static ref BlobArray<float> GetTerrainHeights(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstTerrainHeightIndex;
            count = cell.TerrainHeightCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.TerrainHeights.Length, "world cell terrain height");
            return ref blob.TerrainHeights;
        }

        public static ref BlobArray<sbyte> GetWorldMapSamples(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, out int first, out int count)
        {
            first = cell.FirstWorldMapSampleIndex;
            count = cell.WorldMapSampleCount;
            RuntimeContentBlobUtility.RequireRange(first, count, blob.WorldMapSamples.Length, "world cell map sample");
            return ref blob.WorldMapSamples;
        }

        public static bool TrySampleTerrainHeight(ref RuntimeWorldCellBlob blob, int2 coord, float localX, float localZ, out float height)
        {
            const int N = 65;
            height = 0f;
            if (!TryGetExteriorCellIndex(ref blob, coord, out int cellIndex))
                return false;

            ref RuntimeWorldCellDefBlob cell = ref RequireCell(ref blob, cellIndex);
            ref BlobArray<float> heights = ref GetTerrainHeights(ref blob, ref cell, out int first, out int count);
            if (count < N * N)
                return false;

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float sampleX = math.clamp(localX / cellMeters * (N - 1), 0f, N - 1);
            float sampleZ = math.clamp(localZ / cellMeters * (N - 1), 0f, N - 1);
            int x0 = (int)math.floor(sampleX);
            int z0 = (int)math.floor(sampleZ);
            int x1 = math.min(x0 + 1, N - 1);
            int z1 = math.min(z0 + 1, N - 1);
            float tx = sampleX - x0;
            float tz = sampleZ - z0;

            float h00 = heights[first + z0 * N + x0];
            float h10 = heights[first + z0 * N + x1];
            float h01 = heights[first + z1 * N + x0];
            float h11 = heights[first + z1 * N + x1];
            height = math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
            return true;
        }

        public static bool TryFindRefByPlacedRefId(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, uint placedRefId, out RefEntry entry)
        {
            entry = default;
            if (placedRefId == 0u)
                return false;

            ref BlobArray<RefEntry> refs = ref GetRefs(ref blob, ref cell, out int first, out int count);
            for (int i = 0; i < count; i++)
            {
                RefEntry candidate = refs[first + i];
                if (candidate.PlacedRefId != placedRefId)
                    continue;

                entry = candidate;
                return true;
            }

            return false;
        }

        public static bool TryGetDoorForRef(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, in RefEntry entry, out RuntimeWorldDoorRefDefBlob door)
        {
            door = default;
            if (entry.DoorMetaIndex < 0)
                return false;

            ref BlobArray<RuntimeWorldDoorRefDefBlob> doors = ref GetDoors(ref blob, ref cell, out int first, out int count);
            if ((uint)entry.DoorMetaIndex >= (uint)count)
                throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Ref {entry.PlacedRefId} has invalid door meta index {entry.DoorMetaIndex}; cell door count {count}.");

            door = doors[first + entry.DoorMetaIndex];
            return true;
        }

        public static bool TryGetDoorByPlacedRefId(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, uint placedRefId, out RuntimeWorldDoorRefDefBlob door)
        {
            door = default;
            return TryFindRefByPlacedRefId(ref blob, ref cell, placedRefId, out RefEntry entry)
                   && TryGetDoorForRef(ref blob, ref cell, entry, out door);
        }

        public static bool TryGetLockState(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, uint placedRefId, out RuntimeWorldPlacedRefLockStateBlob state)
        {
            state = default;
            if (placedRefId == 0u)
                return false;

            ref BlobArray<RuntimeWorldPlacedRefLockStateBlob> lockStates = ref GetLockStates(ref blob, ref cell, out int first, out int count);
            for (int i = 0; i < count; i++)
            {
                RuntimeWorldPlacedRefLockStateBlob candidate = lockStates[first + i];
                if (candidate.PlacedRefId != placedRefId)
                    continue;

                state = candidate;
                return true;
            }

            return false;
        }

        public static bool TryGetCapturedSoul(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, uint placedRefId, out RuntimeWorldPlacedRefCapturedSoulBlob soul)
        {
            soul = default;
            if (placedRefId == 0u)
                return false;

            ref BlobArray<RuntimeWorldPlacedRefCapturedSoulBlob> souls = ref GetCapturedSouls(ref blob, ref cell, out int first, out int count);
            for (int i = 0; i < count; i++)
            {
                RuntimeWorldPlacedRefCapturedSoulBlob candidate = souls[first + i];
                if (candidate.PlacedRefId != placedRefId)
                    continue;

                soul = candidate;
                return true;
            }

            return false;
        }

        public static bool TryFindFirstRefWithContent(ref RuntimeWorldCellBlob blob, ref RuntimeWorldCellDefBlob cell, in ContentReference content, out RefEntry entry)
        {
            entry = default;
            ref BlobArray<RefEntry> refs = ref GetRefs(ref blob, ref cell, out int first, out int count);
            for (int i = 0; i < count; i++)
            {
                RefEntry candidate = refs[first + i];
                if (candidate.ContentKind != (int)content.Kind || candidate.ContentHandleValue != content.HandleValue)
                    continue;

                entry = candidate;
                return true;
            }

            return false;
        }

        public static bool TryFindNearestExteriorRefWithContent(ref RuntimeWorldCellBlob blob, in ContentReference content, float3 position, out int cellIndex, out RefEntry entry)
        {
            cellIndex = -1;
            entry = default;
            bool found = false;
            float bestDistanceSq = 0f;

            for (int i = 0; i < blob.ExteriorCellLookup.Length; i++)
            {
                int candidateCellIndex = blob.ExteriorCellLookup[i].CellIndex;
                ref RuntimeWorldCellDefBlob cell = ref RequireCell(ref blob, candidateCellIndex);
                ref BlobArray<RefEntry> refs = ref GetRefs(ref blob, ref cell, out int first, out int count);
                for (int r = 0; r < count; r++)
                {
                    RefEntry candidate = refs[first + r];
                    if (candidate.ContentKind != (int)content.Kind || candidate.ContentHandleValue != content.HandleValue)
                        continue;

                    float3 candidatePosition = new(candidate.PosX, candidate.PosY, candidate.PosZ);
                    float distanceSq = math.lengthsq(candidatePosition.xz - position.xz);
                    if (found && distanceSq >= bestDistanceSq)
                        continue;

                    found = true;
                    bestDistanceSq = distanceSq;
                    cellIndex = candidateCellIndex;
                    entry = candidate;
                }
            }

            return found;
        }

        static long PackCoord(int2 coord)
            => ((long)coord.x << 32) ^ (uint)coord.y;
    }
}
