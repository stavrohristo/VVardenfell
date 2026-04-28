using System;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    static class RuntimeWindowSurfaceUtility
    {
        public static void ApplyNormalizedRect(RectTransform target, RectTransform viewport, Rect normalizedRect)
        {
            if (target == null || viewport == null)
                return;

            float viewportWidth = Mathf.Max(1f, viewport.rect.width);
            float viewportHeight = Mathf.Max(1f, viewport.rect.height);
            target.anchoredPosition = new Vector2(normalizedRect.x * viewportWidth, -normalizedRect.y * viewportHeight);
            target.sizeDelta = new Vector2(normalizedRect.width * viewportWidth, normalizedRect.height * viewportHeight);
        }

        public static Rect CaptureNormalizedRect(RectTransform target, RectTransform viewport)
        {
            if (target == null || viewport == null)
                return new Rect(0f, 0f, 0.1f, 0.1f);

            float viewportWidth = Mathf.Max(1f, viewport.rect.width);
            float viewportHeight = Mathf.Max(1f, viewport.rect.height);
            return new Rect(
                Mathf.Clamp01(target.anchoredPosition.x / viewportWidth),
                Mathf.Clamp01(-target.anchoredPosition.y / viewportHeight),
                Mathf.Clamp(target.rect.width / viewportWidth, 0.1f, 1f),
                Mathf.Clamp(target.rect.height / viewportHeight, 0.1f, 1f));
        }

        public static RuntimeWindowResizeHandle AttachResizeHandle(
            MorrowindWindowView window,
            RectTransform viewport,
            Vector2 minimumSize,
            Action onResized)
        {
            var grip = RuntimeUiFactory.CreateImage("ResizeGrip", window.Root, new Color(1f, 1f, 1f, 0f));
            grip.raycastTarget = true;
            grip.rectTransform.anchorMin = new Vector2(1f, 0f);
            grip.rectTransform.anchorMax = new Vector2(1f, 0f);
            grip.rectTransform.pivot = new Vector2(1f, 0f);
            grip.rectTransform.anchoredPosition = Vector2.zero;
            grip.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Layout(new Vector2(18f, 18f));

            var handle = grip.gameObject.AddComponent<RuntimeWindowResizeHandle>();
            handle.Initialize(window.Root, viewport, minimumSize, onResized);
            return handle;
        }
    }

    static class RuntimeScreenResolutionUtility
    {
        public static void SetResolution(int width, int height, FullScreenMode mode, int refreshRate)
        {
            if (width <= 0 || height <= 0)
                return;

            Screen.SetResolution(width, height, mode, ToRefreshRate(refreshRate));
        }

        public static int ToWholeHertz(Resolution resolution)
        {
            return Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
        }

        static RefreshRate ToRefreshRate(int refreshRate)
        {
            if (refreshRate > 0)
            {
                return new RefreshRate
                {
                    numerator = (uint)refreshRate,
                    denominator = 1u,
                };
            }

            return Screen.currentResolution.refreshRateRatio;
        }
    }
}
