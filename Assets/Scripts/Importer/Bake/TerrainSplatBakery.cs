using System;
using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    public sealed class TerrainSplatBakery
    {
        readonly object _gate = new object();
        readonly Dictionary<string, int> _indexByHash = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<ushort[]> _slices = new List<ushort[]>();

        public int Count => _slices.Count;
        public bool Modified { get; private set; }
        public bool ExistingCacheInvalid { get; private set; }

        public void TryLoadExisting(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                ushort[][] slices = TerrainSplatFile.Read(path);
                lock (_gate)
                {
                    _indexByHash.Clear();
                    _slices.Clear();
                    for (int i = 0; i < slices.Length; i++)
                    {
                        string hash = Hash(slices[i]);
                        if (_indexByHash.ContainsKey(hash))
                            continue;
                        _indexByHash.Add(hash, _slices.Count);
                        _slices.Add(slices[i]);
                    }
                    Modified = false;
                    ExistingCacheInvalid = false;
                }
            }
            catch
            {
                lock (_gate)
                {
                    _indexByHash.Clear();
                    _slices.Clear();
                    Modified = true;
                    ExistingCacheInvalid = true;
                }
            }
        }

        public int AddOrGet(ushort[] layerGrid)
        {
            if (layerGrid == null || layerGrid.Length != TerrainSplatFile.SampleCount)
                throw new InvalidDataException($"Terrain splat layer grid has {(layerGrid == null ? 0 : layerGrid.Length)} samples; expected {TerrainSplatFile.SampleCount}.");

            string hash = Hash(layerGrid);
            lock (_gate)
            {
                if (_indexByHash.TryGetValue(hash, out int existing))
                    return existing;

                int index = _slices.Count;
                var copy = new ushort[layerGrid.Length];
                Buffer.BlockCopy(layerGrid, 0, copy, 0, layerGrid.Length * sizeof(ushort));
                _indexByHash.Add(hash, index);
                _slices.Add(copy);
                Modified = true;
                return index;
            }
        }

        public void WriteTo(string path)
        {
            lock (_gate)
            {
                TerrainSplatFile.Write(path, _slices);
                Modified = false;
                ExistingCacheInvalid = false;
            }
        }

        static string Hash(ushort[] values)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < values.Length; i++)
                {
                    ushort value = values[i];
                    hash ^= (byte)value;
                    hash *= 16777619u;
                    hash ^= (byte)(value >> 8);
                    hash *= 16777619u;
                }
                return hash.ToString("x8");
            }
        }
    }
}
