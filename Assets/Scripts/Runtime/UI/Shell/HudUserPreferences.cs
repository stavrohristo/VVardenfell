namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Static HUD-facing preferences the Options window writes and the HUD model
    /// builder reads. Kept separate from <c>VVardenfell.Core.Config.MorrowindConfig</c>
    /// so the ECS model builder doesn't need to know about disk-backed config — it
    /// just AND-gates its computed visibility with these booleans.
    ///
    /// Defaults match <c>MorrowindConfig</c> defaults so a fresh install (no
    /// config on disk yet) shows the crosshair + subtitles.
    /// </summary>
    public static class HudUserPreferences
    {
        public static bool ShowCrosshair = true;
        public static bool ShowSubtitles = true;
    }
}
