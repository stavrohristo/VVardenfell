using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class DialogueWindowView
    {
        const float WindowWidth = 700f;
        const float WindowHeight = 430f;
        const float CaptionHeight = 28f;
        const float ClientInset = 8f;
        const float ResponseWidth = 470f;
        const float TopicWidth = 190f;
        const float FooterHeight = 30f;
        const float Gap = 8f;
        const float TopicRowHeight = 20f;
        const float ChoicePaneHeight = 96f;
        const float ChoiceRowHeight = 22f;

        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color HeaderTextColor = new(0.96f, 0.72f, 0.36f);
        static readonly Color TopicTextColor = new(0.90f, 0.78f, 0.54f);
        static readonly Color TopicSelectedColor = new(1.00f, 0.88f, 0.54f);
        static readonly Color PanelColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color ButtonColor = new(0.12f, 0.10f, 0.08f, 0.88f);

        sealed class TopicRowView
        {
            public int DialogueIndex;
            public RectTransform Root;
            public BitmapTextGraphic Label;
            public Button Button;
        }

        sealed class ChoiceRowView
        {
            public int Value;
            public RectTransform Root;
            public BitmapTextGraphic Label;
            public Button Button;
        }

        readonly RuntimeUiTheme _theme;
        readonly Action<int> _onTopicSelected;
        readonly Action<int> _onChoiceSelected;
        readonly Action _onGoodbye;
        readonly Action _onClose;
        readonly MorrowindWindowView _window;
        readonly RectTransform _responsePaneRoot;
        readonly BitmapTextGraphic _responseText;
        readonly ScrollRect _responseScroll;
        readonly RectTransform _responseContent;
        readonly RectTransform _choicePaneRoot;
        readonly RectTransform _choiceRowsRoot;
        readonly ScrollRect _choiceScroll;
        readonly RectTransform _topicRowsRoot;
        readonly ScrollRect _topicScroll;
        readonly MorrowindButtonView _goodbyeButton;
        readonly MorrowindButtonView _closeButton;
        readonly List<TopicRowView> _topicRows = new();
        readonly List<ChoiceRowView> _choiceRows = new();

        public DialogueWindowView(
            RectTransform parent,
            RuntimeUiTheme theme,
            Action<int> onTopicSelected,
            Action<int> onChoiceSelected,
            Action onGoodbye,
            Action onClose)
        {
            _theme = theme;
            _onTopicSelected = onTopicSelected;
            _onChoiceSelected = onChoiceSelected;
            _onGoodbye = onGoodbye;
            _onClose = onClose;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "DialogueWindow",
                parent,
                theme,
                "Dialogue",
                RuntimeClassicUiMetrics.Layout(CaptionHeight),
                RuntimeClassicUiMetrics.Layout(ClientInset),
                0.88f,
                RuntimeClassicUiMetrics.Layout(RuntimeClassicUiFontSizes.Caption),
                HeaderTextColor);
            _window.Root.anchorMin = new Vector2(0.5f, 0.5f);
            _window.Root.anchorMax = new Vector2(0.5f, 0.5f);
            _window.Root.pivot = new Vector2(0.5f, 0.5f);
            _window.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(WindowWidth, WindowHeight));
            _window.Root.anchoredPosition = Vector2.zero;

            (_responsePaneRoot, _responseScroll, _responseContent, _responseText) = BuildResponsePane();
            (_choicePaneRoot, _choiceScroll, _choiceRowsRoot) = BuildChoicePane();
            (_topicScroll, _topicRowsRoot) = BuildTopicPane();
            (_goodbyeButton, _closeButton) = BuildFooter();
            _window.Root.gameObject.SetActive(false);
        }

        public bool OwnsSelection(GameObject selected)
            => selected != null && selected.transform.IsChildOf(_window.Root);

        public void SetVisible(bool visible)
        {
            if (_window.Root != null && _window.Root.gameObject.activeSelf != visible)
                _window.Root.gameObject.SetActive(visible);
        }

        public void Sync(DialogueWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.SpeakerName) ? "Dialogue" : model.SpeakerName.Trim();
            SyncResponses(model.Lines ?? Array.Empty<DialogueResponseLineViewModel>());
            SyncChoices(model.Choices ?? Array.Empty<DialogueChoiceRowViewModel>());
            SyncTopics(model.Topics ?? Array.Empty<DialogueTopicRowViewModel>());
            SetButtonEnabled(_goodbyeButton, true);
            SetButtonEnabled(_closeButton, true);
        }

        (RectTransform root, ScrollRect scroll, RectTransform content, BitmapTextGraphic text) BuildResponsePane()
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                "ResponsePane",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(ResponseWidth, -FooterHeight - Gap)));
            root.pivot = new Vector2(0f, 0.5f);
            root.anchoredPosition = Vector2.zero;

            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThinFrame(_theme), PanelColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", frame.Client);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            content.pivot = new Vector2(0f, 1f);

            var text = RuntimeUiFactory.CreateBitmapText("Responses", content, _theme.DefaultFont, 1f, BodyTextColor, BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.WrapMode = BitmapTextWrapMode.Word;
            text.raycastTarget = false;
            RuntimeUiFactory.SetInset(
                text.rectTransform,
                RuntimeClassicUiMetrics.Ui(8f),
                RuntimeClassicUiMetrics.Ui(8f),
                -RuntimeClassicUiMetrics.Ui(8f),
                -RuntimeClassicUiMetrics.Ui(8f));

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return (root, scroll, content, text);
        }

        (RectTransform root, ScrollRect scroll, RectTransform rowsRoot) BuildChoicePane()
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                "ChoicePane",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, RuntimeClassicUiMetrics.Ui(FooterHeight + Gap)),
                RuntimeClassicUiMetrics.Ui(new Vector2(ResponseWidth, ChoicePaneHeight)));
            root.pivot = new Vector2(0f, 0f);

            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThinFrame(_theme), PanelColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", frame.Client);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ChoiceRows",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            rowsRoot.pivot = new Vector2(0f, 1f);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = rowsRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            root.gameObject.SetActive(false);
            return (root, scroll, rowsRoot);
        }

        (ScrollRect scroll, RectTransform rowsRoot) BuildTopicPane()
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                "TopicPane",
                _window.Client,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(TopicWidth, -FooterHeight - Gap)));
            root.pivot = new Vector2(1f, 0.5f);
            root.anchoredPosition = Vector2.zero;

            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThinFrame(_theme), PanelColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", frame.Client);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "TopicRows",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            rowsRoot.pivot = new Vector2(0f, 1f);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = rowsRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return (scroll, rowsRoot);
        }

        (MorrowindButtonView goodbye, MorrowindButtonView close) BuildFooter()
        {
            float buttonWidth = RuntimeClassicUiMetrics.Ui(100f);
            float buttonHeight = RuntimeClassicUiMetrics.Ui(24f);
            float spacing = RuntimeClassicUiMetrics.Ui(8f);

            var goodbye = RuntimeUiFactory.CreateMorrowindButton("GoodbyeButton", _window.Client, _theme, "Goodbye", 1f, BodyTextColor, ButtonColor);
            goodbye.Root.anchorMin = new Vector2(1f, 0f);
            goodbye.Root.anchorMax = new Vector2(1f, 0f);
            goodbye.Root.pivot = new Vector2(1f, 0f);
            goodbye.Root.anchoredPosition = new Vector2(-buttonWidth - spacing, 0f);
            goodbye.Root.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            goodbye.Button.onClick.AddListener(() => _onGoodbye?.Invoke());

            var close = RuntimeUiFactory.CreateMorrowindButton("CloseButton", _window.Client, _theme, "Close", 1f, BodyTextColor, ButtonColor);
            close.Root.anchorMin = new Vector2(1f, 0f);
            close.Root.anchorMax = new Vector2(1f, 0f);
            close.Root.pivot = new Vector2(1f, 0f);
            close.Root.anchoredPosition = Vector2.zero;
            close.Root.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            close.Button.onClick.AddListener(() => _onClose?.Invoke());
            return (goodbye, close);
        }

        void SyncResponses(DialogueResponseLineViewModel[] lines)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!string.IsNullOrWhiteSpace(line.Title))
                {
                    if (builder.Length > 0)
                        builder.AppendLine();
                    builder.Append(line.Title.Trim());
                    builder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(line.Body))
                {
                    builder.Append(line.Body.Trim());
                    builder.AppendLine();
                }
            }

            _responseText.Text = builder.ToString().TrimEnd();
            float minHeight = RuntimeClassicUiMetrics.Ui(300f);
            float estimatedLines = Math.Max(1f, _responseText.Text.Length / 64f + lines.Length * 2f);
            float height = Math.Max(minHeight, estimatedLines * RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body + 4f));
            _responseContent.sizeDelta = new Vector2(0f, height);
            _responseScroll.verticalNormalizedPosition = 0f;
        }

        void SyncChoices(DialogueChoiceRowViewModel[] choices)
        {
            bool hasChoices = choices.Length > 0;
            _choicePaneRoot.gameObject.SetActive(hasChoices);
            _responsePaneRoot.sizeDelta = hasChoices
                ? RuntimeClassicUiMetrics.Ui(new Vector2(ResponseWidth, -FooterHeight - ChoicePaneHeight - Gap * 2f))
                : RuntimeClassicUiMetrics.Ui(new Vector2(ResponseWidth, -FooterHeight - Gap));

            while (_choiceRows.Count < choices.Length)
                _choiceRows.Add(CreateChoiceRow(_choiceRowsRoot, _choiceRows.Count));

            float rowHeight = RuntimeClassicUiMetrics.Ui(ChoiceRowHeight);
            for (int i = 0; i < _choiceRows.Count; i++)
            {
                bool visible = i < choices.Length;
                var row = _choiceRows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var choice = choices[i];
                row.Value = choice.Value;
                row.Root.anchoredPosition = new Vector2(0f, -rowHeight * i);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Label.Text = string.IsNullOrWhiteSpace(choice.Text) ? string.Empty : choice.Text.Trim();
            }

            _choiceRowsRoot.sizeDelta = new Vector2(0f, rowHeight * choices.Length);
            _choiceScroll.verticalNormalizedPosition = 1f;
        }

        void SyncTopics(DialogueTopicRowViewModel[] topics)
        {
            while (_topicRows.Count < topics.Length)
                _topicRows.Add(CreateTopicRow(_topicRowsRoot, _topicRows.Count));

            float rowHeight = RuntimeClassicUiMetrics.Ui(TopicRowHeight);
            for (int i = 0; i < _topicRows.Count; i++)
            {
                bool visible = i < topics.Length;
                var row = _topicRows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var topic = topics[i];
                row.DialogueIndex = topic.DialogueIndex;
                row.Root.anchoredPosition = new Vector2(0f, -rowHeight * i);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Label.Text = string.IsNullOrWhiteSpace(topic.Title) ? string.Empty : topic.Title.Trim();
                row.Label.color = topic.Selected ? TopicSelectedColor : TopicTextColor;
            }

            _topicRowsRoot.sizeDelta = new Vector2(0f, rowHeight * topics.Length);
            _topicScroll.verticalNormalizedPosition = 1f;
        }

        TopicRowView CreateTopicRow(RectTransform parent, int index)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(TopicRowHeight);
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"TopicRow_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, rowHeight));
            root.pivot = new Vector2(0f, 1f);

            var hitArea = RuntimeUiFactory.CreateImage("HitArea", root, new Color(1f, 1f, 1f, 0.001f));
            RuntimeUiFactory.Stretch(hitArea.rectTransform);
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hitArea;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var label = RuntimeUiFactory.CreateBitmapText("Label", root, _theme.DefaultFont, 1f, TopicTextColor, BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.raycastTarget = false;
            RuntimeUiFactory.SetInset(label.rectTransform, RuntimeClassicUiMetrics.Ui(4f), 0f, -RuntimeClassicUiMetrics.Ui(4f), 0f);

            var row = new TopicRowView
            {
                DialogueIndex = -1,
                Root = root,
                Label = label,
                Button = button,
            };
            button.onClick.AddListener(() => _onTopicSelected?.Invoke(row.DialogueIndex));
            return row;
        }

        ChoiceRowView CreateChoiceRow(RectTransform parent, int index)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(ChoiceRowHeight);
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"ChoiceRow_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, rowHeight));
            root.pivot = new Vector2(0f, 1f);

            var hitArea = RuntimeUiFactory.CreateImage("HitArea", root, new Color(1f, 1f, 1f, 0.001f));
            RuntimeUiFactory.Stretch(hitArea.rectTransform);
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hitArea;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var label = RuntimeUiFactory.CreateBitmapText("Label", root, _theme.DefaultFont, 1f, TopicSelectedColor, BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.raycastTarget = false;
            RuntimeUiFactory.SetInset(label.rectTransform, RuntimeClassicUiMetrics.Ui(6f), 0f, -RuntimeClassicUiMetrics.Ui(6f), 0f);

            var row = new ChoiceRowView
            {
                Value = 0,
                Root = root,
                Label = label,
                Button = button,
            };
            button.onClick.AddListener(() => _onChoiceSelected?.Invoke(row.Value));
            return row;
        }

        static void SetButtonEnabled(MorrowindButtonView view, bool enabled)
        {
            if (view?.Button == null)
                return;

            view.Button.interactable = enabled;
            if (view.Frame?.Center != null)
                view.Frame.Center.color = enabled ? ButtonColor : new Color(0.08f, 0.07f, 0.06f, 0.66f);
        }
    }
}
