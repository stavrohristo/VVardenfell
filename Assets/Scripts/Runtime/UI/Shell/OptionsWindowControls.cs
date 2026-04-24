using System;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed partial class OptionsWindowView
    {
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

            var trackFrame = RuntimeUiFactory.CreateBorderFrame(
                "Track",
                sliderRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SliderTrackCenterColor);
            RuntimeUiFactory.Stretch(trackFrame.Root);

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

            var handleFrame = RuntimeUiFactory.CreateBorderFrame(
                "HandleFrame",
                handleRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SliderHandleCenterColor);
            RuntimeUiFactory.Stretch(handleFrame.Root);
            handleFrame.Center.raycastTarget = true;

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
