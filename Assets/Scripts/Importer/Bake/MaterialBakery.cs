using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Dedupes materials and writes <c>materials.bin</c>.
    ///
    /// Since FormatVersion 13 the only axis that produces a distinct material is
    /// the alpha blend/test state (plus packed clip threshold). Every ref samples
    /// a shared <c>Texture2DArray</c> at runtime with a per-instance slice index,
    /// so textures and solid-color fallbacks no longer fork the material table.
    /// Typical output: 1–3 records.
    ///
    /// File layout:
    ///   u32 magic 'MATL'
    ///   u32 count
    ///   MaterialRecord[count]   (4 bytes each: Flags only)
    /// </summary>
    public sealed class MaterialBakery
    {
        public const uint MagicMat = 0x4C54414Du; // 'MATL'

        private readonly Dictionary<uint, int> _indexByFlags = new Dictionary<uint, int>();
        private readonly List<MaterialRecord> _records = new List<MaterialRecord>();

        public int Count => _records.Count;

        public int AddOrGet(uint flags)
        {
            if (_indexByFlags.TryGetValue(flags, out var existing)) return existing;
            int idx = _records.Count;
            _records.Add(new MaterialRecord { Flags = flags });
            _indexByFlags[flags] = idx;
            return idx;
        }

        public void WriteTo(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicMat);
            w.Write((uint)_records.Count);
            foreach (var m in _records) w.Write(m.Flags);
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
