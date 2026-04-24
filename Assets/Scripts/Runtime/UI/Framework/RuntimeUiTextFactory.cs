using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    public static partial class RuntimeUiFactory
    {
        public static BitmapTextGraphic CreateBitmapText(
            string name,
            Transform parent,
            BitmapFontAsset font,
            float scale,
            Color color,
            BitmapTextAlignment alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(BitmapTextGraphic));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<BitmapTextGraphic>();
            text.Font = font;
            text.FontScale = RuntimeUiScaleSettings.ScaleFont(scale);
            text.color = color;
            text.Alignment = alignment;
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.raycastTarget = false;
            return text;
        }

        public static RuntimeUiTextInputView CreateBitmapInputField(
            string name,
            Transform parent,
            RuntimeUiTheme theme,
            float textScale,
            Color textColor,
            Color frameColor,
            string placeholder,
            float horizontalInset = 6f,
            float verticalInset = 4f,
            int hiddenFontSize = 16)
        {
            var root = CreateStretchRect(name, parent);
            var frame = CreateBorderFrame("Frame", root, ResolveThinFrame(theme), frameColor);
            Stretch(frame.Root);

            var inputRoot = CreateStretchRect("InputRoot", frame.Client);
            var inputImage = inputRoot.gameObject.AddComponent<Image>();
            inputImage.color = new Color(1f, 1f, 1f, 0f);
            inputImage.raycastTarget = true;

            var inputField = inputRoot.gameObject.AddComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.contentType = InputField.ContentType.Standard;
            inputField.targetGraphic = inputImage;

            Font hiddenFont = GetHiddenInputFont();

            var hiddenTextGo = new GameObject("HiddenText", typeof(RectTransform), typeof(Text));
            hiddenTextGo.transform.SetParent(inputRoot, false);
            var hiddenText = hiddenTextGo.GetComponent<Text>();
            hiddenText.font = hiddenFont;
            hiddenText.fontSize = RuntimeUiScaleSettings.ScaleFontSize(hiddenFontSize);
            hiddenText.supportRichText = false;
            hiddenText.alignment = TextAnchor.MiddleLeft;
            hiddenText.color = new Color(1f, 1f, 1f, 0f);
            SetInset((RectTransform)hiddenText.transform, RuntimeUiScaleSettings.ScalePixels(horizontalInset), 0f, RuntimeUiScaleSettings.ScalePixels(-horizontalInset), 0f);

            var hiddenPlaceholderGo = new GameObject("HiddenPlaceholder", typeof(RectTransform), typeof(Text));
            hiddenPlaceholderGo.transform.SetParent(inputRoot, false);
            var hiddenPlaceholder = hiddenPlaceholderGo.GetComponent<Text>();
            hiddenPlaceholder.font = hiddenFont;
            hiddenPlaceholder.fontSize = RuntimeUiScaleSettings.ScaleFontSize(hiddenFontSize);
            hiddenPlaceholder.supportRichText = false;
            hiddenPlaceholder.alignment = TextAnchor.MiddleLeft;
            hiddenPlaceholder.color = new Color(1f, 1f, 1f, 0f);
            hiddenPlaceholder.text = placeholder ?? string.Empty;
            SetInset((RectTransform)hiddenPlaceholder.transform, RuntimeUiScaleSettings.ScalePixels(horizontalInset), 0f, RuntimeUiScaleSettings.ScalePixels(-horizontalInset), 0f);

            inputField.textComponent = hiddenText;
            inputField.placeholder = hiddenPlaceholder;
            inputField.customCaretColor = true;
            inputField.caretColor = new Color(0.94f, 0.82f, 0.53f, 1f);
            inputField.selectionColor = new Color(0.38f, 0.28f, 0.12f, 0.72f);

            var overlay = CreateBitmapText(
                "OverlayText",
                inputRoot,
                theme?.DefaultFont,
                textScale,
                textColor,
                BitmapTextAlignment.Left);
            SetInsetText(
                overlay.rectTransform,
                overlay,
                horizontalInset,
                verticalInset,
                -horizontalInset,
                -verticalInset,
                BitmapTextVerticalAlignment.Middle);

            return new RuntimeUiTextInputView
            {
                Root = root,
                Frame = frame,
                InputRoot = inputRoot,
                InputField = inputField,
                HiddenText = hiddenText,
                HiddenPlaceholder = hiddenPlaceholder,
                OverlayText = overlay,
            };
        }

        public static void SetBitmapInputDisplay(
            RuntimeUiTextInputView view,
            string value,
            string placeholder,
            Color textColor,
            Color placeholderColor)
        {
            if (view == null)
                return;

            value ??= string.Empty;
            if (view.InputField != null && view.InputField.text != value)
                view.InputField.SetTextWithoutNotify(value);

            if (view.HiddenPlaceholder != null)
                view.HiddenPlaceholder.text = placeholder ?? string.Empty;

            if (view.OverlayText == null)
                return;

            bool empty = string.IsNullOrEmpty(value);
            view.OverlayText.Text = empty ? placeholder ?? string.Empty : value;
            view.OverlayText.color = empty ? placeholderColor : textColor;
        }

        public static float MeasureLineWidth(BitmapFontAsset font, string text, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0f;

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (font.TryGetGlyph(text[i], out var glyph))
                    width += glyph.Advance * scale;
            }

            return width;
        }

        public static void SetInsetText(
            RectTransform rect,
            BitmapTextGraphic text,
            float left,
            float bottom,
            float right,
            float top,
            BitmapTextVerticalAlignment verticalAlignment = BitmapTextVerticalAlignment.Middle)
        {
            SetInset(
                rect,
                RuntimeUiScaleSettings.ScalePixels(left),
                RuntimeUiScaleSettings.ScalePixels(bottom),
                RuntimeUiScaleSettings.ScalePixels(right),
                RuntimeUiScaleSettings.ScalePixels(top));

            if (text != null)
                text.VerticalAlignment = verticalAlignment;
        }

        public static void CenterSingleLineText(
            RectTransform rect,
            BitmapTextGraphic text,
            float horizontalInset,
            float lineHeightMultiplier = 1.6f,
            float verticalNudge = 0f)
        {
            float inset = RuntimeUiScaleSettings.ScalePixels(horizontalInset);
            float minimumHeight = RuntimeUiScaleSettings.ScalePixels(18f);
            float lineHeight = minimumHeight;
            if (text?.Font != null)
                lineHeight = Mathf.Max(minimumHeight, text.Font.LineHeight * text.FontScale * lineHeightMultiplier);

            if (text != null)
                text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, RuntimeUiScaleSettings.ScalePixels(verticalNudge));
            rect.sizeDelta = new Vector2(-inset * 2f, lineHeight);
        }

        static Font GetHiddenInputFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
                return font;

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
