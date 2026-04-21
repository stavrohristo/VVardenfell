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

            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                if (fourCC == FourCC('D','X','T','1'))
                { format = TextureFormat.DXT1; blockBytes = 8; pixelBytesPerTexel = 0; }
                else if (fourCC == FourCC('D','X','T','3') || fourCC == FourCC('D','X','T','5'))
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
    }
}
