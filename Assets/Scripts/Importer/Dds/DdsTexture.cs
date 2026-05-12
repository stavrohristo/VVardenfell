using System;
using System.IO;
using UnityEngine;

namespace VVardenfell.Importer.Dds
{
    /// <summary>
    /// Minimal DDS loader for Morrowind textures. Supports DXT1, DXT3, DXT5 block-compressed
    /// and 32-bit uncompressed RGBA/BGRA. Ignores cubemaps, volumes, and DX10 (BC7/etc) — those
    /// don't appear in vanilla Morrowind.
    /// </summary>
    public static class DdsTexture
    {
        private const uint MagicDds = 0x20534444;  // "DDS "
        private const int HeaderSize = 128;

        private const uint DDPF_ALPHAPIXELS = 0x1;
        private const uint DDPF_FOURCC = 0x4;
        private const uint DDPF_RGB = 0x40;

        private static uint FourCC(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

        public sealed class Payload
        {
            public int Width;
            public int Height;
            public int MipCount;
            public TextureFormat Format;
            public byte[][] Mips;
        }

        public readonly struct Metadata
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int MipCount;
            public readonly TextureFormat Format;

            public Metadata(int width, int height, int mipCount, TextureFormat format)
            {
                Width = width;
                Height = height;
                MipCount = mipCount;
                Format = format;
            }
        }

        public static Texture2D Load(byte[] data, string nameForLog = null)
        {
            var payload = DecodePayload(data, nameForLog);
            var tex = new Texture2D(payload.Width, payload.Height, payload.Format, payload.MipCount, linear: false);
            tex.name = nameForLog ?? "DDS";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.LoadRawTextureData(JoinMips(payload.Mips));
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        public static Payload DecodePayload(byte[] data, string nameForLog = null)
        {
            if (data == null || data.Length < HeaderSize)
                throw new InvalidDataException($"DDS too small: {nameForLog}");

            using var ms = new MemoryStream(data, writable: false);
            using var r = new BinaryReader(ms);

            if (r.ReadUInt32() != MagicDds)
                throw new InvalidDataException($"Not a DDS file: {nameForLog}");

            r.ReadUInt32();  // header size (124)
            r.ReadUInt32();  // flags
            int height = (int)r.ReadUInt32();
            int width = (int)r.ReadUInt32();
            r.ReadUInt32();  // pitchOrLinearSize
            r.ReadUInt32();  // depth
            uint mipCount = r.ReadUInt32();
            for (int i = 0; i < 11; i++) r.ReadUInt32(); // reserved

            // DDS_PIXELFORMAT (32 bytes)
            r.ReadUInt32();              // size
            uint pfFlags = r.ReadUInt32();
            uint fourCC = r.ReadUInt32();
            uint rgbBits = r.ReadUInt32();
            uint rMask = r.ReadUInt32();
            uint gMask = r.ReadUInt32();
            uint bMask = r.ReadUInt32();
            uint aMask = r.ReadUInt32();

            // Skip caps + reserved (20 bytes)
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();

            // Position now at 128.
            int pixelBytes = data.Length - HeaderSize;
            if (pixelBytes < 0)
                throw new InvalidDataException($"DDS pixel data underflow in {nameForLog}");

            TextureFormat format;
            int blockBytes; // 0 for uncompressed
            int pixelBytesPerTexel;
            bool decodeDxt3 = false;

            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                if (fourCC == FourCC('D','X','T','1'))
                { format = TextureFormat.DXT1; blockBytes = 8; pixelBytesPerTexel = 0; }
                else if (fourCC == FourCC('D','X','T','3'))
                { format = TextureFormat.RGBA32; blockBytes = 16; pixelBytesPerTexel = 0; decodeDxt3 = true; }
                else if (fourCC == FourCC('D','X','T','5'))
                { format = TextureFormat.DXT5; blockBytes = 16; pixelBytesPerTexel = 0; }
                else
                    throw new NotSupportedException($"Unsupported DDS fourCC 0x{fourCC:X8} in {nameForLog}");
            }
            else if ((pfFlags & DDPF_RGB) != 0 && rgbBits == 32)
            {
                bool isBgra = (rMask == 0x00FF0000 && bMask == 0x000000FF);
                format = isBgra ? TextureFormat.BGRA32 : TextureFormat.RGBA32;
                blockBytes = 0;
                pixelBytesPerTexel = 4;
            }
            else
            {
                throw new NotSupportedException($"Unsupported DDS pixel format (flags=0x{pfFlags:X8}) in {nameForLog}");
            }

            int declaredMipCount = Mathf.Max(1, (int)mipCount);

            // Compute total size for a given mip count. Also produce size of just the top mip.
            int TotalSizeForMips(int mips)
            {
                int total = 0;
                for (int i = 0; i < mips; i++)
                {
                    int w = Mathf.Max(1, width >> i);
                    int h = Mathf.Max(1, height >> i);
                    total += blockBytes > 0
                        ? Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4) * blockBytes
                        : w * h * pixelBytesPerTexel;
                }
                return total;
            }

