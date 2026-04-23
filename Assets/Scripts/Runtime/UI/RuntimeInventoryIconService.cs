using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Dds;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI
{
    public sealed class RuntimeInventoryIconService : IDisposable
    {
        readonly Dictionary<string, Sprite> _spriteCache = new(StringComparer.OrdinalIgnoreCase);
        readonly List<Texture2D> _textures = new();
        readonly List<Sprite> _sprites = new();
        readonly HashSet<string> _missingLogged = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _decodeFailedLogged = new(StringComparer.OrdinalIgnoreCase);

        string _installPath;
        BsaArchive _bsaArchive;
        TexturePathResolver _textureResolver;
        Sprite _fallbackSprite;

        public Sprite GetSprite(string rawIconPath)
        {
            EnsureInstallContext();

            string normalized = NormalizeTexturePath(rawIconPath);
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
            _textureResolver = null;
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
                _textureResolver = new TexturePathResolver(_bsaArchive);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VVardenfell][UI] failed opening icon BSA '{bsaPath}': {ex.Message}");
                _bsaArchive?.Dispose();
                _bsaArchive = null;
                _textureResolver = null;
            }
        }

        bool TryLoadTexture(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(_installPath))
                return false;

            if (TryLoadLooseTexture(normalizedPath, out texture))
                return true;

            if (_textureResolver == null || _bsaArchive == null)
                return false;

            if (!_textureResolver.TryResolve(normalizedPath, out var entry, out var resolvedName))
                return false;

            try
            {
                var bytes = _bsaArchive.Read(entry);
                texture = DecodeImage(bytes, Path.GetExtension(resolvedName), resolvedName);
                return texture != null;
            }
            catch (Exception ex)
            {
                WarnDecodeFailureOnce(resolvedName, ex.Message);
                return false;
            }
        }

        bool TryLoadLooseTexture(string normalizedPath, out Texture2D texture)
        {
            texture = null;
            string dataFiles = Path.Combine(_installPath, "Data Files");
            foreach (string candidate in EnumerateLooseCandidates(normalizedPath))
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

        IEnumerable<string> EnumerateLooseCandidates(string normalizedPath)
        {
            yield return ChangeExtension(normalizedPath, ".dds");
            if (!string.Equals(Path.GetExtension(normalizedPath), ".dds", StringComparison.OrdinalIgnoreCase))
                yield return normalizedPath;

            string basename = Path.GetFileName(normalizedPath);
            if (!string.IsNullOrWhiteSpace(basename))
            {
                string fallbackBase = $@"textures\{basename}";
                yield return ChangeExtension(fallbackBase, ".dds");
                if (!string.Equals(Path.GetExtension(fallbackBase), ".dds", StringComparison.OrdinalIgnoreCase))
                    yield return fallbackBase;
            }
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

        static string NormalizeTexturePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string path = rawPath.Trim().Replace('/', '\\').ToLowerInvariant();
            while (path.Contains("\\\\", StringComparison.Ordinal))
                path = path.Replace("\\\\", "\\", StringComparison.Ordinal);

            if (path.StartsWith("\\", StringComparison.Ordinal))
                path = path.Substring(1);

            if (path.StartsWith(@"textures\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(@"bookart\", StringComparison.OrdinalIgnoreCase))
                return path;

            int texturesIndex = path.IndexOf(@"\textures\", StringComparison.OrdinalIgnoreCase);
            if (texturesIndex >= 0)
                return path.Substring(texturesIndex + 1);

            int bookartIndex = path.IndexOf(@"\bookart\", StringComparison.OrdinalIgnoreCase);
            if (bookartIndex >= 0)
                return path.Substring(bookartIndex + 1);

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
