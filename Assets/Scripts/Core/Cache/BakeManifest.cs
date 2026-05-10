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
        public sealed class SourceState
        {
            public string Path;
            public long Size;
            public long MtimeTicks;
        }

        public sealed class BakedCellState
        {
            public string Key;
            public string SectionPath;
            public string Fingerprint;
            public uint PipelineVersion;
            public bool IsInterior;
            public int GridX;
            public int GridY;
            public string InteriorId;
            public int[] MeshIndices;
            public int[] MaterialIndices;
            public int[] TextureIndices;
            public int[] CollisionIndices;
            public int[] TerrainLayerIndices;
        }

        public uint FormatVersion;
        public long EsmSize;
        public long EsmMtimeTicks;
        public long BsaSize;
        public long BsaMtimeTicks;
        public string GameplaySourcesHash;
        public SourceState[] Sources;
        public int MeshCount;
        public int MaterialCount;
        public int TextureCount;
        public int CollisionCount;
        public int CellCount;
        /// <summary>Length-prefixed list of baked cell grid coords (x, y pairs).</summary>
        public (int X, int Y)[] CellGrid;
        public int InteriorCellCount;
        /// <summary>Length-prefixed list of baked interior cell ids/names.</summary>
        public string[] InteriorCellIds;
        public BakedCellState[] CellStates;

        public static BakeManifest FromCurrentSources(string esmPath, string bsaPath, string[] gameplaySourcePaths = null)
        {
            var esm = new FileInfo(esmPath);
            var bsa = new FileInfo(bsaPath ?? string.Empty);
            return new BakeManifest
            {
                FormatVersion = CacheFormat.FormatVersion,
                EsmSize = esm.Exists ? esm.Length : 0L,
                EsmMtimeTicks = esm.Exists ? esm.LastWriteTimeUtc.Ticks : 0L,
                BsaSize = bsa.Exists ? bsa.Length : 0L,
                BsaMtimeTicks = bsa.Exists ? bsa.LastWriteTimeUtc.Ticks : 0L,
                GameplaySourcesHash = BuildGameplaySourcesHash(gameplaySourcePaths),
                Sources = BuildSourceStates(gameplaySourcePaths),
            };
        }

        public bool SourcesMatch(string esmPath, string bsaPath, string[] gameplaySourcePaths = null)
        {
            if (Sources != null && Sources.Length > 0)
                return SourceStatesMatch(gameplaySourcePaths)
                    && FormatVersion == CacheFormat.FormatVersion;

            if (!File.Exists(esmPath) || !File.Exists(bsaPath)) return false;
            var esm = new FileInfo(esmPath);
            var bsa = new FileInfo(bsaPath);
            return esm.Length == EsmSize
                && esm.LastWriteTimeUtc.Ticks == EsmMtimeTicks
                && bsa.Length == BsaSize
                && bsa.LastWriteTimeUtc.Ticks == BsaMtimeTicks
                && string.Equals(GameplaySourcesHash ?? string.Empty, BuildGameplaySourcesHash(gameplaySourcePaths), System.StringComparison.Ordinal)
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
            w.Write(GameplaySourcesHash ?? string.Empty);
            int sourceCount = Sources?.Length ?? 0;
            w.Write(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                var source = Sources[i];
                w.Write(source?.Path ?? string.Empty);
                w.Write(source?.Size ?? 0L);
                w.Write(source?.MtimeTicks ?? 0L);
            }
            w.Write(MeshCount);
            w.Write(MaterialCount);
            w.Write(TextureCount);
            w.Write(CollisionCount);
            w.Write(CellCount);
            for (int i = 0; i < CellCount; i++)
            {
                w.Write(CellGrid[i].X);
                w.Write(CellGrid[i].Y);
            }
            w.Write(InteriorCellCount);
            for (int i = 0; i < InteriorCellCount; i++)
                w.Write(InteriorCellIds[i] ?? string.Empty);
            int stateCount = CellStates?.Length ?? 0;
            w.Write(stateCount);
            for (int i = 0; i < stateCount; i++)
                WriteCellState(w, CellStates[i]);
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
                    GameplaySourcesHash = r.ReadString(),
                };
                int sourceCount = r.ReadInt32();
                m.Sources = new SourceState[sourceCount];
                for (int i = 0; i < sourceCount; i++)
                {
                    m.Sources[i] = new SourceState
                    {
                        Path = r.ReadString(),
                        Size = r.ReadInt64(),
                        MtimeTicks = r.ReadInt64(),
                    };
                }
                m.MeshCount = r.ReadInt32();
                m.MaterialCount = r.ReadInt32();
                m.TextureCount = r.ReadInt32();
                m.CollisionCount = r.ReadInt32();
                m.CellCount = r.ReadInt32();
                m.CellGrid = new (int, int)[m.CellCount];
                for (int i = 0; i < m.CellCount; i++)
                    m.CellGrid[i] = (r.ReadInt32(), r.ReadInt32());
                m.InteriorCellCount = r.ReadInt32();
                m.InteriorCellIds = new string[m.InteriorCellCount];
                for (int i = 0; i < m.InteriorCellCount; i++)
                    m.InteriorCellIds[i] = r.ReadString();
                int stateCount = r.ReadInt32();
                m.CellStates = new BakedCellState[stateCount];
                for (int i = 0; i < stateCount; i++)
                    m.CellStates[i] = ReadCellState(r);
                manifest = m;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteCellState(BinaryWriter w, BakedCellState state)
        {
            w.Write(state?.Key ?? string.Empty);
            w.Write(state?.SectionPath ?? string.Empty);
            w.Write(state?.Fingerprint ?? string.Empty);
            w.Write(state?.PipelineVersion ?? 0u);
            w.Write(state != null && state.IsInterior);
            w.Write(state?.GridX ?? 0);
            w.Write(state?.GridY ?? 0);
            w.Write(state?.InteriorId ?? string.Empty);
            WriteIntArray(w, state?.MeshIndices);
            WriteIntArray(w, state?.MaterialIndices);
            WriteIntArray(w, state?.TextureIndices);
            WriteIntArray(w, state?.CollisionIndices);
            WriteIntArray(w, state?.TerrainLayerIndices);
        }

        private static BakedCellState ReadCellState(BinaryReader r)
        {
            return new BakedCellState
            {
                Key = r.ReadString(),
                SectionPath = r.ReadString(),
                Fingerprint = r.ReadString(),
                PipelineVersion = r.ReadUInt32(),
                IsInterior = r.ReadBoolean(),
                GridX = r.ReadInt32(),
                GridY = r.ReadInt32(),
                InteriorId = r.ReadString(),
                MeshIndices = ReadIntArray(r),
                MaterialIndices = ReadIntArray(r),
                TextureIndices = ReadIntArray(r),
                CollisionIndices = ReadIntArray(r),
                TerrainLayerIndices = ReadIntArray(r),
            };
        }

        private static void WriteIntArray(BinaryWriter w, int[] values)
        {
            int length = values?.Length ?? 0;
            w.Write(length);
            for (int i = 0; i < length; i++)
                w.Write(values[i]);
        }

        private static int[] ReadIntArray(BinaryReader r)
        {
            int length = r.ReadInt32();
            var values = new int[length];
            for (int i = 0; i < length; i++)
                values[i] = r.ReadInt32();
            return values;
        }

        static string BuildGameplaySourcesHash(string[] gameplaySourcePaths)
        {
            var sources = gameplaySourcePaths ?? System.Array.Empty<string>();
            if (sources.Length == 0)
                return string.Empty;

            var manifest = GameplayContentManifest.FromSources(sources);
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                int count = manifest.Sources?.Length ?? 0;
                w.Write(count);
                for (int i = 0; i < count; i++)
                {
                    var source = manifest.Sources[i];
                    w.Write(source?.Path ?? string.Empty);
                    w.Write(source?.Size ?? 0L);
                    w.Write(source?.MtimeTicks ?? 0L);
                }
            }

            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(ms.ToArray());
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        static SourceState[] BuildSourceStates(string[] sourcePaths)
        {
            var sources = sourcePaths ?? System.Array.Empty<string>();
            var states = new SourceState[sources.Length];
            for (int i = 0; i < sources.Length; i++)
                states[i] = BuildSourceState(sources[i]);

            return states;
        }

        static SourceState BuildSourceState(string path)
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return new SourceState
                {
                    Path = path,
                    Size = info.Length,
                    MtimeTicks = info.LastWriteTimeUtc.Ticks,
                };
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return new SourceState
                {
                    Path = path,
                    Size = -1L,
                    MtimeTicks = info.LastWriteTimeUtc.Ticks,
                };
            }

            return new SourceState
            {
                Path = path,
                Size = 0L,
                MtimeTicks = 0L,
            };
        }

        bool SourceStatesMatch(string[] sourcePaths)
        {
            var sources = sourcePaths ?? System.Array.Empty<string>();
            var states = Sources ?? System.Array.Empty<SourceState>();
            if (sources.Length != states.Length)
                return false;

            for (int i = 0; i < sources.Length; i++)
            {
                var state = states[i];
                if (!string.Equals(state.Path, sources[i], System.StringComparison.OrdinalIgnoreCase))
                    return false;

                if (File.Exists(sources[i]))
                {
                    var info = new FileInfo(sources[i]);
                    if (state.Size != info.Length || state.MtimeTicks != info.LastWriteTimeUtc.Ticks)
                        return false;
                    continue;
                }

                if (Directory.Exists(sources[i]))
                {
                    var info = new DirectoryInfo(sources[i]);
                    if (state.Size == -1L && state.MtimeTicks == info.LastWriteTimeUtc.Ticks)
                        continue;

                    if (state.Size == 0L && state.MtimeTicks == 0L)
                        continue;
                }

                return false;
            }

            return true;
        }
    }
}
