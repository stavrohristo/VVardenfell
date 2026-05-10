using System;
using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Config;

namespace VVardenfell.Importer.Bsa
{
    public sealed class ContentAssetResolver : IDisposable
    {
        readonly List<BsaArchive> _archives = new();
        readonly Dictionary<string, BsaEntry> _entries;

        ContentAssetResolver(Dictionary<string, BsaEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyDictionary<string, BsaEntry> Entries => _entries;
        public BsaArchive PrimaryArchive => _archives.Count > 0 ? _archives[0] : null;

        public static ContentAssetResolver Open(MorrowindContentProfile profile)
        {
            var resolver = new ContentAssetResolver(new Dictionary<string, BsaEntry>(StringComparer.OrdinalIgnoreCase));

            foreach (string archivePath in profile?.Archives ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                    continue;

                var archive = BsaArchive.Open(archivePath);
                resolver._archives.Add(archive);
                for (int i = 0; i < archive.Entries.Length; i++)
                    resolver._entries[NormalizePath(archive.Entries[i].Name)] = archive.Entries[i];
            }

            foreach (string root in profile?.DataRoots ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file);
                    string normalized = NormalizePath(relative);
                    var info = new FileInfo(file);
                    resolver._entries[normalized] = new BsaEntry(normalized, file, info.Length);
                }
            }

            return resolver;
        }

        public bool Contains(string path)
            => !string.IsNullOrWhiteSpace(path) && _entries.ContainsKey(NormalizePath(path));

        public bool TryGetEntry(string path, out BsaEntry entry)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                entry = default;
                return false;
            }

            return _entries.TryGetValue(NormalizePath(path), out entry);
        }

        public bool TryReadBytes(string path, out byte[] bytes, out string resolvedPath)
        {
            bytes = null;
            resolvedPath = null;
            if (!TryGetEntry(path, out var entry))
                return false;

            bytes = Read(entry);
            resolvedPath = NormalizePath(entry.Name);
            return true;
        }

        public byte[] Read(in BsaEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.LoosePath))
                return File.ReadAllBytes(entry.LoosePath);

            if (!string.IsNullOrWhiteSpace(entry.ArchivePath))
            {
                for (int i = 0; i < _archives.Count; i++)
                    if (string.Equals(_archives[i].FilePath, entry.ArchivePath, StringComparison.OrdinalIgnoreCase))
                        return _archives[i].Read(entry);
            }

            if (PrimaryArchive == null)
                throw new InvalidOperationException($"No archive is available to read '{entry.Name}'.");

            return PrimaryArchive.Read(entry);
        }

        public Dictionary<string, BsaEntry> CopyEntryMap()
            => new(_entries, StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            for (int i = _archives.Count - 1; i >= 0; i--)
                _archives[i]?.Dispose();
            _archives.Clear();
        }

        public static string NormalizePath(string path)
        {
            string value = (path ?? string.Empty).Trim().Trim('"').Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
            while (value.Contains("\\\\", StringComparison.Ordinal))
                value = value.Replace("\\\\", "\\");
            return value;
        }
    }
}
