using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Core.Config;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla-faithful Options window. Mirrors OpenMW's options layout at
    /// reference 600 x 485 with a caption, six-tab strip, and two-button footer.
    /// </summary>
    public sealed partial class OptionsWindowView
    {
        public enum TabId
        {
            Preferences,
            Audio,
            Controls,
            Video,
            Scripts,
            Language,
        }

        /// <summary>
        /// Grouped callbacks the view invokes when a control changes. Every field
        /// is optional; null means this setting exists in config but the runtime
        /// has no apply hook yet.
        /// </summary>
        public sealed class Callbacks
        {
            public Action<float> UiScale;
            public Action<float> HudScale;
            public Action<int, int, int> Resolution;
            public Action<int> WindowMode;
            public Action<int> VSync;
            public Action<float> Fov;
            public Action<float> FogDistanceScale;
            public Action<float> Gamma;

            public Action<float> MasterVolume;
            public Action<float> MusicVolume;
            public Action<float> EffectsVolume;
            public Action<float> FootstepsVolume;
            public Action<float> VoiceVolume;

            public Action<bool> ShowCrosshair;
            public Action<bool> ShowSubtitles;
            public Action<float> MenuTransparency;
            public Action<int> Difficulty;

            public Action Close;
            public Action ResetToDefaults;
        }

        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color TabCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color TabSelectedCenterColor = new(0.38f, 0.28f, 0.14f, 0.96f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color SliderTrackCenterColor = new(0f, 0f, 0f, 0.45f);
        static readonly Color SliderFillColor = new(0.68f, 0.55f, 0.22f, 0.96f);
        static readonly Color SliderHandleCenterColor = new(0.18f, 0.14f, 0.09f, 0.95f);
        static readonly Color ToggleOffCenterColor = new(0f, 0f, 0f, 0.55f);
        static readonly Color ToggleOnCenterColor = new(0.68f, 0.55f, 0.22f, 0.94f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color SelectedTextColor = new(0.98f, 0.93f, 0.80f);

        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float TabTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float BodyPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float SubtlePixelHeight = RuntimeClassicUiFontSizes.Subtle;
        const float ValuePixelHeight = RuntimeClassicUiFontSizes.Small;
        const float ButtonTextPixelHeight = RuntimeClassicUiFontSizes.Body;

        const float DefaultWindowWidth = 600f;
        const float DefaultWindowHeight = 485f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;

        const float TabStripHeight = 28f;
        const float TabStripBottomGap = 6f;
        const float TabSpacing = 2f;

        const float FooterHeight = 30f;
        const float FooterTopGap = 6f;
        const float FooterButtonHeight = 22f;
        const float FooterResetWidth = 140f;
        const float FooterCloseWidth = 90f;

        const float RowHeight = 28f;
        const float RowSpacing = 2f;
        const float LabelColumnWidth = 160f;
        const float ControlColumnWidth = 320f;
        const float ValueReadoutWidth = 60f;
        const float SliderHandleWidth = 12f;
        const float SliderHandleHeight = 20f;
        const float ToggleSize = 18f;
        const float StepperArrowWidth = 22f;

        static readonly TabDefinition[] k_Tabs =
        {
            new(TabId.Preferences, "Prefs"),
            new(TabId.Audio, "Audio"),
            new(TabId.Controls, "Controls"),
            new(TabId.Video, "Video"),
            new(TabId.Scripts, "Scripts"),
            new(TabId.Language, "Language"),
        };

        readonly struct TabDefinition
        {
            public TabDefinition(TabId id, string label) { Id = id; Label = label; }
            public TabId Id { get; }
            public string Label { get; }
        }

        sealed class TabButtonView
        {
            public TabId Id;
            public MorrowindButtonView View;
        }

        sealed class SliderView
        {
            public Slider Slider;
            public Image Fill;
            public BitmapTextGraphic ValueText;
            public Func<float, string> Formatter;
        }

        sealed class ToggleView
        {
            public Toggle Toggle;
            public Image OnIndicator;
        }

        sealed class StepperView
        {
            public Button Prev;
            public Button Next;
            public BitmapTextGraphic ValueText;
            public Func<int, string> Labeler;
            public int Count;
            public int Index;
            public Action<int> OnChanged;
        }

        readonly RuntimeUiTheme _theme;
        readonly MorrowindConfig _config;
        readonly Callbacks _callbacks;

        readonly RectTransform _root;
        readonly MorrowindWindowView _window;
        readonly List<TabButtonView> _tabButtons = new();
        readonly Dictionary<TabId, RectTransform> _tabContents = new();
        readonly MorrowindButtonView _resetButton;
        readonly MorrowindButtonView _closeButton;
        TabId _activeTab = TabId.Preferences;

        SliderView _uiScaleSlider, _hudScaleSlider, _fovSlider, _fogDistanceSlider, _gammaSlider;
        SliderView _masterSlider, _musicSlider, _effectsSlider, _footstepsSlider, _voiceSlider;
        SliderView _transparencySlider;
        SliderView _difficultySlider;
        ToggleView _crosshairToggle, _subtitlesToggle;
        StepperView _resolutionStepper, _windowModeStepper, _vsyncStepper;
        List<Resolution> _resolutions = new();

        public OptionsWindowView(
            Transform parent,
            RuntimeUiTheme theme,
            MorrowindConfig config,
            Callbacks callbacks)
        {
            _theme = theme;
            _config = config;
            _callbacks = callbacks ?? new Callbacks();

            _root = RuntimeUiFactory.CreateStretchRect("OptionsBrowser", parent);
            _root.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("Backdrop", _root, BackdropColor);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            var windowHolder = RuntimeUiFactory.CreateAnchoredRect(
                "DialogHolder",
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(DefaultWindowWidth, DefaultWindowHeight)));
            windowHolder.pivot = new Vector2(0.5f, 0.5f);

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "OptionsWindow",
                windowHolder,
                theme,
                "Options",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.92f,
                RuntimeClassicUiMetrics.Ui(CaptionPixelHeight),
                new Color(0.94f, 0.82f, 0.53f));
            RuntimeUiFactory.Stretch(_window.Root);

            BuildTabStrip();
            BuildTabContents();
            (_resetButton, _closeButton) = BuildFooter();

            SetActiveTab(TabId.Preferences);
        }

        public RectTransform Root => _root;

        public bool IsVisible => _root.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            _root.gameObject.SetActive(visible);
            if (visible)
                SyncFromConfig();
        }

        public bool OwnsSelection(GameObject selected)
        {
            return selected != null && selected.transform.IsChildOf(_root);
        }

        public void SyncFromConfig()
        {
            if (_config == null)
                return;

            ApplySliderValue(_uiScaleSlider, _config.UiScale);
            ApplySliderValue(_hudScaleSlider, _config.HudScale);
            ApplySliderValue(_fovSlider, _config.Fov);
            ApplySliderValue(_fogDistanceSlider, _config.FogDistanceScale);
            ApplySliderValue(_gammaSlider, _config.Gamma);

            ApplySliderValue(_masterSlider, _config.MasterVolume);
            ApplySliderValue(_musicSlider, _config.MusicVolume);
            ApplySliderValue(_effectsSlider, _config.EffectsVolume);
            ApplySliderValue(_footstepsSlider, _config.FootstepsVolume);
            ApplySliderValue(_voiceSlider, _config.VoiceVolume);

            ApplySliderValue(_transparencySlider, _config.MenuTransparency);
            ApplySliderValue(_difficultySlider, _config.Difficulty);

            ApplyToggleValue(_crosshairToggle, _config.ShowCrosshair);
            ApplyToggleValue(_subtitlesToggle, _config.ShowSubtitles);

            SyncResolutionStepper();
            ApplyStepperIndex(_windowModeStepper, Mathf.Clamp(_config.WindowMode, 0, 2));
            ApplyStepperIndex(_vsyncStepper, Mathf.Clamp(_config.VSync, 0, 2));
        }
    }
}
