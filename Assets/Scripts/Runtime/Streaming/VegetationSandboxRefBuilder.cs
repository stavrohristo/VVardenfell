using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Streaming
{
    internal static class VegetationSandboxRefBuilder
    {
        const uint VegetationPlacedRefBase = 0x41000000u;

        enum VegetationKind
        {
            None,
            Tree,
            Bush,
        }

        readonly struct RenderDrawKey : IEquatable<RenderDrawKey>, IComparable<RenderDrawKey>
        {
            readonly int _renderShardIndex;
            readonly int _localMeshIndex;
            readonly int _localMaterialIndex;
            readonly int _sliceIndex;

            public RenderDrawKey(RefEntry entry)
            {
                _renderShardIndex = entry.RenderShardIndex;
                _localMeshIndex = entry.LocalMeshIndex;
                _localMaterialIndex = entry.LocalMaterialIndex;
                _sliceIndex = entry.SliceIndex;
            }

            public bool Equals(RenderDrawKey other)
            {
                return _renderShardIndex == other._renderShardIndex
                    && _localMeshIndex == other._localMeshIndex
                    && _localMaterialIndex == other._localMaterialIndex
                    && _sliceIndex == other._sliceIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RenderDrawKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _renderShardIndex;
                    hash = (hash * 397) ^ _localMeshIndex;
                    hash = (hash * 397) ^ _localMaterialIndex;
                    hash = (hash * 397) ^ _sliceIndex;
                    return hash;
                }
            }

            public int CompareTo(RenderDrawKey other)
            {
                int result = _renderShardIndex.CompareTo(other._renderShardIndex);
                if (result != 0)
                    return result;
                result = _localMaterialIndex.CompareTo(other._localMaterialIndex);
                if (result != 0)
                    return result;
                result = _localMeshIndex.CompareTo(other._localMeshIndex);
                if (result != 0)
                    return result;
                return _sliceIndex.CompareTo(other._sliceIndex);
            }
        }

        readonly struct VegetationCandidate
        {
            public readonly string StaticId;
            public readonly VegetationKind Kind;
            public readonly RefEntry[] Entries;

            public VegetationCandidate(string staticId, VegetationKind kind, RefEntry[] entries)
            {
                StaticId = staticId ?? string.Empty;
                Kind = kind;
                Entries = entries ?? Array.Empty<RefEntry>();
            }
        }

        public static RefEntry[] Build(
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            Dictionary<int2, CellData> exteriorCells,
            SandboxWorldProfile profile)
        {
            if (cache == null)
                throw new InvalidOperationException("[VVardenfell][VegetationSandbox] cache is unavailable.");
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][VegetationSandbox] runtime content database is unavailable.");
            if (profile == null)
                throw new InvalidOperationException("[VVardenfell][VegetationSandbox] profile is unavailable.");
            if (profile.VegetationStressInstanceCount <= 0)
                throw new InvalidOperationException("[VVardenfell][VegetationSandbox] vegetation instance count must be greater than zero.");

            var targetCell = profile.VegetationStressExteriorCell;
            if (exteriorCells == null || !exteriorCells.TryGetValue(targetCell, out var cell) || cell == null)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][VegetationSandbox] target exterior cell ({targetCell.x},{targetCell.y}) is missing from the baked cache.");
            }

            ValidateGrid(profile);

            var sampleByGlobalMesh = BuildSampleByGlobalMesh(cache, exteriorCells);
            var meshIndicesByModel = BuildMeshIndicesByModel(cache.MeshNames, sampleByGlobalMesh);
            var discoveredCandidates = BuildCandidates(contentDb, meshIndicesByModel, sampleByGlobalMesh);
            var candidates = SelectStressCandidates(discoveredCandidates, profile);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "[VVardenfell][VegetationSandbox] no render-shard-compatible tree/bush STAT records were discovered. " +
                    "Rebake from a complete Morrowind cache and ensure vegetation STATs have baked render-shard refs.");
            }

            int totalRefCount = 0;
            int instanceCount = profile.VegetationStressInstanceCount;
            for (int i = 0; i < instanceCount; i++)
                totalRefCount += candidates[i % candidates.Count].Entries.Length;

            var result = new RefEntry[totalRefCount];
            int write = 0;
            int columns = Math.Max(1, profile.VegetationStressGridColumns);
            float spacing = Math.Max(0.01f, profile.VegetationStressGridSpacing);
            for (int i = 0; i < instanceCount; i++)
            {
                var candidate = candidates[i % candidates.Count];
                int column = i % columns;
                int row = i / columns;
                float localX = profile.VegetationStressGridOrigin.x + column * spacing;
                float localZ = profile.VegetationStressGridOrigin.y + row * spacing;
                float height = ResolveHeight(profile, cell, localX, localZ);
                float3 position = SandboxWorldFixtures.ExteriorCellPosition(targetCell, localX, height, localZ);
                quaternion rotation = quaternion.RotateY((i * 2.3999631f) % (math.PI * 2f));
                float scale = 0.85f + ((i * 37) % 31) * 0.01f;
                uint placedRefId = VegetationPlacedRefBase + (uint)i + 1u;

                for (int entryIndex = 0; entryIndex < candidate.Entries.Length; entryIndex++)
                {
                    var entry = candidate.Entries[entryIndex];
                    entry.PlacedRefId = placedRefId;
                    entry.DoorMetaIndex = -1;
                    entry.ContentHandleValue = 0;
                    entry.ContentKind = (int)ContentReferenceKind.None;
                    entry.CollisionIndex = -1;
                    entry.PosX = position.x;
                    entry.PosY = position.y;
                    entry.PosZ = position.z;
                    entry.RotX = rotation.value.x;
                    entry.RotY = rotation.value.y;
                    entry.RotZ = rotation.value.z;
                    entry.RotW = rotation.value.w;
                    entry.Scale = scale;
                    result[write++] = entry;
                }
            }

            int unsortedRenderKeyRuns = CountContiguousRenderKeyRuns(result);
            if (profile.VegetationStressSortRefsByRenderKey)
                Array.Sort(result, CompareRefEntriesByRenderKey);
            int sortedRenderKeyRuns = CountContiguousRenderKeyRuns(result);

            Debug.Log(
                $"[VVardenfell][VegetationSandbox] generated {instanceCount} vegetation instances " +
                $"({result.Length} render refs) in exterior cell ({targetCell.x},{targetCell.y}) from " +
                $"{candidates.Count} selected tree/bush STAT id(s): {FormatCandidateIds(candidates)}. " +
                $"Discovered candidates={discoveredCandidates.Count}, unique render keys={CountUniqueRenderKeys(candidates)}, " +
                $"render refs per cycle={CountRefsPerCandidateCycle(candidates)}, " +
                $"render key runs before sort={unsortedRenderKeyRuns}, after sort={sortedRenderKeyRuns}.");

            return result;
        }

        static void ValidateGrid(SandboxWorldProfile profile)
        {
            int count = profile.VegetationStressInstanceCount;
            int columns = Math.Max(1, profile.VegetationStressGridColumns);
            int rows = (count + columns - 1) / columns;
            float spacing = Math.Max(0.01f, profile.VegetationStressGridSpacing);
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float maxX = profile.VegetationStressGridOrigin.x + (columns - 1) * spacing;
            float maxZ = profile.VegetationStressGridOrigin.y + (rows - 1) * spacing;
            if (maxX >= cellMeters || maxZ >= cellMeters)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][VegetationSandbox] vegetation grid does not fit in one exterior cell: " +
                    $"max local ({maxX:0.###}, {maxZ:0.###}) >= cell size {cellMeters:0.###}.");
            }
        }

        static Dictionary<int, RefEntry> BuildSampleByGlobalMesh(
            CacheLoader cache,
            Dictionary<int2, CellData> exteriorCells)
        {
            var result = new Dictionary<int, RefEntry>();
            var shards = cache.RenderShardCatalog?.Records ?? Array.Empty<RenderShardRecord>();
            foreach (var pair in exteriorCells)
            {
                var refs = pair.Value?.Refs;
                if (refs == null)
                    continue;

                for (int i = 0; i < refs.Length; i++)
                {
                    var entry = refs[i];
                    if (entry.SpawnModeRaw != (int)RefSpawnMode.RenderShard)
                        continue;
                    if ((uint)entry.RenderShardIndex >= (uint)shards.Length)
                        continue;

                    var globalMeshIndices = shards[entry.RenderShardIndex]?.GlobalMeshIndices;
                    if (globalMeshIndices == null || (uint)entry.LocalMeshIndex >= (uint)globalMeshIndices.Length)
                        continue;

                    int globalMesh = globalMeshIndices[entry.LocalMeshIndex];
                    if (globalMesh < 0 || result.ContainsKey(globalMesh))
                        continue;

                    result.Add(globalMesh, entry);
                }
            }

            return result;
        }

        static Dictionary<string, List<int>> BuildMeshIndicesByModel(
            string[] meshNames,
            Dictionary<int, RefEntry> sampleByGlobalMesh)
        {
            var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; meshNames != null && i < meshNames.Length; i++)
            {
                if (!sampleByGlobalMesh.ContainsKey(i))
                    continue;

                string modelKey = NormalizeModelKey(ExtractModelPath(meshNames[i]));
                if (string.IsNullOrEmpty(modelKey))
                    continue;

                if (!result.TryGetValue(modelKey, out var list))
                {
                    list = new List<int>();
                    result.Add(modelKey, list);
                }
                list.Add(i);
            }

            return result;
        }

        static List<VegetationCandidate> BuildCandidates(
            RuntimeContentDatabase contentDb,
            Dictionary<string, List<int>> meshIndicesByModel,
            Dictionary<int, RefEntry> sampleByGlobalMesh)
        {
            var candidates = new List<VegetationCandidate>();
            for (int i = 0; i < contentDb.StaticCount; i++)
            {
                ref readonly var staticDef = ref contentDb.GetStatic(GenericRecordDefHandle.FromIndex(i));
                VegetationKind kind = ResolveVegetationKind(staticDef);
                if (kind == VegetationKind.None)
                    continue;

                string modelKey = NormalizeModelKey(staticDef.Model);
                if (string.IsNullOrEmpty(modelKey) || !meshIndicesByModel.TryGetValue(modelKey, out var meshIndices))
                    continue;

                var entries = new List<RefEntry>(meshIndices.Count);
                for (int meshIndex = 0; meshIndex < meshIndices.Count; meshIndex++)
                {
                    int globalMesh = meshIndices[meshIndex];
                    if (!sampleByGlobalMesh.TryGetValue(globalMesh, out var sample))
                        continue;

                    entries.Add(sample);
                }

                if (entries.Count > 0)
                    candidates.Add(new VegetationCandidate(staticDef.Id, kind, entries.ToArray()));
            }

            candidates.Sort((a, b) => string.Compare(a.StaticId, b.StaticId, StringComparison.OrdinalIgnoreCase));
            return candidates;
        }

        static List<VegetationCandidate> SelectStressCandidates(
            List<VegetationCandidate> discovered,
            SandboxWorldProfile profile)
        {
            int max = Math.Max(1, profile.VegetationStressUniqueStaticCount);
            var selected = new List<VegetationCandidate>(max);

            if (profile.VegetationStressRequireTreeAndBush && max >= 2)
            {
                if (!TryFindFirst(discovered, VegetationKind.Tree, out var tree))
                    throw new InvalidOperationException("[VVardenfell][VegetationSandbox] no render-shard-compatible tree STAT record was discovered.");
                if (!TryFindFirst(discovered, VegetationKind.Bush, out var bush))
                    throw new InvalidOperationException("[VVardenfell][VegetationSandbox] no render-shard-compatible bush STAT record was discovered.");

                selected.Add(tree);
                selected.Add(bush);
            }

            for (int i = 0; i < discovered.Count && selected.Count < max; i++)
            {
                if (!ContainsStaticId(selected, discovered[i].StaticId))
                    selected.Add(discovered[i]);
            }

            return selected;
        }

        static bool TryFindFirst(List<VegetationCandidate> candidates, VegetationKind kind, out VegetationCandidate candidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Kind == kind)
                {
                    candidate = candidates[i];
                    return true;
                }
            }

            candidate = default;
            return false;
        }

        static bool ContainsStaticId(List<VegetationCandidate> candidates, string staticId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].StaticId, staticId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static VegetationKind ResolveVegetationKind(in GenericRecordDef staticDef)
        {
            string id = ContentId.NormalizeId(staticDef.Id);
            string model = NormalizeModelKey(staticDef.Model);
            if (ContainsVegetationStem(id, "tree") || ContainsVegetationStem(model, "tree"))
                return VegetationKind.Tree;
            if (ContainsVegetationStem(id, "bush") || ContainsVegetationStem(model, "bush"))
                return VegetationKind.Bush;
            return VegetationKind.None;
        }

        static bool ContainsVegetationStem(string value, string stem)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.Contains("flora", StringComparison.OrdinalIgnoreCase))
                return false;

            return value.Contains(stem, StringComparison.OrdinalIgnoreCase);
        }

        static int CountUniqueRenderKeys(List<VegetationCandidate> candidates)
        {
            var keys = new HashSet<RenderDrawKey>();
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var entries = candidates[candidateIndex].Entries;
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                    keys.Add(new RenderDrawKey(entries[entryIndex]));
            }

            return keys.Count;
        }

        static int CountRefsPerCandidateCycle(List<VegetationCandidate> candidates)
        {
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
                count += candidates[i].Entries.Length;
            return count;
        }

        static int CountContiguousRenderKeyRuns(RefEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
                return 0;

            int runs = 0;
            var previous = default(RenderDrawKey);
            bool hasPrevious = false;
            for (int i = 0; i < entries.Length; i++)
            {
                var key = new RenderDrawKey(entries[i]);
                if (!hasPrevious || !key.Equals(previous))
                {
                    runs++;
                    previous = key;
                    hasPrevious = true;
                }
            }

            return runs;
        }

        static int CompareRefEntriesByRenderKey(RefEntry left, RefEntry right)
        {
            return new RenderDrawKey(left).CompareTo(new RenderDrawKey(right));
        }

        static float ResolveHeight(SandboxWorldProfile profile, CellData cell, float localX, float localZ)
        {
            if (profile.GroundVegetationStressGrid
                && WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cell, localX, localZ, out float terrainHeight))
            {
                return terrainHeight;
            }

            return profile.VegetationStressGridHeight;
        }

        static string ExtractModelPath(string meshName)
        {
            if (string.IsNullOrWhiteSpace(meshName))
                return string.Empty;

            int separator = meshName.LastIndexOf('#');
            return separator > 0 ? meshName.Substring(0, separator) : meshName;
        }

        static string NormalizeModelKey(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string value = modelPath.Trim().Replace('/', '\\').ToLowerInvariant();
            while (value.Contains("\\\\", StringComparison.Ordinal))
                value = value.Replace("\\\\", "\\", StringComparison.Ordinal);
            if (value.StartsWith("meshes\\", StringComparison.Ordinal))
                value = value.Substring("meshes\\".Length);
            return value;
        }

        static string FormatCandidateIds(List<VegetationCandidate> candidates)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(candidates[i].StaticId);
            }
            return sb.ToString();
        }
    }
}
