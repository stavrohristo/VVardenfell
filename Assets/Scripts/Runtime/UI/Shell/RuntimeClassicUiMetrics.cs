using UnityEngine;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
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
