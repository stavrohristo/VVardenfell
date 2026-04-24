using UnityEngine;

namespace VVardenfell.Runtime.UI.Framework
{
    public static partial class RuntimeUiFactory
    {
        public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;
        public const float MatchWidthOrHeight = 0.5f;
        public const float PresentationTextScaleMultiplier = 1.5f;
        public const float LoadingVisualScaleMultiplier = 2f;
        public const float MenuVisualScaleMultiplier = 2f;

        public static float PresentationTextScale(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier;
        }

        public static float LoadingTextScale(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier * LoadingVisualScaleMultiplier;
        }

        public static float MenuTextScale(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier * MenuVisualScaleMultiplier;
        }

        public static float LoadingLayout(float value)
        {
            return RuntimeUiScaleSettings.ScalePixels(value * LoadingVisualScaleMultiplier);
        }

        public static float MenuLayout(float value)
        {
            return RuntimeUiScaleSettings.ScalePixels(value * MenuVisualScaleMultiplier);
        }
    }
}