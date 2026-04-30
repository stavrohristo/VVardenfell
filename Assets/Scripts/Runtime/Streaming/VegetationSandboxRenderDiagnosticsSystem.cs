using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellLoadWorkerSystem))]
    public partial class VegetationSandboxRenderDiagnosticsSystem : SystemBase
    {
        readonly struct RenderKey : IEquatable<RenderKey>
        {
            public readonly int RenderMeshArrayIndex;
            public readonly int Material;
            public readonly int Mesh;
            public readonly ushort SubMesh;
            public readonly int Slice;

            public RenderKey(int renderMeshArrayIndex, MaterialMeshInfo materialMesh, TextureSlice textureSlice)
            {
                RenderMeshArrayIndex = renderMeshArrayIndex;
                Material = materialMesh.Material;
                Mesh = materialMesh.Mesh;
                SubMesh = materialMesh.SubMesh;
                Slice = Mathf.RoundToInt(textureSlice.Value);
            }

            public bool Equals(RenderKey other)
            {
                return RenderMeshArrayIndex == other.RenderMeshArrayIndex
                    && Material == other.Material
                    && Mesh == other.Mesh
                    && SubMesh == other.SubMesh
                    && Slice == other.Slice;
            }

            public override bool Equals(object obj)
            {
                return obj is RenderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RenderMeshArrayIndex;
                    hash = (hash * 397) ^ Material;
                    hash = (hash * 397) ^ Mesh;
                    hash = (hash * 397) ^ SubMesh;
                    hash = (hash * 397) ^ Slice;
                    return hash;
                }
            }

            public override string ToString()
            {
                return $"rma={RenderMeshArrayIndex},mat={Material},mesh={Mesh},sub={SubMesh},slice={Slice}";
            }
        }

        struct RenderKeyStats
        {
            public int TotalEntities;
            public int EnabledEntities;
            public int EnabledChunks;
            public int EnabledRuns;
        }

        EntityQuery _query;
        bool _logged;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CellLink>(),
                    ComponentType.ReadOnly<TextureSlice>(),
                    ComponentType.ReadOnly<MaterialMeshInfo>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<CellCoord>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
        }

        protected override void OnUpdate()
        {
            if (_logged || WorldResources.RuntimeMode != BootstrapRuntimeMode.VegetationSandbox)
                return;

            EntityManager.CompleteDependencyBeforeRO<MaterialMeshInfo>();
            using var chunks = _query.ToArchetypeChunkArray(Allocator.Temp);
            if (chunks.Length == 0)
                return;

            var materialMeshHandle = GetComponentTypeHandle<MaterialMeshInfo>(isReadOnly: true);
            var textureSliceHandle = GetComponentTypeHandle<TextureSlice>(isReadOnly: true);
            var renderMeshArrayHandle = GetSharedComponentTypeHandle<RenderMeshArray>();
            var statsByKey = new Dictionary<RenderKey, RenderKeyStats>();
            var keysInChunk = new HashSet<RenderKey>();

            int totalEntities = 0;
            int enabledEntities = 0;
            int enabledChunks = 0;
            int enabledRuns = 0;
            int maxEnabledEntitiesPerChunk = 0;

            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var materialMeshes = chunk.GetNativeArray(ref materialMeshHandle);
                var textureSlices = chunk.GetNativeArray(ref textureSliceHandle);
                int renderMeshArrayIndex = chunk.GetSharedComponentIndex(renderMeshArrayHandle);

                keysInChunk.Clear();
                bool chunkHasEnabled = false;
                int chunkEnabledEntities = 0;
                bool hasPreviousKey = false;
                var previousKey = default(RenderKey);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var key = new RenderKey(renderMeshArrayIndex, materialMeshes[i], textureSlices[i]);
                    AddTotalEntity(statsByKey, key);
                    totalEntities++;

                    if (!chunk.IsComponentEnabled(ref materialMeshHandle, i))
                    {
                        hasPreviousKey = false;
                        continue;
                    }

                    chunkHasEnabled = true;
                    chunkEnabledEntities++;
                    enabledEntities++;
                    keysInChunk.Add(key);
                    AddEnabledEntity(statsByKey, key);

                    if (!hasPreviousKey || !key.Equals(previousKey))
                    {
                        enabledRuns++;
                        AddEnabledRun(statsByKey, key);
                        previousKey = key;
                        hasPreviousKey = true;
                    }
                }

                if (!chunkHasEnabled)
                    continue;

                enabledChunks++;
                maxEnabledEntitiesPerChunk = Math.Max(maxEnabledEntitiesPerChunk, chunkEnabledEntities);
                foreach (var key in keysInChunk)
                    AddEnabledChunk(statsByKey, key);
            }

            if (enabledEntities == 0)
                return;

            _logged = true;
            Debug.Log(BuildReport(
                totalEntities,
                enabledEntities,
                chunks.Length,
                enabledChunks,
                enabledRuns,
                maxEnabledEntitiesPerChunk,
                statsByKey));
        }

        static void AddTotalEntity(Dictionary<RenderKey, RenderKeyStats> statsByKey, RenderKey key)
        {
            statsByKey.TryGetValue(key, out var stats);
            stats.TotalEntities++;
            statsByKey[key] = stats;
        }

        static void AddEnabledEntity(Dictionary<RenderKey, RenderKeyStats> statsByKey, RenderKey key)
        {
            statsByKey.TryGetValue(key, out var stats);
            stats.EnabledEntities++;
            statsByKey[key] = stats;
        }

        static void AddEnabledChunk(Dictionary<RenderKey, RenderKeyStats> statsByKey, RenderKey key)
        {
            statsByKey.TryGetValue(key, out var stats);
            stats.EnabledChunks++;
            statsByKey[key] = stats;
        }

        static void AddEnabledRun(Dictionary<RenderKey, RenderKeyStats> statsByKey, RenderKey key)
        {
            statsByKey.TryGetValue(key, out var stats);
            stats.EnabledRuns++;
            statsByKey[key] = stats;
        }

        static string BuildReport(
            int totalEntities,
            int enabledEntities,
            int totalChunks,
            int enabledChunks,
            int enabledRuns,
            int maxEnabledEntitiesPerChunk,
            Dictionary<RenderKey, RenderKeyStats> statsByKey)
        {
            var entries = new List<KeyValuePair<RenderKey, RenderKeyStats>>(statsByKey);
            entries.Sort((a, b) => b.Value.EnabledRuns.CompareTo(a.Value.EnabledRuns));

            var sb = new StringBuilder();
            sb.Append("[VVardenfell][VegetationSandboxRenderDiag] ");
            sb.Append("totalEntities=").Append(totalEntities);
            sb.Append(", enabledEntities=").Append(enabledEntities);
            sb.Append(", totalChunks=").Append(totalChunks);
            sb.Append(", enabledChunks=").Append(enabledChunks);
            sb.Append(", uniqueKeys=").Append(statsByKey.Count);
            sb.Append(", enabledKeyRuns=").Append(enabledRuns);
            sb.Append(", maxEnabledEntitiesPerChunk=").Append(maxEnabledEntitiesPerChunk);
            sb.Append(". Top keys by enabled runs: ");

            int limit = Math.Min(8, entries.Count);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0)
                    sb.Append(" | ");

                var pair = entries[i];
                var stats = pair.Value;
                sb.Append('[').Append(pair.Key).Append(": entities=")
                    .Append(stats.EnabledEntities).Append('/').Append(stats.TotalEntities)
                    .Append(", chunks=").Append(stats.EnabledChunks)
                    .Append(", runs=").Append(stats.EnabledRuns)
                    .Append(']');
            }

            return sb.ToString();
        }
    }
}
