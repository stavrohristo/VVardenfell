using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Dds;
using VVardenfell.Runtime.UI.Framework;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Assets
{
    public sealed class UiImageAsset
    {
        public string Id;
        public Texture2D Texture;
        public Sprite Sprite;
    }

    public sealed class UiMovieRuntimeInfo
    {
        public string Slot;
        public string CachedClipPath;
        public string FallbackImageId;
        public int Width;
        public int Height;
        public long DurationMs;
        public bool HasAudio;
        public UiMovieFlags Flags;

        public bool HasPlayableClip =>
            (Flags & UiMovieFlags.TranscodedAvailable) != 0 &&
            !string.IsNullOrWhiteSpace(CachedClipPath) &&
            File.Exists(CachedClipPath);
    }

    public sealed class UiRuntimeAssets : IDisposable
    {
        readonly List<UiImageAsset> _allImages = new();
        readonly List<Texture2D> _allTextures = new();
        readonly List<Sprite> _allSprites = new();

        public UiRuntimeAssets(UiCacheManifest manifest)
        {
            Manifest = manifest;
        }

        public UiCacheManifest Manifest { get; }
        public Dictionary<string, UiImageAsset> ImagesById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, UiImageAsset> BootstrapImagesByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BitmapFontAsset> FontsById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, UiMovieRuntimeInfo> MoviesBySlot { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<UiImageAsset> SplashImages { get; } = new();
        public UiImageAsset MenuBackground { get; set; }
        public BitmapFontAsset DefaultFont { get; set; }

        public UiImageAsset GetImage(string id)
            => !string.IsNullOrEmpty(id) && ImagesById.TryGetValue(id, out var image) ? image : null;

        public UiImageAsset GetBootstrapImage(string key)
            => !string.IsNullOrEmpty(key) && BootstrapImagesByKey.TryGetValue(key, out var image) ? image : null;

        public BitmapFontAsset GetFont(string id)
            => !string.IsNullOrEmpty(id) && FontsById.TryGetValue(id, out var font) ? font : null;

        public UiMovieRuntimeInfo GetMovie(string slot)
            => !string.IsNullOrEmpty(slot) && MoviesBySlot.TryGetValue(slot, out var movie) ? movie : null;

        public void RegisterImage(UiImageAsset image)
        {
            if (image == null)
                return;
            ImagesById[image.Id] = image;
            _allImages.Add(image);
            if (image.Texture != null)
                _allTextures.Add(image.Texture);
            if (image.Sprite != null)
                _allSprites.Add(image.Sprite);
        }

        public void Dispose()
        {
            for (int i = 0; i < _allSprites.Count; i++)
                if (_allSprites[i] != null)
                    Object.Destroy(_allSprites[i]);

            for (int i = 0; i < _allTextures.Count; i++)
                if (_allTextures[i] != null)
                    Object.Destroy(_allTextures[i]);
        }
    }

    public sealed class UiAssetLoader
    {
        public UiRuntimeAssets Load()
        {
            if (TryLoadInternal(out var assets, out string error))
                return assets;

            if (!TryRebuildUiCache(error, out string rebuildError))
                throw new InvalidDataException(rebuildError ?? error ?? "UI cache unreadable.");

            if (TryLoadInternal(out assets, out error))
                return assets;

            throw new InvalidDataException(error ?? "UI cache unreadable after rebuild.");
        }

        bool TryLoadInternal(out UiRuntimeAssets assets, out string error)
        {
            assets = null;
            error = null;

            if (!UiCacheManifest.TryRead(CachePaths.UiManifest, out var manifest))
            {
                error = "ui.bin unreadable";
                return false;
            }

            if (!File.Exists(CachePaths.UiPayloads))
            {
                error = "ui_payloads.bin missing";
                return false;
            }

            try
            {
                assets = new UiRuntimeAssets(manifest);

                using var fs = File.OpenRead(CachePaths.UiPayloads);
                using var r = new BinaryReader(fs);

                for (int i = 0; i < manifest.Images.Length; i++)
                {
                    var record = manifest.Images[i];
                    var image = LoadImage(r, record);
                    assets.RegisterImage(image);
                }

                for (int i = 0; i < manifest.BootstrapImages.Length; i++)
                {
                    var binding = manifest.BootstrapImages[i];
                    var image = assets.GetImage(binding.ImageId);
                    if (image != null)
                        assets.BootstrapImagesByKey[binding.Key] = image;
                }

                for (int i = 0; i < manifest.Fonts.Length; i++)
                {
                    var record = manifest.Fonts[i];
                    var font = LoadFont(r, record);
                    assets.FontsById[record.Id] = font;
                }

                for (int i = 0; i < manifest.Movies.Length; i++)
                {
                    var record = manifest.Movies[i];
                    assets.MoviesBySlot[record.Slot] = new UiMovieRuntimeInfo
                    {
                        Slot = record.Slot,
                        CachedClipPath = record.CachedClipPath,
                        FallbackImageId = record.FallbackImageId,
                        Width = record.Width,
                        Height = record.Height,
                        DurationMs = record.DurationMs,
                        HasAudio = record.HasAudio,
                        Flags = record.Flags,
                    };
                }

                for (int i = 0; i < manifest.SplashImageIds.Length; i++)
                {
                    var image = assets.GetImage(manifest.SplashImageIds[i]);
                    if (image != null)
                        assets.SplashImages.Add(image);
                }

                assets.MenuBackground = assets.GetImage(manifest.MenuBackgroundImageId);
                assets.DefaultFont = assets.GetFont(manifest.DefaultFontId);
                ValidateBootstrapVisuals(manifest, assets);
                return true;
            }
            catch (Exception ex)
            {
                assets?.Dispose();
                assets = null;
                error = ex.Message;
                return false;
            }
        }

        bool TryRebuildUiCache(string loadError, out string error)
        {
            error = null;

            if (!ConfigStorage.TryLoad(out var config) || config == null)
            {
                error = $"UI cache rebuild unavailable: no saved install config. Original error: {loadError}";
                return false;
            }

            if (!config.IsValid(out string configError))
            {
                error = $"UI cache rebuild unavailable: {configError}. Original error: {loadError}";
                return false;
            }

            string bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(bsaPath))
            {
                error = $"UI cache rebuild unavailable: Morrowind.bsa missing at '{bsaPath}'. Original error: {loadError}";
                return false;
            }

            try
            {
                CachePaths.EnsureExists();
                using var bsa = BsaArchive.Open(bsaPath);
                UiAssetBakery.Bake(config, bsa);
                Debug.LogWarning($"[VVardenfell][UI] rebuilt stale UI cache after load failure: {loadError}");
                return true;
            }
            catch (Exception ex)
            {
                error = $"UI cache rebuild failed: {ex.Message}. Original error: {loadError}";
                return false;
            }
        }

        private static UiImageAsset LoadImage(BinaryReader r, UiImageRecord record)
        {
            r.BaseStream.Position = record.PayloadOffset;
            int byteCount = r.ReadInt32();
            if (byteCount <= 0 || byteCount > record.PayloadLength)
                throw new InvalidDataException($"Invalid UI image payload length for '{record.Id}'.");

            var bytes = r.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new EndOfStreamException($"Truncated UI image payload for '{record.Id}'.");

            Texture2D texture = DecodeImage(bytes, record.Extension, record.SourcePath);
            texture.name = record.Id;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = record.Id;

            return new UiImageAsset
            {
                Id = record.Id,
                Texture = texture,
                Sprite = sprite,
            };
        }

        private static BitmapFontAsset LoadFont(BinaryReader r, UiFontRecord record)
        {
            r.BaseStream.Position = record.PayloadOffset;
            if (r.ReadUInt32() != MorrowindFontBakery.PayloadMagic)
                throw new InvalidDataException($"UI font payload magic mismatch for '{record.Id}'.");

            float defaultHeight = r.ReadSingle();
            int width = r.ReadInt32();
            int height = r.ReadInt32();
            int glyphCount = r.ReadInt32();
            var glyphs = new Dictionary<int, BitmapGlyph>(glyphCount);

            for (int i = 0; i < glyphCount; i++)
            {
                int codepoint = r.ReadInt32();
                float x = r.ReadSingle();
                float y = r.ReadSingle();
                float glyphWidth = r.ReadSingle();
                float glyphHeight = r.ReadSingle();
                float advance = r.ReadSingle();
                float bearingX = r.ReadSingle();
                float bearingY = r.ReadSingle();

                // Morrowind stores non-drawing glyphs such as spaces with zero bitmap width
                // but a positive horizontal offset; keep that as runtime advance so words
                // retain their authored spacing even against older cached payloads.
                if (advance <= 0f)
                    advance = Mathf.Max(glyphWidth, bearingX, 0f);

                float yBottom = 1f - ((y + glyphHeight) / height);
                var uv = new Rect(
                    x / width,
                    yBottom,
                    glyphWidth / width,
                    glyphHeight / height);

                glyphs[codepoint] = new BitmapGlyph(uv, glyphWidth, glyphHeight, advance, bearingX, bearingY);
            }

            int pixelLength = r.ReadInt32();
            var pixels = r.ReadBytes(pixelLength);
            if (pixels.Length != pixelLength)
                throw new EndOfStreamException($"Truncated UI font atlas for '{record.Id}'.");

            FlipRgbaRowsInPlace(pixels, width, height);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = record.Id + "_atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            texture.LoadRawTextureData(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            return new BitmapFontAsset(record.Id, texture, defaultHeight, glyphs);
        }

        private static Texture2D DecodeImage(byte[] bytes, string extension, string sourcePath)
        {
            extension = (extension ?? "").ToLowerInvariant();
            return extension switch
            {
                ".dds" => DdsTexture.Load(bytes, sourcePath),
                ".tga" => TgaTexture.Load(bytes, sourcePath),
                ".png" or ".bmp" or ".jpg" or ".jpeg" => LoadViaImageConversion(bytes, sourcePath),
                _ => throw new NotSupportedException($"Unsupported UI image format '{extension}' for '{sourcePath}'."),
            };
        }

        private static Texture2D LoadViaImageConversion(byte[] bytes, string sourcePath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = sourcePath ?? "UI Image",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            if (!ImageConversion.LoadImage(tex, bytes, markNonReadable: true))
                throw new InvalidDataException($"Failed to decode UI image '{sourcePath}'.");

            return tex;
        }

        private static void FlipRgbaRowsInPlace(byte[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 1)
                return;

            int stride = checked(width * 4);
            var scratch = new byte[stride];
            int halfHeight = height / 2;
            for (int y = 0; y < halfHeight; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;
                Buffer.BlockCopy(pixels, top, scratch, 0, stride);
                Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
                Buffer.BlockCopy(scratch, 0, pixels, bottom, stride);
            }
        }

        private static void ValidateBootstrapVisuals(UiCacheManifest manifest, UiRuntimeAssets assets)
        {
            if (!manifest.HasRequiredBootstrapImages(out var error))
                throw new InvalidDataException(error);

            for (int i = 0; i < UiBootstrapAssetKeys.RequiredImageKeys.Length; i++)
            {
                string key = UiBootstrapAssetKeys.RequiredImageKeys[i];
                if (assets.GetBootstrapImage(key) == null)
                    throw new InvalidDataException($"Required bootstrap UI image '{key}' is missing from runtime assets.");
            }

            if (assets.MenuBackground == null)
                throw new InvalidDataException("Required menu background image is missing from runtime assets.");

            if (assets.DefaultFont == null)
                throw new InvalidDataException("Required default bitmap font is missing from runtime assets.");
        }
    }
}
