using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Assets;
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
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _root;
        readonly RectTransform _tooltipRoot;
        readonly RectTransform _tooltipContent;
        readonly BitmapTextGraphic _tooltipText;
        readonly RuntimeTooltipBitmapTextLayoutElement _tooltipTextLayout;
        readonly RuntimeSpellTooltipPopupView _spellTooltipView;
        readonly RuntimeMagicEffectTooltipPopupView _magicEffectTooltipView;
        readonly float _frameHorizontalChrome;
        readonly float _frameVerticalChrome;
        readonly float _contentHorizontalPadding;

        RuntimeUiPopupTrigger _hovered;
        string _lastTooltipKey;
        Vector2 _lastMousePosition;
        bool _hasLastMousePosition;
        float _remainingDelay = TooltipDelaySeconds;

        public RuntimeUiPopupLayer(RectTransform parent, RuntimeUiTheme theme, RuntimeInventoryIconService iconService)
        {
            _theme = theme;
            _iconService = iconService;
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

            _spellTooltipView = new RuntimeSpellTooltipPopupView(
                _tooltipContent,
                theme,
                RuntimeClassicUiMetrics.Ui(16f),
                RuntimeClassicUiMetrics.Ui(6f),
                RuntimeClassicUiMetrics.Ui(TooltipLineBottomBreathingRoom));
            _spellTooltipView.SetActive(false);

            _magicEffectTooltipView = new RuntimeMagicEffectTooltipPopupView(
                _tooltipContent,
                theme,
                RuntimeClassicUiMetrics.Ui(16f),
                RuntimeClassicUiMetrics.Ui(6f),
                RuntimeClassicUiMetrics.Ui(TooltipLineBottomBreathingRoom));
            _magicEffectTooltipView.SetActive(false);
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

            if (!hovered.HasTooltip)
            {
                HideTooltip();
                return;
            }

            string tooltipKey = hovered.TooltipKey;

            if (!TryGetPointerPosition(hovered, out Vector2 mousePosition))
            {
                HideTooltip();
                return;
            }

            if (_hovered != hovered)
            {
                _hovered = hovered;
                _lastTooltipKey = null;
                _hasLastMousePosition = false;
                _remainingDelay = TooltipDelaySeconds;
                SetTooltipVisible(false);
            }

            if (!_hasLastMousePosition || (mousePosition - _lastMousePosition).sqrMagnitude > 0.01f)
            {
                _lastMousePosition = mousePosition;
                _hasLastMousePosition = true;
                _remainingDelay = TooltipDelaySeconds;
                SetTooltipVisible(false);
                return;
            }

            if (_remainingDelay > 0f)
            {
                _remainingDelay -= Time.unscaledDeltaTime;
                SetTooltipVisible(false);
                return;
            }

            if (_lastTooltipKey != tooltipKey)
                SetTooltipContent(hovered, tooltipKey);

            PositionTooltip(mousePosition);
        }

        void SetTooltipContent(RuntimeUiPopupTrigger trigger, string tooltipKey)
        {
            _lastTooltipKey = tooltipKey;
            SetTooltipVisible(true);

            float maxWidth = RuntimeClassicUiMetrics.Ui(TooltipMaxWidth);
            float maxContentWidth = Mathf.Max(1f, maxWidth - _frameHorizontalChrome - _contentHorizontalPadding);

            if (trigger.SpellTooltip != null && RuntimeSpellTooltipPopupView.HasContent(trigger.SpellTooltip))
            {
                _tooltipText.gameObject.SetActive(false);
                _magicEffectTooltipView.SetActive(false);
                _spellTooltipView.SetActive(true);
                _spellTooltipView.Sync(trigger.SpellTooltip, _iconService, maxContentWidth);
            }
            else if (trigger.MagicEffectTooltip != null && RuntimeMagicEffectTooltipPopupView.HasContent(trigger.MagicEffectTooltip))
            {
                _tooltipText.gameObject.SetActive(false);
                _spellTooltipView.SetActive(false);
                _magicEffectTooltipView.SetActive(true);
                _magicEffectTooltipView.Sync(trigger.MagicEffectTooltip, _iconService, maxContentWidth);
            }
            else
            {
                _spellTooltipView.SetActive(false);
                _magicEffectTooltipView.SetActive(false);
                _tooltipText.gameObject.SetActive(true);
                _tooltipText.Text = trigger.TooltipText?.Trim() ?? string.Empty;
                _tooltipTextLayout.MaxWidth = maxContentWidth;
            }

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
            _lastTooltipKey = null;
            _hasLastMousePosition = false;
            _remainingDelay = TooltipDelaySeconds;
            SetTooltipVisible(false);
        }

        void SetTooltipVisible(bool visible)
        {
            var go = _tooltipRoot.gameObject;
            if (go.activeSelf != visible)
                go.SetActive(visible);
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
        public RuntimeSpellTooltipViewModel SpellTooltip { get; private set; }
        public RuntimeMagicEffectTooltipViewModel MagicEffectTooltip { get; private set; }
        public Vector2 PointerScreenPosition { get; private set; }
        public bool HasPointerPosition { get; private set; }
        public bool HasTooltip => !string.IsNullOrWhiteSpace(TooltipText)
            || RuntimeSpellTooltipPopupView.HasContent(SpellTooltip)
            || RuntimeMagicEffectTooltipPopupView.HasContent(MagicEffectTooltip);
        public string TooltipKey => RuntimeSpellTooltipPopupView.HasContent(SpellTooltip)
            ? RuntimeSpellTooltipPopupView.BuildKey(SpellTooltip)
            : RuntimeMagicEffectTooltipPopupView.HasContent(MagicEffectTooltip)
                ? RuntimeMagicEffectTooltipPopupView.BuildKey(MagicEffectTooltip)
            : TooltipText;

        public void SetTooltip(string text)
        {
            TooltipText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            SpellTooltip = null;
            MagicEffectTooltip = null;
            if (!HasTooltip && Current == this)
                Current = null;
        }

        public void SetSpellTooltip(RuntimeSpellTooltipViewModel tooltip)
        {
            TooltipText = null;
            SpellTooltip = RuntimeSpellTooltipPopupView.HasContent(tooltip) ? tooltip : null;
            MagicEffectTooltip = null;
            if (!HasTooltip && Current == this)
                Current = null;
        }

        public void SetMagicEffectTooltip(RuntimeMagicEffectTooltipViewModel tooltip)
        {
            TooltipText = null;
            SpellTooltip = null;
            MagicEffectTooltip = RuntimeMagicEffectTooltipPopupView.HasContent(tooltip) ? tooltip : null;
            if (!HasTooltip && Current == this)
                Current = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            PointerScreenPosition = eventData.position;
            HasPointerPosition = true;
            if (HasTooltip)
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

    sealed class RuntimeSpellTooltipPopupView
    {
        const float TitlePixelHeight = RuntimeClassicUiFontSizes.Body;
        const float BodyPixelHeight = RuntimeClassicUiFontSizes.Body;

        sealed class EffectRow
        {
            public RectTransform Root;
            public Image Icon;
            public BitmapTextGraphic Text;
            public RuntimeTooltipBitmapTextLayoutElement TextLayout;
        }

        readonly RectTransform _root;
        readonly BitmapTextGraphic _title;
        readonly BitmapTextGraphic _school;
        readonly RuntimeTooltipBitmapTextLayoutElement _titleLayout;
        readonly RuntimeTooltipBitmapTextLayoutElement _schoolLayout;
        readonly RuntimeTooltipFixedLayoutElement _layout;
        readonly List<EffectRow> _rows = new();
        readonly float _iconSize;
        readonly float _iconTextGap;
        readonly float _extraTextHeight;

        public RuntimeSpellTooltipPopupView(
            RectTransform parent,
            RuntimeUiTheme theme,
            float iconSize,
            float iconTextGap,
            float extraTextHeight)
        {
            _iconSize = Mathf.Max(1f, iconSize);
            _iconTextGap = Mathf.Max(0f, iconTextGap);
            _extraTextHeight = Mathf.Max(0f, extraTextHeight);

            _root = RuntimeUiFactory.CreateAnchoredRect(
                "SpellTooltip",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _root.pivot = new Vector2(0f, 1f);
            _layout = _root.gameObject.AddComponent<RuntimeTooltipFixedLayoutElement>();
            var rootLayout = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.UpperCenter;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = false;
            rootLayout.childForceExpandHeight = false;
            rootLayout.spacing = 0f;
            rootLayout.padding = new RectOffset(0, 0, 0, 0);

            _title = RuntimeUiFactory.CreateBitmapText(
                "Title",
                _root,
                theme?.DefaultFont,
                1f,
                new Color(0.93f, 0.88f, 0.75f, 1f),
                BitmapTextAlignment.Center);
            _title.PixelHeight = RuntimeClassicUiMetrics.Ui(TitlePixelHeight);
            _title.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            _title.raycastTarget = false;
            _titleLayout = _title.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            _titleLayout.Initialize(_title, 1f, extraTextHeight);

            _school = RuntimeUiFactory.CreateBitmapText(
                "School",
                _root,
                theme?.DefaultFont,
                1f,
                new Color(0.93f, 0.78f, 0.53f, 1f),
                BitmapTextAlignment.Left);
            _school.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            _school.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            _school.raycastTarget = false;
            _schoolLayout = _school.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            _schoolLayout.Initialize(_school, 1f, extraTextHeight);
        }

        public void SetActive(bool active)
        {
            _root.gameObject.SetActive(active);
        }

        public void Sync(RuntimeSpellTooltipViewModel model, RuntimeInventoryIconService iconService, float maxWidth)
        {
            if (!HasContent(model))
            {
                SetActive(false);
                return;
            }

            SetActive(true);

            RuntimeSpellTooltipEffectRow[] effects = model.Effects ?? Array.Empty<RuntimeSpellTooltipEffectRow>();
            while (_rows.Count < effects.Length)
                _rows.Add(CreateRow(_rows.Count));

            float maxTextWidth = Mathf.Max(1f, maxWidth);
            _title.Text = string.IsNullOrWhiteSpace(model.Title) ? "--" : model.Title.Trim();
            _titleLayout.MaxWidth = maxTextWidth;

            bool showSchool = !string.IsNullOrWhiteSpace(model.SchoolText);
            _school.gameObject.SetActive(showSchool);
            if (showSchool)
            {
                _school.Text = model.SchoolText.Trim();
                _schoolLayout.MaxWidth = maxTextWidth;
            }

            float rowTextMaxWidth = Mathf.Max(1f, maxTextWidth - _iconSize - _iconTextGap);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < effects.Length && !string.IsNullOrWhiteSpace(effects[i]?.Text);
                var row = _rows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var effect = effects[i];
                row.Icon.sprite = iconService?.GetMagicEffectSprite(effect.IconPath);
                row.Icon.color = row.Icon.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                row.Icon.preserveAspect = true;
                row.Text.Text = effect.Text.Trim();
                row.Text.WrapMode = BitmapTextWrapMode.Word;
                row.TextLayout.MaxWidth = rowTextMaxWidth;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);

            float contentWidth = Mathf.Max(1f, LayoutUtility.GetPreferredWidth(_title.rectTransform));
            float contentHeight = Mathf.Max(1f, LayoutUtility.GetPreferredHeight(_title.rectTransform));
            if (showSchool)
            {
                contentWidth = Mathf.Max(contentWidth, LayoutUtility.GetPreferredWidth(_school.rectTransform));
                contentHeight += Mathf.Max(1f, LayoutUtility.GetPreferredHeight(_school.rectTransform));
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (!row.Root.gameObject.activeSelf)
                    continue;

                contentWidth = Mathf.Max(contentWidth, LayoutUtility.GetPreferredWidth(row.Root));
                contentHeight += Mathf.Max(1f, LayoutUtility.GetPreferredHeight(row.Root));
            }

            contentWidth = Mathf.Min(maxTextWidth, contentWidth);
            _root.sizeDelta = new Vector2(contentWidth, contentHeight);
            _layout.SetPreferredSize(_root.sizeDelta);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
        }

        EffectRow CreateRow(int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"Effect_{index}",
                _root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);

            var icon = RuntimeUiFactory.CreateImage("Icon", root, Color.white);
            icon.raycastTarget = false;
            var iconLayout = icon.gameObject.AddComponent<LayoutElement>();
            iconLayout.minWidth = _iconSize;
            iconLayout.preferredWidth = _iconSize;
            iconLayout.minHeight = _iconSize;
            iconLayout.preferredHeight = _iconSize;

            var text = RuntimeUiFactory.CreateBitmapText(
                "Text",
                root,
                _title.Font,
                1f,
                new Color(0.93f, 0.88f, 0.75f, 1f),
                BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.raycastTarget = false;
            var textLayout = text.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            textLayout.Initialize(text, 1f, _extraTextHeight);

            var rowLayout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = _iconTextGap;
            rowLayout.padding = new RectOffset(0, 0, 0, 0);

            return new EffectRow
            {
                Root = root,
                Icon = icon,
                Text = text,
                TextLayout = textLayout,
            };
        }

        public static bool HasContent(RuntimeSpellTooltipViewModel model)
        {
            if (model == null)
                return false;
            if (!string.IsNullOrWhiteSpace(model.Title) || !string.IsNullOrWhiteSpace(model.SchoolText))
                return true;

            RuntimeSpellTooltipEffectRow[] effects = model.Effects ?? Array.Empty<RuntimeSpellTooltipEffectRow>();
            for (int i = 0; i < effects.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(effects[i]?.Text))
                    return true;
            }

            return false;
        }

        public static string BuildKey(RuntimeSpellTooltipViewModel model)
        {
            if (model == null)
                return string.Empty;

            var key = new System.Text.StringBuilder();
            key.Append(model.Title).Append('|').Append(model.SchoolText);
            RuntimeSpellTooltipEffectRow[] effects = model.Effects ?? Array.Empty<RuntimeSpellTooltipEffectRow>();
            for (int i = 0; i < effects.Length; i++)
                key.Append('|').Append(effects[i]?.IconPath).Append('=').Append(effects[i]?.Text);
            return key.ToString();
        }
    }

    sealed class RuntimeMagicEffectTooltipPopupView
    {
        const float BodyPixelHeight = RuntimeClassicUiFontSizes.Body;

        readonly RectTransform _root;
        readonly RectTransform _header;
        readonly Image _icon;
        readonly BitmapTextGraphic _title;
        readonly BitmapTextGraphic _description;
        readonly RuntimeTooltipBitmapTextLayoutElement _titleLayout;
        readonly RuntimeTooltipBitmapTextLayoutElement _descriptionLayout;
        readonly RuntimeTooltipFixedLayoutElement _layout;
        readonly float _iconSize;
        readonly float _iconTextGap;
        readonly float _extraTextHeight;

        public RuntimeMagicEffectTooltipPopupView(
            RectTransform parent,
            RuntimeUiTheme theme,
            float iconSize,
            float iconTextGap,
            float extraTextHeight)
        {
            _iconSize = Mathf.Max(1f, iconSize);
            _iconTextGap = Mathf.Max(0f, iconTextGap);
            _extraTextHeight = Mathf.Max(0f, extraTextHeight);

            _root = RuntimeUiFactory.CreateAnchoredRect(
                "MagicEffectTooltip",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _root.pivot = new Vector2(0f, 1f);
            _layout = _root.gameObject.AddComponent<RuntimeTooltipFixedLayoutElement>();
            var rootLayout = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.UpperLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = false;
            rootLayout.childForceExpandHeight = false;
            rootLayout.spacing = 0f;
            rootLayout.padding = new RectOffset(0, 0, 0, 0);

            _header = RuntimeUiFactory.CreateAnchoredRect(
                "Header",
                _root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _header.pivot = new Vector2(0f, 1f);
            var headerLayout = _header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.childAlignment = TextAnchor.UpperLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            headerLayout.spacing = _iconTextGap;
            headerLayout.padding = new RectOffset(0, 0, 0, 0);

            _icon = RuntimeUiFactory.CreateImage("Icon", _header, Color.white);
            _icon.raycastTarget = false;
            var iconLayout = _icon.gameObject.AddComponent<LayoutElement>();
            iconLayout.minWidth = _iconSize;
            iconLayout.preferredWidth = _iconSize;
            iconLayout.minHeight = _iconSize;
            iconLayout.preferredHeight = _iconSize;

            _title = RuntimeUiFactory.CreateBitmapText(
                "Title",
                _header,
                theme?.DefaultFont,
                1f,
                new Color(0.93f, 0.88f, 0.75f, 1f),
                BitmapTextAlignment.Left);
            _title.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            _title.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            _title.raycastTarget = false;
            _titleLayout = _title.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            _titleLayout.Initialize(_title, 1f, extraTextHeight);

            _description = RuntimeUiFactory.CreateBitmapText(
                "Description",
                _root,
                theme?.DefaultFont,
                1f,
                new Color(0.93f, 0.88f, 0.75f, 1f),
                BitmapTextAlignment.Left);
            _description.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            _description.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            _description.WrapMode = BitmapTextWrapMode.Word;
            _description.raycastTarget = false;
            _descriptionLayout = _description.gameObject.AddComponent<RuntimeTooltipBitmapTextLayoutElement>();
            _descriptionLayout.Initialize(_description, 1f, extraTextHeight);
        }

        public void SetActive(bool active)
        {
            _root.gameObject.SetActive(active);
        }

        public void Sync(RuntimeMagicEffectTooltipViewModel model, RuntimeInventoryIconService iconService, float maxWidth)
        {
            if (!HasContent(model))
            {
                SetActive(false);
                return;
            }

            SetActive(true);
            float maxTextWidth = Mathf.Max(1f, maxWidth);
            float titleMaxWidth = Mathf.Max(1f, maxTextWidth - _iconSize - _iconTextGap);
            _icon.sprite = iconService?.GetMagicEffectSprite(model.IconPath);
            _icon.color = _icon.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            _icon.preserveAspect = true;
            _title.Text = string.IsNullOrWhiteSpace(model.DisplayName) ? "--" : model.DisplayName.Trim();
            _titleLayout.MaxWidth = titleMaxWidth;

            string description = BuildDescriptionText(model.DescriptionLines);
            bool showDescription = !string.IsNullOrWhiteSpace(description);
            _description.gameObject.SetActive(showDescription);
            if (showDescription)
            {
                _description.Text = description;
                _descriptionLayout.MaxWidth = maxTextWidth;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);

            float headerWidth = _iconSize + _iconTextGap + LayoutUtility.GetPreferredWidth(_title.rectTransform);
            float contentWidth = Mathf.Max(1f, Mathf.Min(maxTextWidth, headerWidth));
            float contentHeight = Mathf.Max(_iconSize, LayoutUtility.GetPreferredHeight(_title.rectTransform));
            if (showDescription)
            {
                contentWidth = Mathf.Max(contentWidth, Mathf.Min(maxTextWidth, LayoutUtility.GetPreferredWidth(_description.rectTransform)));
                contentHeight += Mathf.Max(1f, LayoutUtility.GetPreferredHeight(_description.rectTransform));
            }

            _root.sizeDelta = new Vector2(contentWidth, contentHeight);
            _layout.SetPreferredSize(_root.sizeDelta);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
        }

        static string BuildDescriptionText(string[] lines)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            var result = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                if (result.Length > 0)
                    result.Append('\n');
                result.Append(lines[i].Trim());
            }

            return result.ToString();
        }

        public static bool HasContent(RuntimeMagicEffectTooltipViewModel model)
            => model != null && (!string.IsNullOrWhiteSpace(model.DisplayName) || !string.IsNullOrWhiteSpace(BuildDescriptionText(model.DescriptionLines)));

        public static string BuildKey(RuntimeMagicEffectTooltipViewModel model)
        {
            if (model == null)
                return string.Empty;

            return $"{model.IconPath}|{model.DisplayName}|{BuildDescriptionText(model.DescriptionLines)}";
        }
    }

    sealed class RuntimeTooltipFixedLayoutElement : UIBehaviour, ILayoutElement
    {
        Vector2 _preferredSize = Vector2.one;

        public float minWidth => preferredWidth;
        public float preferredWidth => Mathf.Max(1f, _preferredSize.x);
        public float flexibleWidth => -1f;
        public float minHeight => preferredHeight;
        public float preferredHeight => Mathf.Max(1f, _preferredSize.y);
        public float flexibleHeight => -1f;
        public int layoutPriority => 1;

        public void SetPreferredSize(Vector2 size)
        {
            _preferredSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
            if (IsActive())
                LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
        }

        public void CalculateLayoutInputHorizontal()
        {
        }

        public void CalculateLayoutInputVertical()
        {
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

        public static void SetSpellTooltip(GameObject target, RuntimeSpellTooltipViewModel tooltip)
        {
            if (target == null)
                return;

            if (!target.TryGetComponent<RuntimeUiPopupTrigger>(out var trigger))
                trigger = target.AddComponent<RuntimeUiPopupTrigger>();
            trigger.SetSpellTooltip(tooltip);
        }

        public static void SetMagicEffectTooltip(GameObject target, RuntimeMagicEffectTooltipViewModel tooltip)
        {
            if (target == null)
                return;

            if (!target.TryGetComponent<RuntimeUiPopupTrigger>(out var trigger))
                trigger = target.AddComponent<RuntimeUiPopupTrigger>();
            trigger.SetMagicEffectTooltip(tooltip);
        }
    }
}
