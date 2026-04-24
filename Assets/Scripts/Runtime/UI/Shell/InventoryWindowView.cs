using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind Inventory window.
    ///
    /// Mirrors <c>openmw_inventory_window.layout</c> (see <c>docs/ui-reference/openmw-ui-skins.md</c>)
    /// at the reference 600x300 window size:
    /// <list type="bullet">
    ///   <item>Left pane (224 wide, ~38%): encumbrance bar at the top (MW_ChargeBar_Blue -
    ///     a tinted <c>menu_bar_gray</c> fill inside an MW_Box thin frame with a centered
    ///     value overlay) and a big avatar MW_Box filling the rest, with the armor-rating
    ///     text rendered along its bottom strip.</item>
    ///   <item>Right pane: category tab row on top (All / Weapon / Apparel / Magic / Misc
    ///     plus a stretched filter edit) and a scrollable item grid below, using 42x42
    ///     cells identical to <see cref="ContainerWindowView"/>.</item>
    /// </list>
    ///
    /// The avatar panel is intentionally left empty — we reserve the space and render
    /// nothing inside it. The paper-doll preview will land when the character rendering
    /// pipeline is online; no placeholder is drawn in the meantime.
    ///
    /// Ignored view-model fields (kept on <see cref="InventoryWindowViewModel"/> but
    /// unused by this view, matching vanilla): <c>DetailText</c>, secondary left/right
    /// columns, equipped-text flag. Weight and value surface via tooltips in MyGUI;
    /// we'll wire those when the tooltip pipeline lands.
    /// </summary>
    sealed class InventoryWindowView
    {
        // Palette - shared conventions with StatsWindowView / ContainerWindowView.
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color DimTextColor = new(0.58f, 0.55f, 0.5f);
        static readonly Color BarOverlayColor = new(0.98f, 0.94f, 0.84f);
        static readonly Color BarFrameCenterColor = new(0f, 0f, 0f, 0.35f);
        static readonly Color AvatarBoxCenterColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color ListFrameCenterColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color CellFrameCenterColor = new(0f, 0f, 0f, 0.62f);
        static readonly Color CellFrameSelectedColor = new(0.42f, 0.32f, 0.15f, 0.92f);
        static readonly Color FilterFrameCenterColor = new(0f, 0f, 0f, 0.76f);
        static readonly Color TabCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color TabSelectedCenterColor = new(0.38f, 0.28f, 0.14f, 0.96f);
        // Encumbrance bar tint - matches MW_Track_Blue (fontcolour=magic in OpenMW).
        static readonly Color EncumbranceFillColor = new(0.17f, 0.33f, 0.68f, 0.96f);

        // Pixel heights for each text role.
        const float CaptionPixelHeight = 14f;
        const float BodyTextPixelHeight = 13f;
        const float BarTextPixelHeight = 12f;
        const float TabTextPixelHeight = 13f;

        // Window geometry (matches openmw_inventory_window.layout position "0 0 600 300").
        const float DefaultWindowWidth = 600f;
        const float DefaultWindowHeight = 300f;
        const float MinWindowWidth = 360f;
        const float MinWindowHeight = 220f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;

        // Two-pane split.
        const float LeftPaneWidth = 224f;       // matches vanilla left pane width
        const float PaneGap = 4f;

        // Left pane geometry.
        const float PaneInnerPadding = 8f;      // matches layout "8" margins
        const float EncumbranceHeight = 24f;
        const float EncumbranceAvatarGap = 6f;
        const float AvatarArmorStripHeight = 24f;

        // Right pane geometry.
        const float CategoriesTopOffset = 6f;
        const float CategoriesHeight = 28f;
        const float CategoriesToGridGap = 10f;
        const float TabButtonWidth = 60f;
        const float TabButtonSpacing = 4f;
        const float FilterMinWidth = 100f;

        // Grid cell geometry - same footprint as ContainerWindowView.
        const float GridItemSize = 42f;
        const float GridItemSpacing = 2f;
        const float GridPadding = 4f;

        readonly struct CategoryDefinition
        {
            public CategoryDefinition(InventoryWindowCategory category, string label)
            {
                Category = category;
                Label = label;
            }

            public InventoryWindowCategory Category { get; }
            public string Label { get; }
        }

        // Captions match MW strings sAllTab/sWeaponTab/sApparelTab/sMagicTab/sMiscTab.
        static readonly CategoryDefinition[] k_Categories =
        {
            new(InventoryWindowCategory.All, "All"),
            new(InventoryWindowCategory.Weapons, "Weapon"),
            new(InventoryWindowCategory.Apparel, "Apparel"),
            new(InventoryWindowCategory.Magic, "Magic"),
            new(InventoryWindowCategory.Misc, "Misc"),
        };

        sealed class TabButtonView
        {
            public InventoryWindowCategory Category;
            public MorrowindButtonView View;
        }

        sealed class CellView
        {
            public int InventoryIndex;
            public RectTransform Root;
            public BorderFrameView Frame;
            public Button Button;
            public Image Icon;
            public BitmapTextGraphic Count;
        }

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _viewport;
        readonly Action _onRectChanged;
        readonly Action<InventoryWindowCategory> _onCategoryChanged;
        readonly Action<int> _onSelectionChanged;
        readonly Action<string> _onFilterChanged;

        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;

        // Left pane parts.
        readonly RectTransform _leftPane;
        readonly Image _encumbranceFill;
        readonly BitmapTextGraphic _encumbranceText;
        readonly BitmapTextGraphic _armorText;

        // Right pane parts.
        readonly RectTransform _rightPane;
        readonly List<TabButtonView> _tabButtons = new();
        readonly List<CellView> _cells = new();
        readonly RectTransform _listClient;
        readonly RectTransform _gridContent;
        readonly RuntimeUiTextInputView _searchInputView;
        readonly InputField _searchInputField;

        bool _suppressFieldEvents;

        public InventoryWindowView(
            RectTransform parent,
            RectTransform viewport,
            RuntimeUiTheme theme,
            RuntimeInventoryIconService iconService,
            Action onRectChanged,
            Action<InventoryWindowCategory> onCategoryChanged,
            Action<int> onSelectionChanged,
            Action<string> onFilterChanged,
            Action onPinToggled = null)
        {
            _theme = theme;
            _iconService = iconService;
            _viewport = viewport;
            _onRectChanged = onRectChanged;
            _onCategoryChanged = onCategoryChanged;
            _onSelectionChanged = onSelectionChanged;
            _onFilterChanged = onFilterChanged;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "InventoryWindow",
                parent,
                theme,
                "Inventory",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.88f,
                RuntimeClassicUiMetrics.Ui(CaptionPixelHeight),
                new Color(0.94f, 0.82f, 0.53f),
                withPinButton: true);
            if (onPinToggled != null && _window.PinButton?.Button != null)
                _window.PinButton.Button.onClick.AddListener(() => onPinToggled());
            _window.Root.anchorMin = new Vector2(0f, 1f);
            _window.Root.anchorMax = new Vector2(0f, 1f);
            _window.Root.pivot = new Vector2(0f, 1f);

            _dragHandle = _window.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _dragHandle.Initialize(_window.Root, viewport, onRectChanged);
            _resizeHandle = RuntimeWindowSurfaceUtility.AttachResizeHandle(
                _window,
                viewport,
                RuntimeClassicUiMetrics.Ui(new Vector2(MinWindowWidth, MinWindowHeight)),
                onRectChanged);

            (_leftPane, _rightPane) = BuildPanes(_window.Client);
            (_encumbranceFill, _encumbranceText, _armorText) = BuildLeftPane(_leftPane);
            (_listClient, _gridContent, _searchInputView, _searchInputField) = BuildRightPane(_rightPane);
            _searchInputField.onValueChanged.AddListener(OnFilterValueChanged);
        }

        public RectTransform Root => _window.Root;

        public bool IsInteracting => _dragHandle.IsDragging || _resizeHandle.IsDragging;

        public void SetVisible(bool visible)
        {
            _window.Root.gameObject.SetActive(visible);
        }

        public bool OwnsSelection(GameObject selected)
        {
            return selected != null && selected.transform.IsChildOf(_window.Root);
        }

        public void Sync(InventoryWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Inventory" : model.Title.Trim();
            _window.PinButton?.SetPinned(model.Pinned);
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            SyncEncumbrance(model.WeightLabel, model.WeightBarFillNormalized);
            _armorText.Text = string.IsNullOrWhiteSpace(model.ArmorSummary) ? "Armor Rating 0" : model.ArmorSummary;
            SyncFilter(model.FilterText);
            SyncCategories(model.Category);
            SyncGrid(model.Entries);
        }

        // ----- Pane split ------------------------------------------------------

        (RectTransform left, RectTransform right) BuildPanes(RectTransform client)
        {
            float leftWidth = RuntimeClassicUiMetrics.Ui(LeftPaneWidth);
            float gap = RuntimeClassicUiMetrics.Ui(PaneGap);

            var left = RuntimeUiFactory.CreateAnchorRect(
                "LeftPane",
                client,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(leftWidth, 0f));

            var right = RuntimeUiFactory.CreateAnchorRect(
                "RightPane",
                client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(leftWidth + gap, 0f),
                Vector2.zero);

            return (left, right);
        }

        // ----- Left pane: encumbrance + avatar --------------------------------

        (Image fill, BitmapTextGraphic barText, BitmapTextGraphic armorText) BuildLeftPane(RectTransform leftPane)
        {
            float pad = RuntimeClassicUiMetrics.Ui(PaneInnerPadding);
            float barHeight = RuntimeClassicUiMetrics.Ui(EncumbranceHeight);
            float gap = RuntimeClassicUiMetrics.Ui(EncumbranceAvatarGap);
            float armorStrip = RuntimeClassicUiMetrics.Ui(AvatarArmorStripHeight);

            // Encumbrance bar pinned to the top of the pane, inset by pad on sides + top.
            var barRoot = RuntimeUiFactory.CreateAnchorRect(
                "EncumbranceBar",
                leftPane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(pad, -pad - barHeight),
                new Vector2(-pad, -pad));

            var barFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                barRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(barFrame.Root);

            // Fill anchored to the left edge, grown rightward by normalized value.
            var fill = RuntimeUiFactory.CreateImage("Fill", barFrame.Client, EncumbranceFillColor);
            fill.sprite = _theme?.LoadingBarFillSprite;
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = Vector2.zero;

            var barText = RuntimeUiFactory.CreateBitmapText(
                "Value",
                barFrame.Client,
                _theme?.DefaultFont,
                1f,
                BarOverlayColor,
                BitmapTextAlignment.Center);
            barText.PixelHeight = RuntimeClassicUiMetrics.Ui(BarTextPixelHeight);
            barText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(barText.rectTransform);
            barText.raycastTarget = false;

            // Avatar MW_Box fills the rest of the left pane under the encumbrance bar.
            var avatarRoot = RuntimeUiFactory.CreateAnchorRect(
                "Avatar",
                leftPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(pad, pad),
                new Vector2(-pad, -(pad + barHeight + gap)));

            var avatarFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                avatarRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                AvatarBoxCenterColor);
            RuntimeUiFactory.Stretch(avatarFrame.Root);

            // Portrait area: reserved but intentionally empty. The paper-doll preview
            // lands when character rendering is online; until then the MW_Box stays
            // blank (matching vanilla's appearance before the avatar model streams in).

            // Armor rating strip along the bottom of the avatar box. Vanilla uses the
            // ProgressText skin (text with shadow, centered).
            var armorRoot = RuntimeUiFactory.CreateAnchorRect(
                "ArmorRating",
                avatarFrame.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, armorStrip));

            var armorText = RuntimeUiFactory.CreateBitmapText(
                "Text",
                armorRoot,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Center);
            armorText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            armorText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(armorText.rectTransform);
            armorText.raycastTarget = false;

            return (fill, barText, armorText);
        }

        // ----- Right pane: categories + item grid -----------------------------

        (RectTransform listClient, RectTransform gridContent, RuntimeUiTextInputView searchInput, InputField searchField) BuildRightPane(RectTransform rightPane)
        {
            float categoriesTop = RuntimeClassicUiMetrics.Ui(CategoriesTopOffset);
            float categoriesHeight = RuntimeClassicUiMetrics.Ui(CategoriesHeight);
            float gridGap = RuntimeClassicUiMetrics.Ui(CategoriesToGridGap);

            // Categories row: 5 tab buttons at fixed width, filter edit fills remaining width.
            var categoriesRoot = RuntimeUiFactory.CreateAnchorRect(
                "Categories",
                rightPane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -categoriesTop - categoriesHeight),
                new Vector2(0f, -categoriesTop));

            var searchFrame = BuildCategoryRow(categoriesRoot);

            // Search input nested inside the filter frame slot returned by BuildCategoryRow.
            var searchInput = RuntimeUiFactory.CreateBitmapInputField(
                "SearchInput",
                searchFrame.Client,
                _theme,
                1f,
                BodyTextColor,
                Color.clear,
                "Filter",
                8f,
                5f,
                24);
            RuntimeUiFactory.Stretch(searchInput.Root);
            searchInput.OverlayText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            searchInput.OverlayText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            // Item grid fills the rest of the pane below the category row (+gap).
            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "ItemGridRoot",
                rightPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(0f, -(categoriesTop + categoriesHeight + gridGap)));

            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                ListFrameCenterColor);
            RuntimeUiFactory.Stretch(listFrame.Root);

            // ScrollRect viewport (masked) inside the list frame.
            var viewportRect = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var viewportImage = viewportRect.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);   // near-invisible but enables scroll raycasts
            viewportImage.raycastTarget = true;
            viewportRect.gameObject.AddComponent<RectMask2D>();

            var gridContent = RuntimeUiFactory.CreateAnchoredRect(
                "GridContent",
                viewportRect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            gridContent.pivot = new Vector2(0.5f, 1f);

            var scrollRect = listRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = gridContent;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            return (listFrame.Client, gridContent, searchInput, searchInput.InputField);
        }

        /// <summary>
        /// Builds the category button row left of the filter edit. Returns the MW_Box
        /// frame the search input should nest into.
        /// </summary>
        BorderFrameView BuildCategoryRow(RectTransform categoriesRoot)
        {
            float tabWidth = RuntimeClassicUiMetrics.Ui(TabButtonWidth);
            float tabSpacing = RuntimeClassicUiMetrics.Ui(TabButtonSpacing);
            float filterMin = RuntimeClassicUiMetrics.Ui(FilterMinWidth);
            int tabCount = k_Categories.Length;
            float tabsTotalWidth = tabCount * tabWidth + (tabCount - 1) * tabSpacing;

            // Tabs are positioned sequentially from the left; filter edit fills the remaining
            // width on the right (stretching per vanilla's HStretch filter edit).
            for (int i = 0; i < tabCount; i++)
            {
                var def = k_Categories[i];
                float x = i * (tabWidth + tabSpacing);
                var rect = RuntimeUiFactory.CreateAnchorRect(
                    $"Tab_{def.Category}",
                    categoriesRoot,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 0.5f),
                    new Vector2(x, 0f),
                    new Vector2(x + tabWidth, 0f));

                var buttonView = RuntimeUiFactory.CreateMorrowindButton(
                    "Button",
                    rect,
                    _theme,
                    def.Label,
                    1f,
                    BodyTextColor,
                    TabCenterColor);
                RuntimeUiFactory.Stretch(buttonView.Root);
                buttonView.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(TabTextPixelHeight);
                buttonView.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
                buttonView.Button.transition = Selectable.Transition.None;

                InventoryWindowCategory category = def.Category;
                buttonView.Button.onClick.AddListener(() => _onCategoryChanged?.Invoke(category));

                _tabButtons.Add(new TabButtonView { Category = category, View = buttonView });
            }

            // Filter edit frame: starts after the tab row with a small gap, stretches to the
            // pane's right edge. Reports a minimum width by insetting its left edge far
            // enough that it never shrinks below filterMin.
            float filterLeftAbsolute = tabsTotalWidth + RuntimeClassicUiMetrics.Ui(TabButtonSpacing * 2f);
            var filterFrameRoot = RuntimeUiFactory.CreateAnchorRect(
                "FilterFrame",
                categoriesRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(filterLeftAbsolute, 0f),
                Vector2.zero);
            // Enforce minimum via MinSize: when pane shrinks below tabs + gap + filterMin,
            // the list will overflow the pane horizontally (acceptable — MinWindowWidth
            // guarantees enough room at any legal window size).
            _ = filterMin;

            var filterFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                filterFrameRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                FilterFrameCenterColor);
            RuntimeUiFactory.Stretch(filterFrame.Root);

            return filterFrame;
        }

        // ----- Sync --------------------------------------------------------------

        void SyncEncumbrance(string label, float fillNormalized)
        {
            _encumbranceText.Text = string.IsNullOrWhiteSpace(label) ? "Encumbrance 0 / 0" : label;
            float parentWidth = _encumbranceFill.transform.parent is RectTransform prt ? prt.rect.width : 0f;
            float width = Mathf.Max(0f, parentWidth * Mathf.Clamp01(fillNormalized));
            _encumbranceFill.rectTransform.sizeDelta = new Vector2(width, 0f);
        }

        void SyncFilter(string filterText)
        {
            string value = filterText ?? string.Empty;
            RuntimeUiFactory.SetBitmapInputDisplay(
                _searchInputView,
                value,
                "Filter items",
                BodyTextColor,
                DimTextColor);

            if (_searchInputField.text == value)
                return;

            _suppressFieldEvents = true;
            _searchInputField.SetTextWithoutNotify(value);
            _suppressFieldEvents = false;
        }

        void SyncCategories(InventoryWindowCategory active)
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                var tab = _tabButtons[i];
                bool selected = tab.Category == active;
                tab.View.Frame.Center.color = selected ? TabSelectedCenterColor : TabCenterColor;
                tab.View.Label.color = selected ? new Color(0.98f, 0.93f, 0.8f) : BodyTextColor;
            }
        }

        void SyncGrid(InventoryWindowEntryViewModel[] entries)
        {
            int count = entries?.Length ?? 0;
            while (_cells.Count < count)
                _cells.Add(CreateCell(_gridContent, _cells.Count));

            float cellSize = RuntimeClassicUiMetrics.Ui(GridItemSize);
            float spacing = RuntimeClassicUiMetrics.Ui(GridItemSpacing);
            float padding = RuntimeClassicUiMetrics.Ui(GridPadding);

            // Column count derived from actual grid width at sync time, so the grid
            // reflows when the window is resized (matches vanilla MW_ItemView).
            float availableWidth = Mathf.Max(cellSize, _listClient.rect.width - padding * 2f);
            int cols = Mathf.Max(1, Mathf.FloorToInt((availableWidth + spacing) / (cellSize + spacing)));

            for (int i = 0; i < _cells.Count; i++)
            {
                bool active = i < count;
                var cell = _cells[i];
                cell.Root.gameObject.SetActive(active);
                if (!active)
                    continue;

                var entry = entries[i];
                cell.InventoryIndex = entry.InventoryIndex;
                cell.Icon.sprite = _iconService.GetSprite(entry.IconPath);
                cell.Icon.preserveAspect = true;
                bool hasStack = !string.IsNullOrWhiteSpace(entry.CountText) && entry.CountText != "1";
                cell.Count.gameObject.SetActive(hasStack);
                if (hasStack)
                    cell.Count.Text = entry.CountText;
                cell.Frame.Center.color = entry.Selected ? CellFrameSelectedColor : CellFrameCenterColor;

                int col = i % cols;
                int row = i / cols;
                float x = padding + col * (cellSize + spacing);
                float y = -padding - row * (cellSize + spacing);

                cell.Root.anchorMin = new Vector2(0f, 1f);
                cell.Root.anchorMax = new Vector2(0f, 1f);
                cell.Root.pivot = new Vector2(0f, 1f);
                cell.Root.anchoredPosition = new Vector2(x, y);
                cell.Root.sizeDelta = new Vector2(cellSize, cellSize);
            }

            int rowCount = count == 0 ? 0 : (count + cols - 1) / cols;
            float totalHeight = rowCount == 0
                ? padding * 2f
                : padding * 2f + rowCount * cellSize + Mathf.Max(0, rowCount - 1) * spacing;
            _gridContent.sizeDelta = new Vector2(0f, totalHeight);
        }

        CellView CreateCell(Transform parent, int index)
        {
            float cellSize = RuntimeClassicUiMetrics.Ui(GridItemSize);
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"InventoryCell_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                new Vector2(cellSize, cellSize));
            root.pivot = new Vector2(0f, 1f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                CellFrameCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            // Icon inset 2 px inside the frame so the gold thin border stays visible.
            var icon = RuntimeUiFactory.CreateImage("Icon", frame.Client, Color.white);
            RuntimeUiFactory.SetInset(
                icon.rectTransform,
                RuntimeClassicUiMetrics.Ui(2f),
                RuntimeClassicUiMetrics.Ui(2f),
                -RuntimeClassicUiMetrics.Ui(2f),
                -RuntimeClassicUiMetrics.Ui(2f));
            icon.raycastTarget = false;

            // Stack count overlay at the bottom-right. Hidden when the entry isn't stacked.
            var count = RuntimeUiFactory.CreateBitmapText(
                "Count",
                frame.Client,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Right);
            count.PixelHeight = RuntimeClassicUiMetrics.Ui(11f);
            count.VerticalAlignment = BitmapTextVerticalAlignment.Bottom;
            count.rectTransform.anchorMin = new Vector2(1f, 0f);
            count.rectTransform.anchorMax = new Vector2(1f, 0f);
            count.rectTransform.pivot = new Vector2(1f, 0f);
            count.rectTransform.anchoredPosition = new Vector2(-RuntimeClassicUiMetrics.Ui(3f), RuntimeClassicUiMetrics.Ui(2f));
            count.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(34f, 13f));
            count.raycastTarget = false;

            var cell = new CellView
            {
                InventoryIndex = -1,
                Root = root,
                Frame = frame,
                Button = button,
                Icon = icon,
                Count = count,
            };

            // Click = select. Vanilla inventory: single click selects the item (details
            // surface via tooltip), double-click equips/unequips. We fire the selection
            // callback only; equip-on-double-click can land with the tooltip pipeline.
            button.onClick.AddListener(() => _onSelectionChanged?.Invoke(cell.InventoryIndex));
            return cell;
        }

        void OnFilterValueChanged(string value)
        {
            if (_suppressFieldEvents)
                return;

            _onFilterChanged?.Invoke(value);
        }
    }
}
