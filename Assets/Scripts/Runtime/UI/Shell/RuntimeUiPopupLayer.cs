using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Top-level shell popup layer for tooltip/context surfaces. Mirrors OpenMW's
    /// approach: popup widgets never take mouse focus, and the layer follows the
    /// currently-hovered widget while staying clamped to the viewport.
    /// </summary>
    sealed class RuntimeUiPopupLayer
    {
        const float TooltipMaxWidth = 300f;
        const float TooltipPaddingX = 8f;
        const float TooltipPaddingY = 8f;
        const float TooltipCursorYOffset = 32f;
        const float TooltipCursorFallbackYOffset = 8f;
        const float TooltipTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float TooltipDelaySeconds = 0.35f;
        const float TooltipMinTextWidth = 24f;
        const float TooltipLineBottomBreathingRoom = 2f;

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _root;
        readonly RectTransform _tooltipRoot;
        readonly RectTransform _tooltipContent;
        readonly BitmapTextGraphic _tooltipText;
        readonly RuntimeTooltipBitmapTextLayoutElement _tooltipTextLayout;
        readonly float _frameHorizontalChrome;
        readonly float _frameVerticalChrome;
        readonly float _contentHorizontalPadding;

        RuntimeUiPopupTrigger _hovered;
        string _lastTooltipText;
        Vector2 _lastMousePosition;
        bool _hasLastMousePosition;
        float _remainingDelay = TooltipDelaySeconds;

        public RuntimeUiPopupLayer(RectTransform parent, RuntimeUiTheme theme)
        {
            _theme = theme;
            _root = RuntimeUiFactory.CreateStretchRect("PopupLayer", parent);
            _root.SetAsLastSibling();
            var group = _root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            _tooltipRoot = RuntimeUiFactory.CreateAnchoredRect(
                "Tooltip",
                _root,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero);
            _tooltipRoot.pivot = new Vector2(0f, 1f);
            _tooltipRoot.gameObject.SetActive(false);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                _tooltipRoot,
                RuntimeUiFactory.ResolveThinFrame(theme),
                new Color(0f, 0f, 0f, 0.96f));
            RuntimeUiFactory.Stretch(frame.Root);
            SetFrameRaycastTarget(frame, false);

            _frameHorizontalChrome = Mathf.Max(0f, frame.Client.offsetMin.x - frame.Client.offsetMax.x);
            _frameVerticalChrome = Mathf.Max(0f, frame.Client.offsetMin.y - frame.Client.offsetMax.y);

            _tooltipContent = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                frame.Client,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _tooltipContent.pivot = new Vector2(0f, 1f);

            int paddingX = Mathf.RoundToInt(RuntimeClassicUiMetrics.Ui(TooltipPaddingX));
            int paddingY = Mathf.RoundToInt(RuntimeClassicUiMetrics.Ui(TooltipPaddingY));
            _contentHorizontalPadding = paddingX * 2f;
            var layout = _tooltipContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            layout.padding = new RectOffset(paddingX, paddingX, paddingY, paddingY);

            _tooltipText = RuntimeUiFactory.CreateBitmapText(
                "Text",
                _tooltipContent,
                theme?.DefaultFont,
                1f,
                new Color(0.93f, 0.88f, 0.75f, 1f),
                BitmapTextAlignment.Center);
            _tooltipText.PixelHeight = RuntimeClassicUiMetrics.Ui(TooltipTextPixelHeight);
            _tooltipText.WrapMode = BitmapTextWrapMode.Word;
            _tooltipText.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            _tooltipTextLayout = _tooltipText.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            _tooltipTextLayout.Initialize(_tooltipText, RuntimeClassicUiMetrics.Ui(TooltipMinTextWidth), RuntimeClassicUiMetrics.Ui(TooltipLineBottomBreathingRoom));
        }

        public void Sync()
        {
            _root.SetAsLastSibling();
            RuntimeUiPopupTrigger hovered = RuntimeUiPopupTrigger.Current;
            if (hovered == null || !hovered.isActiveAndEnabled || !hovered.gameObject.activeInHierarchy)
            {
                HideTooltip();
                return;
            }

            string text = hovered.TooltipText;
            if (string.IsNullOrWhiteSpace(text))
            {
                HideTooltip();
                return;
            }

            if (!TryGetPointerPosition(hovered, out Vector2 mousePosition))
            {
                HideTooltip();
                return;
            }

            if (_hovered != hovered)
            {
                _hovered = hovered;
                _lastTooltipText = null;
                _hasLastMousePosition = false;
                _remainingDelay = TooltipDelaySeconds;
                _tooltipRoot.gameObject.SetActive(false);
            }

            if (!_hasLastMousePosition || (mousePosition - _lastMousePosition).sqrMagnitude > 0.01f)
            {
                _lastMousePosition = mousePosition;
                _hasLastMousePosition = true;
                _remainingDelay = TooltipDelaySeconds;
                _tooltipRoot.gameObject.SetActive(false);
                return;
            }

            if (_remainingDelay > 0f)
            {
                _remainingDelay -= Time.unscaledDeltaTime;
                _tooltipRoot.gameObject.SetActive(false);
                return;
            }

            if (_lastTooltipText != text)
                SetTooltipText(text.Trim());

            PositionTooltip(mousePosition);
        }

        void SetTooltipText(string text)
        {
            _lastTooltipText = text;
            _tooltipText.Text = text;
            _tooltipRoot.gameObject.SetActive(true);

            float maxWidth = RuntimeClassicUiMetrics.Ui(TooltipMaxWidth);
            float maxTextWidth = Mathf.Max(1f, maxWidth - _frameHorizontalChrome - _contentHorizontalPadding);
            _tooltipTextLayout.MaxWidth = maxTextWidth;
            RebuildTooltipLayout();
        }

        void RebuildTooltipLayout()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tooltipContent);
            float contentWidth = Mathf.Max(1f, LayoutUtility.GetPreferredWidth(_tooltipContent));
            float contentHeight = Mathf.Max(1f, LayoutUtility.GetPreferredHeight(_tooltipContent));
            _tooltipContent.sizeDelta = new Vector2(contentWidth, contentHeight);
            _tooltipRoot.sizeDelta = new Vector2(contentWidth + _frameHorizontalChrome, contentHeight + _frameVerticalChrome);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tooltipContent);
        }

        void PositionTooltip(Vector2 screenPosition)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPosition, null, out Vector2 local))
                return;

            Vector2 viewportSize = _root.rect.size;
            Vector2 size = _tooltipRoot.rect.size;
            Vector2 cursor = local + viewportSize * 0.5f;
            float xFraction = viewportSize.x > 0f ? Mathf.Clamp01(screenPosition.x / viewportSize.x) : 0f;
            Vector2 position = cursor + new Vector2(-size.x * xFraction, RuntimeClassicUiMetrics.Ui(TooltipCursorYOffset));
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, viewportSize.x - size.x));
            if (position.y - size.y < 0f)
                position.y = cursor.y - RuntimeClassicUiMetrics.Ui(TooltipCursorFallbackYOffset);
            position.y = Mathf.Clamp(position.y, size.y, Mathf.Max(size.y, viewportSize.y));
            _tooltipRoot.anchoredPosition = position;
        }

        static bool TryGetPointerPosition(RuntimeUiPopupTrigger hovered, out Vector2 position)
        {
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.isPressed)
                {
                    position = touch.position.ReadValue();
                    return true;
                }
            }

            if (hovered != null && hovered.HasPointerPosition)
            {
                position = hovered.PointerScreenPosition;
                return true;
            }

            position = default;
            return false;
        }

        void HideTooltip()
        {
            _hovered = null;
            _lastTooltipText = null;
            _hasLastMousePosition = false;
            _remainingDelay = TooltipDelaySeconds;
            _tooltipRoot.gameObject.SetActive(false);
        }

        static void SetFrameRaycastTarget(BorderFrameView frame, bool target)
        {
            if (frame == null)
                return;
            if (frame.Center != null) frame.Center.raycastTarget = target;
            if (frame.Top != null) frame.Top.raycastTarget = target;
            if (frame.Bottom != null) frame.Bottom.raycastTarget = target;
            if (frame.Left != null) frame.Left.raycastTarget = target;
            if (frame.Right != null) frame.Right.raycastTarget = target;
            if (frame.TopLeft != null) frame.TopLeft.raycastTarget = target;
            if (frame.TopRight != null) frame.TopRight.raycastTarget = target;
            if (frame.BottomLeft != null) frame.BottomLeft.raycastTarget = target;
            if (frame.BottomRight != null) frame.BottomRight.raycastTarget = target;
        }
    }

    sealed class RuntimeUiPopupTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        public static RuntimeUiPopupTrigger Current { get; private set; }

        public string TooltipText { get; private set; }
        public Vector2 PointerScreenPosition { get; private set; }
        public bool HasPointerPosition { get; private set; }

        public void SetTooltip(string text)
        {
            TooltipText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (string.IsNullOrWhiteSpace(TooltipText) && Current == this)
                Current = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            PointerScreenPosition = eventData.position;
            HasPointerPosition = true;
            if (!string.IsNullOrWhiteSpace(TooltipText))
                Current = this;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            PointerScreenPosition = eventData.position;
            HasPointerPosition = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HasPointerPosition = false;
            if (Current == this)
                Current = null;
        }

        void OnDisable()
        {
            HasPointerPosition = false;
            if (Current == this)
                Current = null;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }
    }

    sealed class RuntimeTooltipBitmapTextLayoutElement : UIBehaviour, ILayoutElement
    {
        BitmapTextGraphic _text;
        float _minWidth;
        float _extraHeight;
        float _maxWidth = 300f;

        public float minWidth => _minWidth;
        public float preferredWidth => CalculatePreferredWidth();
        public float flexibleWidth => -1f;
        public float minHeight => preferredHeight;
        public float preferredHeight => CalculatePreferredHeight(preferredWidth);
        public float flexibleHeight => -1f;
        public int layoutPriority => 1;

        public float MaxWidth
        {
            get => _maxWidth;
            set
            {
                value = Mathf.Max(1f, value);
                if (Mathf.Approximately(_maxWidth, value))
                    return;
                _maxWidth = value;
                SetDirty();
            }
        }

        public void Initialize(BitmapTextGraphic text, float minWidth, float extraHeight)
        {
            _text = text;
            _minWidth = Mathf.Max(1f, minWidth);
            _extraHeight = Mathf.Max(0f, extraHeight);
            SetDirty();
        }

        public void CalculateLayoutInputHorizontal()
        {
        }

        public void CalculateLayoutInputVertical()
        {
        }

        float CalculatePreferredWidth()
        {
            if (_text == null)
                return _minWidth;

            return Mathf.Clamp(_text.PreferredWidth, _minWidth, _maxWidth);
        }

        float CalculatePreferredHeight(float width)
        {
            if (_text == null)
                return _extraHeight;

            int lineCount = CountWrappedLines(_text.Text, width);
            return Mathf.Max(1f, lineCount * _text.PixelHeight + _extraHeight);
        }

        int CountWrappedLines(string text, float maxLineWidth)
        {
            if (_text?.Font == null || string.IsNullOrEmpty(text))
                return 1;

            float scale = _text.FontScale;
            int lines = 0;
            string[] paragraphs = text.Replace("\r", "").Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p];
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    lines++;
                    continue;
                }

                string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    lines++;
                    continue;
                }

                string currentLine = words[0];
                lines++;
                for (int i = 1; i < words.Length; i++)
                {
                    string candidate = currentLine.Length == 0 ? words[i] : currentLine + ' ' + words[i];
                    if (RuntimeUiFactory.MeasureLineWidth(_text.Font, candidate, scale) <= maxLineWidth)
                    {
                        currentLine = candidate;
                    }
                    else
                    {
                        lines++;
                        currentLine = words[i];
                    }
                }
            }

            return Math.Max(1, lines);
        }

        void SetDirty()
        {
            if (!IsActive())
                return;
            LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
        }
    }

    static class RuntimeUiPopupUtility
    {
        public static void SetTooltip(GameObject target, string text)
        {
            if (target == null)
                return;

            if (!target.TryGetComponent<RuntimeUiPopupTrigger>(out var trigger))
                trigger = target.AddComponent<RuntimeUiPopupTrigger>();
            trigger.SetTooltip(text);
        }
    }
}
