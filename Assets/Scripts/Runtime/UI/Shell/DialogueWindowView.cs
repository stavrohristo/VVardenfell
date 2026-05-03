using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class DialogueWindowView
    {
        const float WindowWidth = 588f;
        const float WindowHeight = 433f;
        const float MinWindowWidth = 380f;
        const float MinWindowHeight = 230f;
        const float CaptionHeight = 20f;
        const float ClientInset = 4f;
        const float OuterMargin = 8f;
        const float RightColumnWidth = 166f;
        const float ColumnGap = 9f;
        const float DispositionHeight = 18f;
        const float DispositionTopicGap = 5f;
        const float GoodbyeHeight = 23f;
        const float GoodbyeTopicGap = 7f;
        const float HistoryInset = 7f;
        const float ScrollbarWidth = 14f;
        const float TopicRowHeight = 22f;
        const float HistorySectionBreak = 9f;
        const float HistoryActionGap = 2f;

        static readonly Color NormalTextColor = Rgb(202, 165, 96);
        static readonly Color HeaderTextColor = Rgb(223, 201, 159);
        static readonly Color PressedTextColor = Rgb(243, 237, 221);
        static readonly Color AnswerTextColor = Rgb(150, 50, 30);
        static readonly Color DisabledTextColor = new(0.42f, 0.38f, 0.31f, 1f);
        static readonly Color SpecificTopicTextColor = new(0.45f, 0.50f, 0.80f, 1f);
        static readonly Color SpecificTopicHoverColor = new(0.60f, 0.60f, 0.85f, 1f);
        static readonly Color SpecificTopicPressedColor = new(0.30f, 0.35f, 0.75f, 1f);
        static readonly Color ExhaustedTopicTextColor = new(0.30f, 0.30f, 0.30f, 1f);
        static readonly Color ExhaustedTopicHoverColor = new(0.55f, 0.55f, 0.55f, 1f);
        static readonly Color ExhaustedTopicPressedColor = new(0.45f, 0.45f, 0.45f, 1f);
        static readonly Color PanelColor = new(0f, 0f, 0f, 0.34f);
        static readonly Color ButtonColor = new(0.12f, 0.10f, 0.08f, 0.88f);
        static readonly Color ButtonDisabledColor = new(0.08f, 0.07f, 0.06f, 0.66f);
        static readonly Color DispositionFillColor = new(0.17f, 0.33f, 0.68f, 0.96f);

        sealed class ResponseBlockView
        {
            public RectTransform Root;
            public BitmapTextGraphic Title;
            public BitmapTextGraphic Body;
        }

        sealed class InlineActionRowView
        {
            public int Value;
            public bool IsGoodbye;
            public RectTransform Root;
            public BitmapTextGraphic Label;
            public Button Button;
        }

        sealed class TopicRowView
        {
            public int DialogueIndex;
            public RectTransform Root;
            public BitmapTextGraphic Label;
            public Button Button;
        }

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _viewport;
        readonly Action<int> _onTopicSelected;
        readonly Action<int> _onChoiceSelected;
        readonly Action _onGoodbye;
        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;

        readonly RectTransform _historyViewport;
        readonly RectTransform _historyContent;
        readonly RectTransform _historyScrollbarRoot;
        readonly ScrollRect _historyScroll;
        readonly Scrollbar _historyScrollbar;
        readonly RectTransform _topicPaneRoot;
        readonly RectTransform _topicRowsRoot;
        readonly ScrollRect _topicScroll;
        readonly RuntimeUiProgressBarView _dispositionBar;
        readonly BitmapTextGraphic _dispositionText;
        readonly MorrowindButtonView _goodbyeButton;

        readonly List<ResponseBlockView> _responseBlocks = new();
        readonly List<InlineActionRowView> _actionRows = new();
        readonly List<TopicRowView> _topicRows = new();

        DialogueWindowViewModel _activeModel;
        bool _hasCentered;

        public DialogueWindowView(
            RectTransform parent,
            RuntimeUiTheme theme,
            Action<int> onTopicSelected,
            Action<int> onChoiceSelected,
            Action onGoodbye)
        {
            _theme = theme;
            _viewport = parent;
            _onTopicSelected = onTopicSelected;
            _onChoiceSelected = onChoiceSelected;
            _onGoodbye = onGoodbye;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "DialogueWindow",
                parent,
                theme,
                "Dialogue",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.88f,
                RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Caption),
                HeaderTextColor);
            _window.Root.anchorMin = new Vector2(0f, 1f);
            _window.Root.anchorMax = new Vector2(0f, 1f);
            _window.Root.pivot = new Vector2(0f, 1f);
            _window.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(WindowWidth, WindowHeight));

            _dragHandle = _window.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _dragHandle.Initialize(_window.Root, _viewport, LayoutActiveModel);
            _resizeHandle = RuntimeWindowSurfaceUtility.AttachResizeHandle(
                _window,
                _viewport,
                RuntimeClassicUiMetrics.Ui(new Vector2(MinWindowWidth, MinWindowHeight)),
                LayoutActiveModel);

            (_historyViewport, _historyScroll, _historyContent, _historyScrollbarRoot, _historyScrollbar) = BuildHistoryPane();
            (_dispositionBar, _dispositionText, _topicPaneRoot, _topicScroll, _topicRowsRoot, _goodbyeButton) = BuildRightColumn();
            _window.Root.gameObject.SetActive(false);
        }

        public RectTransform Root => _window.Root;

        public bool OwnsSelection(GameObject selected)
            => selected != null && selected.transform.IsChildOf(_window.Root);

        public void SetVisible(bool visible)
        {
            if (_window.Root != null && _window.Root.gameObject.activeSelf != visible)
                _window.Root.gameObject.SetActive(visible);

            if (visible)
                CenterOnce();
        }

        public void Sync(DialogueWindowViewModel model)
        {
            _activeModel = model;
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.SpeakerName) ? "Dialogue" : model.SpeakerName.Trim();
            SyncDisposition(model);
            SyncTopics(model.Topics ?? Array.Empty<DialogueTopicRowViewModel>(), model.TopicsEnabled);
            SyncGoodbyeButton(model);
            SyncHistory(model);
            LayoutRightColumn(model.DispositionVisible);
        }

        (RectTransform viewport, ScrollRect scroll, RectTransform content, RectTransform scrollbarRoot, Scrollbar scrollbar) BuildHistoryPane()
        {
            var root = RuntimeUiFactory.CreateAnchorRect(
                "HistoryPane",
                _window.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(OuterMargin, OuterMargin)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-(RightColumnWidth + OuterMargin + ColumnGap), -OuterMargin)));

            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThinFrame(_theme), PanelColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var viewport = RuntimeUiFactory.CreateAnchorRect(
                "Viewport",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(HistoryInset, HistoryInset)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-HistoryInset, -HistoryInset)));
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = RuntimeUiFactory.CreateAnchoredRect(
                "HistoryContent",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            content.pivot = new Vector2(0f, 1f);

            var scrollbarRoot = RuntimeUiFactory.CreateAnchoredRect(
                "VScroll",
                frame.Client,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(-RuntimeClassicUiMetrics.Ui(HistoryInset), 0f),
                new Vector2(RuntimeClassicUiMetrics.Ui(ScrollbarWidth), 0f));
            scrollbarRoot.pivot = new Vector2(1f, 0.5f);

            var track = RuntimeUiFactory.CreateImage("Track", scrollbarRoot, new Color(0f, 0f, 0f, 0.42f));
            track.raycastTarget = true;
            RuntimeUiFactory.Stretch(track.rectTransform);

            var handle = RuntimeUiFactory.CreateImage("Handle", scrollbarRoot, new Color(0.54f, 0.43f, 0.22f, 0.95f));
            handle.raycastTarget = true;
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(1f, 0f);
            handle.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 0f));
            handle.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, 36f));

            var scrollbar = scrollbarRoot.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbarRoot.gameObject.SetActive(false);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(24f);
            return (viewport, scroll, content, scrollbarRoot, scrollbar);
        }

        (RuntimeUiProgressBarView disposition, BitmapTextGraphic dispositionText, RectTransform topicPane, ScrollRect topicScroll, RectTransform topicRows, MorrowindButtonView goodbye) BuildRightColumn()
        {
            var column = RuntimeUiFactory.CreateAnchoredRect(
                "RightColumn",
                _window.Client,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(-RuntimeClassicUiMetrics.Ui(OuterMargin), 0f),
                new Vector2(RuntimeClassicUiMetrics.Ui(RightColumnWidth), -RuntimeClassicUiMetrics.Ui(OuterMargin * 2f)));
            column.pivot = new Vector2(1f, 0.5f);

            var disposition = RuntimeUiFactory.CreateProgressBar(
                "Disposition",
                column,
                _theme,
                new Color(0f, 0f, 0f, 0.42f),
                DispositionFillColor);
            disposition.Root.anchorMin = new Vector2(0f, 1f);
            disposition.Root.anchorMax = new Vector2(1f, 1f);
            disposition.Root.pivot = new Vector2(0f, 1f);
            disposition.Root.anchoredPosition = Vector2.zero;
            disposition.Root.sizeDelta = new Vector2(0f, RuntimeClassicUiMetrics.Ui(DispositionHeight));

            var dispositionText = RuntimeUiFactory.CreateBitmapText(
                "DispositionText",
                disposition.Root,
                _theme?.DefaultFont,
                1f,
                PressedTextColor,
                BitmapTextAlignment.Center);
            dispositionText.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.BarOverlay);
            dispositionText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(dispositionText.rectTransform);

            var topicPane = RuntimeUiFactory.CreateAnchorRect(
                "TopicsList",
                column,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            var topicFrame = RuntimeUiFactory.CreateBorderFrame("Frame", topicPane, RuntimeUiFactory.ResolveThinFrame(_theme), PanelColor);
            RuntimeUiFactory.Stretch(topicFrame.Root);

            var topicViewport = RuntimeUiFactory.CreateAnchorRect(
                "Viewport",
                topicFrame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(3f, 3f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-3f, -3f)));
            var topicViewportImage = topicViewport.gameObject.AddComponent<Image>();
            topicViewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            topicViewportImage.raycastTarget = true;
            topicViewport.gameObject.AddComponent<RectMask2D>();

            var topicRows = RuntimeUiFactory.CreateAnchoredRect(
                "TopicRows",
                topicViewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            topicRows.pivot = new Vector2(0f, 1f);

            var topicScroll = topicPane.gameObject.AddComponent<ScrollRect>();
            topicScroll.viewport = topicViewport;
            topicScroll.content = topicRows;
            topicScroll.horizontal = false;
            topicScroll.vertical = true;
            topicScroll.movementType = ScrollRect.MovementType.Clamped;
            topicScroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(24f);

            var goodbye = RuntimeUiFactory.CreateMorrowindButton("GoodbyeButton", column, _theme, "Goodbye", 1f, NormalTextColor, ButtonColor);
            goodbye.Root.anchorMin = new Vector2(0f, 0f);
            goodbye.Root.anchorMax = new Vector2(1f, 0f);
            goodbye.Root.pivot = new Vector2(0f, 0f);
            goodbye.Root.anchoredPosition = Vector2.zero;
            goodbye.Root.sizeDelta = new Vector2(0f, RuntimeClassicUiMetrics.Ui(GoodbyeHeight));
            RuntimeUiFactory.SetInsetText(goodbye.Label.rectTransform, goodbye.Label, 8f, 3f, -8f, -3f);
            goodbye.Button.onClick.AddListener(() => _onGoodbye?.Invoke());
            return (disposition, dispositionText, topicPane, topicScroll, topicRows, goodbye);
        }

        void SyncDisposition(DialogueWindowViewModel model)
        {
            _dispositionBar.Root.gameObject.SetActive(model.DispositionVisible);
            if (!model.DispositionVisible)
                return;

            RuntimeUiFactory.SetProgressBarFill(_dispositionBar, model.DispositionFillNormalized);
            _dispositionText.Text = $"{Math.Clamp(model.DispositionValue, 0, 100)}/100";
        }

        void SyncGoodbyeButton(DialogueWindowViewModel model)
        {
            _goodbyeButton.Label.Text = string.IsNullOrWhiteSpace(model.GoodbyeText) ? "Goodbye" : model.GoodbyeText.Trim();
            SetButtonEnabled(_goodbyeButton, model.GoodbyeEnabled);
        }

        void SyncTopics(DialogueTopicRowViewModel[] topics, bool enabled)
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
                row.Button.interactable = enabled;
                row.Label.Text = string.IsNullOrWhiteSpace(topic.Title) ? string.Empty : topic.Title.Trim();
                ApplyTopicRowVisual(row, topic, enabled);
            }

            _topicRowsRoot.sizeDelta = new Vector2(0f, rowHeight * topics.Length);
            _topicScroll.verticalNormalizedPosition = 1f;
        }

        void SyncHistory(DialogueWindowViewModel model)
        {
            var lines = model.Lines ?? Array.Empty<DialogueResponseLineViewModel>();
            while (_responseBlocks.Count < lines.Length)
                _responseBlocks.Add(CreateResponseBlock(_historyContent, _responseBlocks.Count));

            for (int i = 0; i < _responseBlocks.Count; i++)
            {
                bool visible = i < lines.Length;
                var block = _responseBlocks[i];
                block.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                block.Title.Text = string.IsNullOrWhiteSpace(lines[i].Title) ? string.Empty : lines[i].Title.Trim();
                block.Body.Text = string.IsNullOrWhiteSpace(lines[i].Body) ? string.Empty : lines[i].Body.Trim();
            }

            int actionCount = (model.Choices?.Length ?? 0) + (model.ShowInlineGoodbye ? 1 : 0);
            while (_actionRows.Count < actionCount)
                _actionRows.Add(CreateInlineActionRow(_historyContent, _actionRows.Count));

            for (int i = 0; i < _actionRows.Count; i++)
            {
                bool visible = i < actionCount;
                var row = _actionRows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                if (i < (model.Choices?.Length ?? 0))
                {
                    var choice = model.Choices[i];
                    row.IsGoodbye = false;
                    row.Value = choice.Value;
                    row.Label.Text = string.IsNullOrWhiteSpace(choice.Text) ? string.Empty : choice.Text.Trim();
                }
                else
                {
                    row.IsGoodbye = true;
                    row.Value = 0;
                    row.Label.Text = string.IsNullOrWhiteSpace(model.GoodbyeText) ? "Goodbye" : model.GoodbyeText.Trim();
                }
            }

            LayoutHistoryContent(model, allowScrollbarToggle: true);
        }

        void LayoutActiveModel()
        {
            if (_activeModel == null || !_window.Root.gameObject.activeSelf)
                return;

            LayoutRightColumn(_activeModel.DispositionVisible);
            LayoutHistoryContent(_activeModel, allowScrollbarToggle: true);
        }

        void LayoutRightColumn(bool dispositionVisible)
        {
            float top = dispositionVisible
                ? RuntimeClassicUiMetrics.Ui(DispositionHeight + DispositionTopicGap)
                : 0f;
            float bottom = RuntimeClassicUiMetrics.Ui(GoodbyeHeight + GoodbyeTopicGap);
            _topicPaneRoot.offsetMin = new Vector2(0f, bottom);
            _topicPaneRoot.offsetMax = new Vector2(0f, -top);
        }

        void LayoutHistoryContent(DialogueWindowViewModel model, bool allowScrollbarToggle)
        {
            float width = ResolveHistoryTextWidth();
            float y = 0f;
            var lines = model.Lines ?? Array.Empty<DialogueResponseLineViewModel>();

            for (int i = 0; i < lines.Length; i++)
            {
                var block = _responseBlocks[i];
                y += i == 0 ? 0f : RuntimeClassicUiMetrics.Ui(HistorySectionBreak);
                block.Root.anchoredPosition = new Vector2(0f, -y);

                float blockHeight = 0f;
                block.Title.gameObject.SetActive(!string.IsNullOrWhiteSpace(block.Title.Text));
                block.Body.gameObject.SetActive(!string.IsNullOrWhiteSpace(block.Body.Text));

                if (block.Title.gameObject.activeSelf)
                {
                    float titleHeight = Mathf.Max(
                        RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body),
                        block.Title.MeasureHeightForWidth(width));
                    SetTopRect(block.Title.rectTransform, blockHeight, titleHeight);
                    blockHeight += titleHeight;
                }

                if (block.Body.gameObject.activeSelf)
                {
                    if (blockHeight > 0f)
                        blockHeight += RuntimeClassicUiMetrics.Ui(HistoryActionGap);

                    float bodyHeight = Mathf.Max(
                        RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body),
                        block.Body.MeasureHeightForWidth(width));
                    SetTopRect(block.Body.rectTransform, blockHeight, bodyHeight);
                    blockHeight += bodyHeight;
                }

                block.Root.sizeDelta = new Vector2(0f, blockHeight);
                y += blockHeight;
            }

            int actionCount = (model.Choices?.Length ?? 0) + (model.ShowInlineGoodbye ? 1 : 0);
            if (actionCount > 0)
                y += RuntimeClassicUiMetrics.Ui(HistorySectionBreak);

            for (int i = 0; i < actionCount; i++)
            {
                var row = _actionRows[i];
                float rowHeight = Mathf.Max(
                    RuntimeClassicUiMetrics.Ui(TopicRowHeight),
                    row.Label.MeasureHeightForWidth(width));
                row.Root.anchoredPosition = new Vector2(0f, -y);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                RuntimeUiFactory.Stretch(row.Label.rectTransform);
                y += rowHeight + RuntimeClassicUiMetrics.Ui(HistoryActionGap);
            }

            _historyContent.sizeDelta = new Vector2(0f, y);
            bool overflow = y > _historyViewport.rect.height + 1f;
            if (allowScrollbarToggle && _historyScrollbarRoot.gameObject.activeSelf != overflow)
            {
                SetHistoryScrollbarVisible(overflow);
                LayoutHistoryContent(model, allowScrollbarToggle: false);
                return;
            }

            _historyScroll.verticalNormalizedPosition = 0f;
        }

        ResponseBlockView CreateResponseBlock(RectTransform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"Response_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);

            var title = CreateHistoryText("Title", root, HeaderTextColor);
            var body = CreateHistoryText("Body", root, NormalTextColor);
            return new ResponseBlockView { Root = root, Title = title, Body = body };
        }

        BitmapTextGraphic CreateHistoryText(string name, RectTransform parent, Color color)
        {
            var text = RuntimeUiFactory.CreateBitmapText(name, parent, _theme?.DefaultFont, 1f, color, BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.WrapMode = BitmapTextWrapMode.Word;
            text.raycastTarget = false;
            return text;
        }

        InlineActionRowView CreateInlineActionRow(RectTransform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"InlineAction_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);

            var hitArea = RuntimeUiFactory.CreateImage("HitArea", root, new Color(1f, 1f, 1f, 0.001f));
            RuntimeUiFactory.Stretch(hitArea.rectTransform);

            var label = RuntimeUiFactory.CreateBitmapText("Label", root, _theme?.DefaultFont, 1f, AnswerTextColor, BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.WrapMode = BitmapTextWrapMode.Word;
            label.raycastTarget = false;
            RuntimeUiFactory.Stretch(label.rectTransform);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateAnswerTextColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var row = new InlineActionRowView { Root = root, Label = label, Button = button };
            button.onClick.AddListener(() =>
            {
                if (row.IsGoodbye)
                    _onGoodbye?.Invoke();
                else
                    _onChoiceSelected?.Invoke(row.Value);
            });
            return row;
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

            var label = RuntimeUiFactory.CreateBitmapText("Label", root, _theme?.DefaultFont, 1f, NormalTextColor, BitmapTextAlignment.Left);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.raycastTarget = false;
            RuntimeUiFactory.SetInset(label.rectTransform, RuntimeClassicUiMetrics.Ui(2f), 0f, -RuntimeClassicUiMetrics.Ui(1f), 0f);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateTopicTextColors(DialogueTopicVisualState.Normal, selected: false);
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var row = new TopicRowView { DialogueIndex = -1, Root = root, Label = label, Button = button };
            button.onClick.AddListener(() =>
            {
                if (row.Button.interactable)
                    _onTopicSelected?.Invoke(row.DialogueIndex);
            });
            return row;
        }

        void CenterOnce()
        {
            if (_hasCentered || _window.Root == null || _viewport == null)
                return;

            float viewportWidth = Mathf.Max(1f, _viewport.rect.width);
            float viewportHeight = Mathf.Max(1f, _viewport.rect.height);
            float width = Mathf.Max(1f, _window.Root.rect.width > 0f ? _window.Root.rect.width : _window.Root.sizeDelta.x);
            float height = Mathf.Max(1f, _window.Root.rect.height > 0f ? _window.Root.rect.height : _window.Root.sizeDelta.y);
            _window.Root.anchoredPosition = new Vector2(
                Mathf.Max(0f, (viewportWidth - width) * 0.5f),
                -Mathf.Max(0f, (viewportHeight - height) * 0.5f));
            _hasCentered = true;
        }

        float ResolveHistoryTextWidth()
        {
            float width = _historyViewport.rect.width;
            if (width <= 1f)
                width = RuntimeClassicUiMetrics.Ui(364f);
            return Mathf.Max(1f, width);
        }

        void SetHistoryScrollbarVisible(bool visible)
        {
            _historyScrollbarRoot.gameObject.SetActive(visible);
            _historyViewport.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(
                visible ? -(HistoryInset + ScrollbarWidth) : -HistoryInset,
                -HistoryInset));
        }

        static void SetTopRect(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }

        static void SetButtonEnabled(MorrowindButtonView view, bool enabled)
        {
            if (view?.Button == null)
                return;

            view.Button.interactable = enabled;
            if (view.Frame?.Center != null)
                view.Frame.Center.color = enabled ? ButtonColor : ButtonDisabledColor;
            if (view.Label != null)
                view.Label.color = enabled ? NormalTextColor : DisabledTextColor;
        }

        static void ApplyTopicRowVisual(TopicRowView row, DialogueTopicRowViewModel topic, bool enabled)
        {
            if (row == null)
                return;

            if (row.Button != null)
                row.Button.colors = CreateTopicTextColors(topic.VisualState, topic.Selected);

            if (row.Label != null)
                row.Label.color = !enabled ? DisabledTextColor : ResolveTopicTextColor(topic.VisualState, topic.Selected);
        }

        static ColorBlock CreateTopicTextColors(DialogueTopicVisualState state, bool selected)
        {
            var block = ColorBlock.defaultColorBlock;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.08f;
            block.normalColor = ResolveTopicTextColor(state, selected);
            block.highlightedColor = ResolveTopicHoverColor(state);
            block.pressedColor = ResolveTopicPressedColor(state);
            block.selectedColor = ResolveTopicHoverColor(state);
            block.disabledColor = DisabledTextColor;
            return block;
        }

        static Color ResolveTopicTextColor(DialogueTopicVisualState state, bool selected)
        {
            if (selected)
                return HeaderTextColor;

            return state switch
            {
                DialogueTopicVisualState.Specific => SpecificTopicTextColor,
                DialogueTopicVisualState.Exhausted => ExhaustedTopicTextColor,
                _ => NormalTextColor,
            };
        }

        static Color ResolveTopicHoverColor(DialogueTopicVisualState state)
            => state switch
            {
                DialogueTopicVisualState.Specific => SpecificTopicHoverColor,
                DialogueTopicVisualState.Exhausted => ExhaustedTopicHoverColor,
                _ => HeaderTextColor,
            };

        static Color ResolveTopicPressedColor(DialogueTopicVisualState state)
            => state switch
            {
                DialogueTopicVisualState.Specific => SpecificTopicPressedColor,
                DialogueTopicVisualState.Exhausted => ExhaustedTopicPressedColor,
                _ => PressedTextColor,
            };

        static ColorBlock CreateAnswerTextColors()
        {
            var block = ColorBlock.defaultColorBlock;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.08f;
            block.normalColor = AnswerTextColor;
            block.highlightedColor = HeaderTextColor;
            block.pressedColor = PressedTextColor;
            block.selectedColor = HeaderTextColor;
            block.disabledColor = DisabledTextColor;
            return block;
        }

        static Color Rgb(byte r, byte g, byte b)
            => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
