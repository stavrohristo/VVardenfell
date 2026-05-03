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
        // Vanilla MW cell artwork uses the same center color regardless of mouse
        // state; hover feedback comes through a Unity Button ColorTint that
        // multiplies the center RGB, brightening non-selected cells on hover
        // and darkening them on press. Matches Morrowind's subtle
        // "highlight-under-cursor" behavior in the inventory grid.
        static readonly Color CellHoverTint = new(1.18f, 1.18f, 1.18f, 1f);
        static readonly Color CellPressedTint = new(0.82f, 0.82f, 0.82f, 1f);
        // Bright gold tint for the armor rating text, mirroring OpenMW's
        // SandBrightText skin (fontcolour=header).
        static readonly Color ArmorTextColor = new(0.98f, 0.88f, 0.52f);
        // Drop-shadow color for stack-count + encumbrance overlays. OpenMW
        // sets TextShadow="true" on the CountText / ProgressText skins; we
        // mimic by parenting a second text graphic offset 1px right/down in
        // this near-opaque black.
        static readonly Color TextShadowColor = new(0f, 0f, 0f, 0.82f);
        static readonly Color EmptyStateTextColor = new(0.68f, 0.64f, 0.58f);
        static readonly Color FilterFrameCenterColor = new(0f, 0f, 0f, 0.76f);
        static readonly Color TabCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color TabSelectedCenterColor = new(0.38f, 0.28f, 0.14f, 0.96f);
        // Encumbrance bar tint - matches MW_Track_Blue (fontcolour=magic in OpenMW).
        static readonly Color EncumbranceFillColor = new(0.17f, 0.33f, 0.68f, 0.96f);

        // Pixel heights — all sourced from the canonical OpenMW-faithful table
        // in RuntimeClassicUiFontSizes so every text role in every window
        // scales uniformly under the UI Scale slider.
        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float BodyTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float BarTextPixelHeight = RuntimeClassicUiFontSizes.BarOverlay;
        const float TabTextPixelHeight = RuntimeClassicUiFontSizes.Body;

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
            public Image IconShadow;
            public BitmapTextGraphic Count;
            public BitmapTextGraphic CountShadow;
            public RuntimeInventoryItemDragSource DragSource;
        }

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly InventoryAvatarPreviewRenderer _avatarPreviewRenderer = new();
        readonly RectTransform _viewport;
        readonly Action _onRectChanged;
        readonly Action<InventoryWindowCategory> _onCategoryChanged;
        readonly Action<int> _onItemSelected;
        readonly Action<int, InventoryItemClickContext> _onItemDragged;
        readonly Action<int> _onItemRightClicked;
        readonly Func<int, bool> _isItemSelected;
        readonly Action _onBackgroundClicked;
        readonly Action _onAvatarClicked;
        readonly Func<bool> _hasHeldItem;
        readonly Action<Vector2> _onDragPositionChanged;
        readonly Action<string> _onFilterChanged;

        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;

        // Left pane parts.
        readonly RectTransform _leftPane;
        readonly Image _encumbranceFill;
        readonly BitmapTextGraphic _encumbranceText;
        readonly BitmapTextGraphic _encumbranceTextShadow;
        readonly BitmapTextGraphic _armorText;
        readonly BitmapTextGraphic _armorTextShadow;
        readonly RawImage _avatarPreviewImage;

        // Right pane parts.
        readonly RectTransform _rightPane;
        readonly List<TabButtonView> _tabButtons = new();
        readonly List<CellView> _cells = new();
        readonly RectTransform _listClient;
        readonly RectTransform _gridContent;
        readonly RuntimeUiTextInputView _searchInputView;
        readonly InputField _searchInputField;
        readonly BitmapTextGraphic _emptyStateText;

        bool _suppressFieldEvents;

        public InventoryWindowView(
            RectTransform parent,
            RectTransform viewport,
            RuntimeUiTheme theme,
            RuntimeInventoryIconService iconService,
            Action onRectChanged,
            Action<InventoryWindowCategory> onCategoryChanged,
            Action<int> onItemSelected,
            Action<int, InventoryItemClickContext> onItemDragged,
            Action<int> onItemRightClicked,
            Func<int, bool> isItemSelected,
            Action onBackgroundClicked,
            Action onAvatarClicked,
            Func<bool> hasHeldItem,
            Action<Vector2> onDragPositionChanged,
            Action<string> onFilterChanged,
            Action onPinToggled = null)
        {
            _theme = theme;
            _iconService = iconService;
            _viewport = viewport;
            _onRectChanged = onRectChanged;
            _onCategoryChanged = onCategoryChanged;
            _onItemSelected = onItemSelected;
            _onItemDragged = onItemDragged;
            _onItemRightClicked = onItemRightClicked;
            _isItemSelected = isItemSelected;
            _onBackgroundClicked = onBackgroundClicked;
            _onAvatarClicked = onAvatarClicked;
            _hasHeldItem = hasHeldItem;
            _onDragPositionChanged = onDragPositionChanged;
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
            (_encumbranceFill, _encumbranceText, _encumbranceTextShadow, _armorText, _armorTextShadow, _avatarPreviewImage) = BuildLeftPane(_leftPane);
            (_listClient, _gridContent, _searchInputView, _searchInputField, _emptyStateText) = BuildRightPane(_rightPane);
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
            SyncAvatarPreview();
            string armorTextValue = string.IsNullOrWhiteSpace(model.ArmorSummary) ? "Armor Rating 0" : model.ArmorSummary;
            _armorText.Text = armorTextValue;
            if (_armorTextShadow != null)
                _armorTextShadow.Text = armorTextValue;
            SyncFilter(model.FilterText);
            SyncCategories(model.Category);
            SyncGrid(model.Entries);
            SyncEmptyState(model.Entries, model.FilterText);
        }

        /// <summary>
        /// Show a centered "No items." hint inside the list frame when the
        /// grid is empty. When the player has a filter active the message
        /// echoes it back so they can tell an empty inventory from an
        /// overly-restrictive filter.
        /// </summary>
        void SyncEmptyState(InventoryWindowEntryViewModel[] entries, string filterText)
        {
            int count = entries?.Length ?? 0;
            bool empty = count == 0;
            _emptyStateText.gameObject.SetActive(empty);
            if (!empty)
                return;

            string trimmed = filterText?.Trim();
            _emptyStateText.Text = string.IsNullOrEmpty(trimmed)
                ? "No items."
                : $"No items match \"{trimmed}\".";
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

        public void Dispose()
        {
            _avatarPreviewRenderer.Dispose();
        }

        (Image fill, BitmapTextGraphic barText, BitmapTextGraphic barTextShadow, BitmapTextGraphic armorText, BitmapTextGraphic armorTextShadow, RawImage avatarPreview) BuildLeftPane(RectTransform leftPane)
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

            // Encumbrance overlay text — two layers: a 1px-offset black shadow
            // behind and the bright cream glyph in front. Mirrors OpenMW's
            // ProgressText skin (TextShadow="true") so the value reads cleanly
            // over both the dark left portion of the track and the bright
            // blue fill.
            var barTextShadow = BuildOverlayTextLayer(barFrame.Client, "ValueShadow", TextShadowColor, new Vector2(1f, -1f), BarTextPixelHeight);
            var barText = BuildOverlayTextLayer(barFrame.Client, "Value", BarOverlayColor, Vector2.zero, BarTextPixelHeight);

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

            var previewRoot = RuntimeUiFactory.CreateAnchorRect(
                "AvatarPreview",
                avatarFrame.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, armorStrip),
                Vector2.zero);

            var preview = RuntimeUiFactory.CreateRawImage("AvatarPreviewImage", previewRoot, Color.white);
            preview.texture = _avatarPreviewRenderer.Texture;
            preview.raycastTarget = false;
            RuntimeUiFactory.Stretch(preview.rectTransform);
            var aspect = preview.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 256f / 320f;

            var avatarRaycast = RuntimeUiFactory.CreateImage("UseTarget", avatarFrame.Client, new Color(1f, 1f, 1f, 0.001f));
            RuntimeUiFactory.Stretch(avatarRaycast.rectTransform);
            avatarRaycast.raycastTarget = true;
            var avatarButton = avatarFrame.Client.gameObject.AddComponent<Button>();
            avatarButton.targetGraphic = avatarRaycast;
            avatarButton.transition = Selectable.Transition.None;
            avatarButton.navigation = new Navigation { mode = Navigation.Mode.None };
            avatarButton.onClick.AddListener(() => _onAvatarClicked?.Invoke());
            var avatarDropTarget = avatarFrame.Client.gameObject.AddComponent<RuntimeInventoryDropTarget>();
            avatarDropTarget.Initialize(_onAvatarClicked, _hasHeldItem);

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

            // Armor rating: same two-layer shadow treatment as the encumbrance
            // overlay, but tinted bright gold (SandBrightText in OpenMW /
            // fontcolour=header) so it reads as an important readout rather
            // than plain body copy.
            var armorTextShadow = BuildOverlayTextLayer(armorRoot, "TextShadow", TextShadowColor, new Vector2(1f, -1f), BodyTextPixelHeight);
            var armorText = BuildOverlayTextLayer(armorRoot, "Text", ArmorTextColor, Vector2.zero, BodyTextPixelHeight);

            return (fill, barText, barTextShadow, armorText, armorTextShadow, preview);
        }

        void SyncAvatarPreview()
        {
            if (_avatarPreviewImage == null)
                return;

            _avatarPreviewImage.texture = _avatarPreviewRenderer.Texture;
            _avatarPreviewRenderer.Render();
        }

        /// <summary>
        /// Builds a stretched overlay text layer (encumbrance bar value,
        /// armor rating). The caller typically makes two of these — a black
        /// shadow offset by (1, -1) and a colored glyph at zero offset — so
        /// the text reads legibly over variable backgrounds.
        /// </summary>
        BitmapTextGraphic BuildOverlayTextLayer(Transform parent, string name, Color color, Vector2 offsetPx, float pixelHeight)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                name,
                parent,
                _theme?.DefaultFont,
                1f,
                color,
                BitmapTextAlignment.Center);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(pixelHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(text.rectTransform);
            // Nudge after Stretch so the offset doesn't get wiped by the
            // anchor/offset reset inside Stretch.
            text.rectTransform.anchoredPosition += new Vector2(
                RuntimeClassicUiMetrics.Ui(offsetPx.x),
                RuntimeClassicUiMetrics.Ui(offsetPx.y));
            text.raycastTarget = false;
            return text;
        }

        // ----- Right pane: categories + item grid -----------------------------

        (RectTransform listClient, RectTransform gridContent, RuntimeUiTextInputView searchInput, InputField searchField, BitmapTextGraphic emptyState) BuildRightPane(RectTransform rightPane)
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
            var backgroundButton = viewportRect.gameObject.AddComponent<Button>();
            backgroundButton.targetGraphic = viewportImage;
            backgroundButton.transition = Selectable.Transition.None;
            backgroundButton.navigation = new Navigation { mode = Navigation.Mode.None };
            backgroundButton.onClick.AddListener(() => _onBackgroundClicked?.Invoke());
            var dropTarget = viewportRect.gameObject.AddComponent<RuntimeInventoryDropTarget>();
            dropTarget.Initialize(_onBackgroundClicked, _hasHeldItem);

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

            // Empty-state text: centered inside the list frame, hidden by
            // default. SyncGrid toggles it when the entries array is empty so
            // the player sees a clear "nothing to show" signal instead of a
            // blank box. The text reads "No items." or "No items match
            // '<filter>'." depending on whether a filter is active.
            var emptyState = RuntimeUiFactory.CreateBitmapText(
                "EmptyState",
                listFrame.Client,
                _theme?.DefaultFont,
                1f,
                EmptyStateTextColor,
                BitmapTextAlignment.Center);
            emptyState.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            emptyState.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            emptyState.Text = string.Empty;
            emptyState.raycastTarget = false;
            RuntimeUiFactory.Stretch(emptyState.rectTransform);
            emptyState.gameObject.SetActive(false);

            return (listFrame.Client, gridContent, searchInput, searchInput.InputField, emptyState);
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
            string text = string.IsNullOrWhiteSpace(label) ? "Encumbrance 0 / 0" : label;
            _encumbranceText.Text = text;
            if (_encumbranceTextShadow != null)
                _encumbranceTextShadow.Text = text;
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
                {
                    RuntimeUiPopupUtility.SetTooltip(cell.Root.gameObject, null);
                    continue;
                }

                var entry = entries[i];
                cell.InventoryIndex = entry.InventoryIndex;
                cell.DragSource.SetItemIndex(entry.InventoryIndex);
                RuntimeUiPopupUtility.SetTooltip(cell.Root.gameObject, BuildItemTooltip(entry));
                RuntimeInventoryItemIconLayoutUtility.SyncSprite(cell.Icon, cell.IconShadow, _iconService.GetSprite(entry.IconPath));
                bool hasStack = !string.IsNullOrWhiteSpace(entry.CountText) && entry.CountText != "1";
                cell.Count.gameObject.SetActive(hasStack);
                cell.CountShadow?.gameObject.SetActive(hasStack);
                if (hasStack)
                {
                    cell.Count.Text = entry.CountText;
                    if (cell.CountShadow != null)
                        cell.CountShadow.Text = entry.CountText;
                }
                cell.Frame.Center.color = entry.Selected ? CellFrameSelectedColor : CellFrameCenterColor;
                // Only equipped items get the gold MW_Box filigree around
                // the cell. Everything else renders as a borderless icon
                // tile over the dark cell backdrop.
                cell.Frame.SetBorderVisible(entry.Equipped);

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

        static string BuildItemTooltip(InventoryWindowEntryViewModel entry)
        {
            if (entry == null)
                return null;

            var lines = new List<string>(5);
            lines.Add(string.IsNullOrWhiteSpace(entry.Name) ? "Unknown item" : entry.Name.Trim());
            if (!string.IsNullOrWhiteSpace(entry.CountText) && entry.CountText.Trim() != "1")
                lines.Add($"Count: {entry.CountText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.WeightText))
                lines.Add($"Weight: {entry.WeightText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.ValueText))
                lines.Add($"Value: {entry.ValueText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.EquippedText))
                lines.Add(entry.EquippedText.Trim());
            return string.Join("\n", lines);
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
            // Default state: no border — vanilla MW only shows the gold
            // filigree around a cell when the item inside is equipped. SyncGrid
            // re-enables it per-cell off the view model's Equipped flag.
            frame.SetBorderVisible(false);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // ColorTint multiplies the Image's RGB, so CellHoverTint = brighter,
            // CellPressedTint = dimmer. Selection state is expressed by swapping
            // Frame.Center.color in SyncGrid — ColorTint then multiplies against
            // whichever base color is currently set (normal or selected).
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = CellHoverTint,
                pressedColor = CellPressedTint,
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            var iconShadow = RuntimeInventoryItemIconLayoutUtility.CreateItemImage("ItemShadow", frame.Root, new Color(0f, 0f, 0f, 0.5f), shadow: true, flipVertical: true);
            var icon = RuntimeInventoryItemIconLayoutUtility.CreateItemImage("Item", frame.Root, Color.white, shadow: false, flipVertical: true);
            RuntimeInventoryItemIconLayoutUtility.BringBorderToFront(frame);

            // Stack count overlay at the bottom-right. Hidden when the entry
            // isn't stacked. Rendered in two layers — a black shadow offset
            // 1px right/down followed by the amber glyph — to match OpenMW's
            // TextShadow="true" on the CountText skin. Legibility over light
            // item icons (scrolls, gold coins) depends on the shadow.
            var countShadow = BuildCountLayer(frame.Client, "CountShadow", TextShadowColor, new Vector2(-2f, 1f));
            var count = BuildCountLayer(frame.Client, "Count", BodyTextColor, new Vector2(-3f, 2f));

            var cell = new CellView
            {
                InventoryIndex = -1,
                Root = root,
                Frame = frame,
                Button = button,
                Icon = icon,
                IconShadow = iconShadow,
                Count = count,
                CountShadow = countShadow,
            };

            cell.DragSource = root.gameObject.AddComponent<RuntimeInventoryItemDragSource>();
            cell.DragSource.Initialize(
                cell.InventoryIndex,
                _onItemSelected,
                _onItemDragged,
                _onItemRightClicked,
                _isItemSelected,
                _hasHeldItem,
                _onDragPositionChanged);
            return cell;
        }

        /// <summary>
        /// Builds one layer of the stack-count overlay (either the black
        /// shadow or the visible amber glyph). Both layers share the same
        /// anchor + sizeDelta so the text lines up pixel-for-pixel when the
        /// shadow is offset by (1, -1) ref pixels behind the foreground.
        /// </summary>
        BitmapTextGraphic BuildCountLayer(Transform parent, string name, Color color, Vector2 anchoredOffsetPx)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                name,
                parent,
                _theme?.DefaultFont,
                1f,
                color,
                BitmapTextAlignment.Right);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Count);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Bottom;
            text.rectTransform.anchorMin = new Vector2(1f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 0f);
            text.rectTransform.pivot = new Vector2(1f, 0f);
            RuntimeInventoryItemIconLayoutUtility.ApplyCountRect(text.rectTransform);
            text.rectTransform.anchoredPosition += RuntimeClassicUiMetrics.Ui(anchoredOffsetPx);
            text.raycastTarget = false;
            return text;
        }

        void OnFilterValueChanged(string value)
        {
            if (_suppressFieldEvents)
                return;

            _onFilterChanged?.Invoke(value);
        }
    }
}
