using System;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    internal static class RuntimeWindowGeometryUtility
    {
        public static void ApplyRectRequest(
            ref RuntimeWindowRect rect,
            ref RuntimeWindowRectRequest request)
        {
            if (request.Pending == 0)
                return;

            rect.X = Clamp01(request.Rect.X);
            rect.Y = Clamp01(request.Rect.Y);
            rect.Width = ClampDimension(request.Rect.Width, rect.Width);
            rect.Height = ClampDimension(request.Rect.Height, rect.Height);

            if (rect.X + rect.Width > 1f)
                rect.X = Math.Max(0f, 1f - rect.Width);
            if (rect.Y + rect.Height > 1f)
                rect.Y = Math.Max(0f, 1f - rect.Height);

            request.Pending = 0;
        }

        public static Rect ToUnityRect(in RuntimeWindowRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static void SetRectRequest(ref RuntimeWindowRectRequest request, Rect normalizedRect)
        {
            request.Pending = 1;
            request.Rect = new RuntimeWindowRect
            {
                X = normalizedRect.x,
                Y = normalizedRect.y,
                Width = normalizedRect.width,
                Height = normalizedRect.height,
            };
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            return Math.Clamp(value, 0f, 1f);
        }

        static float ClampDimension(float requested, float fallback)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0f)
                requested = fallback > 0f ? fallback : 0.1f;

            return Math.Clamp(requested, 0.1f, 1f);
        }
    }
}
