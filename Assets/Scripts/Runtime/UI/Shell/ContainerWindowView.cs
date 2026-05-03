using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind container window.
    ///
    /// Mirrors <c>openmw_container_window.layout</c>:
    /// <list type="bullet">
    ///   <item>Full-window MW_Window chassis with the container object's name as the caption.</item>
    ///   <item>A single item grid fills the client area. Each cell is an HUD_Box-style
    ///     thin-framed square containing the item icon + a small count overlay in the
    ///     bottom-right for stacks. Clicking a cell immediately takes that item
    ///     (chained selection + take-selected callbacks), matching vanilla behavior.</item>
    ///   <item>Right-aligned footer row with three buttons: <c>Dispose Corpse</c>
    ///     (hidden by default, shown only while looting a dead actor), <c>Take All</c>,
    ///     and <c>Close</c>.</item>
    /// </list>
    ///
    /// Intentionally absent vs our previous list-based implementation: no secondary
    /// details text area, no empty-state text, no single-item "Take" footer button -
    /// vanilla has none of those. The corresponding view-model fields
    /// (<c>DetailText</c>, <c>EmptyStateText</c>, <c>CanTakeSelected</c>) are simply
    /// ignored here and can be cleaned up in a follow-up pass.
    /// </summary>
    sealed class ContainerWindowView
    {
        // Vanilla geometry references (openmw_container_window.layout at 600x300).
        const float GridItemSize = 42f;         // matches MW's ItemWidget cell footprint
        const float GridItemSpacing = 2f;
        const float GridPadding = 4f;           // gap between grid frame client and cell wall
        const float FooterHeight = 28f;
        const float FooterTopGap = 6f;
        const float FooterButtonWidth = 90f;
        const float FooterButtonSpacing = 6f;

        // Palette. Slightly darker grid-cell frame color than the body background to
        // make cells pop as separate tiles inside the list frame.
        static readonly Color ListFrameCenterColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color CellFrameCenterColor = new(0f, 0f, 0f, 0.62f);
        static readonly Color CellFrameSelectedColor = new(0.42f, 0.32f, 0.15f, 0.92f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color DimTextColor = new(0.58f, 0.55f, 0.5f);
        static readonly Color ButtonCenterEnabled = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color ButtonCenterDisabled = new(0.08f, 0.07f, 0.06f, 0.66f);

        sealed class CellView
        {
            public int InventoryIndex;
            public RectTransform Root;
            public BorderFrameView Frame;
            public Button Button;
            public Image Icon;
            public BitmapTextGraphic Count;
            public RuntimeInventoryItemDragSource DragSource;
        }

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _viewport;
        readonly Action _onRectChanged;
        readonly Action<int, InventoryItemClickContext> _onItemClicked;
        readonly Action _onBackgroundClicked;
        readonly Func<bool> _hasHeldItem;
        readonly Action _onTakeAll;
        readonly Action _onClose;

        readonly List<CellView> _cells = new();

        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;
        readonly RectTransform _listClient;
        readonly RectTransform _gridContent;
        readonly MorrowindButtonView _disposeCorpseButton;
        readonly MorrowindButtonView _takeAllButton;
        readonly MorrowindButtonView _closeButton;

        public ContainerWindowView(
            RectTransform parent,
            RectTransform viewport,
            RuntimeUiTheme theme,
            RuntimeInventoryIconService iconService,
            Action onRectChanged,
            Action<int, InventoryItemClickContext> onItemClicked,
            Action onBackgroundClicked,
            Func<bool> hasHeldItem,
            Action onTakeAll,
            Action onClose)
        {
            _theme = theme;
            _iconService = iconService;
            _viewport = viewport;
            _onRectChanged = onRectChanged;
            _onItemClicked = onItemClicked;
            _onBackgroundClicked = onBackgroundClicked;
            _hasHeldItem = hasHeldItem;
            _onTakeAll = onTakeAll;
            _onClose = onClose;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "ContainerWindow",
                parent,
                theme,
                "Container",
                RuntimeClassicUiMetrics.Layout(20f),
                RuntimeClassicUiMetrics.Layout(8f),
                0.88f,
                RuntimeClassicUiMetrics.Layout(RuntimeClassicUiFontSizes.Caption),
                new Color(0.94f, 0.82f, 0.53f));
            _window.Root.anchorMin = new Vector2(0f, 1f);
            _window.Root.anchorMax = new Vector2(0f, 1f);
            _window.Root.pivot = new Vector2(0f, 1f);

            _dragHandle = _window.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _dragHandle.Initialize(_window.Root, viewport, onRectChanged);
            _resizeHandle = RuntimeWindowSurfaceUtility.AttachResizeHandle(
                _window,
                viewport,
                RuntimeClassicUiMetrics.Ui(new Vector2(245f, 145f)),
                onRectChanged);

            (_listClient, _gridContent, _disposeCorpseButton, _takeAllButton, _closeButton) = BuildClient();
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

        public void Sync(ContainerWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Container" : model.Title.Trim();
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            SyncGrid(model.Entries);
            // Dispose Corpse visible only while looting a dead actor. View model doesn't
            // carry that flag yet - kept hidden until the corpse-detection wire lands.
            _disposeCorpseButton.Root.gameObject.SetActive(false);
            SyncButtonState(_takeAllButton, model.CanTakeAll);
            SyncButtonState(_closeButton, true);
        }

        (RectTransform listClient, RectTransform gridContent, MorrowindButtonView disposeCorpse, MorrowindButtonView takeAll, MorrowindButtonView close) BuildClient()
        {
            float footerHeight = RuntimeClassicUiMetrics.Layout(FooterHeight);
            float footerTopGap = RuntimeClassicUiMetrics.Layout(FooterTopGap);

            // Item grid frame - fills the client area above the footer.
            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "ItemGridRoot",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, footerHeight + footerTopGap),
                Vector2.zero);
            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "ItemGridFrame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                ListFrameCenterColor);
            RuntimeUiFactory.Stretch(listFrame.Root);

            // ScrollRect viewport (with mask) inside the list frame.
            var viewportRect = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var viewportImage = viewportRect.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f); // near-invisible but needed for scroll raycasts
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

            // Footer: right-aligned button row. Vanilla uses HBox + Spacer to push the
            // buttons to the right. We anchor each button's right edge and count back.
            var footerRoot = RuntimeUiFactory.CreateAnchorRect(
                "FooterRoot",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, footerHeight));

            float buttonWidth = RuntimeClassicUiMetrics.Layout(FooterButtonWidth);
            float buttonSpacing = RuntimeClassicUiMetrics.Layout(FooterButtonSpacing);
            float rightEdge = -RuntimeClassicUiMetrics.Layout(4f);

            MorrowindButtonView close = BuildFooterButton(footerRoot, "CloseButton", "Close", rightEdge, buttonWidth, () => _onClose?.Invoke());
            float takeAllRightEdge = rightEdge - buttonWidth - buttonSpacing;
            MorrowindButtonView takeAll = BuildFooterButton(footerRoot, "TakeAllButton", "Take All", takeAllRightEdge, buttonWidth, () => _onTakeAll?.Invoke());
            float disposeRightEdge = takeAllRightEdge - buttonWidth - buttonSpacing;
            MorrowindButtonView dispose = BuildFooterButton(footerRoot, "DisposeCorpseButton", "Dispose Corpse", disposeRightEdge, buttonWidth * 1.3f, DisposeCorpsePlaceholder);

            return (listFrame.Client, gridContent, dispose, takeAll, close);
        }

        MorrowindButtonView BuildFooterButton(RectTransform footerRoot, string name, string label, float rightEdgeX, float width, UnityEngine.Events.UnityAction onClick)
        {
            var rect = RuntimeUiFactory.CreateAnchorRect(
                $"{name}Rect",
                footerRoot,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(rightEdgeX - width, 0f),
                new Vector2(rightEdgeX, 0f));

            var button = RuntimeUiFactory.CreateMorrowindButton(
                name,
                rect,
                _theme,
                label,
                RuntimeClassicUiMetrics.WindowText(0.46f),
                new Color(0.9f, 0.85f, 0.75f),
                ButtonCenterEnabled);
            RuntimeUiFactory.Stretch(button.Root);
            button.Button.transition = Selectable.Transition.ColorTint;
            button.Button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Placeholder for the Dispose Corpse action. The button is hidden
        /// until the corpse-detection wiring lands; this stub exists only because
        /// <see cref="Button.onClick"/> needs a non-null listener during build.</summary>
        void DisposeCorpsePlaceholder()
        {
            // Intentionally no-op. Wire when corpse-flag lands on ContainerWindowViewModel.
        }

        void SyncGrid(InventoryWindowEntryViewModel[] entries)
        {
            int count = entries?.Length ?? 0;
            while (_cells.Count < count)
                _cells.Add(CreateCell(_gridContent, _cells.Count));

            float cellSize = RuntimeClassicUiMetrics.Layout(GridItemSize);
            float spacing = RuntimeClassicUiMetrics.Layout(GridItemSpacing);
            float padding = RuntimeClassicUiMetrics.Layout(GridPadding);

            // Compute column count from the actual grid content width at sync time.
            // listClient.rect.width gives us the usable area inside the list frame.
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

            // Grow the scroll content to fit all rows.
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

            var lines = new List<string>(4);
            lines.Add(string.IsNullOrWhiteSpace(entry.Name) ? "Unknown item" : entry.Name.Trim());
            if (!string.IsNullOrWhiteSpace(entry.CountText) && entry.CountText.Trim() != "1")
                lines.Add($"Count: {entry.CountText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.WeightText))
                lines.Add($"Weight: {entry.WeightText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.ValueText))
                lines.Add($"Value: {entry.ValueText.Trim()}");
            return string.Join("\n", lines);
        }

        CellView CreateCell(Transform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"ContainerCell_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                new Vector2(RuntimeClassicUiMetrics.Layout(GridItemSize), RuntimeClassicUiMetrics.Layout(GridItemSize)));
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

            // Icon fills most of the cell, inset 2 px from each frame edge so the gold
            // thin border is unobscured.
            var icon = RuntimeUiFactory.CreateImage("Icon", frame.Client, Color.white);
            RuntimeUiFactory.SetInset(
                icon.rectTransform,
                RuntimeClassicUiMetrics.Layout(2f),
                RuntimeClassicUiMetrics.Layout(2f),
                -RuntimeClassicUiMetrics.Layout(2f),
                -RuntimeClassicUiMetrics.Layout(2f));
            icon.raycastTarget = false;

            // Stack count overlay at bottom-right. Hidden when the item isn't stacked.
            var count = RuntimeUiFactory.CreateBitmapText(
                "Count",
                frame.Client,
                _theme.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Right);
            count.PixelHeight = RuntimeClassicUiMetrics.Layout(RuntimeClassicUiFontSizes.Count);
            count.VerticalAlignment = BitmapTextVerticalAlignment.Bottom;
            count.rectTransform.anchorMin = new Vector2(1f, 0f);
            count.rectTransform.anchorMax = new Vector2(1f, 0f);
            count.rectTransform.pivot = new Vector2(1f, 0f);
            count.rectTransform.anchoredPosition = new Vector2(-RuntimeClassicUiMetrics.Layout(3f), RuntimeClassicUiMetrics.Layout(2f));
            count.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Layout(new Vector2(34f, 13f));
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

            cell.DragSource = root.gameObject.AddComponent<RuntimeInventoryItemDragSource>();
            cell.DragSource.Initialize(cell.InventoryIndex, _onItemClicked, _hasHeldItem);
            return cell;
        }

        void SyncButtonState(MorrowindButtonView buttonView, bool enabled)
        {
            buttonView.Button.interactable = enabled;
            buttonView.Frame.Center.color = enabled ? ButtonCenterEnabled : ButtonCenterDisabled;
            buttonView.Label.color = enabled ? BodyTextColor : DimTextColor;
        }
    }
}
