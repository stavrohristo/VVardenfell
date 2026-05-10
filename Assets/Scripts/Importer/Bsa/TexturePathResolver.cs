using System.Collections.Generic;

namespace VVardenfell.Importer.Bsa
{
    /// <summary>
    /// Resolves a NIF's raw texture reference (often something like "Tx_foo_01.tga") to an
    /// actual BSA entry. Matches OpenMW's <c>correctTexturePath</c>:
    ///   1. lowercase + normalise slashes to backslashes + dedupe
    ///   2. prepend "textures\" if not already under textures/ or bookart/
    ///   3. try with extension changed to .dds (Bethesda swapped tga→dds in BSAs)
    ///   4. fall back to original extension
    ///   5. fall back to textures\{basename}
    /// </summary>
    public sealed class TexturePathResolver
    {
        private readonly Dictionary<string, BsaEntry> _entries;
        private static readonly string[] TopLevelDirs = { "textures", "bookart" };

        public TexturePathResolver(BsaArchive bsa)
        {
            _entries = new Dictionary<string, BsaEntry>(bsa.Entries.Length, System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in bsa.Entries) _entries[e.Name] = e;
        }

        public TexturePathResolver(IReadOnlyDictionary<string, BsaEntry> entries)
        {
            _entries = new Dictionary<string, BsaEntry>(entries?.Count ?? 0, System.StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return;

            foreach (var pair in entries)
                _entries[pair.Key] = pair.Value;
        }

        public bool TryResolve(string rawPath, out BsaEntry entry, out string resolvedName)
        {
            string corrected = Correct(rawPath);
            string ddsPath = ChangeExtension(corrected, ".dds");

            if (_entries.TryGetValue(ddsPath, out entry)) { resolvedName = ddsPath; return true; }
            if (_entries.TryGetValue(corrected, out entry)) { resolvedName = corrected; return true; }

            // Fallback to textures\{basename}
            string basename = Basename(corrected);
            string fallback = $"{TopLevelDirs[0]}\\{basename}";
            string fallbackDds = ChangeExtension(fallback, ".dds");
            if (_entries.TryGetValue(fallbackDds, out entry)) { resolvedName = fallbackDds; return true; }
            if (_entries.TryGetValue(fallback, out entry)) { resolvedName = fallback; return true; }

            resolvedName = null;
            entry = default;
            return false;
        }

        private static string Correct(string path)
        {
            string p = path.ToLowerInvariant().Replace('/', '\\');
            // collapse double backslashes
            while (p.Contains("\\\\")) p = p.Replace("\\\\", "\\");
            if (p.StartsWith("\\")) p = p.Substring(1);

            // Already under a known top-level?
            foreach (var dir in TopLevelDirs)
                if (p.StartsWith(dir + "\\"))
                    return p;

            // Try to find a known top-level mid-path and trim
            foreach (var dir in TopLevelDirs)
            {
                int idx = p.IndexOf("\\" + dir + "\\", System.StringComparison.Ordinal);
                if (idx >= 0) return p.Substring(idx + 1);
            }

            return TopLevelDirs[0] + "\\" + p;
        }

        private static string ChangeExtension(string path, string newExt)
        {
            int dot = path.LastIndexOf('.');
            if (dot < 0) return path + newExt;
            return path.Substring(0, dot) + newExt;
        }

        private static string Basename(string path)
        {
            int slash = path.LastIndexOf('\\');
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }
}
