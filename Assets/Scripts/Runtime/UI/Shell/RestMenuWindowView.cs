using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class RestMenuWindowView
    {
        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color DimTextColor = new(0.58f, 0.54f, 0.48f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color SliderTrackCenterColor = new(0f, 0f, 0f, 0.45f);
        static readonly Color SliderFillColor = new(0.68f, 0.55f, 0.22f, 0.96f);
        static readonly Color SliderHandleCenterColor = new(0.18f, 0.14f, 0.09f, 0.95f);

        const float DialogWidth = 600f;
        const float DialogHeight = 200f;
        const float ProgressDialogWidth = 219f;
        const float ProgressDialogHeight = 40f;
        const float DialogInset = 8f;
        const float ButtonHeight = 24f;
        const float UntilHealedButtonWidth = 116f;
        const float StartButtonWidth = 66f;
        const float CancelButtonWidth = 78f;
        const float ButtonSpacing = 8f;
        const float SliderHandleWidth = 12f;
        const float SliderHandleHeight = 22f;

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _root;
        readonly RectTransform _dialogRoot;
        readonly RectTransform _progressDialogRoot;
        readonly RectTransform _startButtonRect;
        readonly RectTransform _untilHealedButtonRect;
        readonly RectTransform _cancelButtonRect;
        readonly BitmapTextGraphic _dateTimeText;
        readonly BitmapTextGraphic _restText;
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

            var dialog = CreateDialog("WaitDialog", DialogWidth, DialogHeight, DialogInset);
            _dialogRoot = dialog.root;
            RectTransform dialogClient = dialog.client;
            _dateTimeText = CreateText("DateTimeText", dialogClient, 10f, 4f, 580f, 22f, BodyTextColor, BitmapTextAlignment.Left);
            _restText = CreateText("RestText", dialogClient, 10f, 38f, 580f, 22f, BodyTextColor, BitmapTextAlignment.Left);
            _hoursText = CreateText("HourText", dialogClient, 10f, 72f, 580f, 22f, BodyTextColor, BitmapTextAlignment.Left);

            _hoursSlider = BuildHoursSlider(dialogClient);
            _hoursSlider.onValueChanged.AddListener(value => RuntimeShellRequestBridge.TrySetRestMenuHours(Mathf.RoundToInt(value), out _));

            _untilHealedButton = BuildButton(dialogClient, "UntilHealedButton", "Until Healed", UntilHealedButtonWidth, out _untilHealedButtonRect, () => RuntimeShellRequestBridge.TryStartRestUntilHealed(out _));
            _startButton = BuildButton(dialogClient, "StartButton", "Rest", StartButtonWidth, out _startButtonRect, () => RuntimeShellRequestBridge.TryStartRestMenu(out _));
            _cancelButton = BuildButton(dialogClient, "CancelButton", "Cancel", CancelButtonWidth, out _cancelButtonRect, () => RuntimeShellRequestBridge.TryCancelRestMenu(out _));

            var progressDialog = CreateDialog("WaitProgressDialog", ProgressDialogWidth, ProgressDialogHeight, 0f);
            _progressDialogRoot = progressDialog.root;
            RectTransform progressClient = progressDialog.client;
            _progressBar = RuntimeUiFactory.CreateProgressBar(
                "ProgressBar",
                progressClient,
                theme,
                SliderTrackCenterColor,
                SliderFillColor);
            _progressBar.Root.anchorMin = new Vector2(0f, 1f);
            _progressBar.Root.anchorMax = new Vector2(0f, 1f);
            _progressBar.Root.pivot = new Vector2(0f, 1f);
            _progressBar.Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(5f, -6f));
            _progressBar.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(199f, 20f));

            _progressText = RuntimeUiFactory.CreateBitmapText("ProgressText", _progressBar.Root, _theme?.DefaultFont, 1f, BodyTextColor, BitmapTextAlignment.Right);
            _progressText.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _progressText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _progressText.raycastTarget = false;
            _progressText.rectTransform.anchorMin = Vector2.zero;
            _progressText.rectTransform.anchorMax = Vector2.one;
            _progressText.rectTransform.offsetMin = Vector2.zero;
            _progressText.rectTransform.offsetMax = new Vector2(-RuntimeClassicUiMetrics.Ui(6f), 0f);
        }

        public RectTransform Root => _root;

        public void Sync(RestMenuViewModel model)
        {
            bool visible = model != null;
            _root.gameObject.SetActive(visible);
            if (!visible)
                return;

            _dateTimeText.Text = model.DateTimeText ?? string.Empty;
            _restText.Text = model.RestText ?? string.Empty;
            _hoursText.Text = model.HoursText ?? string.Empty;
            _hoursSlider.SetValueWithoutNotify(Mathf.Clamp(model.SelectedHours, 1, 24));
            _startButton.Label.Text = model.CanSleep ? "Rest" : "Wait";

            bool showUntilHealed = model.CanUntilHealed && !model.Advancing;
            _untilHealedButtonRect.gameObject.SetActive(showUntilHealed);
            SetButtonEnabled(_untilHealedButton, showUntilHealed);
            SetButtonEnabled(_startButton, !model.Advancing);
            SetButtonEnabled(_cancelButton, !model.Advancing);
            LayoutButtons(showUntilHealed);

            _dialogRoot.gameObject.SetActive(!model.Advancing);
            _progressDialogRoot.gameObject.SetActive(model.Advancing);
            if (model.Advancing)
            {
                _progressText.Text = $"{model.ProgressHours}/{model.TargetHours}";
                SetProgressBarFill(model.ProgressFraction);
            }
        }

        (RectTransform root, RectTransform client) CreateDialog(string name, float width, float height, float clientInset)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            root.pivot = new Vector2(0.5f, 0.5f);

            var background = RuntimeUiFactory.CreateImage("Background", root, new Color(0f, 0f, 0f, 0.92f));
            background.raycastTarget = true;
            RuntimeUiFactory.Stretch(background.rectTransform);

            var frame = RuntimeUiFactory.CreateBorderFrame("Border", root, RuntimeUiFactory.ResolveThickFrame(_theme), Color.clear);
            RuntimeUiFactory.Stretch(frame.Root);

            var client = RuntimeUiFactory.CreateAnchorRect(
                "Client",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(clientInset, clientInset)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-clientInset, -clientInset)));
            return (root, client);
        }

        BitmapTextGraphic CreateText(
            string name,
            Transform parent,
            float left,
            float top,
            float width,
            float height,
            Color color,
            BitmapTextAlignment alignment)
        {
            var text = RuntimeUiFactory.CreateBitmapText(name, parent, _theme?.DefaultFont, 1f, color, alignment);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(0f, 1f);
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(left, -top));
            text.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            return text;
        }

        Slider BuildHoursSlider(Transform parent)
        {
            var sliderRect = RuntimeUiFactory.CreateAnchoredRect(
                "HourSlider",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(RuntimeClassicUiMetrics.Ui(10f), -RuntimeClassicUiMetrics.Ui(108f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(20f), RuntimeClassicUiMetrics.Ui(18f)));
            sliderRect.pivot = new Vector2(0f, 1f);

            var trackFrame = RuntimeUiFactory.CreateBorderFrame("Track", sliderRect, RuntimeUiFactory.ResolveThinFrame(_theme), SliderTrackCenterColor);
            RuntimeUiFactory.Stretch(trackFrame.Root);
            var hitTarget = RuntimeUiFactory.CreateImage("TrackHitTarget", sliderRect, Color.clear);
            hitTarget.raycastTarget = true;
            RuntimeUiFactory.Stretch(hitTarget.rectTransform);
            hitTarget.rectTransform.SetAsFirstSibling();

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

            AddSliderMoveToClick(hitTarget.rectTransform, slider, handleArea);
            return slider;
        }

        MorrowindButtonView BuildButton(
            Transform parent,
            string name,
            string label,
            float width,
            out RectTransform rect,
            UnityEngine.Events.UnityAction onClick)
        {
            rect = RuntimeUiFactory.CreateAnchoredRect(
                name + "Rect",
                parent,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(width, ButtonHeight)));
            rect.pivot = new Vector2(1f, 0f);

            var button = RuntimeUiFactory.CreateMorrowindButton("Button", rect, _theme, label, 1f, BodyTextColor, ButtonCenterColor);
            RuntimeUiFactory.Stretch(button.Root);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            if (onClick != null)
                button.Button.onClick.AddListener(onClick);
            return button;
        }

        void LayoutButtons(bool showUntilHealed)
        {
            float right = RuntimeClassicUiMetrics.Ui(10f);
            float bottom = RuntimeClassicUiMetrics.Ui(8f);
            float spacing = RuntimeClassicUiMetrics.Ui(ButtonSpacing);

            PlaceButton(_cancelButtonRect, right, bottom);
            right += RuntimeClassicUiMetrics.Ui(CancelButtonWidth) + spacing;

            PlaceButton(_startButtonRect, right, bottom);
            right += RuntimeClassicUiMetrics.Ui(StartButtonWidth) + spacing;

            if (showUntilHealed)
                PlaceButton(_untilHealedButtonRect, right, bottom);
        }

        static void PlaceButton(RectTransform rect, float right, float bottom)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-right, bottom);
        }

        void SetProgressBarFill(float fraction)
        {
            float fillWidth = RuntimeClassicUiMetrics.Ui(199f) * Mathf.Clamp01(fraction);
            _progressBar.FillRect.sizeDelta = new Vector2(fillWidth, 0f);
        }

        static void AddSliderMoveToClick(RectTransform sliderRect, Slider slider, RectTransform trackRect)
        {
            var trigger = sliderRect.gameObject.AddComponent<EventTrigger>();
            var pointerDownEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerDown,
            };
            pointerDownEntry.callback.AddListener(eventData =>
            {
                if (eventData is PointerEventData pointerData)
                    SetSliderValueFromPointer(slider, trackRect, pointerData);
            });
            trigger.triggers.Add(pointerDownEntry);

            var dragEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.Drag,
            };
            dragEntry.callback.AddListener(eventData =>
            {
                if (eventData is PointerEventData pointerData)
                    SetSliderValueFromPointer(slider, trackRect, pointerData);
            });
            trigger.triggers.Add(dragEntry);
        }

        static void SetSliderValueFromPointer(Slider slider, RectTransform trackRect, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    trackRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rect = trackRect.rect;
            float fraction = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, Mathf.Clamp01(fraction));
        }

        static void SetButtonEnabled(MorrowindButtonView view, bool enabled)
        {
            view.Button.interactable = enabled;
            view.Label.color = enabled ? BodyTextColor : DimTextColor;
        }
    }
}
