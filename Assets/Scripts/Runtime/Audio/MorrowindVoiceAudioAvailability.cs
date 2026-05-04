using System;
using System.Collections.Generic;
using VVardenfell.Core.Config;

namespace VVardenfell.Runtime.Audio
{
    public static class MorrowindVoiceAudioAvailability
    {
        static readonly Dictionary<string, bool> s_AvailableByRawPath = new(StringComparer.OrdinalIgnoreCase);
        static readonly RuntimeSoundResourceResolver s_Resolver = new();
        static string s_InstallPath;

        public static bool IsVoiceAvailable(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return false;

            string installPath = ResolveInstallPath();
            string key = SoundPathResolver.Correct(rawPath);
            if (s_AvailableByRawPath.TryGetValue(key, out bool available))
                return available;

            available = s_Resolver.TryResolve(installPath, key, out _);
            s_AvailableByRawPath[key] = available;
            return available;
        }

        static string ResolveInstallPath()
        {
            if (!string.IsNullOrWhiteSpace(s_InstallPath))
                return s_InstallPath;

            if (!ConfigStorage.TryLoad(out var config) || config == null || string.IsNullOrWhiteSpace(config.InstallPath))
                throw new InvalidOperationException("[VVardenfell][Audio] Voice availability requires a configured Morrowind install path.");

            s_InstallPath = config.InstallPath;
            return s_InstallPath;
        }
    }
}
