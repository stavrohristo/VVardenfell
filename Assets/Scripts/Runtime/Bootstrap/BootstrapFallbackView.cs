using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed class BootstrapFallbackView : MonoBehaviour
    {
        // Dialog footprints. All three phases share the same chassis width so resizing
        // between phases doesn't feel like swapping dialogs.
        const float WindowWidth = 720f;
        const float PickerHeight = 232f;
        const float ModePickerHeight = 394f;
        const float BattlegroundPickerHeight = 520f;
        const float ProgressHeight = 232f;
        const float ErrorHeight = 204f;
        const float DialogVisualScale = 1.5f;
        const int BattlegroundVisibleRows = 10;

        // Text pixel heights — sourced from the canonical OpenMW-faithful
        // table so the pre-bake chrome reads the same as the post-bake
        // windows that follow. Stage text is one tier up (Header) for the
        // "Loading cells…" announcement; footer is Subtle for the tiny
        // version string.
        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float BodyPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float StagePixelHeight = RuntimeClassicUiFontSizes.Header;
        const float FooterPixelHeight = RuntimeClassicUiFontSizes.Subtle;

        // Palette - shared with the post-bake Stats/Inventory/... windows so the
        // transition from pre-bake to menu doesn't recolor everything.
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color ErrorTextColor = new(0.92f, 0.52f, 0.42f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color ProgressBarCenterColor = new(0f, 0f, 0f, 0.4f);
        static readonly Color ProgressBarFillColor = new(0f, 0.72f, 0.74f, 1f);

        RuntimeUiTheme _theme;
        Font _uiFont;
        RectTransform _rootRect;
        Image _backdrop;
        RawImage _backdropGradient;
        MorrowindWindowView _window;
        Text _windowTitleText;
        RectTransform _pickerRoot;
        RectTransform _modeRoot;
        RectTransform _battlegroundRoot;
        RectTransform _progressRoot;
        RectTransform _errorRoot;
        RuntimeUiTextInputView _pathInput;
        RuntimeUiTextInputView _battlegroundFilterInput;
        MorrowindButtonView _continueButton;
        MorrowindButtonView _browseButton;
        MorrowindButtonView _vanillaButton;
        MorrowindButtonView _projectTamrielButton;
        MorrowindButtonView _sandboxButton;
        MorrowindButtonView _combatSandboxButton;
        MorrowindButtonView _battlegroundPrevButton;
        MorrowindButtonView _battlegroundNextButton;
        Text _pathPromptText;
        Text _pathErrorText;
        Text _modePromptText;
        Text _modeInstallPathText;
        Text _battlegroundPromptText;
        Text _battlegroundCountText;
        Text _battlegroundErrorText;
        Text _progressDescriptionText;
        Text _progressStageText;
        Text _progressDetailText;
        RuntimeUiProgressBarView _progressBar;
        Text _progressFooterText;
        Text _errorBodyText;
        Action<string> _onPathChanged;
        Action _onContinue;
        Action _onBrowse;
        Action<BootstrapRuntimeMode> _onModeSelected;
        Action<string> _onBattlegroundFilterChanged;
        Action<int2> _onBattlegroundSelected;
        (int X, int Y)[] _battlegroundCells = Array.Empty<(int X, int Y)>();
        string _battlegroundFilter = string.Empty;
        int _battlegroundPage;
        bool _showBattlegroundImgui;
        Vector2 _battlegroundImguiScroll;
        GUIStyle _imguiWindowStyle;
        GUIStyle _imguiHeaderStyle;
        GUIStyle _imguiLabelStyle;
        GUIStyle _imguiDimStyle;
        GUIStyle _imguiButtonStyle;
        GUIStyle _imguiTextFieldStyle;
        GUIStyle _imguiErrorStyle;
        readonly List<MorrowindButtonView> _battlegroundButtons = new();
        readonly List<Text> _battlegroundButtonLabels = new();
        readonly int2[] _visibleBattlegroundCells = new int2[BattlegroundVisibleRows];

        public void Initialize(Action<string> onPathChanged, Action onContinue, Action onBrowse)
        {
            _onPathChanged = onPathChanged;
            _onContinue = onContinue;
            _onBrowse = onBrowse;
            _theme = RuntimeUiTheme.CreateEmbeddedFallback();
            _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasView = RuntimeUiFactory.CreateCanvasRoot(gameObject, "Root", short.MaxValue);
            _rootRect = canvasView.Root;

            // Pre-bake backdrop. The solid black matte stays behind everything so raycasts
            // don't leak through, and the embedded theme's `MenuBackground` gradient
            // renders on top — this is the same gradient the post-bake Loading / Menu
            // phases show, so the hand-off between pre-bake and bootstrap presentation
            // doesn't flash from black to gradient and back.
            _backdrop = RuntimeUiFactory.CreateImage("BackdropMatte", _rootRect, Color.black);
            _backdrop.raycastTarget = true;
            RuntimeUiFactory.Stretch(_backdrop.rectTransform);

            if (_theme?.MenuBackground?.Texture != null)
            {
                _backdropGradient = RuntimeUiFactory.CreateRawImage("BackdropGradient", _rootRect, Color.white);
                _backdropGradient.texture = _theme.MenuBackground.Texture;
                _backdropGradient.uvRect = new Rect(0f, 1f, 1f, -1f);
                _backdropGradient.raycastTarget = false;
                RuntimeUiFactory.Stretch(_backdropGradient.rectTransform);
            }

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "FallbackWindow",
                _rootRect,
                _theme,
                "VVardenfell",
                RuntimeUiScaleSettings.ScalePixels(18f),
                RuntimeUiScaleSettings.ScalePixels(10f),
                0.94f,
                RuntimeUiScaleSettings.ScalePixels(14f),
                new Color(0.94f, 0.82f, 0.53f));
            _window.Title.gameObject.SetActive(false);
            _windowTitleText = CreateUnityText(
                "FallbackCaptionText",
                _window.CaptionRoot,
                "VVardenfell",
                RuntimeUiScaleSettings.ScaleFontSize((int)CaptionPixelHeight),
                new Color(0.94f, 0.82f, 0.53f),
                TextAnchor.MiddleCenter);
            RuntimeUiFactory.Stretch(_windowTitleText.rectTransform);
            _window.Root.anchorMin = new Vector2(0.5f, 0.5f);
            _window.Root.anchorMax = new Vector2(0.5f, 0.5f);
            _window.Root.pivot = new Vector2(0.5f, 0.5f);
            _window.Root.localScale = Vector3.one * DialogVisualScale;

            BuildPickerView();
            BuildModePickerView();
            BuildBattlegroundPickerView();
            BuildProgressView();
            BuildErrorView();
            Hide();
        }

        public void ShowPathPicker(string path, string error)
            => ShowPathPicker(path, error, "VVardenfell - Locate Morrowind Installation", "Path to Morrowind");

        public void ShowPathPicker(string path, string error, string title, string placeholder)
        {
            EnsureInitialized();
            _showBattlegroundImgui = false;
            ShowRoot(title, PickerHeight);
            _pickerRoot.gameObject.SetActive(true);
            _modeRoot.gameObject.SetActive(false);
            _battlegroundRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(false);
            SetInputDisplay(path ?? string.Empty, placeholder);
            _pathErrorText.text = string.IsNullOrWhiteSpace(error) ? string.Empty : error.Trim();
            _pathErrorText.gameObject.SetActive(!string.IsNullOrWhiteSpace(error));
            _browseButton.Root.gameObject.SetActive(_onBrowse != null);
        }

        public void ShowModePicker(string installPath, Action<BootstrapRuntimeMode> onModeSelected)
        {
            EnsureInitialized();
            _showBattlegroundImgui = false;
            _onModeSelected = onModeSelected;
            ShowRoot("VVardenfell - Select Startup Mode", ModePickerHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(true);
            _battlegroundRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(false);
            _modeInstallPathText.text = string.IsNullOrWhiteSpace(installPath)
                ? string.Empty
                : $"Install: {installPath.Trim()}";
        }

        public void ShowCombatBattlegroundPicker(
            (int X, int Y)[] cells,
            string filter,
            Action<string> onFilterChanged,
            Action<int2> onBattlegroundSelected)
        {
            EnsureInitialized();
            _onBattlegroundFilterChanged = onFilterChanged;
            _onBattlegroundSelected = onBattlegroundSelected;
            _battlegroundCells = cells ?? Array.Empty<(int X, int Y)>();
            _battlegroundFilter = filter ?? string.Empty;
            _showBattlegroundImgui = true;
            if (_rootRect != null)
                _rootRect.gameObject.SetActive(false);
        }

        public void ShowProgress(string title, string stage, string detail, int current, int total, float fraction, string footer)
        {
            EnsureInitialized();
            _showBattlegroundImgui = false;
            ShowRoot(title, ProgressHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(false);
            _battlegroundRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(true);
            _errorRoot.gameObject.SetActive(false);

            string stageLabel = string.IsNullOrWhiteSpace(stage) ? "Loading" : stage;
            _progressStageText.text = $"Stage: {stageLabel}";
            string detailLabel = string.IsNullOrWhiteSpace(detail) ? stageLabel : detail;
            _progressDetailText.text = detailLabel;
            RuntimeUiFactory.SetProgressBarFill(_progressBar, Mathf.Clamp01(fraction));
            _progressFooterText.text = string.IsNullOrWhiteSpace(footer) ? string.Empty : footer.Trim();
        }

        public void ShowError(string title, string body)
        {
            EnsureInitialized();
            _showBattlegroundImgui = false;
            ShowRoot(title, ErrorHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(false);
            _battlegroundRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(true);
            _errorBodyText.text = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        }

        public void Hide()
        {
            _showBattlegroundImgui = false;
            if (_rootRect != null)
                _rootRect.gameObject.SetActive(false);
        }

        void BuildPickerView()
        {
            _pickerRoot = RuntimeUiFactory.CreateStretchRect("PickerRoot", _window.Client);
            _pathPromptText = CreateUnityText(
                "PromptText",
                _pickerRoot,
                "Path to your Morrowind installation folder (the one containing 'Data Files'):",
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                BodyTextColor,
                TextAnchor.UpperLeft);
            _pathPromptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _pathPromptText.verticalOverflow = VerticalWrapMode.Truncate;
            _pathPromptText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _pathPromptText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _pathPromptText.rectTransform.pivot = new Vector2(0f, 1f);
            _pathPromptText.rectTransform.anchoredPosition = Vector2.zero;
            _pathPromptText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(40f));

            var inputRect = RuntimeUiFactory.CreateAnchoredRect(
                "PathInputRoot",
                _pickerRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(48f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(28f)));
            inputRect.pivot = new Vector2(0f, 1f);

            _pathInput = RuntimeUiFactory.CreateBitmapInputField(
                "PathInput",
                inputRect,
                _theme,
                1f,
                BodyTextColor,
                new Color(0f, 0f, 0f, 0.74f),
                "Path to Morrowind");
            RuntimeUiFactory.Stretch(_pathInput.Root);
            _pathInput.OverlayText.gameObject.SetActive(false);
            _pathInput.HiddenText.color = new Color(0.94f, 0.88f, 0.76f);
            _pathInput.HiddenText.fontSize = RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight);
            _pathInput.HiddenPlaceholder.color = new Color(0.62f, 0.58f, 0.52f);
            _pathInput.HiddenPlaceholder.fontSize = RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight);
            _pathInput.InputField.onValueChanged.AddListener(OnPathChanged);

            var buttonRow = RuntimeUiFactory.CreateAnchoredRect(
                "ButtonRow",
                _pickerRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(26f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(28f)));
            buttonRow.pivot = new Vector2(0f, 0f);

            _continueButton = RuntimeUiFactory.CreateMorrowindButton(
                "ContinueButton",
                buttonRow,
                _theme,
                "Continue",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            _continueButton.Root.anchorMin = new Vector2(1f, 0f);
            _continueButton.Root.anchorMax = new Vector2(1f, 0f);
            _continueButton.Root.pivot = new Vector2(1f, 0f);
            _continueButton.Root.anchoredPosition = Vector2.zero;
            _continueButton.Root.sizeDelta = RuntimeUiScaleSettings.ScalePixels(new Vector2(148f, 28f));
            ReplaceButtonLabel(_continueButton, "Continue");
            _continueButton.Button.onClick.AddListener(OnContinuePressed);

            _browseButton = RuntimeUiFactory.CreateMorrowindButton(
                "BrowseButton",
                buttonRow,
                _theme,
                "Browse...",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            _browseButton.Root.anchorMin = new Vector2(1f, 0f);
            _browseButton.Root.anchorMax = new Vector2(1f, 0f);
            _browseButton.Root.pivot = new Vector2(1f, 0f);
            _browseButton.Root.anchoredPosition = new Vector2(-RuntimeUiScaleSettings.ScalePixels(156f), 0f);
            _browseButton.Root.sizeDelta = RuntimeUiScaleSettings.ScalePixels(new Vector2(132f, 28f));
            ReplaceButtonLabel(_browseButton, "Browse...");
            _browseButton.Button.onClick.AddListener(OnBrowsePressed);

            _pathErrorText = CreateUnityText(
                "PathErrorText",
                _pickerRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                ErrorTextColor,
                TextAnchor.LowerLeft);
            _pathErrorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _pathErrorText.verticalOverflow = VerticalWrapMode.Truncate;
            _pathErrorText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _pathErrorText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _pathErrorText.rectTransform.pivot = new Vector2(0f, 0f);
            _pathErrorText.rectTransform.anchoredPosition = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(62f));
            _pathErrorText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f));
            _pathErrorText.gameObject.SetActive(false);
        }

        void BuildModePickerView()
        {
            _modeRoot = RuntimeUiFactory.CreateStretchRect("ModePickerRoot", _window.Client);
            _modeRoot.gameObject.SetActive(false);

            _modePromptText = CreateUnityText(
                "ModePromptText",
                _modeRoot,
                "Choose how to boot this session.",
                RuntimeUiScaleSettings.ScaleFontSize((int)StagePixelHeight),
                BodyTextColor,
                TextAnchor.UpperLeft);
            _modePromptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modePromptText.verticalOverflow = VerticalWrapMode.Truncate;
            _modePromptText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _modePromptText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modePromptText.rectTransform.pivot = new Vector2(0f, 1f);
            _modePromptText.rectTransform.anchoredPosition = Vector2.zero;
            _modePromptText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(28f));

            _modeInstallPathText = CreateUnityText(
                "ModeInstallPathText",
                _modeRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)FooterPixelHeight),
                SubtleTextColor,
                TextAnchor.UpperLeft);
            _modeInstallPathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modeInstallPathText.verticalOverflow = VerticalWrapMode.Truncate;
            _modeInstallPathText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _modeInstallPathText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modeInstallPathText.rectTransform.pivot = new Vector2(0f, 1f);
            _modeInstallPathText.rectTransform.anchoredPosition = new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(32f));
            _modeInstallPathText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(34f));

            var vanillaRect = RuntimeUiFactory.CreateAnchoredRect(
                "VanillaButtonRow",
                _modeRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(82f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f)));
            vanillaRect.pivot = new Vector2(0f, 1f);

            _vanillaButton = RuntimeUiFactory.CreateMorrowindButton(
                "VanillaButton",
                vanillaRect,
                _theme,
                "Vanilla",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(_vanillaButton.Root);
            ReplaceButtonLabel(_vanillaButton, "Vanilla");
            _vanillaButton.Button.onClick.AddListener(OnVanillaPressed);

            var projectTamrielRect = RuntimeUiFactory.CreateAnchoredRect(
                "ProjectTamrielButtonRow",
                _modeRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(132f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f)));
            projectTamrielRect.pivot = new Vector2(0f, 1f);

            _projectTamrielButton = RuntimeUiFactory.CreateMorrowindButton(
                "ProjectTamrielButton",
                projectTamrielRect,
                _theme,
                "Project Tamriel",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(_projectTamrielButton.Root);
            ReplaceButtonLabel(_projectTamrielButton, "Project Tamriel");
            _projectTamrielButton.Button.onClick.AddListener(OnProjectTamrielPressed);

            var sandboxRect = RuntimeUiFactory.CreateAnchoredRect(
                "SandboxButtonRow",
                _modeRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(182f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f)));
            sandboxRect.pivot = new Vector2(0f, 1f);

            _sandboxButton = RuntimeUiFactory.CreateMorrowindButton(
                "SandboxButton",
                sandboxRect,
                _theme,
                "Sandbox",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(_sandboxButton.Root);
            ReplaceButtonLabel(_sandboxButton, "Sandbox");
            _sandboxButton.Button.onClick.AddListener(OnSandboxPressed);

            var combatSandboxRect = RuntimeUiFactory.CreateAnchoredRect(
                "CombatSandboxButtonRow",
                _modeRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(232f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f)));
            combatSandboxRect.pivot = new Vector2(0f, 1f);

            _combatSandboxButton = RuntimeUiFactory.CreateMorrowindButton(
                "CombatSandboxButton",
                combatSandboxRect,
                _theme,
                "Combat Sandbox",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(_combatSandboxButton.Root);
            ReplaceButtonLabel(_combatSandboxButton, "Combat Sandbox");
            _combatSandboxButton.Button.onClick.AddListener(OnCombatSandboxPressed);

        }

        void BuildBattlegroundPickerView()
        {
            _battlegroundRoot = RuntimeUiFactory.CreateStretchRect("BattlegroundPickerRoot", _window.Client);
            _battlegroundRoot.gameObject.SetActive(false);

            _battlegroundPromptText = CreateUnityText(
                "BattlegroundPromptText",
                _battlegroundRoot,
                "Choose an exterior cell for the battle simulator.",
                RuntimeUiScaleSettings.ScaleFontSize((int)StagePixelHeight),
                BodyTextColor,
                TextAnchor.UpperLeft);
            _battlegroundPromptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _battlegroundPromptText.verticalOverflow = VerticalWrapMode.Truncate;
            _battlegroundPromptText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _battlegroundPromptText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _battlegroundPromptText.rectTransform.pivot = new Vector2(0f, 1f);
            _battlegroundPromptText.rectTransform.anchoredPosition = Vector2.zero;
            _battlegroundPromptText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(30f));

            var filterRect = RuntimeUiFactory.CreateAnchoredRect(
                "BattlegroundFilterRoot",
                _battlegroundRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(42f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(28f)));
            filterRect.pivot = new Vector2(0f, 1f);

            _battlegroundFilterInput = RuntimeUiFactory.CreateBitmapInputField(
                "BattlegroundFilterInput",
                filterRect,
                _theme,
                1f,
                BodyTextColor,
                new Color(0f, 0f, 0f, 0.74f),
                "Search cells, e.g. -2,-9");
            RuntimeUiFactory.Stretch(_battlegroundFilterInput.Root);
            _battlegroundFilterInput.OverlayText.gameObject.SetActive(false);
            _battlegroundFilterInput.HiddenText.color = new Color(0.94f, 0.88f, 0.76f);
            _battlegroundFilterInput.HiddenText.fontSize = RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight);
            _battlegroundFilterInput.HiddenPlaceholder.color = new Color(0.62f, 0.58f, 0.52f);
            _battlegroundFilterInput.HiddenPlaceholder.fontSize = RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight);
            _battlegroundFilterInput.InputField.onValueChanged.AddListener(OnBattlegroundFilterChanged);

            _battlegroundCountText = CreateUnityText(
                "BattlegroundCountText",
                _battlegroundRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)FooterPixelHeight),
                SubtleTextColor,
                TextAnchor.UpperLeft);
            _battlegroundCountText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _battlegroundCountText.verticalOverflow = VerticalWrapMode.Truncate;
            _battlegroundCountText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _battlegroundCountText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _battlegroundCountText.rectTransform.pivot = new Vector2(0f, 1f);
            _battlegroundCountText.rectTransform.anchoredPosition = new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(76f));
            _battlegroundCountText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(22f));

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "BattlegroundRows",
                _battlegroundRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(36f)),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(108f)));
            rowsRoot.pivot = new Vector2(0f, 1f);

            float rowHeight = RuntimeUiScaleSettings.ScalePixels(30f);
            float rowGap = RuntimeUiScaleSettings.ScalePixels(7f);
            for (int i = 0; i < BattlegroundVisibleRows; i++)
            {
                var rowRect = RuntimeUiFactory.CreateAnchoredRect(
                    $"BattlegroundRow{i}",
                    rowsRoot,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -(rowHeight + rowGap) * i),
                    new Vector2(0f, rowHeight));
                rowRect.pivot = new Vector2(0f, 1f);

                var button = RuntimeUiFactory.CreateMorrowindButton(
                    $"BattlegroundButton{i}",
                    rowRect,
                    _theme,
                    string.Empty,
                    1f,
                    BodyTextColor,
                    ButtonCenterColor);
                RuntimeUiFactory.Stretch(button.Root);
                if (button.Label != null)
                    button.Label.gameObject.SetActive(false);

                int rowIndex = i;
                button.Button.onClick.AddListener(() => OnBattlegroundRowPressed(rowIndex));
                _battlegroundButtons.Add(button);

                var label = CreateUnityText(
                    $"BattlegroundLabel{i}",
                    button.Root,
                    string.Empty,
                    RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                    BodyTextColor,
                    TextAnchor.MiddleLeft);
                RuntimeUiFactory.Stretch(label.rectTransform);
                label.rectTransform.offsetMin = RuntimeUiScaleSettings.ScalePixels(new Vector2(12f, 0f));
                label.rectTransform.offsetMax = RuntimeUiScaleSettings.ScalePixels(new Vector2(-12f, 0f));
                _battlegroundButtonLabels.Add(label);
            }

            _battlegroundPrevButton = RuntimeUiFactory.CreateMorrowindButton(
                "BattlegroundPrevPage",
                _battlegroundRoot,
                _theme,
                "Previous",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            _battlegroundPrevButton.Root.anchorMin = new Vector2(0f, 0f);
            _battlegroundPrevButton.Root.anchorMax = new Vector2(0f, 0f);
            _battlegroundPrevButton.Root.pivot = new Vector2(0f, 0f);
            _battlegroundPrevButton.Root.anchoredPosition = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(34f));
            _battlegroundPrevButton.Root.sizeDelta = RuntimeUiScaleSettings.ScalePixels(new Vector2(128f, 28f));
            ReplaceButtonLabel(_battlegroundPrevButton, "Previous");
            _battlegroundPrevButton.Button.onClick.AddListener(OnBattlegroundPreviousPagePressed);

            _battlegroundNextButton = RuntimeUiFactory.CreateMorrowindButton(
                "BattlegroundNextPage",
                _battlegroundRoot,
                _theme,
                "Next",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            _battlegroundNextButton.Root.anchorMin = new Vector2(1f, 0f);
            _battlegroundNextButton.Root.anchorMax = new Vector2(1f, 0f);
            _battlegroundNextButton.Root.pivot = new Vector2(1f, 0f);
            _battlegroundNextButton.Root.anchoredPosition = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(34f));
            _battlegroundNextButton.Root.sizeDelta = RuntimeUiScaleSettings.ScalePixels(new Vector2(128f, 28f));
            ReplaceButtonLabel(_battlegroundNextButton, "Next");
            _battlegroundNextButton.Button.onClick.AddListener(OnBattlegroundNextPagePressed);

            _battlegroundErrorText = CreateUnityText(
                "BattlegroundErrorText",
                _battlegroundRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                ErrorTextColor,
                TextAnchor.LowerLeft);
            _battlegroundErrorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _battlegroundErrorText.verticalOverflow = VerticalWrapMode.Truncate;
            _battlegroundErrorText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _battlegroundErrorText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _battlegroundErrorText.rectTransform.pivot = new Vector2(0f, 0f);
            _battlegroundErrorText.rectTransform.anchoredPosition = Vector2.zero;
            _battlegroundErrorText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(30f));
            _battlegroundErrorText.gameObject.SetActive(false);
        }

        void BuildProgressView()
        {
            _progressRoot = RuntimeUiFactory.CreateStretchRect("ProgressRoot", _window.Client);
            _progressRoot.gameObject.SetActive(false);

            _progressDescriptionText = CreateUnityText(
                "DescriptionText",
                _progressRoot,
                "Vvardenfell United is preparing your game data so it can run smoothly. This only needs to happen once.",
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                BodyTextColor,
                TextAnchor.UpperLeft);
            _progressDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _progressDescriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            _progressDescriptionText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _progressDescriptionText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _progressDescriptionText.rectTransform.pivot = new Vector2(0f, 1f);
            _progressDescriptionText.rectTransform.anchoredPosition = new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(4f));
            _progressDescriptionText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(42f));

            // Stage title (e.g. "Stage: UI") reads as a primary line; progress counter
            // underneath fills in the detail. PixelHeight + theme colors keep the type
            // scale consistent with the post-bake UI.
            _progressStageText = CreateUnityText(
                "StageText",
                _progressRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)StagePixelHeight),
                BodyTextColor,
                TextAnchor.MiddleLeft);
            _progressStageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _progressStageText.verticalOverflow = VerticalWrapMode.Truncate;
            _progressStageText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _progressStageText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _progressStageText.rectTransform.pivot = new Vector2(0f, 1f);
            _progressStageText.rectTransform.anchoredPosition = new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(52f));
            _progressStageText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(22f));

            _progressDetailText = CreateUnityText(
                "DetailText",
                _progressRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                SubtleTextColor,
                TextAnchor.MiddleLeft);
            _progressDetailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _progressDetailText.verticalOverflow = VerticalWrapMode.Truncate;
            _progressDetailText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _progressDetailText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _progressDetailText.rectTransform.pivot = new Vector2(0f, 1f);
            _progressDetailText.rectTransform.anchoredPosition = new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(80f));
            _progressDetailText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(20f));

            // Slimmer, taller progress bar with an MW_Box thin frame and a tinted cyan
            // fill. Uses the same LoadingBarFillSprite that post-bake bars pick up, so
            // the visual feel carries into the Loading phase.
            var progressRect = RuntimeUiFactory.CreateAnchoredRect(
                "ProgressBarRect",
                _progressRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(118f)),
                new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(14f)));
            progressRect.pivot = new Vector2(0f, 1f);

            _progressBar = RuntimeUiFactory.CreateProgressBar(
                "ProgressBar",
                progressRect,
                _theme,
                ProgressBarCenterColor,
                ProgressBarFillColor);
            RuntimeUiFactory.Stretch(_progressBar.Root);

            _progressFooterText = CreateUnityText(
                "FooterText",
                _progressRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)FooterPixelHeight),
                SubtleTextColor,
                TextAnchor.LowerLeft);
            _progressFooterText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _progressFooterText.verticalOverflow = VerticalWrapMode.Truncate;
            _progressFooterText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _progressFooterText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _progressFooterText.rectTransform.pivot = new Vector2(0f, 0f);
            _progressFooterText.rectTransform.anchoredPosition = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(6f));
            _progressFooterText.rectTransform.sizeDelta = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(36f));
        }

        void BuildErrorView()
        {
            _errorRoot = RuntimeUiFactory.CreateStretchRect("ErrorRoot", _window.Client);
            _errorRoot.gameObject.SetActive(false);

            _errorBodyText = CreateUnityText(
                "ErrorBodyText",
                _errorRoot,
                string.Empty,
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                ErrorTextColor,
                TextAnchor.MiddleLeft);
            _errorBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _errorBodyText.verticalOverflow = VerticalWrapMode.Truncate;
            RuntimeUiFactory.Stretch(_errorBodyText.rectTransform);
        }

        void EnsureInitialized()
        {
            if (_theme == null)
                throw new InvalidOperationException("BootstrapFallbackView.Initialize must be called before showing the fallback UI.");
        }

        void ShowRoot(string title, float height)
        {
            _rootRect.gameObject.SetActive(true);
            _windowTitleText.text = string.IsNullOrWhiteSpace(title) ? "VVardenfell" : title.Trim();
            Vector2 scaledSize = RuntimeUiScaleSettings.ScalePixels(new Vector2(WindowWidth, height));
            _window.Root.sizeDelta = scaledSize;
            _window.Root.anchoredPosition = Vector2.zero;
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float maxScaleX = (screenWidth - 48f) / Mathf.Max(1f, scaledSize.x);
            float maxScaleY = (screenHeight - 48f) / Mathf.Max(1f, scaledSize.y);
            float resolvedScale = Mathf.Min(DialogVisualScale, Mathf.Max(0.5f, Mathf.Min(maxScaleX, maxScaleY)));
            _window.Root.localScale = Vector3.one * resolvedScale;
        }

        Text CreateUnityText(string name, Transform parent, string text, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var result = go.GetComponent<Text>();
            result.font = _uiFont;
            result.fontSize = Mathf.Max(1, fontSize);
            result.text = text ?? string.Empty;
            result.color = color;
            result.alignment = alignment;
            result.supportRichText = false;
            result.raycastTarget = false;
            return result;
        }

        void ReplaceButtonLabel(MorrowindButtonView button, string label)
        {
            if (button?.Label != null)
                button.Label.gameObject.SetActive(false);

            var text = CreateUnityText(
                "UnityLabel",
                button.Root,
                label,
                RuntimeUiScaleSettings.ScaleFontSize((int)BodyPixelHeight),
                BodyTextColor,
                TextAnchor.MiddleCenter);
            RuntimeUiFactory.Stretch(text.rectTransform);
        }

        void SetInputDisplay(string value, string placeholder)
        {
            if (_pathInput == null)
                return;

            if (_pathInput.InputField != null && _pathInput.InputField.text != value)
                _pathInput.InputField.SetTextWithoutNotify(value);

            if (_pathInput.HiddenPlaceholder != null)
                _pathInput.HiddenPlaceholder.text = placeholder ?? string.Empty;
        }

        void SetBattlegroundFilterDisplay(string value)
        {
            if (_battlegroundFilterInput == null)
                return;

            value ??= string.Empty;
            if (_battlegroundFilterInput.InputField != null && _battlegroundFilterInput.InputField.text != value)
                _battlegroundFilterInput.InputField.SetTextWithoutNotify(value);

            if (_battlegroundFilterInput.HiddenPlaceholder != null)
                _battlegroundFilterInput.HiddenPlaceholder.text = "Search cells, e.g. -2,-9";
        }

        void SyncBattlegroundRows()
        {
            string filter = (_battlegroundFilter ?? string.Empty).Trim();
            int matched = 0;
            int shown = 0;
            int pageStart = Mathf.Max(0, _battlegroundPage) * BattlegroundVisibleRows;

            for (int i = 0; i < _battlegroundCells.Length; i++)
            {
                var cell = _battlegroundCells[i];
                if (!MatchesBattlegroundFilter(cell, filter))
                    continue;

                int matchIndex = matched;
                matched++;
                if (matchIndex < pageStart)
                    continue;

                if (shown >= BattlegroundVisibleRows)
                    continue;

                _visibleBattlegroundCells[shown] = new int2(cell.X, cell.Y);
                _battlegroundButtons[shown].Root.gameObject.SetActive(true);
                _battlegroundButtonLabels[shown].text = $"Exterior Cell ({cell.X}, {cell.Y})";
                shown++;
            }

            for (int i = shown; i < BattlegroundVisibleRows; i++)
            {
                _visibleBattlegroundCells[i] = default;
                _battlegroundButtons[i].Root.gameObject.SetActive(false);
                _battlegroundButtonLabels[i].text = string.Empty;
            }

            _battlegroundCountText.text = matched == _battlegroundCells.Length
                ? $"{matched} exterior cells baked - page {ResolveBattlegroundDisplayPage(matched)}"
                : $"{matched} matching exterior cells - page {ResolveBattlegroundDisplayPage(matched)}";

            bool hasMatches = matched > 0;
            int maxPage = ResolveBattlegroundMaxPage(matched);
            if (_battlegroundPage > maxPage)
            {
                _battlegroundPage = maxPage;
                SyncBattlegroundRows();
                return;
            }

            _battlegroundPrevButton.Root.gameObject.SetActive(hasMatches && _battlegroundPage > 0);
            _battlegroundNextButton.Root.gameObject.SetActive(hasMatches && _battlegroundPage < maxPage);
            _battlegroundErrorText.text = hasMatches ? string.Empty : "No baked exterior cells match that search.";
            _battlegroundErrorText.gameObject.SetActive(!hasMatches);
        }

        string ResolveBattlegroundDisplayPage(int matched)
        {
            int maxPage = ResolveBattlegroundMaxPage(matched);
            return $"{Mathf.Min(_battlegroundPage, maxPage) + 1}/{maxPage + 1}";
        }

        static int ResolveBattlegroundMaxPage(int matched)
        {
            return Mathf.Max(0, (matched - 1) / BattlegroundVisibleRows);
        }

        static bool MatchesBattlegroundFilter((int X, int Y) cell, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string compact = filter.Replace(" ", string.Empty);
            string cellCompact = $"{cell.X},{cell.Y}";
            string cellWithParens = $"({cell.X},{cell.Y})";
            string cellSpaced = $"{cell.X}, {cell.Y}";
            return cellCompact.IndexOf(compact, StringComparison.OrdinalIgnoreCase) >= 0
                   || cellWithParens.IndexOf(compact, StringComparison.OrdinalIgnoreCase) >= 0
                   || cellSpaced.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void OnPathChanged(string value)
        {
            _onPathChanged?.Invoke(value ?? string.Empty);
        }

        void OnBattlegroundFilterChanged(string value)
        {
            _battlegroundFilter = value ?? string.Empty;
            _battlegroundPage = 0;
            _onBattlegroundFilterChanged?.Invoke(_battlegroundFilter);
            SyncBattlegroundRows();
        }

        void OnBattlegroundPreviousPagePressed()
        {
            _battlegroundPage = Mathf.Max(0, _battlegroundPage - 1);
            SyncBattlegroundRows();
        }

        void OnBattlegroundNextPagePressed()
        {
            _battlegroundPage++;
            SyncBattlegroundRows();
        }

        void OnBattlegroundRowPressed(int index)
        {
            if ((uint)index >= (uint)BattlegroundVisibleRows)
                return;

            _onBattlegroundSelected?.Invoke(_visibleBattlegroundCells[index]);
        }

        void OnContinuePressed()
        {
            _onContinue?.Invoke();
        }

        void OnBrowsePressed()
        {
            _onBrowse?.Invoke();
        }

        void OnVanillaPressed()
        {
            _onModeSelected?.Invoke(BootstrapRuntimeMode.Vanilla);
        }

        void OnSandboxPressed()
        {
            _onModeSelected?.Invoke(BootstrapRuntimeMode.Sandbox);
        }

        void OnProjectTamrielPressed()
        {
            _onModeSelected?.Invoke(BootstrapRuntimeMode.ProjectTamriel);
        }

        void OnCombatSandboxPressed()
        {
            _onModeSelected?.Invoke(BootstrapRuntimeMode.CombatSandbox);
        }

        void OnGUI()
        {
            if (!_showBattlegroundImgui)
                return;

            EnsureBattlegroundImguiStyles();
            DrawBattlegroundImgui();
        }

        void DrawBattlegroundImgui()
        {
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            float width = Mathf.Min(760f, Mathf.Max(320f, Screen.width - 32f));
            float height = Mathf.Min(620f, Mathf.Max(320f, Screen.height - 32f));
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(rect, _imguiWindowStyle);
            GUILayout.Label("Combat Sandbox - Battleground", _imguiHeaderStyle);
            GUILayout.Label("Choose an exterior cell for the battle simulator.", _imguiLabelStyle);
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _imguiLabelStyle, GUILayout.Width(56f));
            string nextFilter = GUILayout.TextField(_battlegroundFilter ?? string.Empty, _imguiTextFieldStyle, GUILayout.Height(26f));
            if (!string.Equals(nextFilter, _battlegroundFilter, StringComparison.Ordinal))
            {
                _battlegroundFilter = nextFilter ?? string.Empty;
                _battlegroundImguiScroll = Vector2.zero;
                _onBattlegroundFilterChanged?.Invoke(_battlegroundFilter);
            }
            GUILayout.EndHorizontal();

            string filter = (_battlegroundFilter ?? string.Empty).Trim();
            int matched = CountBattlegroundMatches(filter);
            GUILayout.Label($"{matched} matching exterior cells", _imguiDimStyle);

            if (matched == 0)
            {
                GUILayout.Label("No baked exterior cells match that search.", _imguiErrorStyle);
                GUILayout.EndArea();
                return;
            }

            _battlegroundImguiScroll = GUILayout.BeginScrollView(_battlegroundImguiScroll, false, true, GUILayout.Height(height - 140f));
            for (int i = 0; i < _battlegroundCells.Length; i++)
            {
                var cell = _battlegroundCells[i];
                if (!MatchesBattlegroundFilter(cell, filter))
                    continue;

                if (GUILayout.Button($"Exterior Cell ({cell.X}, {cell.Y})", _imguiButtonStyle, GUILayout.Height(28f)))
                    _onBattlegroundSelected?.Invoke(new int2(cell.X, cell.Y));
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        int CountBattlegroundMatches(string filter)
        {
            int matched = 0;
            for (int i = 0; i < _battlegroundCells.Length; i++)
            {
                if (MatchesBattlegroundFilter(_battlegroundCells[i], filter))
                    matched++;
            }

            return matched;
        }

        void EnsureBattlegroundImguiStyles()
        {
            if (_imguiWindowStyle != null)
                return;

            Texture2D windowTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            windowTexture.SetPixel(0, 0, new Color(0.11f, 0.09f, 0.07f, 0.98f));
            windowTexture.Apply();

            _imguiWindowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(16, 16, 14, 16),
                normal = { background = windowTexture },
            };
            _imguiHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = BodyTextColor },
            };
            _imguiLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = BodyTextColor },
            };
            _imguiDimStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = SubtleTextColor },
            };
            _imguiErrorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = ErrorTextColor },
            };
            _imguiButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                normal = { textColor = BodyTextColor },
            };
            _imguiTextFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 13,
                normal = { textColor = BodyTextColor },
            };
        }

        void OnDestroy()
        {
            _theme?.Dispose();
            _theme = null;
        }
    }
}
