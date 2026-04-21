namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Parsed Morrowind LAND record. Stores decoded absolute heights (in MW units) for
    /// a 65×65 vertex grid covering one 8192×8192-unit exterior cell at (<see cref="GridX"/>,<see cref="GridY"/>).
    /// </summary>
    public sealed class LandRecord
    {
        public const int Size = 65;                    // vertices per side
        public const int NumVerts = Size * Size;       // 4225
        public const int CellUnits = 8192;             // per side in MW units
        public const int HeightScale = 8;              // VHGT scale factor
        public const int TextureSize = 16;             // VTEX grid side (16x16 = 256 quadrants)
        public const int NumTextures = TextureSize * TextureSize;

        public int GridX;
        public int GridY;
        public bool HasHeights;
        public float MinHeight;
        public float MaxHeight;
        public float[] Heights; // size NumVerts, absolute heights in MW units
        public sbyte[] Normals; // optional, 3 * NumVerts, may be null
        public byte[] Colors;   // optional, 3 * NumVerts, may be null
        public ushort[] VtexIndices; // optional, 256 entries (row-major, already un-transposed); null if absent
    }
}
