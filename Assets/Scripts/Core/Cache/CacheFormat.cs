using System.Runtime.InteropServices;

namespace VVardenfell.Core.Cache
{
    /// <summary>
    /// Constants that define the on-disk cache layout. Bump <see cref="FormatVersion"/>
    /// any time a struct in this namespace changes its binary shape.
    /// </summary>
    public static class CacheFormat
    {
        /// <summary>'VVDF' little-endian.</summary>
        public const uint Magic = 0x46445656u;

        /// <summary>Bump this to force all users to rebake.</summary>
        public const uint FormatVersion = 13;

        // Flags on BakedMeshHeader.Flags
        public const uint MeshFlagHasNormals = 1 << 0;
        public const uint MeshFlagHasUVs     = 1 << 1;
        public const uint MeshFlagHasColors  = 1 << 2;
        public const uint MeshFlagIndex32    = 1 << 3;  // indices are uint32 instead of uint16

        // Flags on MaterialRecord.Flags (low byte = flags, top byte = alpha-clip threshold 0..255)
        public const uint MatFlagAlphaBlend  = 1 << 0;
        public const uint MatFlagAlphaClip   = 1 << 1;
        public const uint MatFlagTwoSided    = 1 << 2;
        public const int  MatAlphaThresholdShift = 24;
        public const uint MatAlphaThresholdMask  = 0xFFu << MatAlphaThresholdShift;

        public static uint PackAlphaThreshold(uint flags, byte threshold)
            => (flags & ~MatAlphaThresholdMask) | ((uint)threshold << MatAlphaThresholdShift);

        public static byte UnpackAlphaThreshold(uint flags)
            => (byte)((flags & MatAlphaThresholdMask) >> MatAlphaThresholdShift);

        // Flags on BakedCellHeader.Flags
        public const uint CellFlagHasTerrain = 1 << 0;
        public const uint CellFlagHasNormals = 1 << 1;
        public const uint CellFlagHasVtex    = 1 << 2;
    }

    /// <summary>
    /// Fixed-size ref entry in a cell file. One per placed object submesh.
    /// Layout is hand-packed 44 bytes, matched exactly by the writer/reader.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RefEntry
    {
        public int MeshIndex;      // 4 — index into the global meshes table
        public int MaterialIndex;  // 4 — blend-variant material (0=Opaque,1=AlphaTest,2=AlphaBlend)
        public int SliceIndex;     // 4 — slice in the shared ref Texture2DArray, or -1 for no texture
        public float PosX, PosY, PosZ;     // 12 — Unity world position (already in meters, Y-up)
        public float RotX, RotY, RotZ, RotW; // 16 — Unity quaternion
        public float Scale;        // 4
                                   // = 44 bytes
    }

    /// <summary>
    /// Material record. Since v13 every ref samples the shared Texture2DArray with a
    /// per-instance slice index, so only the alpha/blend state survives here — roughly
    /// three records total (opaque / alpha-test / alpha-blend).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialRecord
    {
        public uint Flags;         // 4 — MatFlag* bits + packed alpha-clip threshold in top byte
                                   // = 4 bytes
    }
}
