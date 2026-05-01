using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class JournalWindowView
    {
        static readonly Color BodyTextColor = Color.black;
        static readonly Color HeaderTextColor = new(0.60f, 0f, 0f, 1f);
        static readonly Color QuestSelectedColor = new(0.60f, 0f, 0f, 1f);
        static readonly Color QuestTextColor = Color.black;
        static readonly Color QuestCompletedColor = new(0.38f, 0.38f, 0.38f, 1f);

        const float BookWidth = 565f;
        const float BookHeight = 390f;
        const float BackgroundX = -70f;
        const float BackgroundY = 0f;
        const float BackgroundWidth = 705f;
        const float BackgroundHeight = 390f;
        const float LeftPageX = 30f;
        const float RightPageX = 295f;
        const float PageY = 22f;
        const float PageWidth = 240f;
        const float PageHeight = 320f;
        const float PageTextHeight = 16f;
        const float JournalSectionBreak = 30f;
        const float QuestHeaderBreak = 24f;
        const float ButtonY = 350f;
        const float OptionsButtonX = 40f;
        const float PrevButtonX = 205f;
        const float NextButtonX = 300f;
        const float CloseButtonX = 460f;
        const float JournalButtonX = 460f;
        const float PageOneNumberX = 150f;
        const float PageTwoNumberX = 410f;
        const float PageNumberY = 350f;
        const float PageNumberWidth = 32f;
        const float PageNumberHeight = 16f;
        const float BookmarkX = 293f;
        const float BookmarkY = 0f;
        const float BookmarkWidth = 386f;
        const float BookmarkHeight = 350f;
        const float QuestListX = 8f;
        const float QuestListY = 40f;
        const float QuestListWidth = 226f;
        const float QuestListHeight = 212f;
        const float QuestRowHeight = 20f;

        sealed class QuestRow
        {
            public int DialogueIndex;
            public RectTransform Root;
            public BitmapTextGraphic Text;
            public Button Button;
        }

        sealed class PageTextLine
        {
            public RectTransform Root;
            public BitmapTextGraphic Text;
        }

        readonly RuntimeUiTheme _theme;
        readonly Action<int> _onQuestSelected;
        readonly Action<bool> _onShowAllChanged;
        readonly Action<int> _onPageChanged;
        readonly Action<bool> _onOverlayChanged;
        readonly Action _onJournalBookRequested;
        readonly Action _onCloseRequested;
        readonly bool _questOverlayAvailable;

        readonly RectTransform _root;
        readonly Image _background;
        readonly RectTransform _leftPageRoot;
        readonly RectTransform _rightPageRoot;
        readonly BitmapTextGraphic _pageOneNumber;
        readonly BitmapTextGraphic _pageTwoNumber;
        readonly RectTransform _overlayRoot;
        readonly RectTransform _questRowsRoot;
        readonly ScrollRect _questScroll;
        readonly List<QuestRow> _questRows = new();
        readonly List<PageTextLine> _leftPageLines = new();
        readonly List<PageTextLine> _rightPageLines = new();
        readonly JournalBookImageButtonView _optionsButton;
        readonly JournalBookImageButtonView _prevButton;
        readonly JournalBookImageButtonView _nextButton;
        readonly JournalBookImageButtonView _closeButton;
        readonly JournalBookImageButtonView _journalButton;
        readonly JournalBookImageButtonView _cancelButton;
        readonly Image _showAllButtonImage;
        readonly Image _showActiveButtonImage;
        readonly Image _questsButtonImage;

        JournalWindowViewModel _model;
        JournalBookPage[] _pages = Array.Empty<JournalBookPage>();
        int _effectivePage;
        bool _suppressScrollEvents;

        public JournalWindowView(
            RectTransform parent,
            RuntimeUiTheme theme,
            Action<int> onQuestSelected,
            Action<bool> onShowAllChanged,
            Action<int> onPageChanged,
            Action<bool> onOverlayChanged,
            Action onJournalBookRequested,
            Action onCloseRequested)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _onQuestSelected = onQuestSelected;
            _onShowAllChanged = onShowAllChanged;
            _onPageChanged = onPageChanged;
            _onOverlayChanged = onOverlayChanged;
            _onJournalBookRequested = onJournalBookRequested;
            _onCloseRequested = onCloseRequested;
            _questOverlayAvailable = HasQuestOverlayAssets(theme);

            _root = RuntimeUiFactory.CreateAnchoredRect(
                "JournalBook",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(BookWidth, BookHeight)));
            _root.pivot = new Vector2(0.5f, 0.5f);

            _background = RuntimeUiFactory.CreateImage("BookBackground", _root, Color.white);
            _background.sprite = RequireSprite(UiBootstrapAssetKeys.JournalBookBackground);
            SetTopLeft(_background.rectTransform, BackgroundX, BackgroundY, BackgroundWidth, BackgroundHeight);

            _leftPageRoot = CreatePageRoot("LeftBookPage", LeftPageX);
            _rightPageRoot = CreatePageRoot("RightBookPage", RightPageX);
            _pageOneNumber = CreatePageNumber("PageOneNum", PageOneNumberX);
            _pageTwoNumber = CreatePageNumber("PageTwoNum", PageTwoNumberX);

            if (_questOverlayAvailable)
            {
                _optionsButton = JournalBookImageButtonView.Create(
                    "OptionsBTN",
                    _root,
                    theme,
                    UiBootstrapAssetKeys.JournalBookOptionsNormal,
                    UiBootstrapAssetKeys.JournalBookOptionsHighlight,
                    UiBootstrapAssetKeys.JournalBookOptionsHighlight,
                    () => _onOverlayChanged?.Invoke(true));
                _optionsButton.SetGeometry(OptionsButtonX, ButtonY, 64f, 32f);
            }

            _prevButton = JournalBookImageButtonView.Create(
                "PrevPageBTN",
                _root,
                theme,
                UiBootstrapAssetKeys.JournalBookPrevNormal,
                UiBootstrapAssetKeys.JournalBookPrevHighlight,
                UiBootstrapAssetKeys.JournalBookPrevPressed,
                PreviousPage);
            _prevButton.SetGeometry(PrevButtonX, ButtonY, 48f, 32f);

            _nextButton = JournalBookImageButtonView.Create(
                "NextPageBTN",
                _root,
                theme,
                UiBootstrapAssetKeys.JournalBookNextNormal,
                UiBootstrapAssetKeys.JournalBookNextHighlight,
                UiBootstrapAssetKeys.JournalBookNextPressed,
                NextPage);
            _nextButton.SetGeometry(NextButtonX, ButtonY, 48f, 32f);

            _closeButton = JournalBookImageButtonView.Create(
                "CloseBTN",
                _root,
                theme,
                UiBootstrapAssetKeys.JournalBookCloseNormal,
                UiBootstrapAssetKeys.JournalBookCloseHighlight,
                UiBootstrapAssetKeys.JournalBookClosePressed,
                () => _onCloseRequested?.Invoke());
            _closeButton.SetGeometry(CloseButtonX, ButtonY, 48f, 32f);

            _journalButton = JournalBookImageButtonView.Create(
                "JournalBTN",
                _root,
                theme,
                UiBootstrapAssetKeys.JournalBookJournalNormal,
                UiBootstrapAssetKeys.JournalBookJournalHighlight,
                UiBootstrapAssetKeys.JournalBookJournalPressed,
                () => _onJournalBookRequested?.Invoke());
            _journalButton.SetGeometry(JournalButtonX, ButtonY, 64f, 32f);

            (_overlayRoot, _questRowsRoot, _questScroll, _showAllButtonImage, _showActiveButtonImage, _questsButtonImage, _cancelButton) = BuildQuestOverlay();
            if (_questScroll != null)
                _questScroll.onValueChanged.AddListener(_ => OnQuestScrollChanged());
            _root.gameObject.SetActive(false);
        }

        public RectTransform Root => _root;

        public bool IsInteracting => false;

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
        }

        public bool OwnsSelection(GameObject selected)
        {
            return selected != null && selected.transform.IsChildOf(_root);
        }

        public void Sync(JournalWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            _model = model;
            SetVisible(true);
            SyncQuestRows(model.Quests ?? Array.Empty<JournalQuestRowViewModel>());
            SyncBookPages(model);
            SyncButtons(model);
            SyncOverlay(model);
        }

        RectTransform CreatePageRoot(string name, float x)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);
            SetTopLeft(root, x, PageY, PageWidth, PageHeight);

            var raycast = root.gameObject.AddComponent<Image>();
            raycast.color = new Color(1f, 1f, 1f, 0.001f);
            raycast.raycastTarget = true;
            root.gameObject.AddComponent<JournalBookMouseWheelRouter>().Initialize(PreviousPage, NextPage);
            return root;
        }

        BitmapTextGraphic CreatePageNumber(string name, float x)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                name,
                _root,
                _theme.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(PageTextHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.raycastTarget = false;
            SetTopLeft(text.rectTransform, x, PageNumberY, PageNumberWidth, PageNumberHeight);
            return text;
        }

        (RectTransform root, RectTransform rowsRoot, ScrollRect scroll, Image showAllButton, Image showActiveButton, Image questsButton, JournalBookImageButtonView cancel) BuildQuestOverlay()
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                "OptionsOverlay",
                _root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);
            SetTopLeft(root, BookmarkX, BookmarkY, BookmarkWidth, BookmarkHeight);
            root.gameObject.SetActive(false);

            if (!_questOverlayAvailable)
                return (root, null, null, null, null, null, null);

            var bookmark = RuntimeUiFactory.CreateImage("Bookmark", root, Color.white);
            bookmark.sprite = RequireSprite(UiBootstrapAssetKeys.JournalBookBookmark);
            RuntimeUiFactory.Stretch(bookmark.rectTransform);

            var showAll = CreateStaticImage("ShowAllBTN", root, UiBootstrapAssetKeys.JournalBookQuestsAll, 83f, 15f, 72f, 32f);
            var showAllButton = showAll.gameObject.AddComponent<Button>();
            showAllButton.transition = Selectable.Transition.None;
            showAllButton.targetGraphic = showAll;
            showAllButton.navigation = new Navigation { mode = Navigation.Mode.None };
            showAllButton.onClick.AddListener(() => _onShowAllChanged?.Invoke(true));

            var showActive = CreateStaticImage("ShowActiveBTN", root, UiBootstrapAssetKeys.JournalBookQuestsActive, 76f, 15f, 96f, 32f);
            var showActiveButton = showActive.gameObject.AddComponent<Button>();
            showActiveButton.transition = Selectable.Transition.None;
            showActiveButton.targetGraphic = showActive;
            showActiveButton.navigation = new Navigation { mode = Navigation.Mode.None };
            showActiveButton.onClick.AddListener(() => _onShowAllChanged?.Invoke(false));

            var viewport = RuntimeUiFactory.CreateAnchoredRect(
                "QuestsListViewport",
                root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            viewport.pivot = new Vector2(0f, 1f);
            SetTopLeft(viewport, QuestListX, QuestListY, QuestListWidth, QuestListHeight);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "QuestsListRows",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            rowsRoot.pivot = new Vector2(0f, 1f);

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = rowsRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(24f);

            var quests = CreateStaticImage("QuestsBTN", root, UiBootstrapAssetKeys.JournalBookQuests, 148f, 265f, 56f, 32f);
            quests.raycastTarget = false;

            var cancel = JournalBookImageButtonView.Create(
                "CancelBTN",
                root,
                _theme,
                UiBootstrapAssetKeys.JournalBookCancelNormal,
                UiBootstrapAssetKeys.JournalBookCancelHighlight,
                UiBootstrapAssetKeys.JournalBookCancelPressed,
                () => _onOverlayChanged?.Invoke(false));
            cancel.SetGeometry(92f, 290f, 56f, 32f);
            return (root, rowsRoot, scroll, showAll, showActive, quests, cancel);
        }

        Image CreateStaticImage(string name, Transform parent, string key, float x, float y, float width, float height)
        {
            var image = RuntimeUiFactory.CreateImage(name, parent, Color.white);
            image.sprite = RequireSprite(key);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            SetTopLeft(image.rectTransform, x, y, width, height);
            return image;
        }

        void SyncBookPages(JournalWindowViewModel model)
        {
            _pages = JournalBookPaginator.Paginate(
                _theme.DefaultFont,
                PageTextHeight,
                PageWidth,
                PageHeight,
                BuildTextBlocks(model));

            _effectivePage = ResolveEffectivePage(model.Page, _pages.Length, model.Mode);
            SyncPage(_leftPageLines, _leftPageRoot, _effectivePage);
            SyncPage(_rightPageLines, _rightPageRoot, _effectivePage + 1);
            _pageOneNumber.Text = _effectivePage < _pages.Length ? (_effectivePage + 1).ToString() : string.Empty;
            _pageTwoNumber.Text = _effectivePage + 1 < _pages.Length ? (_effectivePage + 2).ToString() : string.Empty;
        }

        List<JournalBookTextBlock> BuildTextBlocks(JournalWindowViewModel model)
        {
            var blocks = new List<JournalBookTextBlock>();
            if (model.Mode == JournalWindowBookMode.Quest)
            {
                if (!string.IsNullOrWhiteSpace(model.SelectedQuestTitle))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = model.SelectedQuestTitle.Trim(),
                        Kind = JournalBookTextKind.Header,
                        SpaceAfter = QuestHeaderBreak,
                    });
                }

                AppendEntryBlocks(blocks, model.Entries, includeTimestamp: false);
            }
            else
            {
                bool hasQuestEntries = model.JournalEntries != null && model.JournalEntries.Length > 0;
                bool hasTopicEntries = model.TopicEntries != null && model.TopicEntries.Length > 0;
                if (!hasQuestEntries && !hasTopicEntries)
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = model.EmptyStateText,
                        Kind = JournalBookTextKind.Header,
                    });
                }
                else
                {
                    if (hasQuestEntries)
                        AppendEntryBlocks(blocks, model.JournalEntries, includeTimestamp: true);
                    if (hasTopicEntries)
                        AppendTopicEntryBlocks(blocks, model.TopicEntries);
                }
            }

            return blocks;
        }

        static void AppendEntryBlocks(List<JournalBookTextBlock> blocks, JournalEntryRowViewModel[] entries, bool includeTimestamp)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (includeTimestamp && !string.IsNullOrWhiteSpace(entry.TimestampText))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = entry.TimestampText.Trim(),
                        Kind = JournalBookTextKind.Header,
                    });
                }

                if (!string.IsNullOrWhiteSpace(entry.BodyText))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = entry.BodyText.Trim(),
                        Kind = JournalBookTextKind.Body,
                        SpaceAfter = JournalSectionBreak,
                    });
                }
            }
        }

        static void AppendTopicEntryBlocks(List<JournalBookTextBlock> blocks, JournalEntryRowViewModel[] entries)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!string.IsNullOrWhiteSpace(entry.StageText))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = entry.StageText.Trim(),
                        Kind = JournalBookTextKind.Header,
                    });
                }
                else if (!string.IsNullOrWhiteSpace(entry.TimestampText))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = entry.TimestampText.Trim(),
                        Kind = JournalBookTextKind.Header,
                    });
                }

                if (!string.IsNullOrWhiteSpace(entry.BodyText))
                {
                    blocks.Add(new JournalBookTextBlock
                    {
                        Text = entry.BodyText.Trim(),
                        Kind = JournalBookTextKind.Body,
                        SpaceAfter = JournalSectionBreak,
                    });
                }
            }
        }

        void SyncPage(List<PageTextLine> views, RectTransform parent, int pageIndex)
        {
            JournalBookPage page = pageIndex >= 0 && pageIndex < _pages.Length ? _pages[pageIndex] : null;
            int lineCount = page?.Lines.Count ?? 0;
            while (views.Count < lineCount)
                views.Add(CreatePageLine(parent, views.Count));

            for (int i = 0; i < views.Count; i++)
            {
                bool visible = i < lineCount;
                var view = views[i];
                view.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var line = page.Lines[i];
                view.Text.Text = line.Text ?? string.Empty;
                view.Text.color = line.Kind == JournalBookTextKind.Header ? HeaderTextColor : BodyTextColor;
                SetTopLeft(view.Root, 0f, line.Y, PageWidth, PageTextHeight);
            }
        }

        PageTextLine CreatePageLine(RectTransform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"Line_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);
            var text = RuntimeUiFactory.CreateBitmapText(
                "Text",
                root,
                _theme.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(PageTextHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.raycastTarget = false;
            RuntimeUiFactory.Stretch(text.rectTransform);
            return new PageTextLine { Root = root, Text = text };
        }

        void SyncButtons(JournalWindowViewModel model)
        {
            bool overlay = model.OverlayOpen && _questOverlayAvailable;
            _optionsButton?.SetVisible(_questOverlayAvailable && !overlay);
            _prevButton.SetVisible(!overlay && _effectivePage > 0);
            _nextButton.SetVisible(!overlay && _effectivePage + 2 < _pages.Length);
            _closeButton.SetVisible(!overlay && model.Mode == JournalWindowBookMode.Journal);
            _journalButton.SetVisible(!overlay && model.Mode == JournalWindowBookMode.Quest);
            _pageOneNumber.gameObject.SetActive(!overlay && _effectivePage < _pages.Length);
            _pageTwoNumber.gameObject.SetActive(!overlay && _effectivePage + 1 < _pages.Length);
        }

        void SyncOverlay(JournalWindowViewModel model)
        {
            bool visible = model.OverlayOpen && _questOverlayAvailable;
            _overlayRoot.gameObject.SetActive(visible);
            if (!visible)
                return;

            _showAllButtonImage.gameObject.SetActive(!model.ShowAll);
            _showActiveButtonImage.gameObject.SetActive(model.ShowAll);
            _questsButtonImage.gameObject.SetActive(true);
            _suppressScrollEvents = true;
            _questScroll.verticalNormalizedPosition = Mathf.Clamp01(model.QuestScrollY);
            _suppressScrollEvents = false;
        }

        void SyncQuestRows(JournalQuestRowViewModel[] quests)
        {
            if (_questRowsRoot == null)
                return;

            while (_questRows.Count < quests.Length)
                _questRows.Add(CreateQuestRow(_questRowsRoot, _questRows.Count));

            float rowHeight = RuntimeClassicUiMetrics.Ui(QuestRowHeight);
            for (int i = 0; i < _questRows.Count; i++)
            {
                bool visible = i < quests.Length;
                var row = _questRows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var quest = quests[i];
                row.DialogueIndex = quest.DialogueIndex;
                row.Root.anchoredPosition = new Vector2(0f, -rowHeight * i);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Text.Text = string.IsNullOrWhiteSpace(quest.Title) ? string.Empty : quest.Title.Trim();
                row.Text.color = quest.Selected
                    ? QuestSelectedColor
                    : quest.Finished ? QuestCompletedColor : QuestTextColor;
            }

            _questRowsRoot.sizeDelta = new Vector2(0f, rowHeight * quests.Length);
        }

        QuestRow CreateQuestRow(RectTransform parent, int index)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(QuestRowHeight);
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"QuestRow_{index}",
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

            var text = RuntimeUiFactory.CreateBitmapText(
                "Text",
                root,
                _theme.DefaultFont,
                1f,
                QuestTextColor,
                BitmapTextAlignment.Left);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(PageTextHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.raycastTarget = false;
            RuntimeUiFactory.SetInset(text.rectTransform, RuntimeClassicUiMetrics.Ui(4f), 0f, -RuntimeClassicUiMetrics.Ui(4f), 0f);

            var row = new QuestRow
            {
                DialogueIndex = -1,
                Root = root,
                Text = text,
                Button = button,
            };
            button.onClick.AddListener(() => _onQuestSelected?.Invoke(row.DialogueIndex));
            return row;
        }

        void PreviousPage()
        {
            if (_model == null || _model.OverlayOpen)
                return;
            _onPageChanged?.Invoke(Mathf.Max(0, _effectivePage - 2));
        }

        void NextPage()
        {
            if (_model == null || _model.OverlayOpen)
                return;
            _onPageChanged?.Invoke(Mathf.Min(Mathf.Max(0, _pages.Length - 1), _effectivePage + 2));
        }

        void OnQuestScrollChanged()
        {
            if (_suppressScrollEvents || _model == null)
                return;
            RuntimeShellRequestBridge.TrySetJournalScroll(_questScroll.verticalNormalizedPosition, 1f, out _);
        }

        int ResolveEffectivePage(int requestedPage, int pageCount, JournalWindowBookMode mode)
        {
            if (pageCount <= 0)
                return 0;

            int page = requestedPage;
            if (page < 0)
                page = mode == JournalWindowBookMode.Journal ? Math.Max(0, pageCount - 1) : 0;
            if ((page & 1) != 0)
                page--;
            return Math.Clamp(page, 0, Math.Max(0, pageCount - 1));
        }

        bool HasQuestOverlayAssets(RuntimeUiTheme theme)
        {
            return theme.GetBootstrapSprite(UiBootstrapAssetKeys.JournalBookOptionsNormal) != null
                && theme.GetBootstrapSprite(UiBootstrapAssetKeys.JournalBookOptionsHighlight) != null
                && theme.GetBootstrapSprite(UiBootstrapAssetKeys.JournalBookQuests) != null
                && theme.GetBootstrapSprite(UiBootstrapAssetKeys.JournalBookQuestsAll) != null
                && theme.GetBootstrapSprite(UiBootstrapAssetKeys.JournalBookQuestsActive) != null;
        }

        Sprite RequireSprite(string key)
        {
            Sprite sprite = _theme.GetBootstrapSprite(key);
            if (sprite == null)
                throw new InvalidOperationException($"Required journal book UI texture '{key}' is missing.");
            return sprite;
        }

        static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            rect.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
        }
    }

}
