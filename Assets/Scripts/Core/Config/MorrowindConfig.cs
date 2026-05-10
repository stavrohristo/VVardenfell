using System;
using System.IO;

namespace VVardenfell.Core.Config
{
    /// <summary>
    /// Persistent per-player settings, serialized to <c>config.json</c> in
    /// <c>Application.persistentDataPath</c> via <see cref="ConfigStorage"/>. Only
    /// <see cref="InstallPath"/> gates launch; every other field is player-facing
    /// (surfaced by the Options window) and has a safe default.
    ///
    /// Back-compat: Unity's <c>JsonUtility</c> fills missing fields with the
    /// declared defaults, so older <c>config.json</c> files from before these
    /// settings existed still load cleanly.
    /// </summary>
    [Serializable]
    public class MorrowindConfig
    {
        public string InstallPath;
        public MorrowindContentProfile ProjectTamrielProfile;

        // --- Bake ---------------------------------------------------------------

        public bool BakeCombinedCellRenderChunks = false;

        // --- Video --------------------------------------------------------------

        /// <summary>UI scale applied to every menu / window chassis.</summary>
        public float UiScale = 1f;

        /// <summary>HUD scale applied independently of UI scale. Default 2 matches
        /// the previously hardcoded HUD multiplier.</summary>
        public float HudScale = 2f;

        /// <summary>Target resolution. 0 means "use current / platform default".</summary>
        public int ResolutionWidth = 0;
        public int ResolutionHeight = 0;
        public int RefreshRate = 0;

        /// <summary>0=Windowed, 1=Fullscreen, 2=Borderless. Maps to
        /// <c>UnityEngine.FullScreenMode</c>.</summary>
        public int WindowMode = 0;

        /// <summary>0=off, 1=on, 2=half. Maps to <c>QualitySettings.vSyncCount</c>.</summary>
        public int VSync = 1;

        /// <summary>Field of view, in degrees, applied to the main camera.</summary>
        public float Fov = 75f;

        /// <summary>Scales resolved fog start/end distances. 1.0 is the previous runtime distance.</summary>
        public float FogDistanceScale = 0.6f;

        /// <summary>Gamma. Applied via <c>Screen.brightness</c> where supported.</summary>
        public float Gamma = 1f;

        // --- Audio (all in [0, 1]) ---------------------------------------------

        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;

        /// <summary>Covers ambient + interaction SFX channels.</summary>
        public float EffectsVolume = 1f;

        /// <summary>Persisted but not yet wired — no footsteps channel exists yet.</summary>
        public float FootstepsVolume = 1f;

        /// <summary>Persisted but not yet wired — no voice channel exists yet.</summary>
        public float VoiceVolume = 1f;

        // --- Preferences --------------------------------------------------------

        public bool ShowCrosshair = true;
        public bool ShowSubtitles = true;

        /// <summary>Window background alpha. [0.3, 1.0].</summary>
        public float MenuTransparency = 0.88f;

        /// <summary>Difficulty slider (persisted, no runtime effect yet).</summary>
        public int Difficulty = 0;

        // -----------------------------------------------------------------------

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(InstallPath))
            {
                error = "Install path is empty.";
                return false;
            }
            if (!Directory.Exists(InstallPath))
            {
                error = $"Directory does not exist: {InstallPath}";
                return false;
            }
            var dataFiles = Path.Combine(InstallPath, "Data Files");
            if (!Directory.Exists(dataFiles))
            {
                error = $"No 'Data Files' folder under: {InstallPath}";
                return false;
            }
            var esm = Path.Combine(dataFiles, "Morrowind.esm");
            if (!File.Exists(esm))
            {
                error = $"Morrowind.esm not found at: {esm}";
                return false;
            }
            error = null;
            return true;
        }

        public MorrowindContentProfile CreateVanillaContentProfile()
        {
            var profile = InstalledContentSources.CreateVanillaProfile(InstallPath);
            profile.RefreshCacheKey();
            return profile;
        }

        /// <summary>
        /// Reset every player-facing field to its launch-ready default. Leaves
        /// <see cref="InstallPath"/> alone — that's a one-time setup not affected
        /// by the Options menu's "Reset to Defaults" button.
        /// </summary>
        public void ResetPlayerSettingsToDefaults()
        {
            UiScale = 1f;
            HudScale = 2f;
            ResolutionWidth = 0;
            ResolutionHeight = 0;
            RefreshRate = 0;
            WindowMode = 0;
            VSync = 1;
            Fov = 75f;
            FogDistanceScale = 0.6f;
            Gamma = 1f;

            MasterVolume = 1f;
            MusicVolume = 0.8f;
            EffectsVolume = 1f;
            FootstepsVolume = 1f;
            VoiceVolume = 1f;

            ShowCrosshair = true;
            ShowSubtitles = true;
            MenuTransparency = 0.88f;
            Difficulty = 0;
        }
    }
}
