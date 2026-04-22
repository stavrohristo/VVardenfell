using System;
using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    public sealed class RenderShardBakery
    {
        public readonly struct Assignment
        {
            public readonly int RenderShardIndex;
            public readonly int LocalMeshIndex;

            public Assignment(int renderShardIndex, int localMeshIndex)
            {
                RenderShardIndex = renderShardIndex;
                LocalMeshIndex = localMeshIndex;
            }
        }

        sealed class ShardState
        {
            public RenderShardRecord Record;
            public Dictionary<int, int> LocalMeshByGlobal = new();
        }

        readonly List<ShardState> _shards = new();
        readonly Dictionary<string, List<int>> _shardIndicesByKey = new(StringComparer.Ordinal);

        public int Count => _shards.Count;
        public bool Modified { get; private set; }
        public int MaxMeshesPerShard { get; }

        public RenderShardBakery(int maxMeshesPerShard = 2048)
        {
            MaxMeshesPerShard = Math.Max(64, maxMeshesPerShard);
        }

        public void TryLoadExisting(string path)
        {
            if (!RenderShardFile.TryRead(path, out var data) || data?.Records == null)
                return;

            var records = data.Records;
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var state = new ShardState
                {
                    Record = new RenderShardRecord
                    {
                        BucketKey = record?.BucketKey ?? 0,
                        FamilyKey = record?.FamilyKey ?? string.Empty,
                        PageIndex = record?.PageIndex ?? 0,
                        GlobalMeshIndices = record?.GlobalMeshIndices ?? Array.Empty<int>(),
                    }
                };

                for (int m = 0; m < state.Record.GlobalMeshIndices.Length; m++)
                    state.LocalMeshByGlobal[state.Record.GlobalMeshIndices[m]] = m;

                int shardIndex = _shards.Count;
                _shards.Add(state);

                string key = ComposeKey(state.Record.BucketKey, state.Record.FamilyKey);
                if (!_shardIndicesByKey.TryGetValue(key, out var shardIndices))
                    _shardIndicesByKey[key] = shardIndices = new List<int>();
                shardIndices.Add(shardIndex);
            }
        }

        public Assignment GetOrAddAssignment(int bucketKey, string familyKey, int globalMeshIndex)
        {
            familyKey ??= string.Empty;
            string key = ComposeKey(bucketKey, familyKey);
            if (!_shardIndicesByKey.TryGetValue(key, out var shardIndices))
                _shardIndicesByKey[key] = shardIndices = new List<int>();

            for (int i = 0; i < shardIndices.Count; i++)
            {
                int shardIndex = shardIndices[i];
                var state = _shards[shardIndex];
                if (state.LocalMeshByGlobal.TryGetValue(globalMeshIndex, out int localMeshIndex))
                    return new Assignment(shardIndex, localMeshIndex);
            }

            for (int i = 0; i < shardIndices.Count; i++)
            {
                int shardIndex = shardIndices[i];
                var state = _shards[shardIndex];
                if (state.Record.GlobalMeshIndices.Length >= MaxMeshesPerShard)
                    continue;

                int localMeshIndex = state.Record.GlobalMeshIndices.Length;
                Array.Resize(ref state.Record.GlobalMeshIndices, localMeshIndex + 1);
                state.Record.GlobalMeshIndices[localMeshIndex] = globalMeshIndex;
                state.LocalMeshByGlobal[globalMeshIndex] = localMeshIndex;
                Modified = true;
                return new Assignment(shardIndex, localMeshIndex);
            }

            int pageIndex = shardIndices.Count;
            var newState = new ShardState
            {
                Record = new RenderShardRecord
                {
                    BucketKey = bucketKey,
                    FamilyKey = familyKey,
                    PageIndex = pageIndex,
                    GlobalMeshIndices = new[] { globalMeshIndex },
                }
            };
            newState.LocalMeshByGlobal[globalMeshIndex] = 0;

            int newShardIndex = _shards.Count;
            _shards.Add(newState);
            shardIndices.Add(newShardIndex);
            Modified = true;
            return new Assignment(newShardIndex, 0);
        }

        public RenderShardCatalogData BuildCatalog()
        {
            var records = new RenderShardRecord[_shards.Count];
            for (int i = 0; i < _shards.Count; i++)
            {
                var record = _shards[i].Record;
                var meshIndices = new int[record.GlobalMeshIndices.Length];
                Array.Copy(record.GlobalMeshIndices, meshIndices, meshIndices.Length);
                records[i] = new RenderShardRecord
                {
                    BucketKey = record.BucketKey,
                    FamilyKey = record.FamilyKey,
                    PageIndex = record.PageIndex,
                    GlobalMeshIndices = meshIndices,
                };
            }

            return new RenderShardCatalogData
            {
                Records = records,
            };
        }

        static string ComposeKey(int bucketKey, string familyKey)
            => bucketKey.ToString() + "|" + (familyKey ?? string.Empty);

        public static string NormalizeFamilyKey(string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(sourceLabel))
                return "__root__";

            string normalized = sourceLabel.Trim().Replace('\\', '/').ToLowerInvariant();
            int submeshSeparator = normalized.LastIndexOf('#');
            if (submeshSeparator > 0)
                normalized = normalized.Substring(0, submeshSeparator);

            while (normalized.Contains("//", StringComparison.Ordinal))
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

            normalized = normalized.Trim('/');
            if (string.IsNullOrEmpty(normalized))
                return "__root__";

            string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
                return "__root__";

            directory = directory.Trim('/');
            return string.IsNullOrEmpty(directory) ? "__root__" : directory;
        }

        public static int PackBucketKey(int width, int height)
            => (Math.Max(1, width) << 16) | (Math.Max(1, height) & 0xFFFF);
    }
}