            int expectedFull = TotalSizeForMips(declaredMipCount);
            int useMips = declaredMipCount;
            // If the file is short, reduce mip count until it fits.
            while (useMips > 1 && TotalSizeForMips(useMips) > pixelBytes) useMips--;
            int useSize = TotalSizeForMips(useMips);
            if (useSize > pixelBytes)
                throw new InvalidDataException($"DDS pixel data too short ({pixelBytes} < {useSize}) in {nameForLog}");

            if (decodeDxt3)
                return DecodeDxt3AsRgba32Payload(data, width, height, useMips, useSize, nameForLog);

            if (format == TextureFormat.BGRA32)
                return DecodeBgra32AsRgba32Payload(data, width, height, useMips, nameForLog);

            _ = expectedFull;
            return new Payload
            {
                Width = width,
                Height = height,
                MipCount = useMips,
                Format = format,
                Mips = SplitMips(data, HeaderSize, width, height, useMips, blockBytes, pixelBytesPerTexel),
            };
        }

        public static Metadata DecodeMetadata(byte[] data, string nameForLog = null)
        {
            if (data == null || data.Length < HeaderSize)
                throw new InvalidDataException($"DDS too small: {nameForLog}");

            using var ms = new MemoryStream(data, writable: false);
            using var r = new BinaryReader(ms);

            if (r.ReadUInt32() != MagicDds)
                throw new InvalidDataException($"Not a DDS file: {nameForLog}");

            r.ReadUInt32();
            r.ReadUInt32();
            int height = (int)r.ReadUInt32();
            int width = (int)r.ReadUInt32();
            r.ReadUInt32();
            r.ReadUInt32();
            uint mipCount = r.ReadUInt32();
            for (int i = 0; i < 11; i++) r.ReadUInt32();

            r.ReadUInt32();
            uint pfFlags = r.ReadUInt32();
            uint fourCC = r.ReadUInt32();
            uint rgbBits = r.ReadUInt32();
            uint rMask = r.ReadUInt32();
            r.ReadUInt32();
            uint bMask = r.ReadUInt32();
            r.ReadUInt32();

            TextureFormat format;
            int blockBytes;
            int pixelBytesPerTexel;
            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                if (fourCC == FourCC('D','X','T','1'))
                { format = TextureFormat.DXT1; blockBytes = 8; pixelBytesPerTexel = 0; }
                else if (fourCC == FourCC('D','X','T','3'))
                { format = TextureFormat.RGBA32; blockBytes = 16; pixelBytesPerTexel = 0; }
                else if (fourCC == FourCC('D','X','T','5'))
                { format = TextureFormat.DXT5; blockBytes = 16; pixelBytesPerTexel = 0; }
                else
                    throw new NotSupportedException($"Unsupported DDS fourCC 0x{fourCC:X8} in {nameForLog}");
            }
            else if ((pfFlags & DDPF_RGB) != 0 && rgbBits == 32)
            {
                bool isBgra = (rMask == 0x00FF0000 && bMask == 0x000000FF);
                format = isBgra ? TextureFormat.RGBA32 : TextureFormat.RGBA32;
                blockBytes = 0;
                pixelBytesPerTexel = 4;
            }
            else
            {
                throw new NotSupportedException($"Unsupported DDS pixel format (flags=0x{pfFlags:X8}) in {nameForLog}");
            }

