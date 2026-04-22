using System;
using System.IO;
using UnityEngine;

namespace VVardenfell.Importer.Dds
{
    /// <summary>
    /// Minimal TGA loader for Morrowind splash screens. Supports uncompressed
    /// true-color 24-bit and 32-bit payloads.
    /// </summary>
    public static class TgaTexture
    {
        public static Texture2D Load(byte[] data, string nameForLog = null)
        {
            if (data == null || data.Length < 18)
                throw new InvalidDataException($"TGA too small: {nameForLog}");

            int idLength = data[0];
            int colorMapType = data[1];
            int imageType = data[2];
            if (colorMapType != 0)
                throw new NotSupportedException($"Color-mapped TGA unsupported: {nameForLog}");
            if (imageType != 2)
                throw new NotSupportedException($"Unsupported TGA image type {imageType}: {nameForLog}");

            int width = data[12] | (data[13] << 8);
            int height = data[14] | (data[15] << 8);
            int bpp = data[16];
            int descriptor = data[17];
            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"Invalid TGA dimensions in {nameForLog}");
            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException($"Unsupported TGA bpp {bpp}: {nameForLog}");

            int bytesPerPixel = bpp / 8;
            int pixelOffset = 18 + idLength;
            int expectedBytes = checked(width * height * bytesPerPixel);
            if (pixelOffset + expectedBytes > data.Length)
                throw new InvalidDataException($"Truncated TGA pixel payload in {nameForLog}");

            bool originTop = (descriptor & 0x20) != 0;
            var rgba = new byte[checked(width * height * 4)];

            for (int y = 0; y < height; y++)
            {
                int srcY = originTop ? y : (height - 1 - y);
                int srcRow = pixelOffset + srcY * width * bytesPerPixel;
                int dstRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int src = srcRow + x * bytesPerPixel;
                    int dst = dstRow + x * 4;
                    rgba[dst + 0] = data[src + 2];
                    rgba[dst + 1] = data[src + 1];
                    rgba[dst + 2] = data[src + 0];
                    rgba[dst + 3] = bytesPerPixel == 4 ? data[src + 3] : (byte)255;
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = nameForLog ?? "TGA",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.LoadRawTextureData(rgba);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }
    }
}
