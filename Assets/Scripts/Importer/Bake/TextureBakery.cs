using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bsa;

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
        private readonly TexturePathResolver _resolver;
        private readonly Dictionary<string, int> _indexByResolved =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _indexByRaw =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _resolvedByIndex = new List<string>();
        private readonly List<string> _hashHexByIndex = new List<string>();

        public int Count => _hashHexByIndex.Count;
        public bool Modified { get; private set; }

        public TextureBakery(BsaArchive bsa, TexturePathResolver resolver)
        {
            _bsa = bsa;
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
                    EnsureTextureFile(entry, _hashHexByIndex[idx]);
                    _indexByRaw[rawTexPath] = idx;
                    return idx;
                }

                string hex = Sha1Hex16(resolved);
                EnsureTextureFile(entry, hex);

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

        private void EnsureTextureFile(BsaEntry entry, string hex)
        {
            string dst = CachePaths.TextureFile(hex);
            if (File.Exists(dst))
                return;

            var bytes = _bsa.Read(entry);
            File.WriteAllBytes(dst, bytes);
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
    }
}
