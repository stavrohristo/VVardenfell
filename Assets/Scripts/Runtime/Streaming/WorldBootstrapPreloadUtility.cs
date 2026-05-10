using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal sealed class WorldBootstrapPreloadResult
    {
        public WorldBootstrapPreloadFailureInfo[] ExteriorFailures;
        public WorldBootstrapPreloadFailureInfo[] InteriorFailures;
    }

    internal enum WorldBootstrapPreloadFailureKind
    {
        MissingFile,
        TruncatedData,
        BlobVersionMismatch,
        BlobPayloadMismatch,
        CorruptData,
        PipelineMismatch,
        UnsupportedSpawnMode,
        Other,
    }

    internal sealed class WorldBootstrapPreloadFailureInfo
    {
        public bool IsInterior;
        public string CellLabel;
        public string Path;
        public WorldBootstrapPreloadFailureKind Kind;
        public string Message;
    }

    internal static class WorldBootstrapPreloadUtility
    {
        public static WorldBootstrapPreloadResult PreloadCells(CacheLoader cache)
            => ValidateCells(cache, null);

        public static WorldBootstrapPreloadResult PreloadSandboxCells(CacheLoader cache, SandboxWorldProfile profile)
            => ValidateCells(cache, profile);

        static WorldBootstrapPreloadResult ValidateCells(CacheLoader cache, SandboxWorldProfile profile)
        {
            var cellGrid = cache.Manifest.CellGrid ?? System.Array.Empty<(int X, int Y)>();
            var interiorIds = cache.Manifest.InteriorCellIds ?? System.Array.Empty<string>();
            var exteriorFailures = new WorldBootstrapPreloadFailureInfo[cellGrid.Length];
            var interiorFailures = new WorldBootstrapPreloadFailureInfo[interiorIds.Length];
            var stateByKey = BuildCellStateLookup(cache.Manifest.CellStates);
            var exteriorIndices = BuildExteriorValidationIndices(cellGrid, profile);
            var interiorIndices = BuildInteriorValidationIndices(interiorIds, profile);
            for (int index = 0; index < exteriorIndices.Length; index++)
            {
                int i = exteriorIndices[index];
                var g = cellGrid[i];
                string label = $"({g.X},{g.Y})";
                string path = ResolveCellSectionPath(ResolveCellState(stateByKey, false, g.X, g.Y, null), false, g.X, g.Y, null);
                if (!File.Exists(path))
                {
                    exteriorFailures[i] = CreateMissingFileFailure(false, label, path);
                    continue;
                }

                try
                {
                    ValidateManifestState(ResolveCellState(stateByKey, false, g.X, g.Y, null), false, label, path);
                    RuntimeCellSectionFile.ValidateFile(path, isInterior: false);
                }
                catch (System.Exception ex)
                {
                    exteriorFailures[i] = CreatePreloadFailure(false, label, path, ex);
                }
            }

            for (int index = 0; index < interiorIndices.Length; index++)
            {
                int i = interiorIndices[index];
                string cellId = interiorIds[i] ?? string.Empty;
                string path = ResolveCellSectionPath(ResolveCellState(stateByKey, true, 0, 0, cellId), true, 0, 0, cellId);
                if (!File.Exists(path))
                {
                    interiorFailures[i] = CreateMissingFileFailure(true, cellId, path);
                    continue;
                }

                try
                {
                    ValidateManifestState(ResolveCellState(stateByKey, true, 0, 0, cellId), true, cellId, path);
                    RuntimeCellSectionFile.ValidateFile(path, isInterior: true, cellId: cellId);
                }
                catch (System.Exception ex)
                {
                    interiorFailures[i] = CreatePreloadFailure(true, cellId, path, ex);
                }
            }

            return new WorldBootstrapPreloadResult
            {
                ExteriorFailures = exteriorFailures,
                InteriorFailures = interiorFailures,
            };
        }

        static int[] BuildExteriorValidationIndices((int X, int Y)[] cellGrid, SandboxWorldProfile profile)
        {
            if (profile == null)
            {
                var all = new int[cellGrid.Length];
                for (int i = 0; i < all.Length; i++)
                    all[i] = i;
                return all;
            }

            var required = new HashSet<int2>();
            AddExteriorCellNeighborhood(required, WorldBootstrap.WorldPositionToCell(profile.PlayerStartPosition), profile.PreloadExteriorCellRadius);
            if (profile.GenerateActorInspectionGrid)
                AddExteriorCellNeighborhood(required, profile.ActorInspectionExteriorCell, profile.PreloadExteriorCellRadius);
            if (profile.GenerateCombatFactionTeams || profile.QueueInitialExteriorCells)
                AddExteriorCellNeighborhood(required, profile.CombatExteriorCell, profile.PreloadExteriorCellRadius);
            var spawns = profile.Spawns ?? System.Array.Empty<SandboxSpawnSpec>();
            for (int i = 0; i < spawns.Length; i++)
            {
                if (!spawns[i].IsInterior)
                    required.Add(spawns[i].ExteriorCell);
            }

            var indices = new List<int>();
            for (int i = 0; i < cellGrid.Length; i++)
            {
                if (required.Contains(new int2(cellGrid[i].X, cellGrid[i].Y)))
                    indices.Add(i);
            }
            return indices.ToArray();
        }

        static int[] BuildInteriorValidationIndices(string[] interiorIds, SandboxWorldProfile profile)
        {
            if (profile == null)
            {
                var all = new int[interiorIds.Length];
                for (int i = 0; i < all.Length; i++)
                    all[i] = i;
                return all;
            }

            var required = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var spawns = profile.Spawns ?? System.Array.Empty<SandboxSpawnSpec>();
            for (int i = 0; i < spawns.Length; i++)
            {
                if (spawns[i].IsInterior)
                    required.Add(spawns[i].InteriorCellId ?? string.Empty);
                if (spawns[i].DoorDestination.Enabled && !string.IsNullOrWhiteSpace(spawns[i].DoorDestination.DestinationCellId))
                    required.Add(spawns[i].DoorDestination.DestinationCellId);
            }

            var indices = new List<int>();
            for (int i = 0; i < interiorIds.Length; i++)
            {
                if (required.Contains(interiorIds[i] ?? string.Empty))
                    indices.Add(i);
            }
            return indices.ToArray();
        }

        static void AddExteriorCellNeighborhood(HashSet<int2> cells, int2 center, int radius)
        {
            int clampedRadius = math.max(0, radius);
            for (int y = -clampedRadius; y <= clampedRadius; y++)
            for (int x = -clampedRadius; x <= clampedRadius; x++)
                cells.Add(new int2(center.x + x, center.y + y));
        }

        public static WorldBootstrapPreloadFailureInfo GetFirstPreloadFailure(WorldBootstrapPreloadResult result)
        {
            if (result?.ExteriorFailures != null)
            {
                for (int i = 0; i < result.ExteriorFailures.Length; i++)
                    if (result.ExteriorFailures[i] != null)
                        return result.ExteriorFailures[i];
            }

            if (result?.InteriorFailures != null)
            {
                for (int i = 0; i < result.InteriorFailures.Length; i++)
                    if (result.InteriorFailures[i] != null)
                        return result.InteriorFailures[i];
            }

            return null;
        }

        public static void LogPreloadFailureSummary(WorldBootstrapPreloadResult result, WorldBootstrapPreloadFailureInfo firstFailure)
        {
            int exteriorFailures = 0;
            int interiorFailures = 0;
            var countsByKind = new int[System.Enum.GetValues(typeof(WorldBootstrapPreloadFailureKind)).Length];
            AccumulateFailures(result?.ExteriorFailures, ref exteriorFailures, countsByKind);
            AccumulateFailures(result?.InteriorFailures, ref interiorFailures, countsByKind);

            var sb = new StringBuilder(512);
            sb.Append("[VVardenfell] section validation failed. First failure: ")
                .Append(firstFailure.IsInterior ? "interior '" : "cell ")
                .Append(firstFailure.CellLabel)
                .Append(firstFailure.IsInterior ? "'" : string.Empty)
                .Append(" [")
                .Append(firstFailure.Kind)
                .Append("] ")
                .Append(firstFailure.Path)
                .AppendLine();
            sb.Append("Summary: ")
                .Append(exteriorFailures)
                .Append(" exterior, ")
                .Append(interiorFailures)
                .Append(" interior failures.");
            Debug.LogError(sb.ToString());
        }

        static void ValidateManifestState(BakeManifest.BakedCellState state, bool isInterior, string cellLabel, string path)
        {
            if (state == null)
                throw new InvalidDataException($"missing manifest cell state for {(isInterior ? "interior" : "cell")} {cellLabel}; rebake required.");
            if (state.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"manifest pipeline {state.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebake required.");
            if (string.IsNullOrWhiteSpace(state.SectionPath))
                throw new InvalidDataException($"manifest cell state for {(isInterior ? "interior" : "cell")} {cellLabel} has no section path; rebake required.");
            if (!string.Equals(state.SectionPath, path, System.StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"manifest section path mismatch for {(isInterior ? "interior" : "cell")} {cellLabel}; rebake required.");
        }

        static Dictionary<string, BakeManifest.BakedCellState> BuildCellStateLookup(BakeManifest.BakedCellState[] states)
        {
            var lookup = new Dictionary<string, BakeManifest.BakedCellState>(System.StringComparer.OrdinalIgnoreCase);
            if (states == null)
                return lookup;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null)
                    continue;
                string key = state.IsInterior ? BuildInteriorCellStateKey(state.InteriorId) : BuildExteriorCellStateKey(state.GridX, state.GridY);
                lookup[key] = state;
            }
            return lookup;
        }

        static BakeManifest.BakedCellState ResolveCellState(Dictionary<string, BakeManifest.BakedCellState> stateByKey, bool isInterior, int gridX, int gridY, string interiorId)
        {
            if (stateByKey == null)
                return null;
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

        static WorldBootstrapPreloadFailureInfo CreateMissingFileFailure(bool isInterior, string cellLabel, string path)
        {
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new WorldBootstrapPreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = WorldBootstrapPreloadFailureKind.MissingFile,
                Message = $"[VVardenfell] missing baked {target} DOTS section at '{path}'",
            };
        }

        static WorldBootstrapPreloadFailureInfo CreatePreloadFailure(bool isInterior, string cellLabel, string path, System.Exception ex)
        {
            var kind = ClassifyPreloadFailure(ex);
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new WorldBootstrapPreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = kind,
                Message = $"[VVardenfell] failed validating {target} DOTS section at '{path}': {ex.Message}",
            };
        }

        static WorldBootstrapPreloadFailureKind ClassifyPreloadFailure(System.Exception ex)
        {
            string message = FlattenExceptionMessage(ex);
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                return WorldBootstrapPreloadFailureKind.MissingFile;
            if (message.IndexOf("version mismatch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return WorldBootstrapPreloadFailureKind.BlobVersionMismatch;
            if (message.IndexOf("deserialize", System.StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("blob payload", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return WorldBootstrapPreloadFailureKind.BlobPayloadMismatch;
            if (message.IndexOf("truncated", System.StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("beyond the end of the stream", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return WorldBootstrapPreloadFailureKind.TruncatedData;
            if (message.IndexOf("unsupported spawn mode", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return WorldBootstrapPreloadFailureKind.UnsupportedSpawnMode;
            if (ex is InvalidDataException)
                return WorldBootstrapPreloadFailureKind.CorruptData;
            return WorldBootstrapPreloadFailureKind.Other;
        }

        static string FlattenExceptionMessage(System.Exception ex)
        {
            if (ex == null)
                return string.Empty;
            var sb = new StringBuilder();
            for (var current = ex; current != null; current = current.InnerException)
                sb.Append(current.Message).Append(' ');
            return sb.ToString();
        }

        static void AccumulateFailures(WorldBootstrapPreloadFailureInfo[] failures, ref int total, int[] countsByKind)
        {
            if (failures == null)
                return;
            for (int i = 0; i < failures.Length; i++)
            {
                var failure = failures[i];
                if (failure == null)
                    continue;
                total++;
                int kind = (int)failure.Kind;
                if ((uint)kind < (uint)countsByKind.Length)
                    countsByKind[kind]++;
            }
        }
    }
}
