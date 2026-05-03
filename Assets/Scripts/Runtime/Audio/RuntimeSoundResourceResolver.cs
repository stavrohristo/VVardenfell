using System;
using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;

namespace VVardenfell.Runtime.Audio
{
    /// <summary>
    /// Resolves vanilla sound resources from loose Data Files first, then Morrowind.bsa.
    /// BSA-backed sounds are extracted to the runtime cache because Unity's audio loader
    /// consumes filesystem paths, not archive offsets.
    /// </summary>
    public sealed class RuntimeSoundResourceResolver : IDisposable
    {
        readonly Dictionary<string, string> _resolvedCache = new(StringComparer.OrdinalIgnoreCase);
        readonly List<BsaArchive> _bsaArchives = new();

        string _installPath;
        Dictionary<string, (BsaArchive Archive, BsaEntry Entry)> _bsaEntriesByPath;

        public bool TryResolve(string installPath, string rawPath, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(rawPath))
                return false;

            EnsureInstallContext(installPath);
            string relativePath = SoundPathResolver.Correct(rawPath);
            foreach (string candidate in EnumerateSoundCandidates(relativePath))
            {
                if (_resolvedCache.TryGetValue(candidate, out string cached) && File.Exists(cached))
                {
                    resolvedPath = cached;
                    return true;
                }

                if (TryResolveLoose(candidate, out resolvedPath))
                {
                    _resolvedCache[candidate] = resolvedPath;
                    return true;
                }

                if (TryResolveBsa(candidate, out resolvedPath))
                {
                    _resolvedCache[candidate] = resolvedPath;
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            for (int i = 0; i < _bsaArchives.Count; i++)
                _bsaArchives[i]?.Dispose();
            _bsaArchives.Clear();
            _bsaEntriesByPath = null;
            _resolvedCache.Clear();
            _installPath = null;
        }

        void EnsureInstallContext(string installPath)
        {
            if (string.Equals(_installPath, installPath, StringComparison.OrdinalIgnoreCase))
                return;

            Dispose();
            _installPath = installPath;

            string[] archivePaths = ResolveArchivePaths(_installPath);
            if (archivePaths.Length == 0)
                return;

            _bsaEntriesByPath = new Dictionary<string, (BsaArchive Archive, BsaEntry Entry)>(StringComparer.OrdinalIgnoreCase);
            for (int archiveIndex = 0; archiveIndex < archivePaths.Length; archiveIndex++)
                OpenArchive(archivePaths[archiveIndex]);
        }

        bool TryResolveLoose(string relativePath, out string resolvedPath)
        {
            resolvedPath = Path.Combine(_installPath, "Data Files", relativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(resolvedPath))
                return true;

            resolvedPath = null;
            return false;
        }

        bool TryResolveBsa(string relativePath, out string resolvedPath)
        {
            resolvedPath = null;
            if (_bsaEntriesByPath == null)
                return false;

            string archivePath = NormalizeArchivePath(relativePath);
            if (!_bsaEntriesByPath.TryGetValue(archivePath, out var source))
                return false;

            CachePaths.EnsureExists();
            string extension = Path.GetExtension(archivePath);
            if (string.IsNullOrWhiteSpace(extension))
                throw new InvalidDataException($"[VVardenfell][Audio] BSA sound entry '{archivePath}' has no audio extension.");

            string extractedPath = Path.Combine(CachePaths.AudioDir, CachePaths.StableHashHex(source.Archive.FilePath + "|" + archivePath) + extension.ToLowerInvariant());
            if (!File.Exists(extractedPath))
            {
                byte[] bytes = source.Archive.Read(source.Entry);
                Directory.CreateDirectory(CachePaths.AudioDir);
                File.WriteAllBytes(extractedPath, bytes);
            }

            resolvedPath = extractedPath;
            return true;
        }

        static IEnumerable<string> EnumerateSoundCandidates(string relativePath)
        {
            string normalized = NormalizeArchivePath(relativePath);
            yield return normalized;

            string mp3 = NormalizeArchivePath(SoundPathResolver.ChangeExtension(normalized, ".mp3"));
            if (!string.Equals(normalized, mp3, StringComparison.OrdinalIgnoreCase))
                yield return mp3;
        }

        void OpenArchive(string bsaPath)
        {
            try
            {
                var archive = BsaArchive.Open(bsaPath);
                _bsaArchives.Add(archive);
                for (int i = 0; i < archive.Entries.Length; i++)
                    _bsaEntriesByPath[NormalizeArchivePath(archive.Entries[i].Name)] = (archive, archive.Entries[i]);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[VVardenfell][Audio] failed opening sound BSA '{bsaPath}': {ex.Message}", ex);
            }
        }

        static string[] ResolveArchivePaths(string installPath)
        {
            string dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(iniPath))
            {
                bool inArchivesSection = false;
                foreach (string rawLine in File.ReadLines(iniPath))
                {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";", StringComparison.Ordinal))
                        continue;

                    if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        inArchivesSection = string.Equals(section, "Archives", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!inArchivesSection)
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string value = line.Substring(equalsIndex + 1).Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(value) || !string.Equals(Path.GetExtension(value), ".bsa", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fullPath = Path.Combine(dataFilesPath, value);
                    if (File.Exists(fullPath) && seen.Add(fullPath))
                        paths.Add(fullPath);
                }
            }

            string fallback = Path.Combine(dataFilesPath, "Morrowind.bsa");
            if (File.Exists(fallback) && seen.Add(fallback))
                paths.Add(fallback);

            return paths.ToArray();
        }

        static string NormalizeArchivePath(string path)
            => (path ?? string.Empty).Trim().Trim('"').Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
    }
}
