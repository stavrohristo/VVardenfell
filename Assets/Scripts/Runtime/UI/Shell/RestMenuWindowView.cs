using System;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class RestMenuWindowView
    {
        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color DimTextColor = new(0.58f, 0.54f, 0.48f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color SliderTrackCenterColor = new(0f, 0f, 0f, 0.45f);
        static readonly Color SliderFillColor = new(0.68f, 0.55f, 0.22f, 0.96f);
        static readonly Color SliderHandleCenterColor = new(0.18f, 0.14f, 0.09f, 0.95f);

        const float WindowWidth = 420f;
        const float WindowHeight = 230f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;
        const float ButtonWidth = 110f;
        const float ButtonHeight = 24f;
        const float SliderHandleWidth = 12f;
        const float SliderHandleHeight = 22f;

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _root;
        readonly RectTransform _controlsRoot;
        readonly RectTransform _progressRoot;
        readonly BitmapTextGraphic _dateText;
        readonly BitmapTextGraphic _timeText;
        readonly BitmapTextGraphic _hoursText;
        readonly BitmapTextGraphic _progressText;
        readonly Slider _hoursSlider;
        readonly RuntimeUiProgressBarView _progressBar;
        readonly MorrowindButtonView _startButton;
        readonly MorrowindButtonView _untilHealedButton;
        readonly MorrowindButtonView _cancelButton;

        public RestMenuWindowView(Transform parent, RuntimeUiTheme theme)
        {
            _theme = theme;
            _root = RuntimeUiFactory.CreateStretchRect("RestMenu", parent);
            _root.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("Backdrop", _root, BackdropColor);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            var holder = RuntimeUiFactory.CreateAnchoredRect(
                "DialogHolder",
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(WindowWidth, WindowHeight)));
            holder.pivot = new Vector2(0.5f, 0.5f);

            var window = RuntimeUiFactory.CreateMorrowindWindow(
                "RestWindow",
                holder,
                theme,
                "Rest",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.92f,
                RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Caption),
                new Color(0.94f, 0.82f, 0.53f));
            RuntimeUiFactory.Stretch(window.Root);

            _dateText = CreateText("Date", window.Client, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RuntimeClassicUiMetrics.Ui(4f)), new Vector2(0f, RuntimeClassicUiMetrics.Ui(24f)), BodyTextColor, BitmapTextAlignment.Center);
            _timeText = CreateText("Time", window.Client, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RuntimeClassicUiMetrics.Ui(30f)), new Vector2(0f, RuntimeClassicUiMetrics.Ui(22f)), SubtleTextColor, BitmapTextAlignment.Center);

            _controlsRoot = RuntimeUiFactory.CreateStretchRect("Controls", window.Client);
            _progressRoot = RuntimeUiFactory.CreateStretchRect("Progress", window.Client);

            _hoursText = CreateText("HoursText", _controlsRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RuntimeClassicUiMetrics.Ui(62f)), new Vector2(0f, RuntimeClassicUiMetrics.Ui(22f)), BodyTextColor, BitmapTextAlignment.Center);
            _hoursSlider = BuildHoursSlider(_controlsRoot);
            _hoursSlider.onValueChanged.AddListener(value => RuntimeShellRequestBridge.TrySetRestMenuHours(Mathf.RoundToInt(value), out _));

            _startButton = BuildButton(_controlsRoot, "StartButton", "Rest", new Vector2(0f, 0f), RuntimeClassicUiMetrics.Ui(new Vector2(18f, 12f)), () => RuntimeShellRequestBridge.TryStartRestMenu(out _));
            _untilHealedButton = BuildButton(_controlsRoot, "UntilHealedButton", "Until Healed", new Vector2(0.5f, 0f), RuntimeClassicUiMetrics.Ui(new Vector2(-ButtonWidth * 0.5f, 12f)), () => RuntimeShellRequestBridge.TryStartRestUntilHealed(out _));
            _cancelButton = BuildButton(_controlsRoot, "CancelButton", "Cancel", new Vector2(1f, 0f), RuntimeClassicUiMetrics.Ui(new Vector2(-(ButtonWidth + 18f), 12f)), () => RuntimeShellRequestBridge.TryCancelRestMenu(out _));

            _progressText = CreateText("ProgressText", _progressRoot, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, RuntimeClassicUiMetrics.Ui(28f)), BodyTextColor, BitmapTextAlignment.Center);
            _progressBar = RuntimeUiFactory.CreateProgressBar(
                "ProgressBar",
                _progressRoot,
                theme,
                SliderTrackCenterColor,
                SliderFillColor);
            _progressBar.Root.anchorMin = new Vector2(0f, 0.5f);
            _progressBar.Root.anchorMax = new Vector2(1f, 0.5f);
            _progressBar.Root.pivot = new Vector2(0.5f, 0.5f);
            _progressBar.Root.anchoredPosition = new Vector2(0f, -RuntimeClassicUiMetrics.Ui(24f));
            _progressBar.Root.sizeDelta = new Vector2(-RuntimeClassicUiMetrics.Ui(64f), RuntimeClassicUiMetrics.Ui(18f));
        }

        public RectTransform Root => _root;

        public void Sync(RestMenuViewModel model)
        {
            bool visible = model != null;
            _root.gameObject.SetActive(visible);
            if (!visible)
                return;

            _dateText.Text = model.DateText ?? string.Empty;
            _timeText.Text = model.TimeText ?? string.Empty;
            _hoursText.Text = model.HoursText ?? string.Empty;
            _hoursSlider.SetValueWithoutNotify(Mathf.Clamp(model.SelectedHours, 1, 24));
            _startButton.Label.Text = model.CanSleep ? "Rest" : "Wait";
            SetButtonEnabled(_untilHealedButton, model.CanUntilHealed && !model.Advancing);
            SetButtonEnabled(_startButton, !model.Advancing);
            SetButtonEnabled(_cancelButton, !model.Advancing);

            _controlsRoot.gameObject.SetActive(!model.Advancing);
            _progressRoot.gameObject.SetActive(model.Advancing);
            if (model.Advancing)
            {
                _progressText.Text = model.ProgressText ?? string.Empty;
                float fraction = model.TargetHours > 0 ? (float)model.ProgressHours / model.TargetHours : 0f;
                RuntimeUiFactory.SetProgressBarFill(_progressBar, fraction);
            }
        }

        BitmapTextGraphic CreateText(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Color color,
            BitmapTextAlignment alignment)
        {
            var text = RuntimeUiFactory.CreateBitmapText(name, parent, _theme?.DefaultFont, 1f, color, alignment);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = anchorMin;
            text.rectTransform.anchorMax = anchorMax;
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.anchoredPosition = anchoredPosition;
            text.rectTransform.sizeDelta = sizeDelta;
            return text;
        }

        Slider BuildHoursSlider(Transform parent)
        {
            var sliderRect = RuntimeUiFactory.CreateAnchorRect(
                "HoursSlider",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(RuntimeClassicUiMetrics.Ui(48f), -RuntimeClassicUiMetrics.Ui(104f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(48f), -RuntimeClassicUiMetrics.Ui(78f)));

            var trackFrame = RuntimeUiFactory.CreateBorderFrame("Track", sliderRect, RuntimeUiFactory.ResolveThinFrame(_theme), SliderTrackCenterColor);
            RuntimeUiFactory.Stretch(trackFrame.Root);

            var fillArea = RuntimeUiFactory.CreateAnchorRect(
                "Fill Area",
                sliderRect,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(2f), -RuntimeClassicUiMetrics.Ui(4f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(2f), RuntimeClassicUiMetrics.Ui(4f)));
            var fillHolder = RuntimeUiFactory.CreateAnchoredRect("Fill", fillArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fillHolder.pivot = new Vector2(0f, 0.5f);
            var fill = RuntimeUiFactory.CreateImage("FillImage", fillHolder, SliderFillColor);
            fill.sprite = _theme?.LoadingBarFillSprite;
            fill.raycastTarget = false;
            RuntimeUiFactory.Stretch(fill.rectTransform);

            var handleArea = RuntimeUiFactory.CreateAnchorRect(
                "Handle Slide Area",
                sliderRect,
                Vector2.zero,
                Vector2.one,
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
            var handleFrame = RuntimeUiFactory.CreateBorderFrame("HandleFrame", handleRect, RuntimeUiFactory.ResolveThinFrame(_theme), SliderHandleCenterColor);
            RuntimeUiFactory.Stretch(handleFrame.Root);
            handleFrame.Center.raycastTarget = true;

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.targetGraphic = handleFrame.Center;
            slider.fillRect = fillHolder;
            slider.handleRect = handleRect;
            slider.minValue = 1f;
            slider.maxValue = 24f;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(1f);
            return slider;
        }

        MorrowindButtonView BuildButton(Transform parent, string name, string label, Vector2 anchor, Vector2 offset, UnityEngine.Events.UnityAction onClick)
        {
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name + "Rect",
                parent,
                anchor,
                anchor,
                offset,
                RuntimeClassicUiMetrics.Ui(new Vector2(ButtonWidth, ButtonHeight)));
            rect.pivot = new Vector2(0f, 0f);

            var button = RuntimeUiFactory.CreateMorrowindButton("Button", rect, _theme, label, 1f, BodyTextColor, ButtonCenterColor);
            RuntimeUiFactory.Stretch(button.Root);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            if (onClick != null)
                button.Button.onClick.AddListener(onClick);
            return button;
        }

        static void SetButtonEnabled(MorrowindButtonView view, bool enabled)
        {
            view.Button.interactable = enabled;
            view.Label.color = enabled ? BodyTextColor : DimTextColor;
        }
    }
}
