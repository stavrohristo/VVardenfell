using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Dds;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Copies unique DDS payloads out of the BSA into the cache texture directory and
    /// preserves stable indices across runs via a sidecar catalog.
    /// </summary>
    public sealed class TextureBakery
    {
        private const uint MagicCatalog = 0x54414354u; // 'TCAT'

        private readonly object _gate = new object();
        private readonly BsaArchive _bsa;
        private readonly ContentAssetResolver _assetResolver;
        private readonly TexturePathResolver _resolver;
        private readonly Dictionary<string, int> _indexByResolved =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _indexByRaw =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _resolvedByIndex = new List<string>();
        private readonly List<string> _hashHexByIndex = new List<string>();
        private readonly Dictionary<int, DdsTexture.Payload> _payloadByIndex = new Dictionary<int, DdsTexture.Payload>();
        private readonly Dictionary<int, DdsTexture.Payload> _rgba32PayloadByIndex = new Dictionary<int, DdsTexture.Payload>();

        public int Count => _hashHexByIndex.Count;
        public bool Modified { get; private set; }

        public TextureBakery(BsaArchive bsa, TexturePathResolver resolver)
        {
            _bsa = bsa;
            _resolver = resolver;
        }

        public TextureBakery(ContentAssetResolver assetResolver, TexturePathResolver resolver)
        {
            _assetResolver = assetResolver;
            _bsa = assetResolver?.PrimaryArchive;
            _resolver = resolver;
        }

        public void TryLoadExisting(string catalogPath)
        {
            if (!File.Exists(catalogPath))
                return;

            try
            {
                using var fs = File.OpenRead(catalogPath);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != MagicCatalog)
                    return;

                uint count = r.ReadUInt32();
                for (int i = 0; i < count; i++)
                {
                    string resolved = r.ReadString();
                    string hash = r.ReadString();
                    int index = _hashHexByIndex.Count;
                    _resolvedByIndex.Add(resolved);
                    _hashHexByIndex.Add(hash);
                    _indexByResolved[resolved] = index;
                }
            }
            catch
            {
                _indexByResolved.Clear();
                _resolvedByIndex.Clear();
                _hashHexByIndex.Clear();
            }
        }

        /// <summary>
        /// Returns the texture index for the given raw NIF path, copying the DDS to the
        /// cache on first use. Returns -1 if the texture can't be resolved in the BSA.
        /// </summary>
        public int AddOrGet(string rawTexPath)
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(rawTexPath))
                    return -1;
                if (_indexByRaw.TryGetValue(rawTexPath, out var idx))
                    return idx;

                if (!_resolver.TryResolve(rawTexPath, out var entry, out var resolved))
                {
                    _indexByRaw[rawTexPath] = -1;
                    return -1;
                }

                if (_indexByResolved.TryGetValue(resolved, out idx))
                {
                    _indexByRaw[rawTexPath] = idx;
                    return idx;
                }

                string hex = Sha1Hex16(resolved);

                idx = _hashHexByIndex.Count;
                _resolvedByIndex.Add(resolved);
                _hashHexByIndex.Add(hex);
                _indexByResolved[resolved] = idx;
                _indexByRaw[rawTexPath] = idx;
                Modified = true;
                return idx;
            }
        }

        public int AddOrGetRequired(string rawTexPath, string context)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(rawTexPath))
                    throw new InvalidDataException($"{context} has an empty texture path.");
                if (_indexByRaw.TryGetValue(rawTexPath, out var idx))
                {
                    if (idx < 0)
                        throw new InvalidDataException($"{context} texture '{rawTexPath}' could not be resolved in configured data roots or archives.");
                    return idx;
                }

                if (!_resolver.TryResolve(rawTexPath, out var entry, out var resolved))
                {
                    _indexByRaw[rawTexPath] = -1;
                    throw new InvalidDataException($"{context} texture '{rawTexPath}' could not be resolved in configured data roots or archives.");
                }

                if (_indexByResolved.TryGetValue(resolved, out idx))
                {
                    _indexByRaw[rawTexPath] = idx;
                    return idx;
                }

                string hex = Sha1Hex16(resolved);

                idx = _hashHexByIndex.Count;
                _resolvedByIndex.Add(resolved);
                _hashHexByIndex.Add(hex);
                _indexByResolved[resolved] = idx;
                _indexByRaw[rawTexPath] = idx;
                Modified = true;
                return idx;
            }
        }

        public IReadOnlyList<string> HashesInOrder => _hashHexByIndex;

        public readonly struct CatalogEntry
        {
            public readonly string ResolvedPath;
            public readonly string HashHex;

            public CatalogEntry(string resolvedPath, string hashHex)
            {
                ResolvedPath = resolvedPath;
                HashHex = hashHex;
            }
        }

        public int2 GetBucketDimensions(int textureIndex)
        {
            if ((uint)textureIndex >= (uint)_hashHexByIndex.Count)
                return new int2(1, 1);

            var payload = GetPayload(textureIndex);
            return new int2(math.max(1, payload.Width), math.max(1, payload.Height));
        }

        public int GetBucketKey(int textureIndex)
        {
            if ((uint)textureIndex >= (uint)_hashHexByIndex.Count)
                return RefTextureBucketFile.MakeBucketKey(1, 1, TextureFormat.RGBA32, 1);

            var payload = GetPayload(textureIndex);
            return RefTextureBucketFile.MakeBucketKey(payload.Width, payload.Height, payload.Format, payload.MipCount);
        }

        public DdsTexture.Payload GetPayload(int textureIndex)
        {
            lock (_gate)
            {
                if ((uint)textureIndex >= (uint)_hashHexByIndex.Count)
                    throw new InvalidDataException($"Texture index {textureIndex} is out of range.");
                if (_payloadByIndex.TryGetValue(textureIndex, out var payload))
                    return payload;

                string resolved = _resolvedByIndex[textureIndex];
                if (!_resolver.TryResolve(resolved, out var entry, out var actualResolved))
                    throw new InvalidDataException($"Texture '{resolved}' is missing from BSA; rebake cannot build texture buckets.");
                if (!string.Equals(resolved, actualResolved, StringComparison.OrdinalIgnoreCase))
                {
                    _resolvedByIndex[textureIndex] = actualResolved;
                    _indexByResolved.Remove(resolved);
                    _indexByResolved[actualResolved] = textureIndex;
                }

                payload = DdsTexture.DecodePayload(ReadEntry(entry), actualResolved);
                _payloadByIndex[textureIndex] = payload;
                return payload;
            }
        }

        public DdsTexture.Payload GetRgba32Payload(int textureIndex)
        {
            lock (_gate)
            {
                if ((uint)textureIndex >= (uint)_hashHexByIndex.Count)
                    throw new InvalidDataException($"Texture index {textureIndex} is out of range.");
                if (_rgba32PayloadByIndex.TryGetValue(textureIndex, out var payload))
                    return payload;

                string resolved = _resolvedByIndex[textureIndex];
                if (!_resolver.TryResolve(resolved, out var entry, out var actualResolved))
                    throw new InvalidDataException($"Texture '{resolved}' is missing from BSA; rebake cannot build terrain texture layers.");

                payload = DdsTexture.DecodeToRgba32Payload(ReadEntry(entry), actualResolved);
                _rgba32PayloadByIndex[textureIndex] = payload;
                return payload;
            }
        }

        public RefTextureBucketData BuildRefTextureBuckets()
        {
            int count = _hashHexByIndex.Count;
            var textureBucketKeys = new int[count];
            var textureSlices = new int[count];
            var groups = new Dictionary<int, List<int>>();
            var bucketMetadata = new Dictionary<int, DdsTexture.Payload>();

            for (int i = 0; i < count; i++)
            {
                var payload = GetPayload(i);
                int key = RefTextureBucketFile.MakeBucketKey(payload.Width, payload.Height, payload.Format, payload.MipCount);
                if (bucketMetadata.TryGetValue(key, out var existing))
                {
                    if (existing.Width != payload.Width || existing.Height != payload.Height || existing.Format != payload.Format || existing.MipCount != payload.MipCount)
                        throw new InvalidDataException($"Ref texture bucket key collision for texture index {i}.");
                }
                else
                {
                    bucketMetadata.Add(key, payload);
                }

                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<int>();
                textureBucketKeys[i] = key;
                textureSlices[i] = list.Count;
                list.Add(i);
            }

            int fallbackKey = RefTextureBucketFile.MakeBucketKey(1, 1, TextureFormat.RGBA32, 1);
            if (!groups.ContainsKey(fallbackKey))
            {
                groups[fallbackKey] = new List<int>();
                bucketMetadata[fallbackKey] = new DdsTexture.Payload
                {
                    Width = 1,
                    Height = 1,
                    MipCount = 1,
                    Format = TextureFormat.RGBA32,
                    Mips = new[] { new byte[] { 255, 255, 255, 255 } },
                };
            }

            var keys = new int[groups.Count];
            groups.Keys.CopyTo(keys, 0);
            Array.Sort(keys);

            var buckets = new RefTextureBucketDef[keys.Length];
            for (int b = 0; b < keys.Length; b++)
            {
                int key = keys[b];
                var metadata = bucketMetadata[key];
                var list = groups[key];
                int fallbackSlice = list.Count;
                var slices = new RefTextureBucketSlice[list.Count + 1];
                for (int s = 0; s < list.Count; s++)
                {
                    var payload = GetPayload(list[s]);
                    slices[s] = new RefTextureBucketSlice { Mips = CloneMips(payload.Mips) };
                }
                slices[fallbackSlice] = new RefTextureBucketSlice
                {
                    Mips = BuildWhiteMips(metadata.Width, metadata.Height, metadata.MipCount, metadata.Format),
                };

                buckets[b] = new RefTextureBucketDef
                {
                    BucketKey = key,
                    Width = metadata.Width,
                    Height = metadata.Height,
                    MipCount = metadata.MipCount,
                    Format = metadata.Format,
                    SliceCount = slices.Length,
                    FallbackSlice = fallbackSlice,
                    Slices = slices,
                };
            }

            return new RefTextureBucketData
            {
                TextureBucketKeys = textureBucketKeys,
                TextureSlices = textureSlices,
                Buckets = buckets,
            };
        }

        static byte[][] CloneMips(byte[][] source)
        {
            var result = new byte[source.Length][];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new byte[source[i].Length];
                Buffer.BlockCopy(source[i], 0, result[i], 0, source[i].Length);
            }

            return result;
        }

        static byte[][] BuildWhiteMips(int width, int height, int mipCount, TextureFormat format)
        {
            var mips = new byte[mipCount][];
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = math.max(1, width >> mip);
                int h = math.max(1, height >> mip);
                if (format == TextureFormat.DXT1)
                {
                    int blocks = math.max(1, (w + 3) / 4) * math.max(1, (h + 3) / 4);
                    byte[] bytes = new byte[blocks * 8];
                    for (int i = 0; i < blocks; i++)
                    {
                        int offset = i * 8;
                        bytes[offset + 0] = 0xFF;
                        bytes[offset + 1] = 0xFF;
                        bytes[offset + 2] = 0xFF;
                        bytes[offset + 3] = 0xFF;
                    }
                    mips[mip] = bytes;
                }
                else if (format == TextureFormat.DXT5)
                {
                    int blocks = math.max(1, (w + 3) / 4) * math.max(1, (h + 3) / 4);
                    byte[] bytes = new byte[blocks * 16];
                    for (int i = 0; i < blocks; i++)
                    {
                        int offset = i * 16;
                        bytes[offset + 0] = 0xFF;
                        bytes[offset + 1] = 0xFF;
                        bytes[offset + 8] = 0xFF;
                        bytes[offset + 9] = 0xFF;
                        bytes[offset + 10] = 0xFF;
                        bytes[offset + 11] = 0xFF;
                    }
                    mips[mip] = bytes;
                }
                else if (format == TextureFormat.RGBA32)
                {
                    byte[] bytes = new byte[w * h * 4];
                    for (int i = 0; i < bytes.Length; i++)
                        bytes[i] = 0xFF;
                    mips[mip] = bytes;
                }
                else
                {
                    throw new InvalidDataException($"Unsupported ref texture bucket fallback format {format}.");
                }
            }

            return mips;
        }

        public void WriteCatalog(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCatalog);
            w.Write((uint)_hashHexByIndex.Count);
            for (int i = 0; i < _hashHexByIndex.Count; i++)
            {
                w.Write(_resolvedByIndex[i] ?? string.Empty);
                w.Write(_hashHexByIndex[i] ?? string.Empty);
            }
            Modified = false;
        }

        public void WriteIndex(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((uint)_hashHexByIndex.Count);
            foreach (var hex in _hashHexByIndex)
            {
                var utf8 = Encoding.ASCII.GetBytes(hex);
                w.Write((ushort)utf8.Length);
                w.Write(utf8);
            }
        }

        public static string[] ReadIndex(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            uint n = r.ReadUInt32();
            var result = new string[n];
            for (int i = 0; i < n; i++)
            {
                int len = r.ReadUInt16();
                var utf8 = r.ReadBytes(len);
                result[i] = Encoding.ASCII.GetString(utf8);
            }
            return result;
        }

        public static CatalogEntry[] ReadCatalog(string path)
        {
            if (!File.Exists(path))
                return Array.Empty<CatalogEntry>();

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicCatalog)
                return Array.Empty<CatalogEntry>();

            uint count = r.ReadUInt32();
            var result = new CatalogEntry[count];
            for (int i = 0; i < count; i++)
                result[i] = new CatalogEntry(r.ReadString(), r.ReadString());
            return result;
        }

        private static string Sha1Hex16(string key)
        {
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.AppendFormat("{0:x2}", hash[i]);
            return sb.ToString();
        }

        byte[] ReadEntry(BsaEntry entry)
        {
            if (_assetResolver != null)
                return _assetResolver.Read(entry);
            if (_bsa == null)
                throw new InvalidDataException($"Texture '{entry.Name}' is unavailable because no archive resolver is loaded.");
            return _bsa.Read(entry);
        }

    }
}
