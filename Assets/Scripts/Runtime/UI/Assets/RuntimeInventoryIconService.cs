using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Dds;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Assets
{
    /// <summary>
    /// Loads vanilla UI icon textures on demand and caches them as sprites.
    ///
    /// Important: MW inventory ITEX and magic effect ITEX paths are relative to
    /// <c>Icons\</c>, NOT <c>Textures\</c> - unlike mesh/material textures. A vanilla
    /// value looks like <c>w\silver_longsword.tga</c> or <c>m\tx_fire_damage.tga</c>,
    /// and the engine prepends <c>Icons\</c> at load time. Some content DBs / mods
    /// pre-mangle the path with a <c>textures\</c> prefix; we strip that and retry
    /// under <c>icons\</c>.
    /// Falls back through loose files first, then the install's Morrowind.bsa.
    /// </summary>
    public sealed class RuntimeInventoryIconService : IDisposable
    {
        readonly Dictionary<string, Sprite> _spriteCache = new(StringComparer.OrdinalIgnoreCase);
        readonly List<Texture2D> _textures = new();
        readonly List<Sprite> _sprites = new();
        readonly HashSet<string> _missingLogged = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _decodeFailedLogged = new(StringComparer.OrdinalIgnoreCase);

        string _installPath;
        BsaArchive _bsaArchive;
        Dictionary<string, BsaEntry> _bsaEntriesByPath;
        Sprite _fallbackSprite;

        public Sprite GetSprite(string rawIconPath)
        {
            EnsureInstallContext();

            string normalized = NormalizeIconPath(rawIconPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return GetFallbackSprite();

            if (_spriteCache.TryGetValue(normalized, out var cached))
                return cached;

            if (!TryLoadTexture(normalized, out var texture))
            {
                WarnMissingOnce(normalized);
                return _spriteCache[normalized] = GetFallbackSprite();
            }

            texture.name = Path.GetFileNameWithoutExtension(normalized);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name + "_sprite";

            _textures.Add(texture);
            _sprites.Add(sprite);
            _spriteCache[normalized] = sprite;
            return sprite;
        }

        public Sprite GetMagicEffectSprite(string rawIconPath) => GetSprite(rawIconPath);

        public bool TryGetTextureSprite(string rawTexturePath, out Sprite sprite)
        {
            EnsureInstallContext();

            sprite = null;
            string normalized = NormalizeTexturePath(rawTexturePath);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (_spriteCache.TryGetValue(normalized, out sprite))
                return sprite != null;

            if (!TryLoadAssetTexture(normalized, out var texture))
                return false;

            texture.name = Path.GetFileNameWithoutExtension(normalized);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name + "_sprite";

            _textures.Add(texture);
            _sprites.Add(sprite);
            _spriteCache[normalized] = sprite;
            return true;
        }

        public Sprite RequireTextureSprite(string rawTexturePath, string label)
        {
            if (TryGetTextureSprite(rawTexturePath, out var sprite))
                return sprite;

            throw new InvalidOperationException($"[VVardenfell][UI] Missing {label} texture '{rawTexturePath}'.");
        }

        public void Dispose()
        {
            if (_fallbackSprite != null)
                _sprites.Remove(_fallbackSprite);

            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i] != null)
                    Object.Destroy(_sprites[i]);
            }

            for (int i = 0; i < _textures.Count; i++)
            {
                if (_textures[i] != null)
                    Object.Destroy(_textures[i]);
            }

            if (_fallbackSprite != null)
            {
                Object.Destroy(_fallbackSprite);
                _fallbackSprite = null;
            }

            _spriteCache.Clear();
            _sprites.Clear();
            _textures.Clear();
            _bsaArchive?.Dispose();
            _bsaArchive = null;
            _bsaEntriesByPath = null;
        }

        void EnsureInstallContext()
        {
            if (!string.IsNullOrWhiteSpace(_installPath))
                return;

            if (!ConfigStorage.TryLoad(out var config) || config == null || !config.IsValid(out _))
                return;

            _installPath = config.InstallPath;

            string bsaPath = Path.Combine(_installPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(bsaPath))
                return;

            try
            {
                _bsaArchive = BsaArchive.Open(bsaPath);
                _bsaEntriesByPath = new Dictionary<string, BsaEntry>(_bsaArchive.Entries.Length, StringComparer.OrdinalIgnoreCase);
                foreach (var e in _bsaArchive.Entries)
                    _bsaEntriesByPath[e.Name] = e;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VVardenfell][UI] failed opening icon BSA '{bsaPath}': {ex.Message}");
                _bsaArchive?.Dispose();
                _bsaArchive = null;
                _bsaEntriesByPath = null;
            }
        }

        bool TryLoadTexture(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(_installPath))
                return false;

            if (TryLoadLooseIcon(normalizedPath, out texture))
                return true;

            if (_bsaEntriesByPath == null || _bsaArchive == null)
                return false;

            foreach (string candidate in EnumerateIconCandidates(normalizedPath))
            {
                if (!_bsaEntriesByPath.TryGetValue(candidate, out var entry))
                    continue;

                try
                {
                    var bytes = _bsaArchive.Read(entry);
                    texture = DecodeImage(bytes, Path.GetExtension(candidate), candidate);
                    if (texture != null)
                        return true;
                }
                catch (Exception ex)
                {
                    WarnDecodeFailureOnce(candidate, ex.Message);
                }
            }

            return false;
        }

        bool TryLoadAssetTexture(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(_installPath))
                return false;

            if (TryLoadLooseAsset(normalizedPath, out texture))
                return true;

            if (_bsaEntriesByPath == null || _bsaArchive == null)
                return false;

            foreach (string candidate in EnumerateIconCandidates(normalizedPath))
            {
                if (!_bsaEntriesByPath.TryGetValue(candidate, out var entry))
                    continue;

                try
                {
                    var bytes = _bsaArchive.Read(entry);
                    texture = DecodeImage(bytes, Path.GetExtension(candidate), candidate);
                    if (texture != null)
                        return true;
                }
                catch (Exception ex)
                {
                    WarnDecodeFailureOnce(candidate, ex.Message);
                }
            }

            return false;
        }

        bool TryLoadLooseAsset(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            string dataFiles = Path.Combine(_installPath, "Data Files");
            foreach (string candidate in EnumerateIconCandidates(normalizedPath))
            {
                string fullPath = Path.Combine(dataFiles, candidate.Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    continue;

                try
                {
                    texture = DecodeImage(File.ReadAllBytes(fullPath), Path.GetExtension(fullPath), candidate);
                    return texture != null;
                }
                catch (Exception ex)
                {
                    WarnDecodeFailureOnce(candidate, ex.Message);
                }
            }

            return false;
        }

        bool TryLoadLooseIcon(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            string dataFiles = Path.Combine(_installPath, "Data Files");
            foreach (string candidate in EnumerateIconCandidates(normalizedPath))
            {
                string fullPath = Path.Combine(dataFiles, candidate.Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    continue;

                try
                {
                    texture = DecodeImage(File.ReadAllBytes(fullPath), Path.GetExtension(fullPath), candidate);
                    return texture != null;
                }
                catch (Exception ex)
                {
                    WarnDecodeFailureOnce(candidate, ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Candidate paths tried in order. MW archives often replace original .tga
        /// icons with .dds copies, so we try the .dds form first, then the original
        /// extension as a fallback.
        /// </summary>
        static IEnumerable<string> EnumerateIconCandidates(string normalizedPath)
        {
            string dds = ChangeExtension(normalizedPath, ".dds");
            yield return dds;
            if (!string.Equals(normalizedPath, dds, StringComparison.OrdinalIgnoreCase))
                yield return normalizedPath;
        }

        Texture2D DecodeImage(byte[] bytes, string extension, string sourcePath)
        {
            extension = (extension ?? string.Empty).ToLowerInvariant();
            return extension switch
            {
                ".dds" => DdsTexture.Load(bytes, sourcePath),
                ".tga" => TgaTexture.Load(bytes, sourcePath),
                ".png" or ".bmp" or ".jpg" or ".jpeg" => LoadViaImageConversion(bytes, sourcePath),
                _ => throw new NotSupportedException($"Unsupported icon image format '{extension}' for '{sourcePath}'."),
            };
        }

        static Texture2D LoadViaImageConversion(byte[] bytes, string sourcePath)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = sourcePath ?? "Inventory Icon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
                throw new InvalidDataException($"Failed to decode inventory icon '{sourcePath}'.");

            return texture;
        }

        Sprite GetFallbackSprite()
        {
            if (_fallbackSprite != null)
                return _fallbackSprite;

            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "VVardenfell.InventoryIconFallback",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            var pixels = new Color32[64];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    bool border = x == 0 || x == 7 || y == 0 || y == 7;
                    pixels[y * 8 + x] = border
                        ? new Color32(145, 122, 82, 255)
                        : new Color32(22, 18, 14, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _textures.Add(texture);
            _fallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            _fallbackSprite.name = texture.name + "_sprite";
            _sprites.Add(_fallbackSprite);
            return _fallbackSprite;
        }

        /// <summary>
        /// Vanilla MW ITEX records, including MGEF icons, are relative to <c>Icons\</c>. This normalizer produces
        /// an <c>icons\...</c> path regardless of what the raw input looks like:
        /// <list type="bullet">
        ///   <item>Already <c>icons\...</c> - pass through.</item>
        ///   <item>Has <c>\icons\</c> embedded - trim to the <c>icons\</c> portion.</item>
        ///   <item>Starts with <c>textures\</c> (mod/content-DB bug) - strip it and re-root under <c>icons\</c>.</item>
        ///   <item>Otherwise - prepend <c>icons\</c> (the common vanilla ITEX case).</item>
        /// </list>
        /// </summary>
        static string NormalizeIconPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string path = rawPath.Trim().Replace('/', '\\').ToLowerInvariant();
            while (path.Contains("\\\\", StringComparison.Ordinal))
                path = path.Replace("\\\\", "\\", StringComparison.Ordinal);

            if (path.StartsWith("\\", StringComparison.Ordinal))
                path = path.Substring(1);

            if (path.StartsWith(@"icons\", StringComparison.OrdinalIgnoreCase))
                return path;

            int iconsIndex = path.IndexOf(@"\icons\", StringComparison.OrdinalIgnoreCase);
            if (iconsIndex >= 0)
                return path.Substring(iconsIndex + 1);

            // ITEX stored with a textures\ prefix is a content-side bug (vanilla stores
            // ITEX relative to Icons\, not Textures\). Strip the misleading prefix so the
            // remainder - typically "w\foo.tga" - can be re-rooted under Icons\.
            if (path.StartsWith(@"textures\", StringComparison.OrdinalIgnoreCase))
                return @"icons\" + path.Substring(@"textures\".Length);

            return @"icons\" + path;
        }

        static string NormalizeTexturePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string path = rawPath.Trim().Replace('/', '\\').ToLowerInvariant();
            while (path.Contains("\\\\", StringComparison.Ordinal))
                path = path.Replace("\\\\", "\\", StringComparison.Ordinal);

            if (path.StartsWith("\\", StringComparison.Ordinal))
                path = path.Substring(1);

            if (path.StartsWith(@"textures\", StringComparison.OrdinalIgnoreCase))
                return path;

            int textureIndex = path.IndexOf(@"\textures\", StringComparison.OrdinalIgnoreCase);
            if (textureIndex >= 0)
                return path.Substring(textureIndex + 1);

            return @"textures\" + path;
        }

        static string ChangeExtension(string path, string newExtension)
        {
            int dot = path.LastIndexOf('.');
            if (dot < 0)
                return path + newExtension;
            return path.Substring(0, dot) + newExtension;
        }

        void WarnMissingOnce(string normalizedPath)
        {
            if (_missingLogged.Add(normalizedPath))
                Debug.LogWarning($"[VVardenfell][UI] missing inventory icon: '{normalizedPath}'.");
        }

        void WarnDecodeFailureOnce(string path, string reason)
        {
            if (_decodeFailedLogged.Add(path))
                Debug.LogWarning($"[VVardenfell][UI] failed decoding inventory icon '{path}': {reason}");
        }
    }
}
