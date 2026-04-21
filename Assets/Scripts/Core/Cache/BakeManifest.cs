using System.IO;

namespace VVardenfell.Core.Cache
{
    /// <summary>
    /// Top-level cache descriptor. Tracks source-file identity so we can invalidate
    /// when the user's Morrowind install changes, and a format version we bump
    /// whenever the binary layout changes.
    /// </summary>
    public sealed class BakeManifest
    {
        public uint FormatVersion;
        public long EsmSize;
        public long EsmMtimeTicks;
        public long BsaSize;
        public long BsaMtimeTicks;
        public int MeshCount;
        public int MaterialCount;
        public int TextureCount;
        public int CellCount;
        /// <summary>Length-prefixed list of baked cell grid coords (x, y pairs).</summary>
        public (int X, int Y)[] CellGrid;

        public static BakeManifest FromCurrentSources(string esmPath, string bsaPath)
        {
            var esm = new FileInfo(esmPath);
            var bsa = new FileInfo(bsaPath);
            return new BakeManifest
            {
                FormatVersion = CacheFormat.FormatVersion,
                EsmSize = esm.Length,
                EsmMtimeTicks = esm.LastWriteTimeUtc.Ticks,
                BsaSize = bsa.Length,
                BsaMtimeTicks = bsa.LastWriteTimeUtc.Ticks,
            };
        }

        public bool SourcesMatch(string esmPath, string bsaPath)
        {
            if (!File.Exists(esmPath) || !File.Exists(bsaPath)) return false;
            var esm = new FileInfo(esmPath);
            var bsa = new FileInfo(bsaPath);
            return esm.Length == EsmSize
                && esm.LastWriteTimeUtc.Ticks == EsmMtimeTicks
                && bsa.Length == BsaSize
                && bsa.LastWriteTimeUtc.Ticks == BsaMtimeTicks
                && FormatVersion == CacheFormat.FormatVersion;
        }

        public void Write(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(CacheFormat.Magic);
            w.Write(FormatVersion);
            w.Write(EsmSize);
            w.Write(EsmMtimeTicks);
            w.Write(BsaSize);
            w.Write(BsaMtimeTicks);
            w.Write(MeshCount);
            w.Write(MaterialCount);
            w.Write(TextureCount);
            w.Write(CellCount);
            for (int i = 0; i < CellCount; i++)
            {
                w.Write(CellGrid[i].X);
                w.Write(CellGrid[i].Y);
            }
        }

        public static bool TryRead(string path, out BakeManifest manifest)
        {
            manifest = null;
            if (!File.Exists(path)) return false;
            try
            {
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != CacheFormat.Magic) return false;
                var m = new BakeManifest
                {
                    FormatVersion = r.ReadUInt32(),
                    EsmSize = r.ReadInt64(),
                    EsmMtimeTicks = r.ReadInt64(),
                    BsaSize = r.ReadInt64(),
                    BsaMtimeTicks = r.ReadInt64(),
                    MeshCount = r.ReadInt32(),
                    MaterialCount = r.ReadInt32(),
                    TextureCount = r.ReadInt32(),
                    CellCount = r.ReadInt32(),
                };
                m.CellGrid = new (int, int)[m.CellCount];
                for (int i = 0; i < m.CellCount; i++)
                    m.CellGrid[i] = (r.ReadInt32(), r.ReadInt32());
                manifest = m;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
