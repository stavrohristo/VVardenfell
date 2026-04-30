using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal sealed class WorldBootstrapPreloadResult
    {
        public CellData[] ExteriorCells;
        public WorldBootstrapPreloadFailureInfo[] ExteriorFailures;
        public CellData[] InteriorCells;
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
        {
            var cellGrid = cache.Manifest.CellGrid;
            var loaded = new CellData[cellGrid.Length];
            var failures = new WorldBootstrapPreloadFailureInfo[cellGrid.Length];
            var interiorIds = cache.Manifest.InteriorCellIds ?? System.Array.Empty<string>();
            var loadedInteriors = new CellData[interiorIds.Length];
            var interiorFailures = new WorldBootstrapPreloadFailureInfo[interiorIds.Length];
            var stateByKey = BuildCellStateLookup(cache.Manifest.CellStates);
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = System.Math.Min(8, System.Math.Max(1, System.Environment.ProcessorCount / 2)),
            };

            Parallel.For(0, cellGrid.Length, options, i =>
            {
                var g = cellGrid[i];
                string path = CachePaths.CellFile(g.Item1, g.Item2);
                if (!File.Exists(path))
                {
                    failures[i] = CreateMissingFileFailure(
                        isInterior: false,
                        cellLabel: $"({g.Item1},{g.Item2})",
                        path: path);
                    return;
                }

                try
                {
                    loaded[i] = CellFile.Read(path);
                    failures[i] = ValidatePreloadedCell(
                        loaded[i],
                        ResolveCellState(stateByKey, false, g.Item1, g.Item2, null),
                        false,
                        $"({g.Item1},{g.Item2})",
                        path);
                    if (failures[i] != null)
                    {
                        loaded[i] = null;
                        return;
                    }
                    TryAttachPlacementAudit(loaded[i], CachePaths.CellPlacementAuditFile(g.Item1, g.Item2));
                }
                catch (System.Exception ex)
                {
                    failures[i] = CreatePreloadFailure(
                        isInterior: false,
                        cellLabel: $"({g.Item1},{g.Item2})",
                        path: path,
                        ex: ex);
                }
            });

            Parallel.For(0, interiorIds.Length, options, i =>
            {
                string cellId = interiorIds[i] ?? string.Empty;
                string path = CachePaths.InteriorCellFile(cellId);
                if (!File.Exists(path))
                {
                    interiorFailures[i] = CreateMissingFileFailure(
                        isInterior: true,
                        cellLabel: cellId,
                        path: path);
                    return;
                }

                try
                {
                    loadedInteriors[i] = CellFile.Read(path, isInterior: true, cellId: cellId);
                    interiorFailures[i] = ValidatePreloadedCell(
                        loadedInteriors[i],
                        ResolveCellState(stateByKey, true, 0, 0, cellId),
                        true,
                        cellId,
                        path);
                    if (interiorFailures[i] != null)
                    {
                        loadedInteriors[i] = null;
                        return;
                    }
                    TryAttachPlacementAudit(loadedInteriors[i], CachePaths.InteriorCellPlacementAuditFile(cellId));
                }
                catch (System.Exception ex)
                {
                    interiorFailures[i] = CreatePreloadFailure(
                        isInterior: true,
                        cellLabel: cellId,
                        path: path,
                        ex: ex);
                }
            });

            return new WorldBootstrapPreloadResult
            {
                ExteriorCells = loaded,
                ExteriorFailures = failures,
                InteriorCells = loadedInteriors,
                InteriorFailures = interiorFailures,
            };
        }

        public static WorldBootstrapPreloadResult PreloadSandboxCells(CacheLoader cache, SandboxWorldProfile profile)
        {
            var cellGrid = cache.Manifest.CellGrid;
            var loaded = new CellData[cellGrid.Length];
            var failures = new WorldBootstrapPreloadFailureInfo[cellGrid.Length];
            var interiorIds = cache.Manifest.InteriorCellIds ?? System.Array.Empty<string>();
            var loadedInteriors = new CellData[interiorIds.Length];
            var interiorFailures = new WorldBootstrapPreloadFailureInfo[interiorIds.Length];
            var stateByKey = BuildCellStateLookup(cache.Manifest.CellStates);
            var exteriorIndexByCoord = BuildExteriorIndexLookup(cellGrid);
            var interiorIndexById = BuildInteriorIndexLookup(interiorIds);
            var requiredExterior = new HashSet<int2>();
            var requiredInteriors = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            if (profile != null)
            {
                requiredExterior.Add(WorldBootstrap.WorldPositionToCell(profile.PlayerStartPosition));
                if (profile.GenerateActorInspectionGrid)
                    requiredExterior.Add(profile.ActorInspectionExteriorCell);

                var spawns = profile.Spawns ?? System.Array.Empty<SandboxSpawnSpec>();
                for (int i = 0; i < spawns.Length; i++)
                {
                    var spawn = spawns[i];
                    if (spawn.IsInterior)
                    {
                        requiredInteriors.Add(spawn.InteriorCellId ?? string.Empty);
                    }
                    else
                    {
                        requiredExterior.Add(spawn.ExteriorCell);
                        requiredExterior.Add(WorldBootstrap.WorldPositionToCell(spawn.Position));
                    }

                    if (spawn.DoorDestination.Enabled)
                    {
                        if (!string.IsNullOrWhiteSpace(spawn.DoorDestination.DestinationCellId))
                            requiredInteriors.Add(spawn.DoorDestination.DestinationCellId);
                        else
                            requiredExterior.Add(WorldBootstrap.WorldPositionToCell(spawn.DoorDestination.Position));
                    }
                }
            }

            foreach (var coord in requiredExterior)
            {
                if (!exteriorIndexByCoord.TryGetValue(coord, out int index))
                {
                    Debug.LogWarning($"[VVardenfell][Sandbox] requested exterior cell ({coord.x},{coord.y}) is not present in the baked cache.");
                    continue;
                }

                string path = CachePaths.CellFile(coord.x, coord.y);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[VVardenfell][Sandbox] requested exterior cell ({coord.x},{coord.y}) has no baked cell file at '{path}'.");
                    continue;
                }

                try
                {
                    loaded[index] = CellFile.Read(path);
                    failures[index] = ValidatePreloadedCell(
                        loaded[index],
                        ResolveCellState(stateByKey, false, coord.x, coord.y, null),
                        false,
                        $"({coord.x},{coord.y})",
                        path);
                    if (failures[index] != null)
                        loaded[index] = null;
                }
                catch (System.Exception ex)
                {
                    failures[index] = CreatePreloadFailure(
                        isInterior: false,
                        cellLabel: $"({coord.x},{coord.y})",
                        path: path,
                        ex: ex);
                }
            }

            foreach (string cellId in requiredInteriors)
            {
                string normalizedCellId = cellId ?? string.Empty;
                if (!interiorIndexById.TryGetValue(normalizedCellId, out int index))
                {
                    Debug.LogWarning($"[VVardenfell][Sandbox] requested interior '{normalizedCellId}' is not present in the baked cache.");
                    continue;
                }

                string path = CachePaths.InteriorCellFile(normalizedCellId);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[VVardenfell][Sandbox] requested interior '{normalizedCellId}' has no baked cell file at '{path}'.");
                    continue;
                }

                try
                {
                    loadedInteriors[index] = CellFile.Read(path, isInterior: true, cellId: normalizedCellId);
                    interiorFailures[index] = ValidatePreloadedCell(
                        loadedInteriors[index],
                        ResolveCellState(stateByKey, true, 0, 0, normalizedCellId),
                        true,
                        normalizedCellId,
                        path);
                    if (interiorFailures[index] != null)
                        loadedInteriors[index] = null;
                }
                catch (System.Exception ex)
                {
                    interiorFailures[index] = CreatePreloadFailure(
                        isInterior: true,
                        cellLabel: normalizedCellId,
                        path: path,
                        ex: ex);
                }
            }

            return new WorldBootstrapPreloadResult
            {
                ExteriorCells = loaded,
                ExteriorFailures = failures,
                InteriorCells = loadedInteriors,
                InteriorFailures = interiorFailures,
            };
        }

        public static WorldBootstrapPreloadFailureInfo GetFirstPreloadFailure(WorldBootstrapPreloadResult result)
        {
            if (result?.ExteriorFailures != null)
            {
                for (int i = 0; i < result.ExteriorFailures.Length; i++)
                {
                    if (result.ExteriorFailures[i] != null)
                        return result.ExteriorFailures[i];
                }
            }

            if (result?.InteriorFailures != null)
            {
                for (int i = 0; i < result.InteriorFailures.Length; i++)
                {
                    if (result.InteriorFailures[i] != null)
                        return result.InteriorFailures[i];
                }
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
            sb.Append("[VVardenfell] preload failed during background cache reads. First failure: ")
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

            bool appendedBreakdown = false;
            for (int i = 0; i < countsByKind.Length; i++)
            {
                if (countsByKind[i] == 0)
                    continue;

                if (!appendedBreakdown)
                {
                    sb.Append(" Breakdown:");
                    appendedBreakdown = true;
                }

                sb.Append(' ')
                    .Append((WorldBootstrapPreloadFailureKind)i)
                    .Append('=')
                    .Append(countsByKind[i]);
            }

            Debug.LogError(sb.ToString());
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

                string key = state.IsInterior
                    ? BuildInteriorCellStateKey(state.InteriorId)
                    : BuildExteriorCellStateKey(state.GridX, state.GridY);
                lookup[key] = state;
            }

            return lookup;
        }

        static Dictionary<int2, int> BuildExteriorIndexLookup((int X, int Y)[] cellGrid)
        {
            var lookup = new Dictionary<int2, int>();
            if (cellGrid == null)
                return lookup;

            for (int i = 0; i < cellGrid.Length; i++)
                lookup[new int2(cellGrid[i].X, cellGrid[i].Y)] = i;
            return lookup;
        }

        static Dictionary<string, int> BuildInteriorIndexLookup(string[] interiorIds)
        {
            var lookup = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            if (interiorIds == null)
                return lookup;

            for (int i = 0; i < interiorIds.Length; i++)
                lookup[interiorIds[i] ?? string.Empty] = i;
            return lookup;
        }

        static BakeManifest.BakedCellState ResolveCellState(
            Dictionary<string, BakeManifest.BakedCellState> stateByKey,
            bool isInterior,
            int gridX,
            int gridY,
            string interiorId)
        {
            if (stateByKey == null)
                return null;

            string key = isInterior
                ? BuildInteriorCellStateKey(interiorId)
                : BuildExteriorCellStateKey(gridX, gridY);
            return stateByKey.TryGetValue(key, out var state) ? state : null;
        }

        static string BuildExteriorCellStateKey(int gridX, int gridY) => $"ext:{gridX},{gridY}";

        static string BuildInteriorCellStateKey(string interiorId) => $"int:{(interiorId ?? string.Empty).Trim().ToLowerInvariant()}";

        static WorldBootstrapPreloadFailureInfo ValidatePreloadedCell(
            CellData cell,
            BakeManifest.BakedCellState state,
            bool isInterior,
            string cellLabel,
            string path)
        {
            if (state == null)
            {
                return CreateValidationFailure(
                    isInterior,
                    cellLabel,
                    path,
                    WorldBootstrapPreloadFailureKind.PipelineMismatch,
                    "missing manifest cell state; rebuild the world cache");
            }

            if (state.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
            {
                return CreateValidationFailure(
                    isInterior,
                    cellLabel,
                    path,
                    WorldBootstrapPreloadFailureKind.PipelineMismatch,
                    $"manifest pipeline {state.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebuild the world cache");
            }

            var refs = cell?.Refs ?? System.Array.Empty<RefEntry>();
            for (int i = 0; i < refs.Length; i++)
            {
                int raw = refs[i].SpawnModeRaw;
                if (!IsSupportedWorldSpawnMode(refs[i]))
                {
                    string mode = System.Enum.IsDefined(typeof(RefSpawnMode), raw)
                        ? ((RefSpawnMode)raw).ToString()
                        : $"unknown({raw})";
                    return CreateValidationFailure(
                        isInterior,
                        cellLabel,
                        path,
                        WorldBootstrapPreloadFailureKind.UnsupportedSpawnMode,
                        $"ref {i} uses unsupported spawn mode {mode} for content kind {(ContentReferenceKind)refs[i].ContentKind}");
                }
            }

            return null;
        }

        static bool IsSupportedWorldSpawnMode(in RefEntry entry)
        {
            if (entry.SpawnModeRaw == (int)RefSpawnMode.RenderShard)
                return true;

            if (entry.SpawnModeRaw != (int)RefSpawnMode.ModelPrefab)
                return false;

            return IsObjectAnimationRuntimeEligible((ContentReferenceKind)entry.ContentKind);
        }

        static bool IsObjectAnimationRuntimeEligible(ContentReferenceKind kind)
        {
            return kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Door
                or ContentReferenceKind.Container
                or ContentReferenceKind.Light;
        }

        static void TryAttachPlacementAudit(CellData cell, string auditPath)
        {
            if (cell == null || string.IsNullOrEmpty(auditPath) || !File.Exists(auditPath))
                return;

            try
            {
                cell.PlacementAudit = RefPlacementAuditFile.Read(auditPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VVardenfell] failed reading placement audit '{auditPath}': {ex.Message}");
            }
        }

        static WorldBootstrapPreloadFailureInfo CreateMissingFileFailure(bool isInterior, string cellLabel, string path)
        {
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new WorldBootstrapPreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = WorldBootstrapPreloadFailureKind.MissingFile,
                Message = $"[VVardenfell] missing baked {target} file at '{path}'",
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
                Message = $"[VVardenfell] failed preloading {target} at '{path}': {ex.Message}",
            };
        }

        static WorldBootstrapPreloadFailureInfo CreateValidationFailure(
            bool isInterior,
            string cellLabel,
            string path,
            WorldBootstrapPreloadFailureKind kind,
            string detail)
        {
            string target = isInterior ? $"interior '{cellLabel}'" : $"cell {cellLabel}";
            return new WorldBootstrapPreloadFailureInfo
            {
                IsInterior = isInterior,
                CellLabel = cellLabel,
                Path = path,
                Kind = kind,
                Message = $"[VVardenfell] invalid baked {target} at '{path}': {detail}",
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
            if (ex is InvalidDataException)
                return WorldBootstrapPreloadFailureKind.CorruptData;
            return WorldBootstrapPreloadFailureKind.Other;
        }

        static string FlattenExceptionMessage(System.Exception ex)
        {
            if (ex == null)
                return string.Empty;

            var sb = new StringBuilder(256);
            for (var cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");
                sb.Append(cursor.Message);
            }
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
                countsByKind[(int)failure.Kind]++;
            }
        }
    }
}
