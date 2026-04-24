using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Core.Config;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla-faithful Options window. Mirrors OpenMW's
    /// <c>openmw_settings_window.layout</c> at reference 600×485: MW_Window chassis
    /// caption "Options" + a six-tab strip (Preferences / Audio / Controls / Video /
    /// Scripts / Language) + a two-button footer (Reset to Defaults / Close).
    ///
    /// Functional tabs (Preferences, Audio, Video) wire sliders, toggles, and
    /// cycle-steppers directly into the caller-supplied <see cref="Callbacks"/>
    /// delegates so values live-apply as the user drags. Unimplemented tabs
    /// (Controls, Scripts, Language) render a short "Not yet implemented — coming
    /// with the ${system} phase" panel on the same chassis so the tab strip is
    /// complete now and those screens slot in without restructuring later.
    ///
    /// The view itself is configuration-passive: it READS a shared
    /// <see cref="MorrowindConfig"/> reference on <see cref="SyncFromConfig"/> and
    /// WRITES back through the callbacks. The caller (e.g. RuntimeHudShellView or
    /// BootstrapPresentationView) owns the config lifetime and the runtime apply
    /// points (RuntimeUiScaleSettings, RuntimeAudioService, Camera.main, etc.).
    /// </summary>
    public sealed class OptionsWindowView
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
        /// is optional — null means "this setting exists in config but the runtime
        /// has no apply hook yet" (e.g. Voice / Footsteps volumes). The view still
        /// writes the new value to the supplied config; persistence happens on
        /// Close.
        /// </summary>
        public sealed class Callbacks
        {
            public Action<float> UiScale;
            public Action<float> HudScale;
            public Action<int, int, int> Resolution;     // width, height, refreshRate
            public Action<int> WindowMode;                // 0 windowed, 1 fullscreen, 2 borderless
            public Action<int> VSync;
            public Action<float> Fov;
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

        // ---- Visual constants ----------------------------------------------------

        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color TabCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color TabSelectedCenterColor = new(0.38f, 0.28f, 0.14f, 0.96f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color SliderTrackCenterColor = new(0f, 0f, 0f, 0.45f);
        static readonly Color SliderFillColor = new(0.68f, 0.55f, 0.22f, 0.96f);
        // Handle center uses the darker MW_Box interior while the border filigree
        // (thin frame) supplies the gold edge — matches OpenMW's MW_ScrollBar
        // tracker which is a MW_Box skinned with the generic thin border.
        static readonly Color SliderHandleCenterColor = new(0.18f, 0.14f, 0.09f, 0.95f);
        static readonly Color ToggleOffCenterColor = new(0f, 0f, 0f, 0.55f);
        static readonly Color ToggleOnCenterColor = new(0.68f, 0.55f, 0.22f, 0.94f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color SelectedTextColor = new(0.98f, 0.93f, 0.80f);

        const float CaptionPixelHeight = 14f;
        const float TabTextPixelHeight = 13f;
        const float BodyPixelHeight = 13f;
        const float SubtlePixelHeight = 12f;
        const float ValuePixelHeight = 12f;
        const float ButtonTextPixelHeight = 13f;

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
        // Vanilla MW's scroll / slider tracker is a compact thin-framed rectangle
        // with a contrasting dark center — small enough not to hide the fill
        // behind it, tall enough to extend slightly above and below the track.
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

        // Control refs per tab, so we can push config values back into them on
        // Reset/open without going through the public API.
        SliderView _uiScaleSlider, _hudScaleSlider, _fovSlider, _gammaSlider;
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

        // ---- Public API ---------------------------------------------------------

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

        /// <summary>
        /// Push every config value into the matching control. Called on open (so
        /// the user sees persisted values) and on Reset (so defaults take).
        /// </summary>
        public void SyncFromConfig()
        {
            if (_config == null)
                return;

            ApplySliderValue(_uiScaleSlider, _config.UiScale);
            ApplySliderValue(_hudScaleSlider, _config.HudScale);
            ApplySliderValue(_fovSlider, _config.Fov);
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

        // ---- Tab strip ----------------------------------------------------------

        void BuildTabStrip()
        {
            float stripHeight = RuntimeClassicUiMetrics.Ui(TabStripHeight);
            float spacing = RuntimeClassicUiMetrics.Ui(TabSpacing);

            var strip = RuntimeUiFactory.CreateAnchorRect(
                "TabStrip",
                _window.Client,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -stripHeight),
                Vector2.zero);

            int count = k_Tabs.Length;
            float widthPerTab = 1f / count;
            for (int i = 0; i < count; i++)
            {
                var def = k_Tabs[i];
                float min = i * widthPerTab;
                float max = (i + 1) * widthPerTab;

                var tabRoot = RuntimeUiFactory.CreateAnchorRect(
                    $"Tab_{def.Id}",
                    strip,
                    new Vector2(min, 0f),
                    new Vector2(max, 1f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(i == 0 ? 0f : spacing * 0.5f, 0f),
                    new Vector2(i == count - 1 ? 0f : -spacing * 0.5f, 0f));

                var button = RuntimeUiFactory.CreateMorrowindButton(
                    "Button",
                    tabRoot,
                    _theme,
                    def.Label,
                    1f,
                    BodyTextColor,
                    TabCenterColor);
                RuntimeUiFactory.Stretch(button.Root);
                button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(TabTextPixelHeight);
                button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
                button.Button.transition = Selectable.Transition.None;

                TabId captured = def.Id;
                button.Button.onClick.AddListener(() => SetActiveTab(captured));
                _tabButtons.Add(new TabButtonView { Id = captured, View = button });
            }
        }

        void SetActiveTab(TabId tab)
        {
            _activeTab = tab;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool selected = _tabButtons[i].Id == tab;
                _tabButtons[i].View.Frame.Center.color = selected ? TabSelectedCenterColor : TabCenterColor;
                _tabButtons[i].View.Label.color = selected ? SelectedTextColor : BodyTextColor;
            }

            foreach (var kvp in _tabContents)
                kvp.Value.gameObject.SetActive(kvp.Key == tab);
        }

        // ---- Tab content frames -------------------------------------------------

        void BuildTabContents()
        {
            float stripHeight = RuntimeClassicUiMetrics.Ui(TabStripHeight);
            float stripGap = RuntimeClassicUiMetrics.Ui(TabStripBottomGap);
            float footerHeight = RuntimeClassicUiMetrics.Ui(FooterHeight);
            float footerGap = RuntimeClassicUiMetrics.Ui(FooterTopGap);

            var contentArea = RuntimeUiFactory.CreateAnchorRect(
                "TabContent",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, footerHeight + footerGap),
                new Vector2(0f, -(stripHeight + stripGap)));

            foreach (var def in k_Tabs)
            {
                var tabRoot = RuntimeUiFactory.CreateStretchRect($"Content_{def.Id}", contentArea);
                _tabContents[def.Id] = tabRoot;
            }

            BuildPreferencesTab(_tabContents[TabId.Preferences]);
            BuildAudioTab(_tabContents[TabId.Audio]);
            BuildVideoTab(_tabContents[TabId.Video]);
            BuildStubTab(_tabContents[TabId.Controls], "Controls rebinding", "input-rebinding");
            BuildStubTab(_tabContents[TabId.Scripts], "Lua script catalog", "script");
            BuildStubTab(_tabContents[TabId.Language], "Localization picker", "localization");
        }

        // ---- Footer -------------------------------------------------------------

        (MorrowindButtonView reset, MorrowindButtonView close) BuildFooter()
        {
            float footerHeight = RuntimeClassicUiMetrics.Ui(FooterHeight);
            var footer = RuntimeUiFactory.CreateAnchorRect(
                "Footer",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, footerHeight));

            float resetWidth = RuntimeClassicUiMetrics.Ui(FooterResetWidth);
            float closeWidth = RuntimeClassicUiMetrics.Ui(FooterCloseWidth);

            var reset = BuildFooterButton(
                footer,
                "ResetButton",
                "Reset to Defaults",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero,
                offsetMax: new Vector2(resetWidth, 0f),
                clickAction: () => _callbacks.ResetToDefaults?.Invoke());

            var close = BuildFooterButton(
                footer,
                "CloseButton",
                "Close",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-closeWidth, 0f),
                offsetMax: Vector2.zero,
                clickAction: () => _callbacks.Close?.Invoke());

            return (reset, close);
        }

        MorrowindButtonView BuildFooterButton(
            RectTransform footer,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            UnityEngine.Events.UnityAction clickAction)
        {
            var rect = RuntimeUiFactory.CreateAnchorRect(
                name + "Rect",
                footer,
                anchorMin,
                anchorMax,
                new Vector2(0.5f, 0.5f),
                offsetMin,
                offsetMax);

            var button = RuntimeUiFactory.CreateMorrowindButton(
                "Button",
                rect,
                _theme,
                label,
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(button.Root);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            button.Button.transition = Selectable.Transition.ColorTint;
            if (clickAction != null)
                button.Button.onClick.AddListener(clickAction);
            return button;
        }

        // ---- Tab: Preferences ---------------------------------------------------

        void BuildPreferencesTab(RectTransform root)
        {
            float y = 0f;
            _transparencySlider = CreateRowSlider(root, y, "Menu Transparency", 0.3f, 1f, _config.MenuTransparency,
                formatter: v => $"{v:0.00}",
                onChanged: v =>
                {
                    _config.MenuTransparency = v;
                    _callbacks.MenuTransparency?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _difficultySlider = CreateRowSlider(root, y, "Difficulty", -100f, 100f, _config.Difficulty,
                formatter: v => Mathf.RoundToInt(v).ToString(),
                wholeNumbers: true,
                onChanged: v =>
                {
                    int rounded = Mathf.RoundToInt(v);
                    _config.Difficulty = rounded;
                    _callbacks.Difficulty?.Invoke(rounded);
                });
            y += RowHeight + RowSpacing;

            _crosshairToggle = CreateRowToggle(root, y, "Show Crosshair", _config.ShowCrosshair,
                onChanged: v =>
                {
                    _config.ShowCrosshair = v;
                    _callbacks.ShowCrosshair?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _subtitlesToggle = CreateRowToggle(root, y, "Show Subtitles", _config.ShowSubtitles,
                onChanged: v =>
                {
                    _config.ShowSubtitles = v;
                    _callbacks.ShowSubtitles?.Invoke(v);
                });
        }

        // ---- Tab: Audio ---------------------------------------------------------

        void BuildAudioTab(RectTransform root)
        {
            float y = 0f;
            _masterSlider = CreateRowSlider(root, y, "Master Volume", 0f, 1f, _config.MasterVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.MasterVolume = v;
                    _callbacks.MasterVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _musicSlider = CreateRowSlider(root, y, "Music Volume", 0f, 1f, _config.MusicVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.MusicVolume = v;
                    _callbacks.MusicVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _effectsSlider = CreateRowSlider(root, y, "Effects Volume", 0f, 1f, _config.EffectsVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.EffectsVolume = v;
                    _callbacks.EffectsVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _footstepsSlider = CreateRowSlider(root, y, "Footsteps Volume", 0f, 1f, _config.FootstepsVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.FootstepsVolume = v;
                    _callbacks.FootstepsVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _voiceSlider = CreateRowSlider(root, y, "Voice Volume", 0f, 1f, _config.VoiceVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.VoiceVolume = v;
                    _callbacks.VoiceVolume?.Invoke(v);
                });
        }

        // ---- Tab: Video ---------------------------------------------------------

        void BuildVideoTab(RectTransform root)
        {
            float y = 0f;
            // UI / HUD scale bounds are deliberately wide — they're preference
            // knobs, not balance knobs, and the CanvasScaler-driven apply path
            // copes fine with extreme values. Minimums match
            // RuntimeUiScaleSettings' own floor guards.
            _uiScaleSlider = CreateRowSlider(root, y, "UI Scale", 0.25f, 4f, _config.UiScale,
                formatter: v => $"{v:0.00}x",
                onChanged: v =>
                {
                    _config.UiScale = v;
                    _callbacks.UiScale?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _hudScaleSlider = CreateRowSlider(root, y, "HUD Scale", 0.5f, 6f, _config.HudScale,
                formatter: v => $"{v:0.00}x",
                onChanged: v =>
                {
                    _config.HudScale = v;
                    _callbacks.HudScale?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _fovSlider = CreateRowSlider(root, y, "Field of View", 30f, 110f, _config.Fov,
                formatter: v => $"{Mathf.RoundToInt(v)}°",
                onChanged: v =>
                {
                    _config.Fov = v;
                    _callbacks.Fov?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _gammaSlider = CreateRowSlider(root, y, "Gamma", 0.1f, 3f, _config.Gamma,
                formatter: v => $"{v:0.00}",
                onChanged: v =>
                {
                    _config.Gamma = v;
                    _callbacks.Gamma?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            // Resolution stepper — populated from Screen.resolutions. Fresh configs
            // default to 0,0 meaning "use current"; we land on the entry matching
            // Screen.width/height if we can find one.
            _resolutions = BuildResolutionList();
            _resolutionStepper = CreateRowStepper(root, y, "Resolution", _resolutions.Count,
                labeler: i =>
                {
                    if (i < 0 || i >= _resolutions.Count) return "—";
                    var r = _resolutions[i];
                    return $"{r.width} × {r.height}";
                },
                onChanged: i =>
                {
                    if (i < 0 || i >= _resolutions.Count) return;
                    var r = _resolutions[i];
                    _config.ResolutionWidth = r.width;
                    _config.ResolutionHeight = r.height;
#if UNITY_2022_2_OR_NEWER
                    _config.RefreshRate = Mathf.RoundToInt((float)r.refreshRateRatio.value);
#else
                    _config.RefreshRate = r.refreshRate;
#endif
                    _callbacks.Resolution?.Invoke(r.width, r.height, _config.RefreshRate);
                });
            y += RowHeight + RowSpacing;

            _windowModeStepper = CreateRowStepper(root, y, "Window Mode", count: 3,
                labeler: i => i switch { 0 => "Windowed", 1 => "Fullscreen", 2 => "Borderless", _ => "—" },
                onChanged: i =>
                {
                    _config.WindowMode = i;
                    _callbacks.WindowMode?.Invoke(i);
                });
            y += RowHeight + RowSpacing;

            _vsyncStepper = CreateRowStepper(root, y, "VSync", count: 3,
                labeler: i => i switch { 0 => "Off", 1 => "On", 2 => "Half", _ => "—" },
                onChanged: i =>
                {
                    _config.VSync = i;
                    _callbacks.VSync?.Invoke(i);
                });
        }

        List<Resolution> BuildResolutionList()
        {
            var all = Screen.resolutions;
            var list = new List<Resolution>(all.Length);
            // Unity returns sorted ascending; some duplicates by refresh rate — keep
            // only unique (width,height) picking the highest refresh rate.
            var seen = new HashSet<long>();
            for (int i = all.Length - 1; i >= 0; i--)
            {
                long key = ((long)all[i].width << 32) | (uint)all[i].height;
                if (seen.Add(key))
                    list.Add(all[i]);
            }
            list.Reverse();
            return list;
        }

        void SyncResolutionStepper()
        {
            if (_resolutionStepper == null)
                return;

            int target = -1;
            int w = _config.ResolutionWidth > 0 ? _config.ResolutionWidth : Screen.width;
            int h = _config.ResolutionHeight > 0 ? _config.ResolutionHeight : Screen.height;
            for (int i = 0; i < _resolutions.Count; i++)
            {
                if (_resolutions[i].width == w && _resolutions[i].height == h)
                {
                    target = i;
                    break;
                }
            }
            if (target < 0 && _resolutions.Count > 0)
                target = _resolutions.Count - 1;

            ApplyStepperIndex(_resolutionStepper, Mathf.Max(0, target));
        }

        // ---- Tab: stubs ---------------------------------------------------------

        void BuildStubTab(RectTransform root, string topicLabel, string systemHint)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                "StubText",
                root,
                _theme?.DefaultFont,
                1f,
                SubtleTextColor,
                BitmapTextAlignment.Center);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.WrapMode = BitmapTextWrapMode.Word;
            text.Text = $"{topicLabel} is not yet implemented.\nComing with the {systemHint} phase.";
            text.raycastTarget = false;
            RuntimeUiFactory.Stretch(text.rectTransform);
        }

        // ---- Widget builders ----------------------------------------------------

        RectTransform CreateRow(RectTransform parent, float y, string labelText)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            var row = RuntimeUiFactory.CreateAnchoredRect(
                $"Row_{labelText}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeClassicUiMetrics.Ui(y)),
                new Vector2(0f, rowHeight));
            row.pivot = new Vector2(0f, 1f);

            var label = RuntimeUiFactory.CreateBitmapText(
                "Label",
                row,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.Text = labelText;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0f, 1f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(RuntimeClassicUiMetrics.Ui(4f), 0f);
            label.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.Ui(LabelColumnWidth), 0f);

            return row;
        }

        SliderView CreateRowSlider(
            RectTransform parent,
            float y,
            string labelText,
            float minValue,
            float maxValue,
            float initial,
            Func<float, string> formatter,
            Action<float> onChanged,
            bool wholeNumbers = false)
        {
            var row = CreateRow(parent, y, labelText);

            // Control column rect.
            float controlLeft = RuntimeClassicUiMetrics.Ui(LabelColumnWidth + 6f);
            float valueWidth = RuntimeClassicUiMetrics.Ui(ValueReadoutWidth);
            float valueGap = RuntimeClassicUiMetrics.Ui(6f);

            var sliderRect = RuntimeUiFactory.CreateAnchorRect(
                "Slider",
                row,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(controlLeft, RuntimeClassicUiMetrics.Ui(6f)),
                new Vector2(-(valueWidth + valueGap), -RuntimeClassicUiMetrics.Ui(6f)));

            // Background: MW_Box thin-frame track.
            var trackFrame = RuntimeUiFactory.CreateBorderFrame(
                "Track",
                sliderRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SliderTrackCenterColor);
            RuntimeUiFactory.Stretch(trackFrame.Root);

            // Fill (child of fill-area per Unity Slider contract).
            var fillArea = RuntimeUiFactory.CreateAnchorRect(
                "Fill Area",
                sliderRect,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(2f), -RuntimeClassicUiMetrics.Ui(4f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(2f), RuntimeClassicUiMetrics.Ui(4f)));

            var fillHolder = RuntimeUiFactory.CreateAnchoredRect(
                "Fill",
                fillArea,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            fillHolder.pivot = new Vector2(0f, 0.5f);

            var fill = RuntimeUiFactory.CreateImage("FillImage", fillHolder, SliderFillColor);
            fill.sprite = _theme?.LoadingBarFillSprite;
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;
            RuntimeUiFactory.Stretch(fill.rectTransform);

            // Handle (child of handle-area per Unity Slider contract).
            var handleArea = RuntimeUiFactory.CreateAnchorRect(
                "Handle Slide Area",
                sliderRect,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(SliderHandleWidth * 0.5f), 0f),
                new Vector2(-RuntimeClassicUiMetrics.Ui(SliderHandleWidth * 0.5f), 0f));

            var handleRect = RuntimeUiFactory.CreateAnchoredRect(
                "Handle",
                handleArea,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(SliderHandleWidth, SliderHandleHeight)));
            handleRect.pivot = new Vector2(0.5f, 0.5f);

            // MW-style tracker: MW_Box thin frame with a dark amber center (the
            // same treatment OpenMW's scrollbar "tracker" uses). Frame.Center is
            // the raycast target so the whole handle rect is grabbable, not just
            // a tiny interior pixel.
            var handleFrame = RuntimeUiFactory.CreateBorderFrame(
                "HandleFrame",
                handleRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SliderHandleCenterColor);
            RuntimeUiFactory.Stretch(handleFrame.Root);
            handleFrame.Center.raycastTarget = true;

            // Slider component.
            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.targetGraphic = handleFrame.Center;
            slider.fillRect = fillHolder;
            slider.handleRect = handleRect;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(Mathf.Clamp(initial, minValue, maxValue));

            // Value readout to the right of the slider.
            var value = RuntimeUiFactory.CreateBitmapText(
                "Value",
                row,
                _theme?.DefaultFont,
                1f,
                SubtleTextColor,
                BitmapTextAlignment.Right);
            value.PixelHeight = RuntimeClassicUiMetrics.Ui(ValuePixelHeight);
            value.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            value.raycastTarget = false;
            value.rectTransform.anchorMin = new Vector2(1f, 0f);
            value.rectTransform.anchorMax = new Vector2(1f, 1f);
            value.rectTransform.pivot = new Vector2(1f, 0.5f);
            value.rectTransform.anchoredPosition = new Vector2(-RuntimeClassicUiMetrics.Ui(4f), 0f);
            value.rectTransform.sizeDelta = new Vector2(valueWidth, 0f);

            var view = new SliderView
            {
                Slider = slider,
                Fill = fill,
                ValueText = value,
                Formatter = formatter,
            };
            value.Text = formatter?.Invoke(slider.value) ?? slider.value.ToString("0.00");

            slider.onValueChanged.AddListener(v =>
            {
                view.ValueText.Text = formatter?.Invoke(v) ?? v.ToString("0.00");
                onChanged?.Invoke(v);
            });
            return view;
        }

        ToggleView CreateRowToggle(
            RectTransform parent,
            float y,
            string labelText,
            bool initial,
            Action<bool> onChanged)
        {
            var row = CreateRow(parent, y, labelText);

            float controlLeft = RuntimeClassicUiMetrics.Ui(LabelColumnWidth + 6f);
            float size = RuntimeClassicUiMetrics.Ui(ToggleSize);

            var holder = RuntimeUiFactory.CreateAnchorRect(
                "Toggle",
                row,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(controlLeft, -size * 0.5f),
                new Vector2(controlLeft + size, size * 0.5f));

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                holder,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                ToggleOffCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var on = RuntimeUiFactory.CreateImage("OnIndicator", frame.Client, ToggleOnCenterColor);
            on.sprite = _theme?.LoadingBarFillSprite;
            on.type = Image.Type.Simple;
            RuntimeUiFactory.SetInset(
                on.rectTransform,
                RuntimeClassicUiMetrics.Ui(2f),
                RuntimeClassicUiMetrics.Ui(2f),
                -RuntimeClassicUiMetrics.Ui(2f),
                -RuntimeClassicUiMetrics.Ui(2f));
            on.raycastTarget = false;
            on.gameObject.SetActive(initial);

            var toggle = holder.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.targetGraphic = frame.Center;
            toggle.graphic = on;
            toggle.isOn = initial;

            var view = new ToggleView { Toggle = toggle, OnIndicator = on };
            toggle.onValueChanged.AddListener(v =>
            {
                on.gameObject.SetActive(v);
                onChanged?.Invoke(v);
            });
            return view;
        }

        StepperView CreateRowStepper(
            RectTransform parent,
            float y,
            string labelText,
            int count,
            Func<int, string> labeler,
            Action<int> onChanged)
        {
            var row = CreateRow(parent, y, labelText);

            float controlLeft = RuntimeClassicUiMetrics.Ui(LabelColumnWidth + 6f);
            float arrowWidth = RuntimeClassicUiMetrics.Ui(StepperArrowWidth);
            float arrowGap = RuntimeClassicUiMetrics.Ui(4f);

            var holder = RuntimeUiFactory.CreateAnchorRect(
                "Stepper",
                row,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(controlLeft, RuntimeClassicUiMetrics.Ui(4f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(4f), -RuntimeClassicUiMetrics.Ui(4f)));

            var prevRect = RuntimeUiFactory.CreateAnchorRect(
                "Prev",
                holder,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(arrowWidth, 0f));

            var prevBtn = RuntimeUiFactory.CreateMorrowindButton(
                "PrevButton",
                prevRect,
                _theme,
                "<",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(prevBtn.Root);
            prevBtn.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            prevBtn.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var nextRect = RuntimeUiFactory.CreateAnchorRect(
                "Next",
                holder,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-arrowWidth, 0f),
                Vector2.zero);

            var nextBtn = RuntimeUiFactory.CreateMorrowindButton(
                "NextButton",
                nextRect,
                _theme,
                ">",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(nextBtn.Root);
            nextBtn.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            nextBtn.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var valueRect = RuntimeUiFactory.CreateAnchorRect(
                "Value",
                holder,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(arrowWidth + arrowGap, 0f),
                new Vector2(-(arrowWidth + arrowGap), 0f));

            var valueFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                valueRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SliderTrackCenterColor);
            RuntimeUiFactory.Stretch(valueFrame.Root);

            var valueText = RuntimeUiFactory.CreateBitmapText(
                "Text",
                valueFrame.Client,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Center);
            valueText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            valueText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            valueText.raycastTarget = false;
            RuntimeUiFactory.Stretch(valueText.rectTransform);

            var view = new StepperView
            {
                Prev = prevBtn.Button,
                Next = nextBtn.Button,
                ValueText = valueText,
                Labeler = labeler,
                Count = Mathf.Max(1, count),
                Index = 0,
                OnChanged = onChanged,
            };
            valueText.Text = labeler?.Invoke(0) ?? "0";

            prevBtn.Button.onClick.AddListener(() => StepStepper(view, -1));
            nextBtn.Button.onClick.AddListener(() => StepStepper(view, +1));
            return view;
        }

        static void StepStepper(StepperView view, int delta)
        {
            if (view == null || view.Count <= 0)
                return;

            int next = (view.Index + delta + view.Count) % view.Count;
            if (next == view.Index)
                return;

            view.Index = next;
            view.ValueText.Text = view.Labeler?.Invoke(next) ?? next.ToString();
            view.OnChanged?.Invoke(next);
        }

        // ---- Sync helpers -------------------------------------------------------

        static void ApplySliderValue(SliderView view, float value)
        {
            if (view?.Slider == null)
                return;

            float clamped = Mathf.Clamp(value, view.Slider.minValue, view.Slider.maxValue);
            view.Slider.SetValueWithoutNotify(clamped);
            view.ValueText.Text = view.Formatter?.Invoke(clamped) ?? clamped.ToString("0.00");
        }

        static void ApplyToggleValue(ToggleView view, bool value)
        {
            if (view?.Toggle == null)
                return;

            view.Toggle.SetIsOnWithoutNotify(value);
            if (view.OnIndicator != null)
                view.OnIndicator.gameObject.SetActive(value);
        }

        static void ApplyStepperIndex(StepperView view, int index)
        {
            if (view == null)
                return;

            view.Index = Mathf.Clamp(index, 0, Mathf.Max(0, view.Count - 1));
            view.ValueText.Text = view.Labeler?.Invoke(view.Index) ?? view.Index.ToString();
        }
    }
}
