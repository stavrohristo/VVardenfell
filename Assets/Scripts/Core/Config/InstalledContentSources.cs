using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VVardenfell.Core.Config
{
    /// <summary>
    /// Resolves gameplay-content source files from the installed Morrowind data set.
    /// The current primary source is <c>Morrowind.ini</c>'s <c>[Game Files]</c> list,
    /// falling back to <c>Morrowind.esm</c> when no explicit load order is available.
    /// </summary>
    public static class InstalledContentSources
    {
        const string BaseGameMaster = "Morrowind.esm";
        const string BaseGameArchive = "Morrowind.bsa";

        static readonly string[] VanillaMasters =
        {
            BaseGameMaster,
            "Tribunal.esm",
            "Bloodmoon.esm",
        };

        static readonly string[] ProjectMainCandidates =
        {
            "TR_Mainland.esm",
            "Sky_Main.esm",
            "Cyr_Main.esm",
            "Cyrodiil_Main.esm",
            "PC_Main.esm",
        };

        static readonly string[][] ProjectMainCandidateGroups =
        {
            new[] { "TR_Mainland.esm" },
            new[] { "Sky_Main.esm" },
            new[] { "PC_Main.esm", "Cyr_Main.esm", "Cyrodiil_Main.esm" },
        };

        static readonly string[] OptionalProjectPlugins =
        {
            "TR_Factions.esp",
        };

        static readonly string[] TamrielDataArchives =
        {
            "TR_Data.bsa",
            "PC_Data.bsa",
            "Sky_Data.bsa",
        };

        public static MorrowindContentProfile CreateVanillaProfile(string installPath)
        {
            string dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            string masterPath = Path.Combine(dataFilesPath, BaseGameMaster);
            string archivePath = Path.Combine(dataFilesPath, BaseGameArchive);

            var profile = new MorrowindContentProfile
            {
                ProfileId = "vanilla",
                DisplayName = "Vanilla",
                InstallPath = installPath ?? string.Empty,
                DataRoots = Directory.Exists(dataFilesPath) ? new[] { dataFilesPath } : Array.Empty<string>(),
                ContentFiles = File.Exists(masterPath) ? new[] { masterPath } : Array.Empty<string>(),
                Archives = File.Exists(archivePath) ? new[] { archivePath } : Array.Empty<string>(),
            };
            profile.RefreshCacheKey();
            return profile;
        }

        public static bool TryCreateProjectTamrielProfile(string installPath, out MorrowindContentProfile profile, out string error)
            => TryCreateProjectTamrielProfile(installPath, installPath, out profile, out error);

        public static bool TryCreateProjectTamrielProfile(string baseInstallPath, string projectTamrielPath, out MorrowindContentProfile profile, out string error)
        {
            profile = null;
            error = null;

            if (!TryResolveDataFilesRoot(projectTamrielPath, out string installPath, out string dataFilesPath, out error))
                return false;

            var content = new List<string>();
            var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryAddVanillaMasters(baseInstallPath, dataFilesPath, content, seenContent, out error))
                return false;

            string[] iniSources = ResolveGameplayRecordSources(installPath);
            if (ContainsProjectTamrielContent(iniSources))
            {
                for (int i = 0; i < iniSources.Length; i++)
                {
                    if (!IsVanillaMasterName(Path.GetFileName(iniSources[i])))
                        AddContentFile(content, seenContent, iniSources[i]);
                }

                profile = new MorrowindContentProfile
                {
                    ProfileId = "project-tamriel",
                    DisplayName = ContainsProjectTamrielContent(iniSources) ? "Project Tamriel" : "Tamriel Data",
                    InstallPath = installPath ?? string.Empty,
                    DataRoots = BuildProjectDataRoots(baseInstallPath, dataFilesPath),
                    ContentFiles = content.ToArray(),
                    Archives = ResolveProjectTamrielArchiveSources(baseInstallPath, installPath),
                };
                profile.RefreshCacheKey();
                return true;
            }

            string tamrielData = Path.Combine(dataFilesPath, "Tamriel_Data.esm");
            if (!File.Exists(tamrielData))
            {
                error = "Project Tamriel profile requires 'Tamriel_Data.esm' in Data Files.";
                return false;
            }
            AddContentFile(content, seenContent, tamrielData);

            var foundMains = ResolveProjectMainFiles(dataFilesPath, out error);
            if (error != null)
                return false;

            if (foundMains.Count == 0)
            {
                profile = new MorrowindContentProfile
                {
                    ProfileId = "project-tamriel",
                    DisplayName = "Tamriel Data",
                    InstallPath = installPath ?? string.Empty,
                    DataRoots = BuildProjectDataRoots(baseInstallPath, dataFilesPath),
                    ContentFiles = content.ToArray(),
                    Archives = ResolveProjectTamrielArchiveSources(baseInstallPath, installPath),
                };
                profile.RefreshCacheKey();
                return true;
            }

            for (int i = 0; i < foundMains.Count; i++)
                AddContentFile(content, seenContent, foundMains[i]);
            foreach (string plugin in OptionalProjectPlugins)
            {
                string path = Path.Combine(dataFilesPath, plugin);
                if (File.Exists(path))
                    AddContentFile(content, seenContent, path);
            }

            profile = new MorrowindContentProfile
            {
                ProfileId = "project-tamriel",
                DisplayName = "Project Tamriel",
                InstallPath = installPath ?? string.Empty,
                DataRoots = BuildProjectDataRoots(baseInstallPath, dataFilesPath),
                ContentFiles = content.ToArray(),
                Archives = ResolveProjectTamrielArchiveSources(baseInstallPath, installPath),
            };
            profile.RefreshCacheKey();
            return true;
        }

        public static string[] ResolveGameplayRecordSources(MorrowindContentProfile profile)
            => profile?.ContentFiles ?? Array.Empty<string>();

        public static string[] ResolveGameplayDependencySources(MorrowindContentProfile profile)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string source in profile?.ContentFiles ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(source) && seen.Add(source))
                    results.Add(source);

            foreach (string archive in profile?.Archives ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(archive) && seen.Add(archive))
                    results.Add(archive);

            foreach (string root in profile?.DataRoots ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(root) && seen.Add(root))
                    results.Add(root);

            return results.ToArray();
        }

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
                string fallback = Path.Combine(dataFilesPath, BaseGameMaster);
                if (File.Exists(fallback))
                    results.Add(fallback);
            }

            return results.ToArray();
        }

        public static string[] ResolveGameplayDependencySources(string installPath)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string source in ResolveGameplayRecordSources(installPath))
            {
                if (seen.Add(source))
                    results.Add(source);
            }

            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");
            if (File.Exists(iniPath) && seen.Add(iniPath))
                results.Add(iniPath);

            return results.ToArray();
        }

        public static string[] ResolveArchiveSources(string installPath)
        {
            string dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddArchive(string archiveName)
            {
                if (string.IsNullOrWhiteSpace(archiveName) || !string.Equals(Path.GetExtension(archiveName), ".bsa", StringComparison.OrdinalIgnoreCase))
                    return;

                string fullPath = Path.IsPathRooted(archiveName)
                    ? archiveName
                    : Path.Combine(dataFilesPath, archiveName);
                if (File.Exists(fullPath) && seen.Add(fullPath))
                    paths.Add(fullPath);
            }

            AddArchive(BaseGameArchive);
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

                    AddArchive(line.Substring(equalsIndex + 1).Trim().Trim('"'));
                }
            }

            return paths.ToArray();
        }

        static string[] ResolveProjectTamrielArchiveSources(string baseInstallPath, string projectTamrielInstallPath)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRange(string[] archives)
            {
                for (int i = 0; i < (archives?.Length ?? 0); i++)
                    if (!string.IsNullOrWhiteSpace(archives[i]) && seen.Add(archives[i]))
                        paths.Add(archives[i]);
            }

            AddRange(ResolveArchiveSources(baseInstallPath));
            AddRange(ResolveArchiveSources(projectTamrielInstallPath));
            string projectDataFiles = Path.Combine(projectTamrielInstallPath ?? string.Empty, "Data Files");
            for (int i = 0; i < TamrielDataArchives.Length; i++)
            {
                string archive = Path.Combine(projectDataFiles, TamrielDataArchives[i]);
                if (File.Exists(archive) && seen.Add(archive))
                    paths.Add(archive);
            }
            return paths.ToArray();
        }

        static bool TryAddVanillaMasters(
            string baseInstallPath,
            string projectDataFilesPath,
            List<string> content,
            HashSet<string> seen,
            out string error)
        {
            error = null;
            string baseDataFilesPath = Path.Combine(baseInstallPath ?? string.Empty, "Data Files");
            foreach (string master in VanillaMasters)
            {
                string projectPath = Path.Combine(projectDataFilesPath ?? string.Empty, master);
                string basePath = Path.Combine(baseDataFilesPath, master);
                string path = File.Exists(projectPath) ? projectPath : basePath;
                if (!File.Exists(path))
                {
                    error = $"Project Tamriel profile requires '{master}' from the configured Morrowind install or selected Data Files overlay.";
                    return false;
                }
                AddContentFile(content, seen, path);
            }

            return true;
        }

        static string[] BuildProjectDataRoots(string baseInstallPath, string projectDataFilesPath)
        {
            var roots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string baseDataFilesPath = Path.Combine(baseInstallPath ?? string.Empty, "Data Files");
            if (Directory.Exists(baseDataFilesPath) && seen.Add(baseDataFilesPath))
                roots.Add(baseDataFilesPath);

            if (Directory.Exists(projectDataFilesPath) && seen.Add(projectDataFilesPath))
                roots.Add(projectDataFilesPath);

            return roots.ToArray();
        }

        static List<string> ResolveProjectMainFiles(string dataFilesPath, out string error)
        {
            error = null;
            var found = new List<string>();
            for (int groupIndex = 0; groupIndex < ProjectMainCandidateGroups.Length; groupIndex++)
            {
                var group = ProjectMainCandidateGroups[groupIndex];
                var groupMatches = new List<string>();
                for (int i = 0; i < group.Length; i++)
                {
                    string path = Path.Combine(dataFilesPath, group[i]);
                    if (File.Exists(path))
                        groupMatches.Add(path);
                }

                if (groupMatches.Count > 1)
                {
                    error = "Multiple aliases for the same Project Tamriel main file were found; remove duplicates or set an explicit Morrowind.ini load order: "
                        + string.Join(", ", groupMatches.Select(Path.GetFileName));
                    return found;
                }

                if (groupMatches.Count == 1)
                    found.Add(groupMatches[0]);
            }

            return found;
        }

        static void AddContentFile(List<string> content, HashSet<string> seen, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && seen.Add(path))
                content.Add(path);
        }

        static bool IsVanillaMasterName(string fileName)
        {
            for (int i = 0; i < VanillaMasters.Length; i++)
                if (string.Equals(fileName, VanillaMasters[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static bool TryResolveDataFilesRoot(string selectedPath, out string installPath, out string dataFilesPath, out string error)
        {
            installPath = null;
            dataFilesPath = null;
            error = null;

            string trimmed = selectedPath?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                error = "Project Tamriel path is empty.";
                return false;
            }

            if (!Directory.Exists(trimmed))
            {
                error = $"Directory does not exist: {trimmed}";
                return false;
            }

            string folderName = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(folderName, "Data Files", StringComparison.OrdinalIgnoreCase))
            {
                dataFilesPath = trimmed;
                installPath = Directory.GetParent(trimmed)?.FullName;
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    error = $"Could not resolve install folder from Data Files path: {trimmed}";
                    return false;
                }
                return true;
            }

            string candidateDataFiles = Path.Combine(trimmed, "Data Files");
            if (!Directory.Exists(candidateDataFiles))
            {
                error = $"No 'Data Files' folder under: {trimmed}";
                return false;
            }

            installPath = trimmed;
            dataFilesPath = candidateDataFiles;
            return true;
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

        static bool ContainsProjectTamrielContent(string[] sources)
        {
            bool hasTamrielData = false;
            bool hasProjectMain = false;
            for (int i = 0; i < (sources?.Length ?? 0); i++)
            {
                string name = Path.GetFileName(sources[i]);
                if (string.Equals(name, "Tamriel_Data.esm", StringComparison.OrdinalIgnoreCase))
                    hasTamrielData = true;
                for (int candidateIndex = 0; candidateIndex < ProjectMainCandidates.Length; candidateIndex++)
                    if (string.Equals(name, ProjectMainCandidates[candidateIndex], StringComparison.OrdinalIgnoreCase))
                        hasProjectMain = true;
            }

            return hasTamrielData && hasProjectMain;
        }

        static bool ContainsTamrielDataContent(string[] sources)
        {
            for (int i = 0; i < (sources?.Length ?? 0); i++)
                if (string.Equals(Path.GetFileName(sources[i]), "Tamriel_Data.esm", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
