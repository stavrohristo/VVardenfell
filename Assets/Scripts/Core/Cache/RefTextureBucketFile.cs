using System;
using System.IO;
using UnityEngine;

namespace VVardenfell.Core.Cache
{
    public sealed class RefTextureBucketData
    {
        public int[] TextureBucketKeys;
        public int[] TextureSlices;
        public RefTextureBucketDef[] Buckets;
    }

    public sealed class RefTextureBucketDef
    {
        public int BucketKey;
        public int Width;
        public int Height;
        public int MipCount;
        public TextureFormat Format;
        public int SliceCount;
        public int FallbackSlice;
        public RefTextureBucketSlice[] Slices;
    }

    public sealed class RefTextureBucketSlice
    {
        public byte[][] Mips;
    }

    public static class RefTextureBucketFile
    {
        const uint Magic = 0x4258_5452u; // 'RTXB'
        const uint Version = 1;

        public static int MakeBucketKey(int width, int height, TextureFormat format, int mipCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, width);
                Mix(ref hash, height);
                Mix(ref hash, (int)format);
                Mix(ref hash, mipCount);
                int key = (int)hash;
                return key != 0 ? key : 1;
            }
        }

        static void Mix(ref uint hash, int value)
        {
            hash ^= (uint)value;
            hash *= 16777619u;
        }

        public static void Write(string path, RefTextureBucketData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.TextureBucketKeys == null || data.TextureSlices == null || data.TextureBucketKeys.Length != data.TextureSlices.Length)
                throw new InvalidDataException("Ref texture bucket data has invalid texture map.");
            if (data.Buckets == null || data.Buckets.Length == 0)
                throw new InvalidDataException("Ref texture bucket data has no buckets.");

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);
            w.Write(data.TextureBucketKeys.Length);
            w.Write(data.Buckets.Length);

            for (int i = 0; i < data.TextureBucketKeys.Length; i++)
            {
                w.Write(data.TextureBucketKeys[i]);
                w.Write(data.TextureSlices[i]);
            }

            for (int i = 0; i < data.Buckets.Length; i++)
            {
                var bucket = data.Buckets[i] ?? throw new InvalidDataException($"Ref texture bucket {i} is null.");
                if (bucket.Slices == null || bucket.Slices.Length != bucket.SliceCount)
                    throw new InvalidDataException($"Ref texture bucket {i} has invalid slice table.");

                w.Write(bucket.BucketKey);
                w.Write(bucket.Width);
                w.Write(bucket.Height);
                w.Write(bucket.MipCount);
                w.Write((int)bucket.Format);
                w.Write(bucket.SliceCount);
                w.Write(bucket.FallbackSlice);

                for (int s = 0; s < bucket.SliceCount; s++)
                {
                    var slice = bucket.Slices[s] ?? throw new InvalidDataException($"Ref texture bucket {i} slice {s} is null.");
                    if (slice.Mips == null || slice.Mips.Length != bucket.MipCount)
                        throw new InvalidDataException($"Ref texture bucket {i} slice {s} has invalid mip table.");

                    for (int m = 0; m < bucket.MipCount; m++)
                    {
                        byte[] bytes = slice.Mips[m] ?? throw new InvalidDataException($"Ref texture bucket {i} slice {s} mip {m} is null.");
                        w.Write(bytes.Length);
                        w.Write(bytes);
                    }
                }
            }
        }

        public static RefTextureBucketData Read(string path)
        {
            if (!File.Exists(path))
                throw new InvalidDataException($"ref texture bucket file '{path}' is missing; rebake required.");

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Magic)
                throw new InvalidDataException($"Bad magic in {path}; rebake required.");
            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported ref texture bucket version {version} in {path}; rebake required.");

            int textureCount = r.ReadInt32();
            int bucketCount = r.ReadInt32();
            if (textureCount < 0 || bucketCount <= 0)
                throw new InvalidDataException($"Invalid ref texture bucket counts in {path}.");

            var textureBucketKeys = new int[textureCount];
            var textureSlices = new int[textureCount];
            for (int i = 0; i < textureCount; i++)
            {
                textureBucketKeys[i] = r.ReadInt32();
                textureSlices[i] = r.ReadInt32();
            }

            var buckets = new RefTextureBucketDef[bucketCount];
            for (int i = 0; i < bucketCount; i++)
            {
                var bucket = new RefTextureBucketDef
                {
                    BucketKey = r.ReadInt32(),
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    MipCount = r.ReadInt32(),
                    Format = (TextureFormat)r.ReadInt32(),
                    SliceCount = r.ReadInt32(),
                    FallbackSlice = r.ReadInt32(),
                };

                if (bucket.Width <= 0 || bucket.Height <= 0 || bucket.MipCount <= 0 || bucket.SliceCount <= 0)
                    throw new InvalidDataException($"Invalid ref texture bucket {i} metadata in {path}.");
                if ((uint)bucket.FallbackSlice >= (uint)bucket.SliceCount)
                    throw new InvalidDataException($"Invalid ref texture bucket {i} fallback slice in {path}.");
                int expectedKey = MakeBucketKey(bucket.Width, bucket.Height, bucket.Format, bucket.MipCount);
                if (bucket.BucketKey != expectedKey)
                    throw new InvalidDataException($"Ref texture bucket {i} key mismatch in {path}; rebake required.");

                bucket.Slices = new RefTextureBucketSlice[bucket.SliceCount];
                for (int s = 0; s < bucket.SliceCount; s++)
                {
                    var slice = new RefTextureBucketSlice { Mips = new byte[bucket.MipCount][] };
                    for (int m = 0; m < bucket.MipCount; m++)
                    {
                        int length = r.ReadInt32();
                        if (length <= 0)
                            throw new InvalidDataException($"Invalid ref texture bucket {i} slice {s} mip {m} length in {path}.");
                        slice.Mips[m] = r.ReadBytes(length);
                        if (slice.Mips[m].Length != length)
                            throw new EndOfStreamException($"Truncated ref texture bucket {i} slice {s} mip {m} in {path}.");
                    }
                    bucket.Slices[s] = slice;
                }
                buckets[i] = bucket;
            }

            if (fs.Position != fs.Length)
                throw new InvalidDataException($"Unexpected trailing data in {path} at offset {fs.Position}/{fs.Length}.");

            return new RefTextureBucketData
            {
                TextureBucketKeys = textureBucketKeys,
                TextureSlices = textureSlices,
                Buckets = buckets,
            };
        }
    }
}
