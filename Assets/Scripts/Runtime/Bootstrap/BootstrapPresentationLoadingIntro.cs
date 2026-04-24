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
        void HandleIntroSkipInput()
        {
            if (_phase != PresentationPhase.IntroCompany && _phase != PresentationPhase.IntroLogo)
                return;

            bool escapePressed = Keyboard.current?.escapeKey.wasPressedThisFrame ?? false;
            bool mousePressed = Mouse.current?.leftButton.wasPressedThisFrame ?? false;
            if (!escapePressed && !mousePressed)
                return;

            AdvanceFromCurrentIntroPhase();
        }


        void BuildIntroFallback()
        {
            _introFallbackGroup = RuntimeUiFactory.CreateAnchorRect(
                "IntroFallback",
                _rootRect,
                new Vector2(0.08f, 0.56f),
                new Vector2(0.92f, 0.86f),
                Vector2.zero,
                Vector2.zero,
                Vector2.zero);
            _introFallbackTitle = RuntimeUiFactory.CreateBitmapText(
                "IntroFallbackTitle",
                _introFallbackGroup,
                _theme.DefaultFont,
                ScaleText(1.6f),
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_introFallbackTitle.rectTransform);

            _introFallbackSubtitle = RuntimeUiFactory.CreateBitmapText(
                "IntroFallbackSubtitle",
                _introFallbackGroup,
                _theme.DefaultFont,
                ScaleText(0.82f),
                new Color(0.92f, 0.87f, 0.74f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.SetInset(_introFallbackSubtitle.rectTransform, 0f, -RuntimeUiScaleSettings.ScalePixels(108f), 0f, 0f);
        }

        void BuildLoadingView()
        {
            _loadingRoot = RuntimeUiFactory.CreateStretchRect("LoadingRoot", _rootRect);
            _loadingRoot.gameObject.SetActive(false);

            _loadingDialogRect = RuntimeUiFactory.CreateAnchoredRect(
                "LoadingDialog",
                _loadingRoot,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleLoadingLayout(LoadingDialogBottomMargin)),
                new Vector2(ScaleLoadingLayout(LoadingDialogMinWidth), ScaleLoadingLayout(LoadingDialogHeight)));
            _loadingDialogFrame = RuntimeUiFactory.CreateBorderFrame(
                "LoadingDialogFrame",
                _loadingDialogRect,
                RuntimeUiFactory.ResolveThickFrame(_theme),
                new Color(0f, 0f, 0f, 0.92f));
            RuntimeUiFactory.Stretch(_loadingDialogFrame.Root);

            _loadingText = RuntimeUiFactory.CreateBitmapText(
                "LoadingText",
                _loadingDialogRect,
                _theme.DefaultFont,
                ScaleLoadingText(0.72f),
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Center);
            _loadingText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _loadingText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _loadingText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _loadingText.rectTransform.anchoredPosition = new Vector2(0f, -ScaleLoadingLayout(5f));
            _loadingText.rectTransform.sizeDelta = new Vector2(-ScaleLoadingLayout(32f), ScaleLoadingLayout(18f));

            _loadingBarRect = RuntimeUiFactory.CreateAnchoredRect(
                "LoadingBar",
                _loadingDialogRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleLoadingLayout(16f)),
                new Vector2(ScaleLoadingLayout(LoadingDialogMinWidth - 32f), ScaleLoadingLayout(LoadingBarHeight)));
            _loadingBarFrame = RuntimeUiFactory.CreateBorderFrame(
                "LoadingBarFrame",
                _loadingBarRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                Color.clear);
            RuntimeUiFactory.Stretch(_loadingBarFrame.Root);

            _loadingBarFillRect = RuntimeUiFactory.CreateAnchoredRect(
                "LoadingBarFillRect",
                _loadingBarRect,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _loadingBarFillRect.pivot = new Vector2(0f, 0.5f);
            _loadingBarFillRect.SetAsFirstSibling();

            _loadingBarFill = RuntimeUiFactory.CreateImage("LoadingBarFill", _loadingBarFillRect, new Color(0f, 0.815f, 0.82f, 1f));
            _loadingBarFill.raycastTarget = false;
            var fillSprite = _theme.LoadingBarFillSprite;
            _loadingBarFill.sprite = fillSprite;
            RuntimeUiFactory.Stretch(_loadingBarFill.rectTransform);
        }


        void UpdateLoadingVisuals()
        {
            if (_loadingText == null || _progress == null)
                return;

            string label = BuildLoadingLabel();
            _loadingText.Text = label;

            float width = Mathf.Max(
                ScaleLoadingLayout(LoadingDialogMinWidth),
                Mathf.Ceil(RuntimeUiFactory.MeasureLineWidth(_theme.DefaultFont, label, _loadingText.FontScale) + ScaleLoadingLayout(40f)));
            _loadingDialogRect.sizeDelta = new Vector2(width, ScaleLoadingLayout(LoadingDialogHeight));
            _loadingBarRect.sizeDelta = new Vector2(width - ScaleLoadingLayout(32f), ScaleLoadingLayout(LoadingBarHeight));
            UpdateLoadingBarFill(Mathf.Clamp01(_progress.Fraction));
        }


        void SignalLoadingPhaseReady()
        {
            if (_loadingPhaseSignaled)
                return;

            _loadingPhaseSignaled = true;
            _onLoadingPhaseReady?.Invoke();
        }


        void BeginTimedIntroFallbackIfNeeded()
        {
            if (_phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo)
                BeginTimedIntroFallback();
        }

        void BeginTimedIntroFallback()
        {
            _phaseWaitingForMovieCompletion = false;
            _activeMovieOwnsPhase = false;
            _phaseStartTime = Time.unscaledTime;
        }

        bool ShouldAdvanceIntroPhase(float fallbackSeconds)
        {
            if (_phaseWaitingForMovieCompletion)
                return false;

            return Time.unscaledTime - _phaseStartTime >= fallbackSeconds;
        }

        void AdvanceFromCurrentIntroPhase()
        {
            switch (_phase)
            {
                case PresentationPhase.IntroCompany:
                    SwitchPhase(PresentationPhase.Loading);
                    break;
                case PresentationPhase.IntroLogo:
                    SwitchPhase(PresentationPhase.Menu);
                    break;
            }
        }

        void ConfigureIntroFallback(string title, string subtitle, float titleScale, float subtitleScale, bool show)
        {
            _introFallbackTitle.Font = _theme.DefaultFont;
            _introFallbackTitle.FontScale = RuntimeUiScaleSettings.ScaleFont(titleScale);
            _introFallbackTitle.Text = title ?? string.Empty;

            _introFallbackSubtitle.Font = _theme.DefaultFont;
            _introFallbackSubtitle.FontScale = RuntimeUiScaleSettings.ScaleFont(subtitleScale);
            _introFallbackSubtitle.Text = subtitle ?? string.Empty;

            _introFallbackGroup.gameObject.SetActive(show);
        }

        string BuildLoadingLabel()
        {
            if (_progress == null)
                return "Loading";

            string stage = string.IsNullOrWhiteSpace(_progress.Stage) ? "Loading" : _progress.Stage;
            string detail = string.IsNullOrWhiteSpace(_progress.Label) ? stage : $"{stage}: {_progress.Label}";
            return detail;
        }

        void UpdateLoadingBarFill(float fraction)
        {
            if (_loadingBarFillRect == null)
                return;

            RectTransform frameRect = _loadingBarFrame?.Root;
            float totalWidth = frameRect != null ? frameRect.rect.width : _loadingBarRect.rect.width;
            float fillWidth = Mathf.Max(0f, totalWidth * fraction);
            _loadingBarFillRect.sizeDelta = new Vector2(fillWidth, 0f);
        }

    }
}
