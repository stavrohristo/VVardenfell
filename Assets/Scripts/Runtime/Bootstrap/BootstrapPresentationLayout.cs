using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView
    {
        void RefreshScreenDependentLayout()
        {
            if (_rootRect == null)
                return;

            Vector2 size = _rootRect.rect.size;
            if ((size - _lastCanvasSize).sqrMagnitude <= 0.01f)
                return;

            _lastCanvasSize = size;

            if (_activeBackgroundImage?.Texture != null)
                ApplyTextureLayout(_backgroundImage.rectTransform, _activeBackgroundImage.Texture.width, _activeBackgroundImage.Texture.height, StretchMenuBackground || _phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo);

            UpdateVideoLayout();
        }

        void UpdateVideoLayout()
        {
            if (_videoImage == null || !_videoImage.enabled || _activeMovie == null)
                return;

            bool stretch = _phase != PresentationPhase.Menu || StretchMenuBackground;
            ApplyTextureLayout(_videoImage.rectTransform, Mathf.Max(1, _activeMovie.Width), Mathf.Max(1, _activeMovie.Height), stretch);
        }

        void ApplyTextureLayout(RectTransform rect, int textureWidth, int textureHeight, bool stretch)
        {
            if (stretch || textureWidth <= 0 || textureHeight <= 0 || _rootRect == null)
            {
                RuntimeUiFactory.Stretch(rect);
                return;
            }

            float rootWidth = Mathf.Max(1f, _rootRect.rect.width);
            float rootHeight = Mathf.Max(1f, _rootRect.rect.height);
            float textureAspect = textureWidth / (float)textureHeight;
            float viewAspect = rootWidth / rootHeight;

            float width;
            float height;
            if (textureAspect > viewAspect)
            {
                width = rootWidth;
                height = width / textureAspect;
            }
            else
            {
                height = rootHeight;
                width = height * textureAspect;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        bool ShouldStretchBackgroundForCurrentPhase()
        {
            return _phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo || StretchMenuBackground;
        }

        float ScaleText(float baseScale)
        {
            return RuntimeUiFactory.PresentationTextScale(baseScale);
        }

        float ScaleLoadingText(float baseScale)
        {
            return RuntimeUiFactory.LoadingTextScale(baseScale);
        }

        float ScaleMenuText(float baseScale)
        {
            return RuntimeUiFactory.MenuTextScale(baseScale);
        }

        float ScaleLoadingLayout(float value)
        {
            return RuntimeUiFactory.LoadingLayout(value);
        }

        float ScaleMenuLayout(float value)
        {
            return RuntimeUiFactory.MenuLayout(value);
        }

    }
}
