using UnityEngine;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Canonical font pixel heights for every text role in the classic MW UI,
    /// modeled directly on OpenMW.
    ///
    /// In OpenMW, a single <c>font size = 16</c> entry in
    /// <c>settings-default.cfg</c> controls every <c>SandText</c> /
    /// <c>NormalText</c> / <c>SandBrightText</c> / <c>CountText</c> /
    /// <c>SandTextButton</c> widget (see <c>openmw_text.skin.xml</c>). There is
    /// no per-widget font-size hierarchy — differences are expressed through
    /// color and alignment, not size. Our classic UI mirrors that choice so
    /// the UI Scale slider rescales every pixel uniformly instead of drifting
    /// relative proportions between, say, a row label and a caption.
    ///
    /// The few exceptions (bar overlay, count badges, subtle footer text) are
    /// the same concessions OpenMW's widget rects make when they crop tight
    /// onto something smaller than a standard 18-tall row.
    /// </summary>
    static class RuntimeClassicUiFontSizes
    {
        /// <summary>Default body text. Matches OpenMW <c>font size = 16</c>.</summary>
        public const float Body = 16f;

        /// <summary>Caption / title text. OpenMW uses the same font as body
        /// inside a 20-tall <c>MW_Caption</c> band — no size bump.</summary>
        public const float Caption = 16f;

        /// <summary>Section headers inside windows (Stats "Major Skills" etc.).
        /// OpenMW uses <c>SandBrightText</c> which is the same size as body,
        /// just tinted gold.</summary>
        public const float Header = 16f;

        /// <summary>Text drawn inside an 18-tall progress bar
        /// (<c>MW_Progress_Red/Blue/Green</c>). A hair smaller than body so
        /// glyphs don't touch the frame edges.</summary>
        public const float BarOverlay = 14f;

        /// <summary>Count / quantity overlay on item tiles
        /// (<c>CountText</c> rendered at the corner of a 42×42 item icon).
        /// OpenMW uses the same 16 as body, but we keep it tighter so it
        /// doesn't overlap the tile artwork.</summary>
        public const float Count = 14f;

        /// <summary>Footer / metadata text: save slot timestamp, version
        /// number, debug readouts. Smaller than body by design.</summary>
        public const float Small = 14f;

        /// <summary>Subtle hint / placeholder text. The smallest legal size;
        /// used sparingly for e.g. Options "Not yet implemented" panels or
        /// faint secondary labels.</summary>
        public const float Subtle = 12f;
    }

    static class RuntimeClassicUiMetrics
    {
        public static float Ui(float value) => RuntimeUiScaleSettings.ScalePixels(value);

        public static Vector2 Ui(Vector2 value) => RuntimeUiScaleSettings.ScalePixels(value);

        public static float Layout(float value) => Ui(value);

        public static Vector2 Layout(Vector2 value) => Ui(value);

        // HUD has its own scale knob (RuntimeUiScaleSettings.HudScale) independent
        // of the menu/window GlobalScale so players can tune HUD and menu sizes
        // separately. Defaults to 2× for backward-compat with the previously
        // hardcoded multiplier.
        public static float HudLayout(float value) => RuntimeUiScaleSettings.ScaleHudPixels(value);

        public static Vector2 HudLayout(Vector2 value) => RuntimeUiScaleSettings.ScaleHudPixels(value);

        public static float WindowText(float baseScale) => RuntimeUiFactory.MenuTextScale(baseScale);

        public static float HudText(float baseScale) => RuntimeUiFactory.MenuTextScale(baseScale);

        public static float OverlayText(float baseScale) => RuntimeUiFactory.PresentationTextScale(baseScale);
    }
}
