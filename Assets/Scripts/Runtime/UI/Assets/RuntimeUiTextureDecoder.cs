using System;
using System.IO;
using UnityEngine;
using VVardenfell.Importer.Dds;

namespace VVardenfell.Runtime.UI.Assets
{
    static class RuntimeUiTextureDecoder
    {
        public static Texture2D Decode(byte[] bytes, string extension, string sourcePath, FilterMode filterMode)
        {
            extension = (extension ?? string.Empty).ToLowerInvariant();
            return extension switch
            {
                ".dds" => DecodeDds(bytes, sourcePath, filterMode),
                ".tga" => DecodeTga(bytes, sourcePath, filterMode),
                ".png" or ".bmp" or ".jpg" or ".jpeg" => LoadViaImageConversion(bytes, sourcePath, filterMode),
                _ => throw new NotSupportedException($"Unsupported UI image format '{extension}' for '{sourcePath}'."),
            };
        }

        public static Texture2D CreateRgba32Texture(
            byte[] rgba,
            int width,
            int height,
            string textureName,
            FilterMode filterMode)
        {
            if (rgba == null)
                throw new ArgumentNullException(nameof(rgba));
            int expected = checked(width * height * 4);
            if (width <= 0 || height <= 0 || rgba.Length != expected)
                throw new InvalidDataException($"Invalid RGBA32 UI texture payload for '{textureName}'.");

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = textureName ?? "UI Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filterMode,
            };
            texture.LoadRawTextureData(rgba);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        static Texture2D DecodeDds(byte[] bytes, string sourcePath, FilterMode filterMode)
        {
            var payload = DdsTexture.DecodeToRgba32Payload(bytes, sourcePath);
            byte[] topMip = CloneTopMip(payload);
            return CreateRgba32Texture(topMip, payload.Width, payload.Height, sourcePath ?? "DDS", filterMode);
        }

        static Texture2D DecodeTga(byte[] bytes, string sourcePath, FilterMode filterMode)
        {
            DecodeTgaToTopLeftRgba32(bytes, sourcePath, out int width, out int height, out byte[] rgba);
            return CreateRgba32Texture(rgba, width, height, sourcePath ?? "TGA", filterMode);
        }

        static Texture2D LoadViaImageConversion(byte[] bytes, string sourcePath, FilterMode filterMode)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = sourcePath ?? "UI Image",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filterMode,
            };

            if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
                throw new InvalidDataException($"Failed to decode UI image '{sourcePath}'.");

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filterMode;
            return texture;
        }

        static byte[] CloneTopMip(DdsTexture.Payload payload)
        {
            if (payload?.Mips == null || payload.Mips.Length == 0 || payload.Mips[0] == null)
                throw new InvalidDataException("DDS payload has no top mip.");

            byte[] topMip = payload.Mips[0];
            int expected = checked(payload.Width * payload.Height * 4);
            if (topMip.Length != expected)
                throw new InvalidDataException($"DDS top mip has invalid RGBA32 length ({topMip.Length} != {expected}).");

            var clone = new byte[topMip.Length];
            Buffer.BlockCopy(topMip, 0, clone, 0, topMip.Length);
            return clone;
        }

        static void DecodeTgaToTopLeftRgba32(byte[] data, string nameForLog, out int width, out int height, out byte[] rgba)
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

            width = data[12] | (data[13] << 8);
            height = data[14] | (data[15] << 8);
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
            rgba = new byte[checked(width * height * 4)];
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
        }

    }
}
