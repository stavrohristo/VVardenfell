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
        public const uint FormatVersion = 35;

        /// <summary>
        /// Version salt for bake-pipeline behavior that can change without altering the
        /// runtime cell payload layout. Stored per baked cell so the planner can decide
        /// whether an existing cell file is still reusable.
        /// </summary>
        public const uint WorldBakePipelineVersion = 16;
        public const uint GameplayContentVersion = 13;

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
        public const uint CellFlagHasEnvironment     = 1 << 4;
        public const uint CellFlagHasWorldMap        = 1 << 5;  // LAND WNAM 9x9 global-map color indices
    }

    public enum RefSpawnMode : int
    {
        RenderShard = 0,
        ModelPrefab = 1,
    }

    /// <summary>
    /// Fixed-size ref entry in a cell file.
    ///
    /// World refs now use a mixed-mode layout:
    /// - <see cref="RefSpawnMode.RenderShard"/> keeps the legacy submesh-centric actor path.
    /// - <see cref="RefSpawnMode.ModelPrefab"/> stores one placed-ref-centric entry that
    ///   resolves to a baked model-prefab graph.
    ///
    /// Layout is hand-packed 72 bytes, matched exactly by the writer/reader.
    ///
    /// <see cref="CollisionIndex"/> is -1 for refs with no per-ref collider - either
    /// because the source NIF has no RootCollisionNode, or because the ref is a pure
    /// STAT and its collision was accumulated into the cell's combined static-collision
    /// chunk (see <see cref="CacheFormat.CellFlagHasStaticCollision"/>). Non-negative
    /// values index into the global collision payload table (collisions.bin) and are
    /// reserved for interactable record types (DOOR/ACTI/CONT/LIGH/pickable items) so
    /// they can be raycasted individually at runtime.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 72)]
    public struct RefEntry
    {
        [FieldOffset(0)] public int PrimaryIndex;        // 4 - RenderShardIndex or ModelPrefabIndex by SpawnMode
        [FieldOffset(0)] public int RenderShardIndex;
        [FieldOffset(0)] public int ModelPrefabIndex;
        [FieldOffset(4)] public int LocalMeshIndex;      // 4 - local mesh index within the shard RMA (legacy path)
        [FieldOffset(8)] public int LocalMaterialIndex;  // 4 - local material index within the shard RMA (legacy path)
        [FieldOffset(12)] public int SliceIndex;         // 4 - global texture index, or -1 for no texture (legacy path)
        [FieldOffset(16)] public int CollisionIndex;     // 4 - index into collisions.bin, or -1 if no per-ref collider
        [FieldOffset(20)] public uint PlacedRefId;       // 4 - stable CELL ref id (FRMR/FormId) shared by this placed object
        [FieldOffset(24)] public int DoorMetaIndex;      // 4 - index into the per-cell door metadata table, or -1
        [FieldOffset(28)] public int ContentHandleValue; // 4 - stable typed gameplay-content handle value, or 0
        [FieldOffset(32)] public int ContentKind;        // 4 - <see cref="ContentReferenceKind"/> as int, or 0
        [FieldOffset(36)] public float PosX;             // 4 - Unity world position X (meters, Y-up)
        [FieldOffset(40)] public float PosY;             // 4 - Unity world position Y
        [FieldOffset(44)] public float PosZ;             // 4 - Unity world position Z
        [FieldOffset(48)] public float RotX;             // 4 - Unity quaternion X
        [FieldOffset(52)] public float RotY;             // 4 - Unity quaternion Y
        [FieldOffset(56)] public float RotZ;             // 4 - Unity quaternion Z
        [FieldOffset(60)] public float RotW;             // 4 - Unity quaternion W
        [FieldOffset(64)] public float Scale;            // 4
        [FieldOffset(68)] public int SpawnModeRaw;       // 4 - <see cref="RefSpawnMode"/>

        // Burst can hold onto stale field hashes across hot reloads. These aliases let
        // older generated method metadata resolve while keeping the new shard layout.
        [FieldOffset(4)] public int MeshIndex;
        [FieldOffset(8)] public int MaterialIndex;
        // = 72 bytes
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

    public struct CellEnvironmentData
    {
        public byte HasMood;
        public byte HasWater;
        public uint AmbientColorRgba;
        public uint DirectionalColorRgba;
        public uint FogColorRgba;
        public float FogDensity;
        public float WaterHeight;
        public string RegionId;

        public bool HasAnyData =>
            HasMood != 0 ||
            HasWater != 0 ||
            !string.IsNullOrWhiteSpace(RegionId);
    }

    [System.Flags]
    public enum RefPlacementAuditFlags : uint
    {
        None = 0,
        IsDoor = 1u << 0,
        IsTeleportDoor = 1u << 1,
        HasDuplicatePlacedRefId = 1u << 2,
        HasWorldBounds = 1u << 3,
        WasBaked = 1u << 4,
        MissingBaseRecord = 1u << 5,
        MissingModel = 1u << 6,
        MissingGameplayContentReference = 1u << 7,
    }

    public struct RefPlacementAuditEntry
    {
        public uint PlacedRefId;
        public string BaseId;
        public float SourcePosX;
        public float SourcePosY;
        public float SourcePosZ;
        public float SourceRotX;
        public float SourceRotY;
        public float SourceRotZ;
        public float SourceScale;
        public float UnityPosX;
        public float UnityPosY;
        public float UnityPosZ;
        public float UnityRotX;
        public float UnityRotY;
        public float UnityRotZ;
        public float UnityRotW;
        public float UnityScale;
        public float BoundsCenterX;
        public float BoundsCenterY;
        public float BoundsCenterZ;
        public float BoundsExtentsX;
        public float BoundsExtentsY;
        public float BoundsExtentsZ;
        public int SpawnedSubmeshCount;
        public int DuplicatePlacedRefCount;
        public RefPlacementAuditFlags Flags;
    }

    public sealed class CellPlacementAuditData
    {
        public bool IsInterior;
        public string CellId;
        public int GridX;
        public int GridY;
        public RefPlacementAuditEntry[] Entries;
    }

    public static class RefPlacementAuditFile
    {
        const uint Magic = 0x54434150u; // 'PACT'
        const uint Version = 1u;

        public static void Write(string path, CellPlacementAuditData data)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? string.Empty);

            using var fs = System.IO.File.Create(path);
            using var w = new System.IO.BinaryWriter(fs);
            Write(w, data);
        }

        public static byte[] Serialize(CellPlacementAuditData data)
        {
            using var ms = new System.IO.MemoryStream();
            using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                Write(w, data);
            return ms.ToArray();
        }

        public static CellPlacementAuditData Read(string path)
        {
            using var fs = System.IO.File.OpenRead(path);
            using var r = new System.IO.BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new System.IO.InvalidDataException($"Bad placement audit magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new System.IO.InvalidDataException($"Unsupported placement audit version {version} in '{path}'.");

            var data = new CellPlacementAuditData
            {
                IsInterior = r.ReadBoolean(),
                CellId = r.ReadString(),
                GridX = r.ReadInt32(),
                GridY = r.ReadInt32(),
            };

            int count = r.ReadInt32();
            if (count < 0)
                throw new System.IO.InvalidDataException($"Negative placement audit entry count {count} in '{path}'.");

            data.Entries = new RefPlacementAuditEntry[count];
            for (int i = 0; i < count; i++)
                data.Entries[i] = ReadEntry(r);

            return data;
        }

        public static bool TryRead(string path, out CellPlacementAuditData data)
        {
            data = null;
            if (!System.IO.File.Exists(path))
                return false;

            data = Read(path);
            return true;
        }

        static void Write(System.IO.BinaryWriter w, CellPlacementAuditData data)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(data?.IsInterior ?? false);
            w.Write(data?.CellId ?? string.Empty);
            w.Write(data?.GridX ?? 0);
            w.Write(data?.GridY ?? 0);

            var entries = data?.Entries ?? System.Array.Empty<RefPlacementAuditEntry>();
            w.Write(entries.Length);
            for (int i = 0; i < entries.Length; i++)
                WriteEntry(w, entries[i]);
        }

        static void WriteEntry(System.IO.BinaryWriter w, RefPlacementAuditEntry entry)
        {
            w.Write(entry.PlacedRefId);
            w.Write(entry.BaseId ?? string.Empty);
            w.Write(entry.SourcePosX);
            w.Write(entry.SourcePosY);
            w.Write(entry.SourcePosZ);
            w.Write(entry.SourceRotX);
            w.Write(entry.SourceRotY);
            w.Write(entry.SourceRotZ);
            w.Write(entry.SourceScale);
            w.Write(entry.UnityPosX);
            w.Write(entry.UnityPosY);
            w.Write(entry.UnityPosZ);
            w.Write(entry.UnityRotX);
            w.Write(entry.UnityRotY);
            w.Write(entry.UnityRotZ);
            w.Write(entry.UnityRotW);
            w.Write(entry.UnityScale);
            w.Write(entry.BoundsCenterX);
            w.Write(entry.BoundsCenterY);
            w.Write(entry.BoundsCenterZ);
            w.Write(entry.BoundsExtentsX);
            w.Write(entry.BoundsExtentsY);
            w.Write(entry.BoundsExtentsZ);
            w.Write(entry.SpawnedSubmeshCount);
            w.Write(entry.DuplicatePlacedRefCount);
            w.Write((uint)entry.Flags);
        }

        static RefPlacementAuditEntry ReadEntry(System.IO.BinaryReader r)
        {
            return new RefPlacementAuditEntry
            {
                PlacedRefId = r.ReadUInt32(),
                BaseId = r.ReadString(),
                SourcePosX = r.ReadSingle(),
                SourcePosY = r.ReadSingle(),
                SourcePosZ = r.ReadSingle(),
                SourceRotX = r.ReadSingle(),
                SourceRotY = r.ReadSingle(),
                SourceRotZ = r.ReadSingle(),
                SourceScale = r.ReadSingle(),
                UnityPosX = r.ReadSingle(),
                UnityPosY = r.ReadSingle(),
                UnityPosZ = r.ReadSingle(),
                UnityRotX = r.ReadSingle(),
                UnityRotY = r.ReadSingle(),
                UnityRotZ = r.ReadSingle(),
                UnityRotW = r.ReadSingle(),
                UnityScale = r.ReadSingle(),
                BoundsCenterX = r.ReadSingle(),
                BoundsCenterY = r.ReadSingle(),
                BoundsCenterZ = r.ReadSingle(),
                BoundsExtentsX = r.ReadSingle(),
                BoundsExtentsY = r.ReadSingle(),
                BoundsExtentsZ = r.ReadSingle(),
                SpawnedSubmeshCount = r.ReadInt32(),
                DuplicatePlacedRefCount = r.ReadInt32(),
                Flags = (RefPlacementAuditFlags)r.ReadUInt32(),
            };
        }
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
