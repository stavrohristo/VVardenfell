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
        /// <summary>Stream position of the record header (start of the 16-byte record).</summary>
        public readonly long RecordOffset;

        public CellHeader(string name, CellFlags flags, int gridX, int gridY, long recordOffset)
        {
            Name = name;
            Flags = flags;
            GridX = gridX;
            GridY = gridY;
            RecordOffset = recordOffset;
        }

        public bool IsInterior => (Flags & CellFlags.Interior) != 0;
    }
}
