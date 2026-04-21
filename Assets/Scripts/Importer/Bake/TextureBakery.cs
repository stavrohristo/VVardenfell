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
    /// Copies unique DDS payloads out of the BSA into the cache texture directory.
    /// Keyed by normalised BSA path; filename on disk is the truncated SHA-1.
    /// Returns stable indices (0..N-1) that can be referenced by MaterialRecord.
    /// </summary>
    public sealed class TextureBakery
    {
        private readonly BsaArchive _bsa;
        private readonly TexturePathResolver _resolver;
        private readonly Dictionary<string, int> _indexByRaw =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _hashHexByIndex = new List<string>();

        public int Count => _hashHexByIndex.Count;

        public TextureBakery(BsaArchive bsa, TexturePathResolver resolver)
        {
            _bsa = bsa;
            _resolver = resolver;
        }

        /// <summary>
        /// Returns the texture index for the given raw NIF path, copying the DDS to the
        /// cache on first use. Returns -1 if the texture can't be resolved in the BSA.
        /// </summary>
        public int AddOrGet(string rawTexPath)
        {
            if (string.IsNullOrEmpty(rawTexPath)) return -1;
            if (_indexByRaw.TryGetValue(rawTexPath, out var idx)) return idx;

            if (!_resolver.TryResolve(rawTexPath, out var entry, out var resolved))
            {
                _indexByRaw[rawTexPath] = -1;
                return -1;
            }

            string hex = Sha1Hex16(resolved);
            string dst = CachePaths.TextureFile(hex);
            if (!File.Exists(dst))
            {
                var bytes = _bsa.Read(entry);
                File.WriteAllBytes(dst, bytes);
            }

            idx = _hashHexByIndex.Count;
            _hashHexByIndex.Add(hex);
            _indexByRaw[rawTexPath] = idx;
            return idx;
        }

        public IReadOnlyList<string> HashesInOrder => _hashHexByIndex;

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

        /// <summary>Read an index produced by <see cref="WriteIndex"/>. Returns the ordered hash list.</summary>
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

        private static string Sha1Hex16(string key)
        {
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.AppendFormat("{0:x2}", hash[i]);
            return sb.ToString();
        }
    }
}