            int pixelBytes = data.Length - HeaderSize;
            int declaredMipCount = Mathf.Max(1, (int)mipCount);
            int TotalSizeForMips(int mips)
            {
                int total = 0;
                for (int i = 0; i < mips; i++)
                {
                    int w = Mathf.Max(1, width >> i);
                    int h = Mathf.Max(1, height >> i);
                    total += blockBytes > 0
                        ? Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4) * blockBytes
                        : w * h * pixelBytesPerTexel;
                }
                return total;
            }

            int useMips = declaredMipCount;
            while (useMips > 1 && TotalSizeForMips(useMips) > pixelBytes)
                useMips--;
            if (TotalSizeForMips(useMips) > pixelBytes)
                throw new InvalidDataException($"DDS pixel data too short in {nameForLog}");

            return new Metadata(width, height, useMips, format);
        }

        public static Payload DecodeToRgba32Payload(byte[] data, string nameForLog = null)
        {
            var payload = DecodePayload(data, nameForLog);
            if (payload.Format == TextureFormat.RGBA32)
                return payload;

            if (payload.Format != TextureFormat.DXT1 && payload.Format != TextureFormat.DXT5)
                throw new NotSupportedException($"Cannot decode texture format {payload.Format} to RGBA32 in {nameForLog}.");

            int rgbaBytes = 0;
            for (int mip = 0; mip < payload.MipCount; mip++)
            {
                int w = Mathf.Max(1, payload.Width >> mip);
                int h = Mathf.Max(1, payload.Height >> mip);
                rgbaBytes += w * h * 4;
            }

            var raw = new byte[rgbaBytes];
            int dstOffset = 0;
            for (int mip = 0; mip < payload.MipCount; mip++)
            {
                int w = Mathf.Max(1, payload.Width >> mip);
                int h = Mathf.Max(1, payload.Height >> mip);
                if (payload.Format == TextureFormat.DXT1)
                    DecodeDxt1Mip(payload.Mips[mip], 0, payload.Mips[mip].Length, raw, dstOffset, w, h, nameForLog);
                else
                    DecodeDxt5Mip(payload.Mips[mip], 0, payload.Mips[mip].Length, raw, dstOffset, w, h, nameForLog);
                dstOffset += w * h * 4;
            }

            return new Payload
            {
                Width = payload.Width,
                Height = payload.Height,
                MipCount = payload.MipCount,
                Format = TextureFormat.RGBA32,
                Mips = SplitRgbaMips(raw, payload.Width, payload.Height, payload.MipCount),
            };
        }

        static Payload DecodeDxt3AsRgba32Payload(byte[] data, int width, int height, int mipCount, int compressedBytes, string nameForLog)
        {
            int rgbaBytes = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = Mathf.Max(1, width >> mip);
                int h = Mathf.Max(1, height >> mip);
                rgbaBytes += w * h * 4;
            }

            var rgba = new byte[rgbaBytes];
            int srcOffset = HeaderSize;
            int dstOffset = 0;
            int srcEnd = HeaderSize + compressedBytes;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = Mathf.Max(1, width >> mip);
                int h = Mathf.Max(1, height >> mip);
                int blocksX = Mathf.Max(1, (w + 3) / 4);
                int blocksY = Mathf.Max(1, (h + 3) / 4);
                DecodeDxt3Mip(data, srcOffset, srcEnd, rgba, dstOffset, w, h, blocksX, blocksY, nameForLog);
                srcOffset += blocksX * blocksY * 16;
                dstOffset += w * h * 4;
            }

            return new Payload
            {
                Width = width,
                Height = height,
                MipCount = mipCount,
                Format = TextureFormat.RGBA32,
                Mips = SplitRgbaMips(rgba, width, height, mipCount),
            };
        }

        static Payload DecodeBgra32AsRgba32Payload(byte[] data, int width, int height, int mipCount, string nameForLog)
        {
            byte[][] mips = SplitMips(data, HeaderSize, width, height, mipCount, 0, 4);
            for (int mip = 0; mip < mips.Length; mip++)
            {
                byte[] bytes = mips[mip];
                for (int i = 0; i < bytes.Length; i += 4)
                {
                    byte b = bytes[i + 0];
                    bytes[i + 0] = bytes[i + 2];
                    bytes[i + 2] = b;
                }
            }

            return new Payload
            {
                Width = width,
                Height = height,
                MipCount = mipCount,
                Format = TextureFormat.RGBA32,
                Mips = mips,
            };
        }

        static byte[] JoinMips(byte[][] mips)
        {
            int length = 0;
            for (int i = 0; i < mips.Length; i++)
                length += mips[i].Length;

            var bytes = new byte[length];
            int offset = 0;
            for (int i = 0; i < mips.Length; i++)
            {
                Buffer.BlockCopy(mips[i], 0, bytes, offset, mips[i].Length);
                offset += mips[i].Length;
            }

            return bytes;
        }

        static byte[][] SplitMips(byte[] data, int srcOffset, int width, int height, int mipCount, int blockBytes, int pixelBytesPerTexel)
        {
            var mips = new byte[mipCount][];
            int offset = srcOffset;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = Mathf.Max(1, width >> mip);
                int h = Mathf.Max(1, height >> mip);
                int length = blockBytes > 0
                    ? Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4) * blockBytes
                    : w * h * pixelBytesPerTexel;
                mips[mip] = new byte[length];
                Buffer.BlockCopy(data, offset, mips[mip], 0, length);
                offset += length;
            }

            return mips;
        }

        static byte[][] SplitRgbaMips(byte[] data, int width, int height, int mipCount)
        {
            var mips = new byte[mipCount][];
            int offset = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = Mathf.Max(1, width >> mip);
                int h = Mathf.Max(1, height >> mip);
                int length = w * h * 4;
                mips[mip] = new byte[length];
                Buffer.BlockCopy(data, offset, mips[mip], 0, length);
                offset += length;
            }

            return mips;
        }

        static void DecodeDxt3Mip(
            byte[] src,
            int srcOffset,
            int srcEnd,
            byte[] dst,
            int dstOffset,
            int width,
            int height,
            int blocksX,
            int blocksY,
            string nameForLog)
        {
            int blockOffset = srcOffset;
            var colors = new Color32[4];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (blockOffset + 16 > srcEnd)
                        throw new InvalidDataException($"DXT3 block data underflow in {nameForLog}");

                    ushort c0 = ReadU16(src, blockOffset + 8);
                    ushort c1 = ReadU16(src, blockOffset + 10);
                    BuildDxtColorPalette(c0, c1, colors);
                    uint indices = ReadU32(src, blockOffset + 12);

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= height)
                            continue;

                        ushort alphaRow = ReadU16(src, blockOffset + py * 2);
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= width)
                                continue;

                            int colorIndex = (int)((indices >> (2 * (py * 4 + px))) & 0x3);
                            byte alpha4 = (byte)((alphaRow >> (px * 4)) & 0xF);
                            Color32 color = colors[colorIndex];
                            int dstPixel = dstOffset + ((y * width + x) * 4);
                            dst[dstPixel + 0] = color.r;
                            dst[dstPixel + 1] = color.g;
                            dst[dstPixel + 2] = color.b;
                            dst[dstPixel + 3] = (byte)((alpha4 << 4) | alpha4);
                        }
                    }

                    blockOffset += 16;
                }
            }
        }

        static void DecodeDxt1Mip(byte[] src, int srcOffset, int srcEnd, byte[] dst, int dstOffset, int width, int height, string nameForLog)
        {
            int blocksX = Mathf.Max(1, (width + 3) / 4);
            int blocksY = Mathf.Max(1, (height + 3) / 4);
            int blockOffset = srcOffset;
            var colors = new Color32[4];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (blockOffset + 8 > srcEnd)
                        throw new InvalidDataException($"DXT1 block data underflow in {nameForLog}");

                    ushort c0 = ReadU16(src, blockOffset + 0);
                    ushort c1 = ReadU16(src, blockOffset + 2);
                    BuildDxt1ColorPalette(c0, c1, colors);
                    uint indices = ReadU32(src, blockOffset + 4);
                    WriteDxtColorBlock(dst, dstOffset, width, height, bx, by, colors, indices);
                    blockOffset += 8;
                }
            }
        }

        static void DecodeDxt5Mip(byte[] src, int srcOffset, int srcEnd, byte[] dst, int dstOffset, int width, int height, string nameForLog)
        {
            int blocksX = Mathf.Max(1, (width + 3) / 4);
            int blocksY = Mathf.Max(1, (height + 3) / 4);
            int blockOffset = srcOffset;
            var colors = new Color32[4];
            var alpha = new byte[8];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (blockOffset + 16 > srcEnd)
                        throw new InvalidDataException($"DXT5 block data underflow in {nameForLog}");

                    BuildDxt5AlphaPalette(src[blockOffset], src[blockOffset + 1], alpha);
                    ulong alphaBits = 0UL;
                    for (int i = 0; i < 6; i++)
                        alphaBits |= (ulong)src[blockOffset + 2 + i] << (8 * i);

                    ushort c0 = ReadU16(src, blockOffset + 8);
                    ushort c1 = ReadU16(src, blockOffset + 10);
                    BuildDxtColorPalette(c0, c1, colors);
                    uint colorIndices = ReadU32(src, blockOffset + 12);

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= height)
                            continue;

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= width)
                                continue;

                            int pixel = py * 4 + px;
                            int colorIndex = (int)((colorIndices >> (2 * pixel)) & 0x3);
                            int alphaIndex = (int)((alphaBits >> (3 * pixel)) & 0x7);
                            Color32 color = colors[colorIndex];
                            int dstPixel = dstOffset + ((y * width + x) * 4);
                            dst[dstPixel + 0] = color.r;
                            dst[dstPixel + 1] = color.g;
                            dst[dstPixel + 2] = color.b;
                            dst[dstPixel + 3] = alpha[alphaIndex];
                        }
                    }

                    blockOffset += 16;
                }
            }
        }

        static void WriteDxtColorBlock(byte[] dst, int dstOffset, int width, int height, int bx, int by, Color32[] colors, uint indices)
        {
            for (int py = 0; py < 4; py++)
            {
                int y = by * 4 + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = bx * 4 + px;
                    if (x >= width)
                        continue;

                    int pixel = py * 4 + px;
                    int colorIndex = (int)((indices >> (2 * pixel)) & 0x3);
                    Color32 color = colors[colorIndex];
                    int dstPixel = dstOffset + ((y * width + x) * 4);
                    dst[dstPixel + 0] = color.r;
                    dst[dstPixel + 1] = color.g;
                    dst[dstPixel + 2] = color.b;
                    dst[dstPixel + 3] = color.a;
                }
            }
        }

        static void BuildDxtColorPalette(ushort c0, ushort c1, Color32[] colors)
        {
            colors[0] = UnpackRgb565(c0);
            colors[1] = UnpackRgb565(c1);
            colors[2] = new Color32(
                (byte)((2 * colors[0].r + colors[1].r) / 3),
                (byte)((2 * colors[0].g + colors[1].g) / 3),
                (byte)((2 * colors[0].b + colors[1].b) / 3),
                255);
            colors[3] = new Color32(
                (byte)((colors[0].r + 2 * colors[1].r) / 3),
                (byte)((colors[0].g + 2 * colors[1].g) / 3),
                (byte)((colors[0].b + 2 * colors[1].b) / 3),
                255);
        }

        static void BuildDxt1ColorPalette(ushort c0, ushort c1, Color32[] colors)
        {
            colors[0] = UnpackRgb565(c0);
            colors[1] = UnpackRgb565(c1);
            if (c0 > c1)
            {
                colors[2] = new Color32(
                    (byte)((2 * colors[0].r + colors[1].r) / 3),
                    (byte)((2 * colors[0].g + colors[1].g) / 3),
                    (byte)((2 * colors[0].b + colors[1].b) / 3),
                    255);
                colors[3] = new Color32(
                    (byte)((colors[0].r + 2 * colors[1].r) / 3),
                    (byte)((colors[0].g + 2 * colors[1].g) / 3),
                    (byte)((colors[0].b + 2 * colors[1].b) / 3),
                    255);
            }
            else
            {
                colors[2] = new Color32(
                    (byte)((colors[0].r + colors[1].r) / 2),
                    (byte)((colors[0].g + colors[1].g) / 2),
                    (byte)((colors[0].b + colors[1].b) / 2),
                    255);
                colors[3] = new Color32(0, 0, 0, 0);
            }
        }

        static void BuildDxt5AlphaPalette(byte a0, byte a1, byte[] alpha)
        {
            alpha[0] = a0;
            alpha[1] = a1;
            if (a0 > a1)
            {
                alpha[2] = (byte)((6 * a0 + 1 * a1) / 7);
                alpha[3] = (byte)((5 * a0 + 2 * a1) / 7);
                alpha[4] = (byte)((4 * a0 + 3 * a1) / 7);
                alpha[5] = (byte)((3 * a0 + 4 * a1) / 7);
                alpha[6] = (byte)((2 * a0 + 5 * a1) / 7);
                alpha[7] = (byte)((1 * a0 + 6 * a1) / 7);
            }
            else
            {
                alpha[2] = (byte)((4 * a0 + 1 * a1) / 5);
                alpha[3] = (byte)((3 * a0 + 2 * a1) / 5);
                alpha[4] = (byte)((2 * a0 + 3 * a1) / 5);
                alpha[5] = (byte)((1 * a0 + 4 * a1) / 5);
                alpha[6] = 0;
                alpha[7] = 255;
            }
        }

        static Color32 UnpackRgb565(ushort value)
        {
            int r = (value >> 11) & 0x1F;
            int g = (value >> 5) & 0x3F;
            int b = value & 0x1F;
            return new Color32(
                (byte)((r << 3) | (r >> 2)),
                (byte)((g << 2) | (g >> 4)),
                (byte)((b << 3) | (b >> 2)),
                255);
        }

        static ushort ReadU16(byte[] bytes, int offset)
            => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

        static uint ReadU32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
    }
}
