using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public enum ModelPrefabNodeKind : byte
    {
        None = 0,
        SyntheticRoot = 1,
        Transform = 2,
        RenderLeaf = 3,
        Billboard = 4,
        Switch = 5,
        Lod = 6,
        FltAnimation = 7,
        BsAnimation = 8,
        BsParticle = 9,
        RootCollision = 10,
        Avoid = 11,
        CollisionSwitch = 12,
    }

    public sealed class ModelPrefabNodeDef
    {
        public ModelPrefabNodeKind Kind;
        public string Name;
        public int ParentIndex;
        public int FirstChildIndex;
        public int ChildCount;
        public int SelectedChildIndex;
        public int GlobalMeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public float RotW;
        public float Scale = 1f;
        public float BoundsCenterX;
        public float BoundsCenterY;
        public float BoundsCenterZ;
        public float BoundsExtentsX;
        public float BoundsExtentsY;
        public float BoundsExtentsZ;
        public ushort Flags;
    }

    public sealed class ModelPrefabDef
    {
        public string ModelPath;
        public int RootNodeIndex;
        public int CollisionIndex;
        public ModelPrefabNodeDef[] Nodes = Array.Empty<ModelPrefabNodeDef>();
        public int[] ChildIndices = Array.Empty<int>();
    }

    public sealed class ModelPrefabCatalogData
    {
        public ModelPrefabDef[] Records = Array.Empty<ModelPrefabDef>();
    }

    public static class ModelPrefabFile
    {
        const uint Magic = 0x50464D44u; // 'DMFP'
        const uint Version = 1u;

        public static bool TryRead(string path, out ModelPrefabCatalogData data)
        {
            data = null;
            if (!File.Exists(path))
                return false;

            try
            {
                data = Read(path);
                return true;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static ModelPrefabCatalogData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad model prefab magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported model prefab version {version} in '{path}'.");

            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Negative model prefab count {count} in '{path}'.");

            var records = new ModelPrefabDef[count];
            for (int i = 0; i < count; i++)
                records[i] = ReadDef(r, path, i);

            return new ModelPrefabCatalogData
            {
                Records = records,
            };
        }

        public static void Write(string path, ModelPrefabCatalogData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);

            var records = data?.Records ?? Array.Empty<ModelPrefabDef>();
            w.Write(records.Length);
            for (int i = 0; i < records.Length; i++)
                WriteDef(w, records[i]);
        }

        static ModelPrefabDef ReadDef(BinaryReader r, string path, int index)
        {
            int nodeCount = r.ReadInt32();
            int childIndexCount = r.ReadInt32();
            if (nodeCount < 0 || childIndexCount < 0)
                throw new InvalidDataException($"Invalid model prefab table sizes in '{path}' for record {index}.");

            var nodes = new ModelPrefabNodeDef[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                nodes[i] = ReadNode(r);

            var childIndices = new int[childIndexCount];
            for (int i = 0; i < childIndexCount; i++)
                childIndices[i] = r.ReadInt32();

            return new ModelPrefabDef
            {
                ModelPath = r.ReadString(),
                RootNodeIndex = r.ReadInt32(),
                CollisionIndex = r.ReadInt32(),
                Nodes = nodes,
                ChildIndices = childIndices,
            };
        }

        static ModelPrefabNodeDef ReadNode(BinaryReader r)
        {
            return new ModelPrefabNodeDef
            {
                Kind = (ModelPrefabNodeKind)r.ReadByte(),
                Name = r.ReadString(),
                ParentIndex = r.ReadInt32(),
                FirstChildIndex = r.ReadInt32(),
                ChildCount = r.ReadInt32(),
                SelectedChildIndex = r.ReadInt32(),
                GlobalMeshIndex = r.ReadInt32(),
                MaterialIndex = r.ReadInt32(),
                TextureIndex = r.ReadInt32(),
                PosX = r.ReadSingle(),
                PosY = r.ReadSingle(),
                PosZ = r.ReadSingle(),
                RotX = r.ReadSingle(),
                RotY = r.ReadSingle(),
                RotZ = r.ReadSingle(),
                RotW = r.ReadSingle(),
                Scale = r.ReadSingle(),
                BoundsCenterX = r.ReadSingle(),
                BoundsCenterY = r.ReadSingle(),
                BoundsCenterZ = r.ReadSingle(),
                BoundsExtentsX = r.ReadSingle(),
                BoundsExtentsY = r.ReadSingle(),
                BoundsExtentsZ = r.ReadSingle(),
                Flags = r.ReadUInt16(),
            };
        }

        static void WriteDef(BinaryWriter w, ModelPrefabDef value)
        {
            var nodes = value?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            var childIndices = value?.ChildIndices ?? Array.Empty<int>();

            w.Write(nodes.Length);
            w.Write(childIndices.Length);
            for (int i = 0; i < nodes.Length; i++)
                WriteNode(w, nodes[i]);
            for (int i = 0; i < childIndices.Length; i++)
                w.Write(childIndices[i]);

            w.Write(value?.ModelPath ?? string.Empty);
            w.Write(value?.RootNodeIndex ?? -1);
            w.Write(value?.CollisionIndex ?? -1);
        }

        static void WriteNode(BinaryWriter w, ModelPrefabNodeDef value)
        {
            w.Write((byte)(value?.Kind ?? ModelPrefabNodeKind.None));
            w.Write(value?.Name ?? string.Empty);
            w.Write(value?.ParentIndex ?? -1);
            w.Write(value?.FirstChildIndex ?? -1);
            w.Write(value?.ChildCount ?? 0);
            w.Write(value?.SelectedChildIndex ?? -1);
            w.Write(value?.GlobalMeshIndex ?? -1);
            w.Write(value?.MaterialIndex ?? -1);
            w.Write(value?.TextureIndex ?? -1);
            w.Write(value?.PosX ?? 0f);
            w.Write(value?.PosY ?? 0f);
            w.Write(value?.PosZ ?? 0f);
            w.Write(value?.RotX ?? 0f);
            w.Write(value?.RotY ?? 0f);
            w.Write(value?.RotZ ?? 0f);
            w.Write(value?.RotW ?? 1f);
            w.Write(value?.Scale ?? 1f);
            w.Write(value?.BoundsCenterX ?? 0f);
            w.Write(value?.BoundsCenterY ?? 0f);
            w.Write(value?.BoundsCenterZ ?? 0f);
            w.Write(value?.BoundsExtentsX ?? 0f);
            w.Write(value?.BoundsExtentsY ?? 0f);
            w.Write(value?.BoundsExtentsZ ?? 0f);
            w.Write(value?.Flags ?? 0);
        }
    }
}
