using System;
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
        const float ModePickerHeight = 294f;
        const float ProgressHeight = 232f;
        const float ErrorHeight = 204f;
        const float DialogVisualScale = 1.5f;

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
        RectTransform _progressRoot;
        RectTransform _errorRoot;
        RuntimeUiTextInputView _pathInput;
        MorrowindButtonView _continueButton;
        MorrowindButtonView _browseButton;
        MorrowindButtonView _vanillaButton;
        MorrowindButtonView _sandboxButton;
        Text _pathPromptText;
        Text _pathErrorText;
        Text _modePromptText;
        Text _modeInstallPathText;
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
            BuildProgressView();
            BuildErrorView();
            Hide();
        }

        public void ShowPathPicker(string path, string error)
        {
            EnsureInitialized();
            ShowRoot("VVardenfell - Locate Morrowind Installation", PickerHeight);
            _pickerRoot.gameObject.SetActive(true);
            _modeRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(false);
            SetInputDisplay(path ?? string.Empty, "Path to Morrowind");
            _pathErrorText.text = string.IsNullOrWhiteSpace(error) ? string.Empty : error.Trim();
            _pathErrorText.gameObject.SetActive(!string.IsNullOrWhiteSpace(error));
            _browseButton.Root.gameObject.SetActive(_onBrowse != null);
        }

        public void ShowModePicker(string installPath, Action<BootstrapRuntimeMode> onModeSelected)
        {
            EnsureInitialized();
            _onModeSelected = onModeSelected;
            ShowRoot("VVardenfell - Select Startup Mode", ModePickerHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(true);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(false);
            _modeInstallPathText.text = string.IsNullOrWhiteSpace(installPath)
                ? string.Empty
                : $"Install: {installPath.Trim()}";
        }

        public void ShowProgress(string title, string stage, string detail, int current, int total, float fraction, string footer)
        {
            EnsureInitialized();
            ShowRoot(title, ProgressHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(false);
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
            ShowRoot(title, ErrorHeight);
            _pickerRoot.gameObject.SetActive(false);
            _modeRoot.gameObject.SetActive(false);
            _progressRoot.gameObject.SetActive(false);
            _errorRoot.gameObject.SetActive(true);
            _errorBodyText.text = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        }

        public void Hide()
        {
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

            var sandboxRect = RuntimeUiFactory.CreateAnchoredRect(
                "SandboxButtonRow",
                _modeRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeUiScaleSettings.ScalePixels(132f)),
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
            _window.Root.sizeDelta = RuntimeUiScaleSettings.ScalePixels(new Vector2(WindowWidth, height));
            _window.Root.anchoredPosition = Vector2.zero;
            _window.Root.localScale = Vector3.one * DialogVisualScale;
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

        void OnPathChanged(string value)
        {
            _onPathChanged?.Invoke(value ?? string.Empty);
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

        void OnDestroy()
        {
            _theme?.Dispose();
            _theme = null;
        }
    }
}
