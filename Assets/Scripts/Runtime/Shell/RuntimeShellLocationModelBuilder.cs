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
            var result = new LocationPresentation
            {
                DisplayName = string.Empty,
                RegionText = "--",
                CellText = "--",
                StreamingText = "Streaming state unavailable",
            };

            bool hasTransition = SystemAPI.HasSingleton<InteriorTransitionState>();
            bool hasStreaming = SystemAPI.HasSingleton<StreamingConfig>();
            bool hasLoaded = SystemAPI.HasSingleton<LoadedCellsMap>();

            if (hasTransition)
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                if (transition.InteriorActive != 0)
                {
                    string interiorId = transition.ActiveInteriorCellId.ToString();
                    result.InteriorActive = true;
                    result.DisplayName = string.IsNullOrWhiteSpace(interiorId) ? "Interior" : interiorId;
                    result.CellText = string.IsNullOrWhiteSpace(interiorId) ? "Interior active" : interiorId;
                    result.RegionText = "Interior";
                    result.StreamingText = "Exterior streaming paused";
                    return result;
                }
            }

            if (!hasStreaming)
                return result;

            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            int2 cellCoord = streaming.CameraCell;
            result.DisplayName = $"Wilderness ({cellCoord.x}, {cellCoord.y})";
            result.CellText = $"Exterior cell {cellCoord.x}, {cellCoord.y}";

            if (WorldResources.Cells.TryGetValue(cellCoord, out CellData cell) && cell != null)
            {
                if (!string.IsNullOrWhiteSpace(cell.CellId))
                    result.DisplayName = cell.CellId.Trim();

                string regionId = cell.Environment.RegionId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(regionId))
                {
                    result.RegionText = regionId;
                    if (contentDb != null && contentDb.TryGetRegionHandle(regionId, out var regionHandle))
                    {
                        ref readonly var region = ref contentDb.Get(regionHandle);
                        if (!string.IsNullOrWhiteSpace(region.Name))
                            result.RegionText = region.Name.Trim();
                    }
                }
            }

            if (hasLoaded)
            {
                var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
                int loadedCount = loaded.Map.IsCreated ? loaded.Map.Count : 0;
                int activeCount = loaded.Active.IsCreated ? loaded.Active.Count : 0;
                result.StreamingText = $"Exterior streaming: {activeCount} active / {loadedCount} loaded";
            }

            return result;
        }
    }
}

