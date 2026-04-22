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

        /// <summary>
        /// Bump this to force all users to rebake when the binary layout or baked-content
        /// semantics change.
        /// </summary>
        public const uint FormatVersion = 22;

        /// <summary>
        /// Version salt for bake-pipeline behavior that can change without altering the
        /// runtime cell payload layout. Stored per baked cell so the planner can decide
        /// whether an existing cell file is still reusable.
        /// </summary>
        public const uint WorldBakePipelineVersion = 2;

        /// <summary>
        /// Passed through Unity's official blob serialization path for every serialized
        /// Unity Physics collider blob written by the importer. Bump this independently of
        /// <see cref="FormatVersion"/> only if Unity Physics changes the blob payload layout;
        /// version mismatch is treated as invalid cached physics data and requires a rebake.
        /// </summary>
        public const int PhysicsBlobVersion = 1;

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
        public const uint CellFlagHasTerrain         = 1 << 0;
        public const uint CellFlagHasNormals         = 1 << 1;
        public const uint CellFlagHasVtex            = 1 << 2;
        public const uint CellFlagHasStaticCollision = 1 << 3;  // per-cell combined STAT triangle soup
    }

    /// <summary>
    /// Fixed-size ref entry in a cell file. One per placed object submesh.
    /// Layout is hand-packed 56 bytes, matched exactly by the writer/reader.
    ///
    /// <see cref="CollisionIndex"/> is -1 for refs with no per-ref collider - either
    /// because the source NIF has no RootCollisionNode, or because the ref is a pure
    /// STAT and its collision was accumulated into the cell's combined static-collision
    /// chunk (see <see cref="CacheFormat.CellFlagHasStaticCollision"/>). Non-negative
    /// values index into the global collision payload table (collisions.bin) and are
    /// reserved for interactable record types (DOOR/ACTI/CONT/LIGH/pickable items) so
    /// they can be raycasted individually at runtime.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RefEntry
    {
        public int MeshIndex;      // 4 - index into the global meshes table
        public int MaterialIndex;  // 4 - blend-variant material (0=Opaque,1=AlphaTest,2=AlphaBlend)
        public int SliceIndex;     // 4 - slice in the shared ref Texture2DArray, or -1 for no texture
        public int CollisionIndex; // 4 - index into collisions.bin, or -1 if no per-ref collider
        public uint PlacedRefId;   // 4 - stable CELL ref id (FRMR/FormId) shared by this placed object
        public int DoorMetaIndex;  // 4 - index into the per-cell door metadata table, or -1
        public float PosX, PosY, PosZ;     // 12 - Unity world position (already in meters, Y-up)
        public float RotX, RotY, RotZ, RotW; // 16 - Unity quaternion
        public float Scale;        // 4
                                   // = 56 bytes
    }

    /// <summary>
    /// Per-cell metadata for door refs. Referenced by <see cref="RefEntry.DoorMetaIndex"/>.
    /// Destination cell is empty for teleport doors that lead to an exterior location.
    /// </summary>
    public struct DoorRefEntry
    {
        public const uint FlagTeleport = 1u << 0;

        public uint PlacedRefId;
        public uint Flags;
        public float DestPosX, DestPosY, DestPosZ;
        public float DestRotX, DestRotY, DestRotZ, DestRotW;
        public string DestinationCellId;
    }

    /// <summary>
    /// Material record. Since v13 every ref samples the shared Texture2DArray with a
    /// per-instance slice index, so only the alpha/blend state survives here - roughly
    /// three records total (opaque / alpha-test / alpha-blend).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialRecord
    {
        public uint Flags;         // 4 - MatFlag* bits + packed alpha-clip threshold in top byte
                                   // = 4 bytes
    }
}
