using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Dedupes materials and preserves stable append-only indices across runs.
    /// </summary>
    public sealed class MaterialBakery
    {
        public const uint MagicMat = 0x4C54414Du; // 'MATL'
        private const uint MagicCatalog = 0x54414341u; // 'ACAT'

        private readonly object _gate = new object();
        private readonly Dictionary<uint, int> _indexByFlags = new Dictionary<uint, int>();
        private readonly List<MaterialRecord> _records = new List<MaterialRecord>();

        public int Count => _records.Count;
        public bool Modified { get; private set; }

        public void TryLoadExisting(string catalogPath)
        {
            if (!File.Exists(catalogPath))
                return;

            try
            {
                using var fs = File.OpenRead(catalogPath);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != MagicCatalog)
                    return;

                uint count = r.ReadUInt32();
                for (int i = 0; i < count; i++)
                {
                    uint flags = r.ReadUInt32();
                    int index = _records.Count;
                    _records.Add(new MaterialRecord { Flags = flags });
                    _indexByFlags[flags] = index;
                }
            }
            catch
            {
                _indexByFlags.Clear();
                _records.Clear();
            }
        }

        public int AddOrGet(uint flags)
        {
            lock (_gate)
            {
                if (_indexByFlags.TryGetValue(flags, out var existing))
                    return existing;
                int idx = _records.Count;
                _records.Add(new MaterialRecord { Flags = flags });
                _indexByFlags[flags] = idx;
                Modified = true;
                return idx;
            }
        }

        public uint GetFlags(int index)
        {
            lock (_gate)
            {
                if ((uint)index >= (uint)_records.Count)
                    throw new InvalidDataException($"Material index {index} is outside catalog length {_records.Count}.");
                return _records[index].Flags;
            }
        }

        public void WriteCatalog(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCatalog);
            w.Write((uint)_records.Count);
            foreach (var record in _records)
                w.Write(record.Flags);
            Modified = false;
        }

        public void WriteTo(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicMat);
            w.Write((uint)_records.Count);
            foreach (var m in _records)
                w.Write(m.Flags);
        }

        public static MaterialRecord[] ReadAll(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicMat)
                throw new InvalidDataException($"Bad magic in {path}");
            uint n = r.ReadUInt32();
            var arr = new MaterialRecord[n];
            for (int i = 0; i < n; i++)
                arr[i] = new MaterialRecord { Flags = r.ReadUInt32() };
            return arr;
        }
    }
}
