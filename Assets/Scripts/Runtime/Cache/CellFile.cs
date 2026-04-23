using System.IO;
using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;
using BinaryReader = System.IO.BinaryReader;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// In-memory form of a baked cell file. Heights are already in Unity meters (Y-up).
    /// Collision state is pre-built blobs deserialized straight off disk - runtime
    /// never rebuilds BVHs. See <see cref="CellBakery"/> for the file layout.
    /// </summary>
    public sealed class CellData
    {
        public int GridX, GridY;
        public bool IsInterior;
        public string CellId;
        public bool HasTerrain;
        public float[] Heights;        // null if !HasTerrain; length = 65 * 65
        public sbyte[] Normals;        // null if absent; length = 3 * 65 * 65
        public ushort[] LayerGrid;     // null if absent; length = 16 * 16, dense bakery layer indices

        // Pre-built Unity Physics blobs. The combined STAT blob covers every non-interactable
        // STAT ref in the cell (cell-origin-local Unity meters); the terrain blob wraps the
        // 65x65 heightfield. Interactable refs keep per-ref blobs via
        // <see cref="RefEntry.CollisionIndex"/> into <c>collisions.bin</c>.
        // Ownership moves to <c>WorldResources</c> dictionaries at install time.
        public BlobAssetReference<Collider> StaticColliderBlob;
        public BlobAssetReference<Collider> TerrainColliderBlob;
        public CellEnvironmentData Environment;

        public RefEntry[] Refs;
        public DoorRefEntry[] Doors;
        public CellPlacementAuditData PlacementAudit;

        public bool HasStaticCollider => StaticColliderBlob.IsCreated;
        public bool HasTerrainCollider => TerrainColliderBlob.IsCreated;
    }

    public static class CellFile
    {
        const int TerrainSize = 65;
        const int TerrainSampleCount = TerrainSize * TerrainSize;
        const int TerrainNormalCount = 3 * TerrainSampleCount;
        const int LayerGridCount = 16 * 16;
        const int RefEntrySizeBytes = 72;
        const int DoorEntryFixedBytes = 36;

        static readonly ProfilerMarker k_ReadHeader = new("VV.Runtime.CellRead.Header");
        static readonly ProfilerMarker k_ReadTerrainHeights = new("VV.Runtime.CellRead.TerrainHeights");
        static readonly ProfilerMarker k_ReadTerrainNormals = new("VV.Runtime.CellRead.TerrainNormals");
        static readonly ProfilerMarker k_ReadTerrainColliderBlob = new("VV.Runtime.CellRead.TerrainColliderBlob");
        static readonly ProfilerMarker k_ReadLayerGrid = new("VV.Runtime.CellRead.LayerGrid");
        static readonly ProfilerMarker k_ReadStaticColliderBlob = new("VV.Runtime.CellRead.StaticColliderBlob");
        static readonly ProfilerMarker k_ReadRefTable = new("VV.Runtime.CellRead.RefTable");
        static readonly ProfilerMarker k_ReadDoorTable = new("VV.Runtime.CellRead.DoorTable");

        public static CellData Read(string path, bool isInterior = false, string cellId = null)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            string currentSection = "header";

            try
            {
                CellData cell;
                bool hasNormals;
                bool hasVtex;
                bool hasStaticCollision;
                bool hasEnvironment;

                using (k_ReadHeader.Auto())
                {
                    EnsureRemaining(r, sizeof(uint) + sizeof(int) + sizeof(int) + sizeof(uint), path, cellId, isInterior, currentSection);

                    uint magic = r.ReadUInt32();
                    if (magic != CellBakery.MagicCell)
                        throw CreateInvalidDataException(
                            path,
                            cellId,
                            isInterior,
                            currentSection,
                            fs,
                            $"Bad cell magic 0x{magic:X8}; expected 0x{CellBakery.MagicCell:X8}.");

                    cell = new CellData
                    {
                        GridX = r.ReadInt32(),
                        GridY = r.ReadInt32(),
                        IsInterior = isInterior,
                        CellId = cellId ?? string.Empty,
                    };

                    uint flags = r.ReadUInt32();
                    cell.HasTerrain = (flags & CacheFormat.CellFlagHasTerrain) != 0;
                    hasNormals = (flags & CacheFormat.CellFlagHasNormals) != 0;
                    hasVtex = (flags & CacheFormat.CellFlagHasVtex) != 0;
                    hasStaticCollision = (flags & CacheFormat.CellFlagHasStaticCollision) != 0;
                    hasEnvironment = (flags & CacheFormat.CellFlagHasEnvironment) != 0;
                }

                if (hasEnvironment)
                {
                    currentSection = "cell environment";
                    cell.Environment = ReadEnvironmentData(r, path, cellId, isInterior, currentSection);
                }

                if (cell.HasTerrain)
                {
                    currentSection = "terrain heights";
                    using (k_ReadTerrainHeights.Auto())
                    {
                        EnsureRemaining(r, TerrainSampleCount * sizeof(float), path, cellId, isInterior, currentSection);
                        cell.Heights = new float[TerrainSampleCount];
                        for (int i = 0; i < TerrainSampleCount; i++)
                            cell.Heights[i] = r.ReadSingle();
                    }

                    if (hasNormals)
                    {
                        currentSection = "terrain normals";
                        using (k_ReadTerrainNormals.Auto())
                        {
                            EnsureRemaining(r, TerrainNormalCount, path, cellId, isInterior, currentSection);
                            cell.Normals = new sbyte[TerrainNormalCount];
                            for (int i = 0; i < cell.Normals.Length; i++)
                                cell.Normals[i] = r.ReadSByte();
                        }
                    }

                    currentSection = "terrain collider blob";
                    using (k_ReadTerrainColliderBlob.Auto())
                    {
                        cell.TerrainColliderBlob = BlobStreamIO.ReadLengthPrefixed<Collider>(
                            r,
                            CacheFormat.PhysicsBlobVersion,
                            BuildBlobContext(path, cellId, isInterior, currentSection));
                    }

                    if (hasVtex)
                    {
                        currentSection = "layer grid";
                        using (k_ReadLayerGrid.Auto())
                        {
                            EnsureRemaining(r, LayerGridCount * sizeof(ushort), path, cellId, isInterior, currentSection);
                            cell.LayerGrid = new ushort[LayerGridCount];
                            for (int i = 0; i < cell.LayerGrid.Length; i++)
                                cell.LayerGrid[i] = r.ReadUInt16();
                        }
                    }
                }

                if (hasStaticCollision)
                {
                    currentSection = "static collider blob";
                    using (k_ReadStaticColliderBlob.Auto())
                    {
                        cell.StaticColliderBlob = BlobStreamIO.ReadLengthPrefixed<Collider>(
                            r,
                            CacheFormat.PhysicsBlobVersion,
                            BuildBlobContext(path, cellId, isInterior, currentSection));
                    }
                }

                currentSection = "ref table count";
                EnsureRemaining(r, sizeof(uint), path, cellId, isInterior, currentSection);
                uint refCount = r.ReadUInt32();

                currentSection = "ref table";
                using (k_ReadRefTable.Auto())
                {
                    EnsureEntryTableFits(r, refCount, RefEntrySizeBytes, path, cellId, isInterior, currentSection, "refs");
                    cell.Refs = new RefEntry[refCount];
                    for (int i = 0; i < refCount; i++)
                    {
                        cell.Refs[i] = new RefEntry
                        {
                            SpawnModeRaw = r.ReadInt32(),
                            RenderShardIndex = r.ReadInt32(),
                            LocalMeshIndex = r.ReadInt32(),
                            LocalMaterialIndex = r.ReadInt32(),
                            SliceIndex = r.ReadInt32(),
                            CollisionIndex = r.ReadInt32(),
                            PlacedRefId = r.ReadUInt32(),
                            DoorMetaIndex = r.ReadInt32(),
                            ContentHandleValue = r.ReadInt32(),
                            ContentKind = r.ReadInt32(),
                            PosX = r.ReadSingle(),
                            PosY = r.ReadSingle(),
                            PosZ = r.ReadSingle(),
                            RotX = r.ReadSingle(),
                            RotY = r.ReadSingle(),
                            RotZ = r.ReadSingle(),
                            RotW = r.ReadSingle(),
                            Scale = r.ReadSingle(),
                        };
                    }
                }

                currentSection = "door table count";
                EnsureRemaining(r, sizeof(uint), path, cellId, isInterior, currentSection);
                uint doorCount = r.ReadUInt32();

                currentSection = "door table";
                using (k_ReadDoorTable.Auto())
                {
                    EnsureEntryTableFits(r, doorCount, DoorEntryFixedBytes, path, cellId, isInterior, currentSection, "door entries");
                    cell.Doors = new DoorRefEntry[doorCount];
                    for (int i = 0; i < doorCount; i++)
                    {
                        string entrySection = $"door table entry {i}";
                        EnsureRemaining(r, DoorEntryFixedBytes, path, cellId, isInterior, entrySection);
                        cell.Doors[i] = new DoorRefEntry
                        {
                            PlacedRefId = r.ReadUInt32(),
                            Flags = r.ReadUInt32(),
                            DestPosX = r.ReadSingle(),
                            DestPosY = r.ReadSingle(),
                            DestPosZ = r.ReadSingle(),
                            DestRotX = r.ReadSingle(),
                            DestRotY = r.ReadSingle(),
                            DestRotZ = r.ReadSingle(),
                            DestRotW = r.ReadSingle(),
                            DestinationCellId = ReadCellString(r, path, cellId, isInterior, $"{entrySection} destination cell id"),
                        };
                    }
                }

                return cell;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (EndOfStreamException ex)
            {
                throw CreateInvalidDataException(path, cellId, isInterior, currentSection, fs, "Truncated cell payload.", ex);
            }
            catch (IOException ex)
            {
                throw CreateInvalidDataException(path, cellId, isInterior, currentSection, fs, "I/O error while reading cell payload.", ex);
            }
            catch (System.Exception ex)
            {
                throw CreateInvalidDataException(path, cellId, isInterior, currentSection, fs, "Unexpected cell read failure.", ex);
            }
        }

        static void EnsureRemaining(BinaryReader reader, long bytesNeeded, string path, string cellId, bool isInterior, string section)
        {
            if (bytesNeeded < 0)
                throw CreateInvalidDataException(
                    path,
                    cellId,
                    isInterior,
                    section,
                    reader.BaseStream,
                    $"Negative byte requirement {bytesNeeded} while validating section.");

            Stream stream = reader.BaseStream;
            if (!stream.CanSeek)
                return;

            long remaining = stream.Length - stream.Position;
            if (remaining < bytesNeeded)
            {
                throw CreateInvalidDataException(
                    path,
                    cellId,
                    isInterior,
                    section,
                    stream,
                    $"Truncated section: need {bytesNeeded} more bytes, but only {remaining} remain.");
            }
        }

        static void EnsureEntryTableFits(
            BinaryReader reader,
            uint count,
            int fixedEntryBytes,
            string path,
            string cellId,
            bool isInterior,
            string section,
            string label)
        {
            long totalBytes;
            try
            {
                totalBytes = checked((long)count * fixedEntryBytes);
            }
            catch (System.OverflowException ex)
            {
                throw CreateInvalidDataException(
                    path,
                    cellId,
                    isInterior,
                    section,
                    reader.BaseStream,
                    $"Invalid {label} count {count}; fixed-size table would overflow.", ex);
            }

            EnsureRemaining(reader, totalBytes, path, cellId, isInterior, section);
        }

        static string ReadCellString(BinaryReader reader, string path, string cellId, bool isInterior, string section)
        {
            try
            {
                return reader.ReadString();
            }
            catch (EndOfStreamException ex)
            {
                throw CreateInvalidDataException(path, cellId, isInterior, section, reader.BaseStream, "Truncated string payload.", ex);
            }
            catch (IOException ex)
            {
                throw CreateInvalidDataException(path, cellId, isInterior, section, reader.BaseStream, "Failed reading string payload.", ex);
            }
        }

        static CellEnvironmentData ReadEnvironmentData(BinaryReader reader, string path, string cellId, bool isInterior, string section)
        {
            EnsureRemaining(
                reader,
                2L + sizeof(uint) * 3L + sizeof(float) * 2L,
                path,
                cellId,
                isInterior,
                section);

            var environment = new CellEnvironmentData
            {
                HasMood = reader.ReadByte(),
                HasWater = reader.ReadByte(),
                AmbientColorRgba = reader.ReadUInt32(),
                DirectionalColorRgba = reader.ReadUInt32(),
                FogColorRgba = reader.ReadUInt32(),
                FogDensity = reader.ReadSingle(),
                WaterHeight = reader.ReadSingle(),
                RegionId = ReadCellString(reader, path, cellId, isInterior, $"{section} region id"),
            };
            return environment;
        }

        static string BuildBlobContext(string path, string cellId, bool isInterior, string section)
        {
            string cellLabel = BuildCellLabel(cellId, isInterior);
            return string.IsNullOrEmpty(cellLabel)
                ? $"cell file '{path}' section '{section}'"
                : $"cell file '{path}' ({cellLabel}) section '{section}'";
        }

        static InvalidDataException CreateInvalidDataException(
            string path,
            string cellId,
            bool isInterior,
            string section,
            Stream stream,
            string reason,
            System.Exception inner = null)
        {
            string cellLabel = BuildCellLabel(cellId, isInterior);
            string location = FormatLocation(stream);
            string message = string.IsNullOrEmpty(cellLabel)
                ? $"Cell read failure in '{path}', section '{section}' at {location}: {reason}"
                : $"Cell read failure in '{path}' ({cellLabel}), section '{section}' at {location}: {reason}";
            return inner == null ? new InvalidDataException(message) : new InvalidDataException(message, inner);
        }

        static string BuildCellLabel(string cellId, bool isInterior)
        {
            if (string.IsNullOrWhiteSpace(cellId))
                return isInterior ? "interior" : string.Empty;
            return isInterior ? $"interior '{cellId}'" : $"cell '{cellId}'";
        }

        static string FormatLocation(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
                return "unknown offset";
            return $"offset {stream.Position}/{stream.Length}";
        }
    }
}
