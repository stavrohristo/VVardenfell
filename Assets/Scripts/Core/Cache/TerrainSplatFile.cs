using System;
using System.Collections.Generic;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public static class TerrainSplatFile
    {
        const uint Magic = 0x4C505354u; // 'TSPL'
        const uint Version = 1u;

        public const int Width = 16;
        public const int Height = 16;
        public const int SampleCount = Width * Height;

        public static void Write(string path, IReadOnlyList<ushort[]> slices)
        {
            if (slices == null)
                throw new InvalidDataException("Terrain splat slice list is null.");
            if (slices.Count <= 0)
                throw new InvalidDataException("Terrain splat cache cannot be written with zero slices.");

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);
            w.Write(Width);
            w.Write(Height);
            w.Write(slices.Count);
            for (int s = 0; s < slices.Count; s++)
            {
                ushort[] slice = slices[s] ?? throw new InvalidDataException($"Terrain splat slice {s} is null.");
                if (slice.Length != SampleCount)
                    throw new InvalidDataException($"Terrain splat slice {s} has {slice.Length} samples; expected {SampleCount}.");
                for (int i = 0; i < slice.Length; i++)
                    w.Write(slice[i]);
            }
        }

        public static ushort[][] Read(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"terrain_splats.bin missing; rebake required.", path);

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Magic)
                throw new InvalidDataException("terrain_splats.bin magic mismatch; rebake required.");
            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"terrain_splats.bin version mismatch (found {version}, expected {Version}); rebake required.");
            int width = r.ReadInt32();
            int height = r.ReadInt32();
            if (width != Width || height != Height)
                throw new InvalidDataException($"terrain_splats.bin dimensions {width}x{height} do not match expected {Width}x{Height}; rebake required.");
            int count = r.ReadInt32();
            if (count <= 0)
                throw new InvalidDataException("terrain_splats.bin has no splat slices; rebake required.");

            var slices = new ushort[count][];
            for (int s = 0; s < count; s++)
            {
                var slice = new ushort[SampleCount];
                for (int i = 0; i < slice.Length; i++)
                    slice[i] = r.ReadUInt16();
                slices[s] = slice;
            }
            if (fs.Position != fs.Length)
                throw new InvalidDataException("terrain_splats.bin has trailing bytes; rebake required.");
            return slices;
        }
    }
}
