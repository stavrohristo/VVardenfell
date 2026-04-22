using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VVardenfell.Core.Cache
{
    [Serializable]
    public sealed class RenderShardRecord
    {
        public int BucketKey;
        public string FamilyKey;
        public int PageIndex;
        public int[] GlobalMeshIndices;
    }

    public sealed class RenderShardCatalogData
    {
        public RenderShardRecord[] Records;
    }

    public static class RenderShardFile
    {
        const uint Magic = 0x44524853u; // 'SHRD'
        const uint Version = 2u;

        public static bool TryRead(string path, out RenderShardCatalogData data)
        {
            data = null;
            if (!File.Exists(path))
                return false;

            try
            {
                data = Read(path);
                return true;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static RenderShardCatalogData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad render shard magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported render shard version {version} in '{path}'.");

            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Negative render shard count {count} in '{path}'.");

            var records = new RenderShardRecord[count];
            for (int i = 0; i < count; i++)
            {
                int meshCount = r.ReadInt32();
                if (meshCount < 0)
                    throw new InvalidDataException($"Negative render shard mesh count {meshCount} in '{path}'.");

                var globalMeshIndices = new int[meshCount];
                for (int m = 0; m < meshCount; m++)
                    globalMeshIndices[m] = r.ReadInt32();

                records[i] = new RenderShardRecord
                {
                    BucketKey = r.ReadInt32(),
                    FamilyKey = r.ReadString(),
                    PageIndex = r.ReadInt32(),
                    GlobalMeshIndices = globalMeshIndices,
                };
            }

            return new RenderShardCatalogData
            {
                Records = records,
            };
        }

        public static void Write(string path, RenderShardCatalogData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);

            var records = data?.Records ?? Array.Empty<RenderShardRecord>();
            w.Write(records.Length);
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var globalMeshIndices = record?.GlobalMeshIndices ?? Array.Empty<int>();
                w.Write(globalMeshIndices.Length);
                for (int m = 0; m < globalMeshIndices.Length; m++)
                    w.Write(globalMeshIndices[m]);

                w.Write(record?.BucketKey ?? 0);
                w.Write(record?.FamilyKey ?? string.Empty);
                w.Write(record?.PageIndex ?? 0);
            }
        }

        public static void LogShardStats(RenderShardCatalogData data, string contextLabel)
        {
            var records = data?.Records ?? Array.Empty<RenderShardRecord>();
            if (records.Length == 0)
            {
                UnityEngine.Debug.Log($"[VVardenfell] render shards ({contextLabel}): 0");
                return;
            }

            int maxMeshes = 0;
            var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var bucketCounts = new Dictionary<int, int>();
            var meshCountByFamily = new Dictionary<string, int>(StringComparer.Ordinal);
            var warnings = new List<string>();
            var topFamilies = new List<KeyValuePair<string, int>>();
            var topShards = new List<(int Index, string FamilyKey, int MeshCount, int BucketKey, int PageIndex)>();
            int overflowFamilyCount = 0;
            int overflowShardCount = 0;

            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                int meshCount = record?.GlobalMeshIndices?.Length ?? 0;
                if (meshCount > maxMeshes)
                    maxMeshes = meshCount;

                string familyKey = record?.FamilyKey ?? string.Empty;
                familyCounts.TryGetValue(familyKey, out int familyCount);
                familyCounts[familyKey] = familyCount + 1;
                meshCountByFamily.TryGetValue(familyKey, out int familyMeshCount);
                meshCountByFamily[familyKey] = familyMeshCount + meshCount;

                int bucketKey = record?.BucketKey ?? 0;
                bucketCounts.TryGetValue(bucketKey, out int bucketCount);
                bucketCounts[bucketKey] = bucketCount + 1;

                if (meshCount == 0)
                    warnings.Add($"empty shard[{i}] family='{familyKey}'");

                InsertTopShard(topShards, (i, familyKey, meshCount, bucketKey, record?.PageIndex ?? 0), 5);
            }

            int maxFamilyPages = 0;
            foreach (var pair in familyCounts)
            {
                maxFamilyPages = Math.Max(maxFamilyPages, pair.Value);
                if (pair.Value > 1)
                {
                    overflowFamilyCount++;
                    overflowShardCount += pair.Value - 1;
                }

                InsertTopFamily(topFamilies, pair, 5);
            }

            var summary = new System.Text.StringBuilder(256);
            summary.Append("[VVardenfell] render shards (")
                .Append(contextLabel)
                .Append("): count=")
                .Append(records.Length)
                .Append(", largestMeshCount=")
                .Append(maxMeshes)
                .Append(", largestFamilyPages=")
                .Append(maxFamilyPages)
                .Append(", familyCount=")
                .Append(familyCounts.Count)
                .Append(", bucketCount=")
                .Append(bucketCounts.Count)
                .Append(", overflowFamilies=")
                .Append(overflowFamilyCount)
                .Append(", overflowShards=")
                .Append(overflowShardCount);

            if (warnings.Count > 0)
            {
                summary.Append(" warnings=");
                summary.Append(string.Join("; ", warnings));
            }

            UnityEngine.Debug.Log(summary.ToString());

            var familySummary = new StringBuilder(256);
            familySummary.Append("[VVardenfell] render shard families (")
                .Append(contextLabel)
                .Append("): ");
            AppendTopFamilies(familySummary, topFamilies, meshCountByFamily);
            UnityEngine.Debug.Log(familySummary.ToString());

            var shardSummary = new StringBuilder(256);
            shardSummary.Append("[VVardenfell] largest render shards (")
                .Append(contextLabel)
                .Append("): ");
            AppendTopShards(shardSummary, topShards);
            UnityEngine.Debug.Log(shardSummary.ToString());

            var bucketSummary = new StringBuilder(256);
            bucketSummary.Append("[VVardenfell] shard bucket distribution (")
                .Append(contextLabel)
                .Append("): ");
            AppendBucketDistribution(bucketSummary, bucketCounts, 8);
            UnityEngine.Debug.Log(bucketSummary.ToString());
        }

        static void InsertTopFamily(List<KeyValuePair<string, int>> topFamilies, KeyValuePair<string, int> candidate, int limit)
        {
            int insertIndex = topFamilies.Count;
            for (int i = 0; i < topFamilies.Count; i++)
            {
                if (candidate.Value > topFamilies[i].Value)
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex < topFamilies.Count)
                topFamilies.Insert(insertIndex, candidate);
            else if (topFamilies.Count < limit)
                topFamilies.Add(candidate);

            if (topFamilies.Count > limit)
                topFamilies.RemoveAt(topFamilies.Count - 1);
        }

        static void InsertTopShard(List<(int Index, string FamilyKey, int MeshCount, int BucketKey, int PageIndex)> topShards, (int Index, string FamilyKey, int MeshCount, int BucketKey, int PageIndex) candidate, int limit)
        {
            int insertIndex = topShards.Count;
            for (int i = 0; i < topShards.Count; i++)
            {
                if (candidate.MeshCount > topShards[i].MeshCount)
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex < topShards.Count)
                topShards.Insert(insertIndex, candidate);
            else if (topShards.Count < limit)
                topShards.Add(candidate);

            if (topShards.Count > limit)
                topShards.RemoveAt(topShards.Count - 1);
        }

        static void AppendTopFamilies(StringBuilder builder, List<KeyValuePair<string, int>> topFamilies, Dictionary<string, int> meshCountByFamily)
        {
            if (topFamilies.Count == 0)
            {
                builder.Append("none");
                return;
            }

            for (int i = 0; i < topFamilies.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");

                var pair = topFamilies[i];
                meshCountByFamily.TryGetValue(pair.Key ?? string.Empty, out int meshCount);
                builder.Append(pair.Key)
                    .Append(" pages=")
                    .Append(pair.Value)
                    .Append(" meshes=")
                    .Append(meshCount);
            }
        }

        static void AppendTopShards(StringBuilder builder, List<(int Index, string FamilyKey, int MeshCount, int BucketKey, int PageIndex)> topShards)
        {
            if (topShards.Count == 0)
            {
                builder.Append("none");
                return;
            }

            for (int i = 0; i < topShards.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");

                var shard = topShards[i];
                builder.Append('#')
                    .Append(shard.Index)
                    .Append(" family=")
                    .Append(shard.FamilyKey)
                    .Append(" meshes=")
                    .Append(shard.MeshCount)
                    .Append(" bucket=0x")
                    .Append(shard.BucketKey.ToString("X8"))
                    .Append(" page=")
                    .Append(shard.PageIndex);
            }
        }

        static void AppendBucketDistribution(StringBuilder builder, Dictionary<int, int> bucketCounts, int limit)
        {
            if (bucketCounts.Count == 0)
            {
                builder.Append("none");
                return;
            }

            var entries = new List<KeyValuePair<int, int>>(bucketCounts);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            int count = Math.Min(limit, entries.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append("; ");

                builder.Append("0x")
                    .Append(entries[i].Key.ToString("X8"))
                    .Append('=')
                    .Append(entries[i].Value);
            }
        }
    }
}
