using System.IO;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// In-memory form of a baked cell file. Heights are already in Unity meters (Y-up).
    /// </summary>
    public sealed class CellData
    {
        public int GridX, GridY;
        public bool HasTerrain;
        public float[] Heights;        // null if !HasTerrain; length = 65 * 65
        public sbyte[] Normals;        // null if absent; length = 3 * 65 * 65
        public ushort[] LayerGrid;     // null if absent; length = 16 * 16, dense bakery layer indices
        public RefEntry[] Refs;
    }

    public static class CellFile
    {
        public static CellData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != CellBakery.MagicCell)
                throw new InvalidDataException($"Bad magic in {path}");
            var cell = new CellData
            {
                GridX = r.ReadInt32(),
                GridY = r.ReadInt32(),
            };
            uint flags = r.ReadUInt32();
            cell.HasTerrain = (flags & CacheFormat.CellFlagHasTerrain) != 0;
            bool hasNormals = (flags & CacheFormat.CellFlagHasNormals) != 0;
            bool hasVtex    = (flags & CacheFormat.CellFlagHasVtex) != 0;

            if (cell.HasTerrain)
            {
                const int N = 65;
                cell.Heights = new float[N * N];
                for (int i = 0; i < N * N; i++) cell.Heights[i] = r.ReadSingle();
                if (hasNormals)
                {
                    cell.Normals = new sbyte[3 * N * N];
                    for (int i = 0; i < cell.Normals.Length; i++) cell.Normals[i] = r.ReadSByte();
                }
                if (hasVtex)
                {
                    cell.LayerGrid = new ushort[16 * 16];
                    for (int i = 0; i < cell.LayerGrid.Length; i++) cell.LayerGrid[i] = r.ReadUInt16();
                }
            }

            uint refCount = r.ReadUInt32();
            cell.Refs = new RefEntry[refCount];
            for (int i = 0; i < refCount; i++)
            {
                cell.Refs[i] = new RefEntry
                {
                    MeshIndex = r.ReadInt32(),
                    MaterialIndex = r.ReadInt32(),
                    SliceIndex = r.ReadInt32(),
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
            return cell;
        }
    }
}
