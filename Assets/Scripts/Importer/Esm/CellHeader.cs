using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Esm
{
    [System.Flags]
    public enum CellFlags : uint
    {
        Interior = 0x01,
        HasWater = 0x02,
        NoSleep = 0x04,
        QuasiExterior = 0x80,
    }

    /// <summary>
    /// Minimal CELL header (NAME + DATA). Reference data is not parsed here.
    /// </summary>
    public readonly struct CellHeader
    {
        public readonly string Name;
        public readonly CellFlags Flags;
        public readonly int GridX;
        public readonly int GridY;
        public readonly CellEnvironmentData Environment;
        /// <summary>Stream position of the record header (start of the 16-byte record).</summary>
        public readonly long RecordOffset;
        public readonly string SourcePath;
        public readonly int ContentFileIndex;

        public CellHeader(string name, CellFlags flags, int gridX, int gridY, CellEnvironmentData environment, long recordOffset, string sourcePath = null, int contentFileIndex = 0)
        {
            Name = name;
            Flags = flags;
            GridX = gridX;
            GridY = gridY;
            Environment = environment;
            RecordOffset = recordOffset;
            SourcePath = sourcePath ?? string.Empty;
            ContentFileIndex = contentFileIndex;
        }

        public bool IsInterior => (Flags & CellFlags.Interior) != 0;
    }
}
