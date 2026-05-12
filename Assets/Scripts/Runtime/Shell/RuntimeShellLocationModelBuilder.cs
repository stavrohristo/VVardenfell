using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        LocationPresentation BuildLocationPresentation(ref RuntimeContentBlob contentBlob)
        {
            bool hasTransition = SystemAPI.HasSingleton<InteriorTransitionState>();
            bool hasStreaming = SystemAPI.HasSingleton<StreamingConfig>();
            bool hasLoaded = SystemAPI.HasSingleton<LoadedCellsMap>();

            if (hasTransition)
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                if (transition.InteriorActive != 0)
                {
                    if (!_location.InteriorActive || !_lastInteriorCellId.Equals(transition.ActiveInteriorCellId))
                    {
                        _lastInteriorCellId = transition.ActiveInteriorCellId;
                        string interiorId = transition.ActiveInteriorCellId.ToString();
                        _location.InteriorActive = true;
                        _location.DisplayName = string.IsNullOrWhiteSpace(interiorId) ? "Interior" : interiorId;
                        _location.CellText = string.IsNullOrWhiteSpace(interiorId) ? "Interior active" : interiorId;
                        _location.RegionText = "Interior";
                        _location.StreamingText = "Exterior streaming paused";
                        _lastLocationExteriorCell = new int2(int.MinValue, int.MinValue);
                        _lastLocationCellId = default;
                        _lastLocationRegionHash = 0UL;
                    }

                    return _location;
                }
            }

            if (_location.InteriorActive)
            {
                _location = LocationPresentation.Unavailable;
                _lastLocationExteriorCell = new int2(int.MinValue, int.MinValue);
                _lastLocationCellId = default;
                _lastLocationRegionHash = 0UL;
                _lastLocationLoadedCount = -1;
                _lastLocationActiveCount = -1;
            }

            if (!hasStreaming)
            {
                if (!_lastLocationExteriorCell.Equals(new int2(int.MinValue, int.MinValue))
                    || !_lastLocationCellId.IsEmpty
                    || _lastLocationRegionHash != 0UL
                    || _lastLocationLoadedCount != -1
                    || _lastLocationActiveCount != -1)
                {
                    _location = LocationPresentation.Unavailable;
                    _lastLocationExteriorCell = new int2(int.MinValue, int.MinValue);
                    _lastLocationCellId = default;
                    _lastLocationRegionHash = 0UL;
                    _lastLocationLoadedCount = -1;
                    _lastLocationActiveCount = -1;
                }

                return _location;
            }

            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            int2 cellCoord = streaming.CameraCell;
            FixedString128Bytes cellId = default;
            ulong regionHash = 0UL;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] shell location presentation requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, cellCoord, out int cellIndex))
            {
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                cellId = cell.CellId;
                regionHash = cell.Environment.RegionIdHash;
            }

            if (!_lastLocationExteriorCell.Equals(cellCoord)
                || !_lastLocationCellId.Equals(cellId)
                || _lastLocationRegionHash != regionHash)
            {
                _lastLocationExteriorCell = cellCoord;
                _lastLocationCellId = cellId;
                _lastLocationRegionHash = regionHash;
                _location.InteriorActive = false;
                _location.DisplayName = $"Wilderness ({cellCoord.x}, {cellCoord.y})";
                _location.CellText = $"Exterior cell {cellCoord.x}, {cellCoord.y}";
                _location.RegionText = "--";

                string cellIdText = cellId.ToString();
                if (!string.IsNullOrWhiteSpace(cellIdText))
                    _location.DisplayName = cellIdText.Trim();

                if (regionHash != 0UL)
                {
                    if (RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref contentBlob, regionHash, out var regionHandle) && regionHandle.IsValid)
                    {
                        ref RuntimeRegionDefBlob region = ref RuntimeContentBlobUtility.Get(ref contentBlob, regionHandle);
                        string name = region.Name.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                            _location.RegionText = name.Trim();
                    }
                    else
                    {
                        throw new InvalidOperationException($"[VVardenfell][Shell] exterior location region hash 0x{regionHash:X16} does not resolve.");
                    }
                }
            }

            if (hasLoaded)
            {
                var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
                int loadedCount = loaded.Streamed.IsCreated ? loaded.Streamed.Count : 0;
                int activeCount = loaded.Active.IsCreated ? loaded.Active.Count : 0;
                if (loadedCount != _lastLocationLoadedCount || activeCount != _lastLocationActiveCount)
                {
                    _lastLocationLoadedCount = loadedCount;
                    _lastLocationActiveCount = activeCount;
                    _location.StreamingText = $"Exterior streaming: {activeCount} active / {loadedCount} loaded";
                }
            }
            else if (_lastLocationLoadedCount != -1 || _lastLocationActiveCount != -1)
            {
                _lastLocationLoadedCount = -1;
                _lastLocationActiveCount = -1;
                _location.StreamingText = "Streaming state unavailable";
            }

            return _location;
        }
    }
}

