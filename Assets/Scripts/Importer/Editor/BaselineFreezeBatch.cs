using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;

namespace VVardenfell.Importer.Editor
{
    public static class BaselineFreezeBatch
    {
        const string InstallPathArg = "-vv.installPath=";
        const string InstallPathSafeArg = "-vvInstallPath=";
        const string ColdGameplayArg = "-vv.coldGameplay";
        const string ColdGameplaySafeArg = "-vvColdGameplay";

        public static void RunGameplayContentBake()
        {
            try
            {
                CachePaths.Warmup();
                var config = ResolveConfig();
                if (!config.IsValid(out string configError))
                    throw new InvalidOperationException(configError);

                if (HasArg(ColdGameplayArg) || HasArg(ColdGameplaySafeArg) || HasArg(".coldGameplay"))
                    DeleteGameplayContentArtifacts();

                var progress = new BakeProgress();
                IEnumerator bake = BakeCoordinator.BakeGameplayOnly(config, progress);
                while (bake.MoveNext())
                {
                }

                if (!string.IsNullOrEmpty(progress.Error))
                    throw new InvalidOperationException(progress.Error);

                ValidateGameplayContentArtifacts();
                Debug.Log("[VVardenfell][BaselineFreeze] gameplay content bake and validation passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VVardenfell][BaselineFreeze] failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        static MorrowindConfig ResolveConfig()
        {
            string installPath = GetArgValue(InstallPathArg);
            if (string.IsNullOrWhiteSpace(installPath))
                installPath = GetArgValue(InstallPathSafeArg);
            if (string.IsNullOrWhiteSpace(installPath))
                installPath = ReadSavedInstallPath();

            return new MorrowindConfig
            {
                InstallPath = installPath,
            };
        }

        static string ReadSavedInstallPath()
        {
            string path = Path.Combine(Application.persistentDataPath, "config.json");
            if (!File.Exists(path))
                return null;

            return JsonUtility.FromJson<SavedConfig>(File.ReadAllText(path))?.InstallPath;
        }

        static bool HasArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static string GetArgValue(string prefix)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length).Trim('"');
            }

            return null;
        }

        static void DeleteGameplayContentArtifacts()
        {
            DeleteFileIfExists(CachePaths.GameplayContent);
            DeleteFileIfExists(CachePaths.GameplayContentManifest);
            DeleteFileIfExists(CachePaths.GameplayValidationReport);
        }

        static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static void ValidateGameplayContentArtifacts()
        {
            if (!File.Exists(CachePaths.GameplayContent))
                throw new FileNotFoundException("Missing gameplay content binary.", CachePaths.GameplayContent);
            if (!GameplayContentManifest.TryRead(CachePaths.GameplayContentManifest, out var manifest))
                throw new InvalidDataException("Gameplay content manifest is missing, stale, or unreadable.");

            GameplayContentData data = GameplayContentFile.Read(CachePaths.GameplayContent);
            if (manifest.GameplayContentVersion != CacheFormat.GameplayContentVersion)
                throw new InvalidDataException($"Gameplay content version mismatch: manifest={manifest.GameplayContentVersion}, runtime={CacheFormat.GameplayContentVersion}.");
            if (data.CreatureLeveledLists.Length > 0 && data.CreatureLeveledListEntries.Length == 0)
                throw new InvalidDataException("Creature leveled lists were imported without any entries.");

            string report = File.Exists(CachePaths.GameplayValidationReport)
                ? File.ReadAllText(CachePaths.GameplayValidationReport)
                : string.Empty;

            string[] requiredReportTokens =
            {
                "TES3=world-owned",
                "CELL=world-owned",
                "LAND=world-owned",
                "GMST=",
                "GLOB=",
                "CLAS=",
                "FACT=",
                "RACE=",
                "BSGN=",
                "SKIL=",
                "MGEF=",
                "SCPT=",
                "SSCR=",
                "REGN=",
                "SOUN=",
                "SNDG=",
                "LTEX=",
                "STAT=",
                "ACTI=",
                "DOOR=",
                "CONT=",
                "LIGH=",
                "LOCK=",
                "PROB=",
                "REPA=",
                "MISC=",
                "WEAP=",
                "ARMO=",
                "CLOT=",
                "BOOK=",
                "ALCH=",
                "APPA=",
                "INGR=",
                "BODY=",
                "NPC_=",
                "CREA=",
                "LEVI=",
                "LEVC=",
                "SPEL=",
                "ENCH=",
                "DIAL=",
                "INFO=",
                "PGRD=",
            };

            for (int i = 0; i < requiredReportTokens.Length; i++)
            {
                if (!report.Contains(requiredReportTokens[i], StringComparison.Ordinal))
                    throw new InvalidDataException($"Gameplay validation report is missing '{requiredReportTokens[i]}'.");
            }
        }

        [Serializable]
        sealed class SavedConfig
        {
            public string InstallPath;
        }
    }
}
