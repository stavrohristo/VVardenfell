using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class BookReaderWindowView
    {
        const float BookWidth = 584f;
        const float BookHeight = 398f;
        const float BookBackgroundX = -71f;
        const float BookBackgroundY = 0f;
        const float BookBackgroundWidth = 728f;
        const float BookBackgroundHeight = 398f;
        const float LeftPageX = 30f;
        const float RightPageX = 310f;
        const float PageY = 15f;
        const float PageWidth = 250f;
        const float PageHeight = 328f;
        const float ButtonY = 358f;
        const float TakeBookX = 40f;
        const float PrevBookX = 205f;
        const float NextBookX = 330f;
        const float CloseBookX = 488f;
        const float PageNumberY = 358f;

        const float ScrollWidth = 512f;
        const float ScrollHeight = 372f;
        const float ScrollTakeX = 12f;
        const float ScrollTakeY = 14f;
        const float ScrollCloseX = 415f;
        const float ScrollCloseY = 20f;
        const float ScrollTextX = 60f;
        const float ScrollTextY = 84f;
        const float ScrollTextWidth = 410f;
        const float ScrollTextHeight = 235f;
        const float TextPixelHeight = 16f;

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _bookRoot;
        readonly RectTransform _leftPage;
        readonly RectTransform _rightPage;
        readonly BitmapTextGraphic _leftPageNumber;
        readonly BitmapTextGraphic _rightPageNumber;
        readonly JournalBookImageButtonView _bookTake;
        readonly JournalBookImageButtonView _bookPrev;
        readonly JournalBookImageButtonView _bookNext;
        readonly JournalBookImageButtonView _bookClose;
        readonly RectTransform _scrollRoot;
        readonly ScrollRect _scrollRect;
        readonly RectTransform _scrollContent;
        readonly JournalBookImageButtonView _scrollTake;
        readonly JournalBookImageButtonView _scrollClose;
        readonly List<GameObject> _leftBookElementObjects = new();
        readonly List<GameObject> _rightBookElementObjects = new();
        readonly List<GameObject> _scrollElementObjects = new();
        readonly BookReaderMarkupFormatter _formatter;

        BookReaderViewModel _model;
        BookReaderMarkupFormatter.Page[] _pages = Array.Empty<BookReaderMarkupFormatter.Page>();
        ulong _lastSignature;
        string _lastText;
        float _scrollContentHeight;
        bool _suppressScrollRequest;

        public BookReaderWindowView(RectTransform parent, RuntimeUiTheme theme, RuntimeInventoryIconService iconService)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
            _formatter = new BookReaderMarkupFormatter(theme.DefaultFont, iconService, RuntimeClassicUiMetrics.Ui(TextPixelHeight));

            _bookRoot = RuntimeUiFactory.CreateAnchoredRect(
                "BookReader",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(BookWidth, BookHeight)));
            _bookRoot.pivot = new Vector2(0.5f, 0.5f);

            var bookBackground = RuntimeUiFactory.CreateImage("BookBackground", _bookRoot, Color.white);
            bookBackground.sprite = RequireSprite(UiBootstrapAssetKeys.JournalBookBackground);
            SetTopLeft(bookBackground.rectTransform, BookBackgroundX, BookBackgroundY, BookBackgroundWidth, BookBackgroundHeight);

            _leftPage = CreateBookPage("LeftPage", LeftPageX);
            _rightPage = CreateBookPage("RightPage", RightPageX);
            _leftPageNumber = CreatePageNumber("LeftPageNumber", LeftPageX);
            _rightPageNumber = CreatePageNumber("RightPageNumber", RightPageX);

            _bookTake = CreateButton("TakeBTN", _bookRoot, UiBootstrapAssetKeys.JournalBookTakeNormal, UiBootstrapAssetKeys.JournalBookTakeHighlight, UiBootstrapAssetKeys.JournalBookTakePressed, RequestTake);
            _bookTake.SetGeometry(TakeBookX, ButtonY, 64f, 32f);
            _bookPrev = CreateButton("PrevBTN", _bookRoot, UiBootstrapAssetKeys.JournalBookPrevNormal, UiBootstrapAssetKeys.JournalBookPrevHighlight, UiBootstrapAssetKeys.JournalBookPrevPressed, RequestPrevious);
            _bookPrev.SetGeometry(PrevBookX, ButtonY, 48f, 32f);
            _bookNext = CreateButton("NextBTN", _bookRoot, UiBootstrapAssetKeys.JournalBookNextNormal, UiBootstrapAssetKeys.JournalBookNextHighlight, UiBootstrapAssetKeys.JournalBookNextPressed, RequestNext);
            _bookNext.SetGeometry(NextBookX, ButtonY, 48f, 32f);
            _bookClose = CreateButton("CloseBTN", _bookRoot, UiBootstrapAssetKeys.JournalBookCloseNormal, UiBootstrapAssetKeys.JournalBookCloseHighlight, UiBootstrapAssetKeys.JournalBookClosePressed, RequestClose);
            _bookClose.SetGeometry(CloseBookX, ButtonY, 48f, 32f);
            _bookRoot.gameObject.SetActive(false);

            _scrollRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ScrollReader",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(ScrollWidth, ScrollHeight)));
            _scrollRoot.pivot = new Vector2(0.5f, 0.5f);
            var scrollBackground = RuntimeUiFactory.CreateImage("ScrollBackground", _scrollRoot, Color.white);
            scrollBackground.sprite = RequireSprite(UiBootstrapAssetKeys.ScrollBackground);
            RuntimeUiFactory.Stretch(scrollBackground.rectTransform);

            _scrollTake = CreateButton("ScrollTakeBTN", _scrollRoot, UiBootstrapAssetKeys.JournalBookTakeNormal, UiBootstrapAssetKeys.JournalBookTakeHighlight, UiBootstrapAssetKeys.JournalBookTakePressed, RequestTake);
            _scrollTake.SetGeometry(ScrollTakeX, ScrollTakeY, 128f, 32f);
            _scrollClose = CreateButton("ScrollCloseBTN", _scrollRoot, UiBootstrapAssetKeys.JournalBookCloseNormal, UiBootstrapAssetKeys.JournalBookCloseHighlight, UiBootstrapAssetKeys.JournalBookClosePressed, RequestClose);
            _scrollClose.SetGeometry(ScrollCloseX, ScrollCloseY, 128f, 32f);
            (_scrollRect, _scrollContent) = CreateScrollTextArea();
            _scrollRect.onValueChanged.AddListener(_ => OnScrollMoved());
            _scrollRoot.gameObject.SetActive(false);
        }

        public bool Visible => _bookRoot.gameObject.activeSelf || _scrollRoot.gameObject.activeSelf;

        public bool OwnsSelection(GameObject selected)
            => selected != null && (selected.transform.IsChildOf(_bookRoot) || selected.transform.IsChildOf(_scrollRoot));

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                _bookRoot.gameObject.SetActive(false);
                _scrollRoot.gameObject.SetActive(false);
            }
        }

        public void Sync(BookReaderViewModel model)
        {
            if (model == null || !model.Visible)
            {
                SetVisible(false);
                return;
            }

            _model = model;
            if (model.IsScroll)
                SyncScroll(model);
            else
                SyncBook(model);
        }

        void SyncBook(BookReaderViewModel model)
        {
            _scrollRoot.gameObject.SetActive(false);
            _bookRoot.gameObject.SetActive(true);
            EnsurePages(model);

            int current = Math.Clamp(model.CurrentPage, 0, Math.Max(0, _pages.Length - 1));
            if ((current & 1) != 0)
                current--;

            SyncPage(_leftPage, _pages.Length > current ? _pages[current] : null, _leftBookElementObjects);
            SyncPage(_rightPage, _pages.Length > current + 1 ? _pages[current + 1] : null, _rightBookElementObjects);
            _leftPageNumber.Text = _pages.Length > current ? (current + 1).ToString() : string.Empty;
            _rightPageNumber.Text = _pages.Length > current + 1 ? (current + 2).ToString() : string.Empty;
            _bookTake.SetVisible(model.AllowTake);
            _bookPrev.SetVisible(current > 0);
            _bookNext.SetVisible(current + 2 < _pages.Length);
        }

        void SyncScroll(BookReaderViewModel model)
        {
            _bookRoot.gameObject.SetActive(false);
            _scrollRoot.gameObject.SetActive(true);
            _scrollTake.SetVisible(model.AllowTake);

            if (_lastSignature == model.ContentSignature && string.Equals(_lastText, model.Text, StringComparison.Ordinal))
            {
                ApplyScrollOffset(model.ScrollOffset);
                return;
            }

            ClearObjects(_scrollElementObjects);
            _lastSignature = model.ContentSignature;
            _lastText = model.Text;
            var page = _formatter.FormatScroll(model.Text, RuntimeClassicUiMetrics.Ui(ScrollTextWidth), shrinkEsm3Text: true, out float contentHeight);
            _scrollContentHeight = Math.Max(RuntimeClassicUiMetrics.Ui(ScrollTextHeight), contentHeight);
            _scrollContent.sizeDelta = new Vector2(RuntimeClassicUiMetrics.Ui(ScrollTextWidth), _scrollContentHeight);
            CreateElements(_scrollContent, page, _scrollElementObjects);
            ApplyScrollOffset(model.ScrollOffset);
        }

        void EnsurePages(BookReaderViewModel model)
        {
            if (_lastSignature == model.ContentSignature && string.Equals(_lastText, model.Text, StringComparison.Ordinal))
                return;

            _lastSignature = model.ContentSignature;
            _lastText = model.Text;
            _pages = _formatter.FormatPages(
                model.Text,
                RuntimeClassicUiMetrics.Ui(PageWidth),
                RuntimeClassicUiMetrics.Ui(PageHeight),
                shrinkEsm3Text: true);
        }

        void SyncPage(RectTransform root, BookReaderMarkupFormatter.Page page, List<GameObject> pool)
        {
            ClearObjects(pool);
            if (page != null)
                CreateElements(root, page, pool);
        }

        void CreateElements(RectTransform parent, BookReaderMarkupFormatter.Page page, List<GameObject> created)
        {
            for (int i = 0; i < page.Elements.Count; i++)
            {
                var element = page.Elements[i];
                if (element.Image != null)
                {
                    var image = RuntimeUiFactory.CreateImage("BookImage", parent, Color.white);
                    image.sprite = element.Image;
                    image.preserveAspect = false;
                    image.raycastTarget = false;
                    SetTopLeftUi(image.rectTransform, element.X, element.Y, element.Width, element.Height);
                    image.rectTransform.localScale = new Vector3(1f, -1f, 1f);
                    created.Add(image.gameObject);
                }
                else
                {
                    var lineRoot = RuntimeUiFactory.CreateAnchoredRect(
                        "BookTextLine",
                        parent,
                        new Vector2(0f, 1f),
                        new Vector2(0f, 1f),
                        Vector2.zero,
                        Vector2.zero);
                    var text = RuntimeUiFactory.CreateBitmapText("BookText", lineRoot, _theme.DefaultFont, 1f, element.Color, element.Alignment);
                    text.PixelHeight = RuntimeClassicUiMetrics.Ui(TextPixelHeight);
                    text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
                    text.Text = element.Text;
                    text.raycastTarget = false;
                    RuntimeUiFactory.Stretch(text.rectTransform);
                    SetTopLeftUi(lineRoot, element.X, element.Y, element.Width, Mathf.Max(element.Height, RuntimeClassicUiMetrics.Ui(TextPixelHeight + 2f)));
                    created.Add(lineRoot.gameObject);
                }
            }
        }

        RectTransform CreateBookPage(string name, float x)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(name, _bookRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            root.pivot = new Vector2(0f, 1f);
            SetTopLeft(root, x, PageY, PageWidth, PageHeight);
            var raycast = root.gameObject.AddComponent<Image>();
            raycast.color = new Color(1f, 1f, 1f, 0.001f);
            raycast.raycastTarget = true;
            root.gameObject.AddComponent<JournalBookMouseWheelRouter>().Initialize(RequestPrevious, RequestNext);
            return root;
        }

        BitmapTextGraphic CreatePageNumber(string name, float x)
        {
            var text = RuntimeUiFactory.CreateBitmapText(name, _bookRoot, _theme.DefaultFont, 1f, Color.black, BitmapTextAlignment.Center);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(TextPixelHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            text.raycastTarget = false;
            SetTopLeft(text.rectTransform, x, PageNumberY, PageWidth, 16f);
            return text;
        }

        (ScrollRect scroll, RectTransform content) CreateScrollTextArea()
        {
            var root = RuntimeUiFactory.CreateAnchoredRect("ScrollTextArea", _scrollRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            root.pivot = new Vector2(0f, 1f);
            SetTopLeft(root, ScrollTextX, ScrollTextY, ScrollTextWidth, ScrollTextHeight);
            var mask = root.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            var content = RuntimeUiFactory.CreateAnchoredRect("ScrollContent", root, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.viewport = root;
            scroll.content = content;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(40f);
            return (scroll, content);
        }

        JournalBookImageButtonView CreateButton(string name, Transform parent, string normal, string highlight, string pressed, Action action)
            => JournalBookImageButtonView.Create(name, parent, _theme, normal, highlight, pressed, action);

        void RequestClose() => RuntimeShellRequestBridge.TryBookReaderClose(out _);
        void RequestNext() => RuntimeShellRequestBridge.TryBookReaderNextPage(out _);
        void RequestPrevious() => RuntimeShellRequestBridge.TryBookReaderPreviousPage(out _);
        void RequestTake() => RuntimeShellRequestBridge.TryBookReaderTake(out _);

        void ApplyScrollOffset(float offset)
        {
            float maxOffset = Math.Max(0f, _scrollContentHeight - RuntimeClassicUiMetrics.Ui(ScrollTextHeight));
            float clamped = Mathf.Clamp(offset, 0f, maxOffset);
            _suppressScrollRequest = true;
            _scrollRect.verticalNormalizedPosition = maxOffset <= 0f ? 1f : 1f - clamped / maxOffset;
            _suppressScrollRequest = false;
        }

        void OnScrollMoved()
        {
            if (_suppressScrollRequest || !_scrollRoot.gameObject.activeSelf)
                return;

            float maxOffset = Math.Max(0f, _scrollContentHeight - RuntimeClassicUiMetrics.Ui(ScrollTextHeight));
            float offset = maxOffset <= 0f ? 0f : (1f - _scrollRect.verticalNormalizedPosition) * maxOffset;
            RuntimeShellRequestBridge.TrySetBookReaderScroll(offset, out _);
        }

        void ClearObjects(List<GameObject> objects)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                    UnityEngine.Object.Destroy(objects[i]);
            }
            objects.Clear();
        }

        Sprite RequireSprite(string key)
        {
            Sprite sprite = _theme.GetBootstrapSprite(key);
            if (sprite == null)
                throw new InvalidOperationException($"[VVardenfell][Books] Required book UI texture '{key}' is missing.");
            return sprite;
        }

        static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
            => SetTopLeftUi(rect, RuntimeClassicUiMetrics.Ui(x), RuntimeClassicUiMetrics.Ui(y), RuntimeClassicUiMetrics.Ui(width), RuntimeClassicUiMetrics.Ui(height));

        static void SetTopLeftUi(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
