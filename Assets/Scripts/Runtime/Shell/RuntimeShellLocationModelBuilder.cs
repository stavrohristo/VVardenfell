using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        LocationPresentation BuildLocationPresentation(RuntimeContentDatabase contentDb)
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
                        _lastLocationCellData = null;
                    }

                    return _location;
                }
            }

            if (_location.InteriorActive)
            {
                _location = LocationPresentation.Unavailable;
                _lastLocationExteriorCell = new int2(int.MinValue, int.MinValue);
                _lastLocationCellData = null;
                _lastLocationLoadedCount = -1;
                _lastLocationActiveCount = -1;
            }

            if (!hasStreaming)
            {
                if (!_lastLocationExteriorCell.Equals(new int2(int.MinValue, int.MinValue))
                    || _lastLocationCellData != null
                    || _lastLocationLoadedCount != -1
                    || _lastLocationActiveCount != -1)
                {
                    _location = LocationPresentation.Unavailable;
                    _lastLocationExteriorCell = new int2(int.MinValue, int.MinValue);
                    _lastLocationCellData = null;
                    _lastLocationLoadedCount = -1;
                    _lastLocationActiveCount = -1;
                }

                return _location;
            }

            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            int2 cellCoord = streaming.CameraCell;
            WorldResources.Cells.TryGetValue(cellCoord, out CellData cell);
            if (!_lastLocationExteriorCell.Equals(cellCoord) || !ReferenceEquals(_lastLocationCellData, cell))
            {
                _lastLocationExteriorCell = cellCoord;
                _lastLocationCellData = cell;
                _location.InteriorActive = false;
                _location.DisplayName = $"Wilderness ({cellCoord.x}, {cellCoord.y})";
                _location.CellText = $"Exterior cell {cellCoord.x}, {cellCoord.y}";
                _location.RegionText = "--";

                if (cell != null && !string.IsNullOrWhiteSpace(cell.CellId))
                    _location.DisplayName = cell.CellId.Trim();

                string regionId = cell?.Environment.RegionId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(regionId))
                {
                    _location.RegionText = regionId;
                    if (contentDb != null && contentDb.TryGetRegionHandle(regionId, out var regionHandle))
                    {
                        ref readonly var region = ref contentDb.Get(regionHandle);
                        if (!string.IsNullOrWhiteSpace(region.Name))
                            _location.RegionText = region.Name.Trim();
                    }
                }
            }

            if (hasLoaded)
            {
                var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
                int loadedCount = loaded.Map.IsCreated ? loaded.Map.Count : 0;
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

