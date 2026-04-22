using System;
using System.Collections.Generic;
using System.IO;

namespace VVardenfell.Core.Config
{
    /// <summary>
    /// Resolves gameplay-content source files from the installed Morrowind data set.
    /// The current primary source is <c>Morrowind.ini</c>'s <c>[Game Files]</c> list,
    /// falling back to <c>Morrowind.esm</c> when no explicit load order is available.
    /// </summary>
    public static class InstalledContentSources
    {
        public static string[] ResolveGameplayRecordSources(string installPath)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");

            if (File.Exists(iniPath))
            {
                bool inGameFilesSection = false;
                foreach (string rawLine in File.ReadLines(iniPath))
                {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                        continue;

                    if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        inGameFilesSection = string.Equals(section, "Game Files", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!inGameFilesSection)
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = line.Substring(0, equalsIndex).Trim();
                    if (!key.StartsWith("GameFile", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line.Substring(equalsIndex + 1).Trim();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    string extension = Path.GetExtension(value);
                    if (!string.Equals(extension, ".esm", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(extension, ".esp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string fullPath = Path.Combine(dataFilesPath, value);
                    if (File.Exists(fullPath) && seen.Add(fullPath))
                        results.Add(fullPath);
                }
            }

            if (results.Count == 0)
            {
                string fallback = Path.Combine(dataFilesPath, "Morrowind.esm");
                if (File.Exists(fallback))
                    results.Add(fallback);
            }

            return results.ToArray();
        }

        public static string[] ResolveMusicTracks(string installPath)
        {
            string musicRoot = Path.Combine(installPath ?? string.Empty, "Data Files", "Music");
            if (!Directory.Exists(musicRoot))
                return Array.Empty<string>();

            var tracks = new List<string>();
            foreach (string categoryDir in new[] { "Battle", "Explore", "Special" })
            {
                string absoluteDir = Path.Combine(musicRoot, categoryDir);
                if (!Directory.Exists(absoluteDir))
                    continue;

                string[] files = Directory.GetFiles(absoluteDir, "*.*", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string extension = Path.GetExtension(files[i]);
                    if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        tracks.Add(files[i]);
                    }
                }
            }

            return tracks.ToArray();
        }
    }
}
