using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class DialogueServiceWindowView
    {
        const float WindowWidth = 472f;
        const float WindowHeight = 360f;
        const float PersuasionWindowWidth = 220f;
        const float PersuasionWindowHeight = 192f;
        const float PersuasionButtonWidth = 188f;
        const float PersuasionButtonHeight = 16f;
        const float PersuasionButtonGap = 2f;
        const float CaptionHeight = 20f;
        const float ClientInset = 4f;
        const float RowHeight = 24f;
        const float FooterHeight = 30f;

        static readonly Color TextColor = Rgb(202, 165, 96);
        static readonly Color HeaderColor = Rgb(223, 201, 159);
        static readonly Color DisabledColor = new(0.42f, 0.38f, 0.31f, 1f);
        static readonly Color PanelColor = new(0f, 0f, 0f, 0.34f);
        static readonly Color ButtonColor = new(0.12f, 0.10f, 0.08f, 0.88f);

        sealed class RowView
        {
            public RectTransform Root;
            public BitmapTextGraphic Left;
            public BitmapTextGraphic Right;
            public Button Button;
            public MorrowindDialogueServiceAction Action;
            public int Int0;
            public int Int1;
        }

        sealed class ButtonView
        {
            public MorrowindButtonView FramedView;
            public MorrowindButtonView TextView;
            public MorrowindDialogueServiceAction Action;
            public int Int0;
            public int Int1;
            public bool TextHovered;
            public bool TextPressed;
        }

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _viewport;
        readonly Action<MorrowindDialogueServiceAction, int, int> _onAction;
        readonly MorrowindWindowView _window;
        readonly BitmapTextGraphic _header;
        readonly BitmapTextGraphic _footer;
        readonly RectTransform _persuasionRoot;
        readonly RectTransform _persuasionActionsRoot;
        readonly BitmapTextGraphic _persuasionHeader;
        readonly BitmapTextGraphic _persuasionGold;
        readonly RectTransform _listRoot;
        readonly RectTransform _rowsRoot;
        readonly ScrollRect _scroll;
        readonly RectTransform _buttonRoot;
        readonly List<RowView> _rows = new();
        readonly List<ButtonView> _buttons = new();
        DialogueServiceWindowLayoutKind _layoutKind;
        bool _hasCentered;

        public DialogueServiceWindowView(
            RectTransform parent,
            RuntimeUiTheme theme,
            Action<MorrowindDialogueServiceAction, int, int> onAction)
        {
            _theme = theme;
            _viewport = parent;
            _onAction = onAction;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "DialogueServiceWindow",
                parent,
                theme,
                "Service",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.88f,
                RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Caption),
                HeaderColor);
            _window.Root.anchorMin = new Vector2(0.5f, 0.5f);
            _window.Root.anchorMax = new Vector2(0.5f, 0.5f);
            _window.Root.pivot = new Vector2(0.5f, 0.5f);
            _window.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(WindowWidth, WindowHeight));

            _header = RuntimeUiFactory.CreateBitmapText("Header", _window.Client, theme?.DefaultFont, 1f, HeaderColor, BitmapTextAlignment.Left);
            _header.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _header.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _header.WrapMode = BitmapTextWrapMode.Word;
            _header.rectTransform.anchorMin = new Vector2(0f, 1f);
            _header.rectTransform.anchorMax = new Vector2(1f, 1f);
            _header.rectTransform.pivot = new Vector2(0f, 1f);
            _header.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(10f, -54f));
            _header.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-10f, -8f));

            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "Rows",
                _window.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(10f, FooterHeight + 10f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-10f, -58f)));
            _listRoot = listRoot;
            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", listRoot, RuntimeUiFactory.ResolveThinFrame(theme), PanelColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var viewport = RuntimeUiFactory.CreateAnchorRect("Viewport", frame.Client, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), RuntimeClassicUiMetrics.Ui(new Vector2(4f, 4f)), RuntimeClassicUiMetrics.Ui(new Vector2(-4f, -4f)));
            viewport.gameObject.AddComponent<RectMask2D>();
            var image = viewport.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;

            _rowsRoot = RuntimeUiFactory.CreateAnchoredRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _rowsRoot.pivot = new Vector2(0f, 1f);
            _scroll = listRoot.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _rowsRoot;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(24f);

            _buttonRoot = RuntimeUiFactory.CreateAnchorRect("Buttons", _window.Client, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), RuntimeClassicUiMetrics.Ui(new Vector2(10f, 8f)), RuntimeClassicUiMetrics.Ui(new Vector2(-10f, FooterHeight + 8f)));

            _footer = RuntimeUiFactory.CreateBitmapText("Footer", _window.Client, theme?.DefaultFont, 1f, HeaderColor, BitmapTextAlignment.Left);
            _footer.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _footer.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _footer.raycastTarget = false;
            _footer.rectTransform.anchorMin = new Vector2(0f, 0f);
            _footer.rectTransform.anchorMax = new Vector2(1f, 0f);
            _footer.rectTransform.pivot = new Vector2(0f, 0f);
            _footer.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(8f, 8f));
            _footer.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-80f, 32f));
            _footer.gameObject.SetActive(false);

            (_persuasionRoot, _persuasionHeader, _persuasionActionsRoot, _persuasionGold) = BuildPersuasionDialog(parent, theme);
            _window.Root.gameObject.SetActive(false);
        }

        public void Sync(DialogueServiceWindowViewModel model)
        {
            if (model == null)
            {
                _window.Root.gameObject.SetActive(false);
                _persuasionRoot.gameObject.SetActive(false);
                return;
            }

            _layoutKind = model.LayoutKind;
            if (_layoutKind == DialogueServiceWindowLayoutKind.Persuasion)
            {
                _window.Root.gameObject.SetActive(false);
                if (!_persuasionRoot.gameObject.activeSelf)
                    _persuasionRoot.gameObject.SetActive(true);
                _persuasionRoot.SetAsLastSibling();
                _persuasionHeader.Text = string.IsNullOrWhiteSpace(model.Header) ? string.Empty : model.Header.Trim();
                _persuasionGold.Text = string.IsNullOrWhiteSpace(model.FooterText) ? string.Empty : model.FooterText.Trim();
                SyncRows(Array.Empty<DialogueServiceRowViewModel>());
                SyncButtons(model.Buttons ?? Array.Empty<DialogueServiceButtonViewModel>());
                return;
            }

            _persuasionRoot.gameObject.SetActive(false);
            if (!_window.Root.gameObject.activeSelf)
                _window.Root.gameObject.SetActive(true);
            _window.Root.SetAsLastSibling();
            CenterOnce();

            ApplyLayout(model);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Service" : model.Title.Trim();
            _header.Text = string.IsNullOrWhiteSpace(model.Header) ? string.Empty : model.Header.Trim();
            _footer.Text = string.IsNullOrWhiteSpace(model.FooterText) ? string.Empty : model.FooterText.Trim();
            SyncRows(model.Rows ?? Array.Empty<DialogueServiceRowViewModel>());
            SyncButtons(model.Buttons ?? Array.Empty<DialogueServiceButtonViewModel>());
        }

        void ApplyLayout(DialogueServiceWindowViewModel model)
        {
            _layoutKind = model.LayoutKind;
            bool persuasion = _layoutKind == DialogueServiceWindowLayoutKind.Persuasion;
            _window.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(persuasion
                ? new Vector2(PersuasionWindowWidth, PersuasionWindowHeight)
                : new Vector2(WindowWidth, WindowHeight));

            _listRoot.gameObject.SetActive(!persuasion);
            _buttonRoot.gameObject.SetActive(!persuasion);
            _footer.gameObject.SetActive(persuasion && !string.IsNullOrWhiteSpace(model.FooterText));

            if (persuasion)
            {
                _header.rectTransform.anchorMin = new Vector2(0f, 1f);
                _header.rectTransform.anchorMax = new Vector2(1f, 1f);
                _header.rectTransform.pivot = new Vector2(0.5f, 1f);
                _header.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(8f, -32f));
                _header.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-8f, -4f));
                _header.Alignment = BitmapTextAlignment.Center;

                _buttonRoot.anchorMin = new Vector2(0.5f, 1f);
                _buttonRoot.anchorMax = new Vector2(0.5f, 1f);
                _buttonRoot.pivot = new Vector2(0.5f, 1f);
                _buttonRoot.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(0f, -36f));
                _buttonRoot.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(196f, 126f));
                return;
            }

            _header.rectTransform.anchorMin = new Vector2(0f, 1f);
            _header.rectTransform.anchorMax = new Vector2(1f, 1f);
            _header.rectTransform.pivot = new Vector2(0f, 1f);
            _header.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(10f, -54f));
            _header.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-10f, -8f));
            _header.Alignment = BitmapTextAlignment.Left;

            _buttonRoot.anchorMin = new Vector2(0f, 0f);
            _buttonRoot.anchorMax = new Vector2(1f, 0f);
            _buttonRoot.pivot = new Vector2(0.5f, 0f);
            _buttonRoot.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(10f, 8f));
            _buttonRoot.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-10f, FooterHeight + 8f));
        }

        (RectTransform root, BitmapTextGraphic header, RectTransform actionsRoot, BitmapTextGraphic gold) BuildPersuasionDialog(
            RectTransform parent,
            RuntimeUiTheme theme)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                "PersuasionDialog",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(PersuasionWindowWidth, PersuasionWindowHeight)));
            root.pivot = new Vector2(0.5f, 0.5f);

            var background = RuntimeUiFactory.CreateImage("Background", root, new Color(0f, 0f, 0f, 0.88f));
            background.raycastTarget = true;
            RuntimeUiFactory.Stretch(background.rectTransform);

            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThickFrame(theme), Color.clear);
            RuntimeUiFactory.Stretch(frame.Root);

            var header = RuntimeUiFactory.CreateBitmapText("Header", root, theme?.DefaultFont, 1f, HeaderColor, BitmapTextAlignment.Center);
            header.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            header.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            header.raycastTarget = false;
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(0f, -4f));
            header.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(220f, 24f));

            var actionsBox = RuntimeUiFactory.CreateAnchoredRect(
                "ActionsBox",
                root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(8f, -32f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(196f, 114f)));
            var actionsFrame = RuntimeUiFactory.CreateBorderFrame("Frame", actionsBox, RuntimeUiFactory.ResolveThinFrame(theme), PanelColor);
            RuntimeUiFactory.Stretch(actionsFrame.Root);
            var actionsRoot = RuntimeUiFactory.CreateStretchRect("Actions", actionsFrame.Client);

            var gold = RuntimeUiFactory.CreateBitmapText("Gold", root, theme?.DefaultFont, 1f, HeaderColor, BitmapTextAlignment.Left);
            gold.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            gold.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            gold.raycastTarget = false;
            gold.rectTransform.anchorMin = new Vector2(0f, 1f);
            gold.rectTransform.anchorMax = new Vector2(0f, 1f);
            gold.rectTransform.pivot = new Vector2(0f, 1f);
            gold.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(8f, -158f));
            gold.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(102f, 24f));

            root.gameObject.SetActive(false);
            return (root, header, actionsRoot, gold);
        }

        void SyncRows(DialogueServiceRowViewModel[] rows)
        {
            while (_rows.Count < rows.Length)
                _rows.Add(CreateRow(_rowsRoot, _rows.Count));

            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < rows.Length;
                var row = _rows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var model = rows[i];
                row.Action = model.Action;
                row.Int0 = model.Int0;
                row.Int1 = model.Int1;
                row.Root.anchoredPosition = new Vector2(0f, -i * rowHeight);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Left.Text = string.IsNullOrWhiteSpace(model.LeftText) ? string.Empty : model.LeftText.Trim();
                row.Right.Text = string.IsNullOrWhiteSpace(model.RightText) ? string.Empty : model.RightText.Trim();
                row.Button.interactable = model.Enabled;
                row.Left.color = model.Enabled ? TextColor : DisabledColor;
                row.Right.color = model.Enabled ? HeaderColor : DisabledColor;
            }

            _rowsRoot.sizeDelta = new Vector2(0f, rowHeight * rows.Length);
        }

        void SyncButtons(DialogueServiceButtonViewModel[] buttons)
        {
            while (_buttons.Count < buttons.Length)
                _buttons.Add(CreateFooterButton(_buttonRoot, _buttons.Count));

            bool persuasion = _layoutKind == DialogueServiceWindowLayoutKind.Persuasion;
            float gap = RuntimeClassicUiMetrics.Ui(persuasion ? PersuasionButtonGap : 8f);
            float width = persuasion
                ? RuntimeClassicUiMetrics.Ui(PersuasionButtonWidth)
                : buttons.Length > 0
                    ? (_buttonRoot.rect.width - gap * (buttons.Length - 1)) / buttons.Length
                    : 0f;
            if (width <= 0f)
                width = RuntimeClassicUiMetrics.Ui(persuasion ? PersuasionButtonWidth : 92f);
            float height = RuntimeClassicUiMetrics.Ui(persuasion ? PersuasionButtonHeight : FooterHeight);

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool visible = i < buttons.Length;
                var button = _buttons[i];
                button.FramedView.Root.gameObject.SetActive(false);
                button.TextView.Root.gameObject.SetActive(false);
                if (!visible)
                {
                    button.TextHovered = false;
                    button.TextPressed = false;
                    continue;
                }

                var model = buttons[i];
                button.Action = model.Action;
                button.Int0 = model.Int0;
                button.Int1 = model.Int1;
                if (persuasion)
                {
                    bool close = model.Action == MorrowindDialogueServiceAction.Close;
                    MorrowindButtonView view = close ? button.FramedView : button.TextView;
                    view.Root.gameObject.SetActive(true);
                    view.Root.SetParent(close ? _persuasionRoot : _persuasionActionsRoot, false);
                    view.Root.anchorMin = new Vector2(0f, 1f);
                    view.Root.anchorMax = new Vector2(0f, 1f);
                    view.Root.pivot = close ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
                    float stride = height + gap;
                    view.Root.anchoredPosition = close
                        ? RuntimeClassicUiMetrics.Ui(new Vector2(212f, -154f))
                        : new Vector2(RuntimeClassicUiMetrics.Ui(4f), -i * stride);
                    view.Root.sizeDelta = close
                        ? RuntimeClassicUiMetrics.Ui(new Vector2(70f, 24f))
                        : new Vector2(width, height);
                    view.Label.Alignment = close ? BitmapTextAlignment.Center : BitmapTextAlignment.Left;
                    view.Label.Text = string.IsNullOrWhiteSpace(model.Text) ? string.Empty : model.Text.Trim();
                    view.Button.interactable = model.Enabled;
                    if (close)
                        view.Label.color = model.Enabled ? TextColor : DisabledColor;
                    else
                        ApplyPersuasionTextColor(button, model.Enabled);
                }
                else
                {
                    var view = button.FramedView;
                    view.Root.gameObject.SetActive(true);
                    view.Root.SetParent(_buttonRoot, false);
                    view.Root.anchorMin = new Vector2(0f, 0f);
                    view.Root.anchorMax = new Vector2(0f, 1f);
                    view.Root.pivot = new Vector2(0f, 0.5f);
                    view.Root.anchoredPosition = new Vector2(i * (width + gap), 0f);
                    view.Root.sizeDelta = new Vector2(width, 0f);
                    view.Label.Alignment = BitmapTextAlignment.Center;
                    view.Label.Text = string.IsNullOrWhiteSpace(model.Text) ? string.Empty : model.Text.Trim();
                    view.Button.interactable = model.Enabled;
                    view.Label.color = model.Enabled ? TextColor : DisabledColor;
                }
            }
        }

        RowView CreateRow(RectTransform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect($"Row_{index}", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, RuntimeClassicUiMetrics.Ui(new Vector2(0f, RowHeight)));
            root.pivot = new Vector2(0f, 1f);
            var hit = RuntimeUiFactory.CreateImage("Hit", root, new Color(1f, 1f, 1f, 0.001f));
            RuntimeUiFactory.Stretch(hit.rectTransform);

            var left = RuntimeUiFactory.CreateBitmapText("Left", root, _theme?.DefaultFont, 1f, TextColor, BitmapTextAlignment.Left);
            left.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            left.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            left.raycastTarget = false;
            RuntimeUiFactory.SetInset(left.rectTransform, RuntimeClassicUiMetrics.Ui(4f), 0f, -RuntimeClassicUiMetrics.Ui(92f), 0f);

            var right = RuntimeUiFactory.CreateBitmapText("Right", root, _theme?.DefaultFont, 1f, HeaderColor, BitmapTextAlignment.Right);
            right.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            right.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            right.raycastTarget = false;
            RuntimeUiFactory.SetInset(right.rectTransform, RuntimeClassicUiMetrics.Ui(4f), 0f, -RuntimeClassicUiMetrics.Ui(4f), 0f);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = left;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var row = new RowView { Root = root, Left = left, Right = right, Button = button };
            button.onClick.AddListener(() => _onAction?.Invoke(row.Action, row.Int0, row.Int1));
            return row;
        }

        ButtonView CreateFooterButton(RectTransform parent, int index)
        {
            var framed = RuntimeUiFactory.CreateMorrowindButton($"Button_{index}", parent, _theme, string.Empty, 1f, TextColor, ButtonColor);
            RuntimeUiFactory.SetInsetText(framed.Label.rectTransform, framed.Label, 8f, 3f, -8f, -3f);
            framed.Root.gameObject.SetActive(false);

            var text = CreatePersuasionTextButton($"TextButton_{index}", parent);
            text.Root.gameObject.SetActive(false);

            var button = new ButtonView
            {
                FramedView = framed,
                TextView = text,
            };
            InstallPersuasionTextHighlight(button);
            framed.Button.onClick.AddListener(() => _onAction?.Invoke(button.Action, button.Int0, button.Int1));
            text.Button.onClick.AddListener(() => _onAction?.Invoke(button.Action, button.Int0, button.Int1));
            return button;
        }

        void InstallPersuasionTextHighlight(ButtonView button)
        {
            var trigger = button.TextView.Root.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            {
                button.TextHovered = true;
                ApplyPersuasionTextColor(button, button.TextView.Button.interactable);
            });
            AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            {
                button.TextHovered = false;
                button.TextPressed = false;
                ApplyPersuasionTextColor(button, button.TextView.Button.interactable);
            });
            AddTrigger(trigger, EventTriggerType.PointerDown, _ =>
            {
                button.TextPressed = true;
                ApplyPersuasionTextColor(button, button.TextView.Button.interactable);
            });
            AddTrigger(trigger, EventTriggerType.PointerUp, _ =>
            {
                button.TextPressed = false;
                ApplyPersuasionTextColor(button, button.TextView.Button.interactable);
            });
        }

        static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        static void ApplyPersuasionTextColor(ButtonView button, bool enabled)
        {
            if (button?.TextView?.Label == null)
                return;

            button.TextView.Label.color = enabled && (button.TextHovered || button.TextPressed)
                ? Rgb(243, 237, 221)
                : enabled ? TextColor : DisabledColor;
        }

        MorrowindButtonView CreatePersuasionTextButton(string name, Transform parent)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            var hit = RuntimeUiFactory.CreateImage("Hit", root, new Color(1f, 1f, 1f, 0.001f));
            hit.raycastTarget = true;
            RuntimeUiFactory.Stretch(hit.rectTransform);

            var label = RuntimeUiFactory.CreateBitmapText("Label", root, _theme?.DefaultFont, 1f, TextColor, BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.raycastTarget = false;
            RuntimeUiFactory.SetInset(label.rectTransform, 0f, 0f, 0f, 0f);

            var unityButton = root.gameObject.AddComponent<Button>();
            unityButton.targetGraphic = label;
            unityButton.transition = Selectable.Transition.None;
            unityButton.navigation = new Navigation { mode = Navigation.Mode.None };

            return new MorrowindButtonView
            {
                Root = root,
                Button = unityButton,
                Label = label,
            };
        }

        void CenterOnce()
        {
            if (_hasCentered)
                return;
            _window.Root.anchoredPosition = Vector2.zero;
            _hasCentered = true;
        }

        static Color Rgb(byte r, byte g, byte b)
            => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
