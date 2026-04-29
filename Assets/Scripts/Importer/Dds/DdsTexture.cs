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

        public static Texture2D Load(byte[] data, string nameForLog = null)
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
                return LoadDxt3AsRgba32(data, width, height, useMips, useSize, nameForLog);

            var tex = new Texture2D(width, height, format, useMips, linear: false);
            tex.name = nameForLog ?? "DDS";
            tex.wrapMode = TextureWrapMode.Repeat;

            var slice = new byte[useSize];
            Buffer.BlockCopy(data, HeaderSize, slice, 0, useSize);
            tex.LoadRawTextureData(slice);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _ = expectedFull;
            return tex;
        }

        static Texture2D LoadDxt3AsRgba32(byte[] data, int width, int height, int mipCount, int compressedBytes, string nameForLog)
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

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, linear: false);
            tex.name = nameForLog ?? "DDS";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.LoadRawTextureData(rgba);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
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
