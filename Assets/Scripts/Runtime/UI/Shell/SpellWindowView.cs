using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind Magic (spell) window.
    ///
    /// Mirrors <c>openmw_spell_window.layout</c> (see <c>docs/ui-reference/openmw-ui-skins.md</c>)
    /// at the reference 300x600 portrait size:
    /// <list type="bullet">
    ///   <item>Effects strip at the very top - a thin MW_Box (23 tall) displaying the
    ///     active magic effects as 16px icons, matching OpenMW's SpellIcons widget.</item>
    ///   <item>Spell list filling the middle - a scrollable MW_Box with one row per known
    ///     spell/power/enchanted item. Each row: name left, cost right. Selected row
    ///     is tinted gold. Single-click selects (vanilla double-clicks cast).</item>
    ///   <item>Bottom row (23 tall) - filter edit on the left (HStretch), Delete
    ///     button on the right (60 px wide). Delete is visual-only for now; it fires
    ///     nothing until the shell exposes a <c>RequestDeleteSpell</c> action.</item>
    /// </list>
    ///
    /// The empty-state label ("No known spells") renders centered inside the list frame
    /// when there are zero entries, matching the vanilla behavior of an empty SpellView.
    /// </summary>
    sealed class SpellWindowView
    {
        // Palette - shared with StatsWindowView / InventoryWindowView / ContainerWindowView.
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color DimTextColor = new(0.68f, 0.64f, 0.56f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color EffectsBoxCenterColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color ListFrameCenterColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color FilterFrameCenterColor = new(0f, 0f, 0f, 0.76f);
        static readonly Color RowAltColor = new(0f, 0f, 0f, 0.20f);
        static readonly Color RowBaseColor = new(0f, 0f, 0f, 0.06f);
        static readonly Color RowSelectedColor = new(0.42f, 0.32f, 0.15f, 0.92f);
        static readonly Color SelectedTextColor = new(0.98f, 0.93f, 0.80f);
        static readonly Color DeleteButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color ChargeTrackColor = new(0.03f, 0.025f, 0.02f, 0.88f);
        static readonly Color ChargeFillColor = new(0.41f, 0.56f, 0.78f, 0.95f);

        // Pixel heights.
        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float BodyTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float CostTextPixelHeight = RuntimeClassicUiFontSizes.Small;

        // Window geometry (matches openmw_spell_window.layout position "0 0 300 600").
        const float DefaultWindowWidth = 300f;
        const float DefaultWindowHeight = 600f;
        const float MinWindowWidth = 280f;
        const float MinWindowHeight = 420f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;

        // Client geometry.
        const float Margin = 8f;
        const float EffectsStripHeight = 23f;
        const float EffectsToListGap = 7f;
        const float ListToBottomGap = 7f;
        const float BottomRowHeight = 23f;
        const float DeleteButtonWidth = 60f;
        const float FilterDeleteGap = 6f;

        const float SpellRowHeight = 22f;

        sealed class SpellRow
        {
            public int EntryIndex;
            public SpellWindowEntryViewModel Entry;
            public RectTransform Root;
            public Image Background;
            public Image Separator;
            public BitmapTextGraphic Name;
            public BitmapTextGraphic Cost;
            public RectTransform ChargeRoot;
            public Image ChargeTrack;
            public RectTransform ChargeFill;
            public Button Button;
        }

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _viewport;
        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;
        readonly Action<SpellWindowEntryViewModel> _onSelectionChanged;
        readonly Action<SpellWindowEntryViewModel> _onDeleteEntry;
        readonly Action<string> _onFilterChanged;
        readonly Action _onDelete;

        readonly RuntimeMagicEffectIconStripView _effectsStrip;
        readonly BitmapTextGraphic _emptyText;
        readonly RectTransform _listClient;
        readonly RectTransform _rowsRoot;
        readonly RuntimeUiTextInputView _filterInput;
        readonly MorrowindButtonView _deleteButton;
        readonly List<SpellRow> _spellRows = new();
        bool _suppressFieldEvents;

        public SpellWindowView(
            RectTransform parent,
            RectTransform viewport,
            RuntimeUiTheme theme,
            RuntimeInventoryIconService iconService,
            Action onRectChanged,
            Action<SpellWindowEntryViewModel> onSelectionChanged,
            Action<SpellWindowEntryViewModel> onDeleteEntry,
            Action<string> onFilterChanged,
            Action onDelete,
            Action onPinToggled = null)
        {
            _theme = theme;
            _viewport = viewport;
            _iconService = iconService;
            _onSelectionChanged = onSelectionChanged;
            _onDeleteEntry = onDeleteEntry;
            _onFilterChanged = onFilterChanged;
            _onDelete = onDelete;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "SpellWindow",
                parent,
                theme,
                "Magic",
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

            (_effectsStrip, _listClient, _rowsRoot, _emptyText, _filterInput, _deleteButton) = BuildClient();
            _filterInput.InputField.onValueChanged.AddListener(OnFilterValueChanged);
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

        public void Sync(SpellWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Magic" : model.Title.Trim();
            _window.PinButton?.SetPinned(model.Pinned);
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            _effectsStrip.Sync(model.ActiveEffects, collapseRoot: false);

            SyncSpellRows(model.Entries ?? Array.Empty<SpellWindowEntryViewModel>());

            // Empty state centered inside the list frame when there are zero entries.
            int entryCount = model.Entries?.Length ?? 0;
            _emptyText.Text = string.IsNullOrWhiteSpace(model.EmptyStateText)
                ? "No known spells"
                : model.EmptyStateText.Trim();
            _emptyText.gameObject.SetActive(entryCount == 0);

            RuntimeUiFactory.SetBitmapInputDisplay(
                _filterInput,
                model.FilterText ?? string.Empty,
                "Filter",
                BodyTextColor,
                new Color(0.58f, 0.55f, 0.52f));
            SyncFilterText(model.FilterText);

            // Delete button caption comes from the view model (typically "Delete").
            string deleteLabel = string.IsNullOrWhiteSpace(model.FooterButtonText) ? "Delete" : model.FooterButtonText.Trim();
            _deleteButton.Label.Text = deleteLabel;
        }

        // ----- Build ------------------------------------------------------------

        (RuntimeMagicEffectIconStripView effectsStrip, RectTransform listClient, RectTransform rowsRoot, BitmapTextGraphic emptyText, RuntimeUiTextInputView filterInput, MorrowindButtonView deleteButton) BuildClient()
        {
            float margin = RuntimeClassicUiMetrics.Ui(Margin);
            float effectsHeight = RuntimeClassicUiMetrics.Ui(EffectsStripHeight);
            float effectsGap = RuntimeClassicUiMetrics.Ui(EffectsToListGap);
            float bottomGap = RuntimeClassicUiMetrics.Ui(ListToBottomGap);
            float bottomHeight = RuntimeClassicUiMetrics.Ui(BottomRowHeight);

            // Effects strip pinned to the top of the client.
            var effectsRoot = RuntimeUiFactory.CreateAnchorRect(
                "EffectsBox",
                _window.Client,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -effectsHeight),
                Vector2.zero);

            var effectsFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                effectsRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                EffectsBoxCenterColor);
            RuntimeUiFactory.Stretch(effectsFrame.Root);
            effectsFrame.Center.raycastTarget = true;

            var effectsStrip = new RuntimeMagicEffectIconStripView(
                effectsFrame.Client,
                _iconService,
                RuntimeClassicUiMetrics.Ui(16f),
                RuntimeClassicUiMetrics.Ui(0f),
                RuntimeClassicUiMetrics.Ui(2f),
                RuntimeClassicUiMetrics.Ui(2f),
                rightAnchored: false);

            // Bottom row pinned to the bottom of the client.
            var bottomRoot = RuntimeUiFactory.CreateAnchorRect(
                "BottomRow",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, bottomHeight));

            float deleteWidth = RuntimeClassicUiMetrics.Ui(DeleteButtonWidth);
            float filterGap = RuntimeClassicUiMetrics.Ui(FilterDeleteGap);

            // Filter edit fills the left side of the bottom row, stopping short of the
            // Delete button by filterGap.
            var filterFrameRoot = RuntimeUiFactory.CreateAnchorRect(
                "FilterFrame",
                bottomRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-(deleteWidth + filterGap), 0f));

            var filterFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                filterFrameRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                FilterFrameCenterColor);
            RuntimeUiFactory.Stretch(filterFrame.Root);

            var filterInput = RuntimeUiFactory.CreateBitmapInputField(
                "FilterInput",
                filterFrame.Client,
                _theme,
                1f,
                BodyTextColor,
                Color.clear,
                "Filter",
                8f,
                4f,
                24);
            RuntimeUiFactory.Stretch(filterInput.Root);
            filterInput.OverlayText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            filterInput.OverlayText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            // Read-only until the shell wires the filter callback; vanilla MW type-ahead
            // maps to the same Bridge pipeline as inventory filter — not yet exposed here.
            filterInput.InputField.readOnly = false;
            filterInput.InputField.interactable = true;

            // Delete button pinned to the right side of the bottom row.
            var deleteRoot = RuntimeUiFactory.CreateAnchorRect(
                "DeleteButton",
                bottomRoot,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-deleteWidth, 0f),
                Vector2.zero);
            var deleteButton = RuntimeUiFactory.CreateMorrowindButton(
                "Button",
                deleteRoot,
                _theme,
                "Delete",
                1f,
                BodyTextColor,
                DeleteButtonCenterColor);
            RuntimeUiFactory.Stretch(deleteButton.Root);
            deleteButton.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            deleteButton.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            deleteButton.Button.onClick.AddListener(() => _onDelete?.Invoke());

            // Spell list fills the space between the effects strip and the bottom row.
            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "SpellList",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, bottomHeight + bottomGap),
                new Vector2(0f, -(effectsHeight + effectsGap)));

            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                ListFrameCenterColor);
            RuntimeUiFactory.Stretch(listFrame.Root);

            var viewportRect = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var viewportImage = viewportRect.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewportRect.gameObject.AddComponent<RectMask2D>();

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "Rows",
                viewportRect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            rowsRoot.pivot = new Vector2(0.5f, 1f);

            var scroll = listRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = rowsRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var emptyText = RuntimeUiFactory.CreateBitmapText(
                "EmptyState",
                listFrame.Client,
                _theme?.DefaultFont,
                1f,
                SubtleTextColor,
                BitmapTextAlignment.Center);
            emptyText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            emptyText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(emptyText.rectTransform);
            emptyText.raycastTarget = false;

            _ = margin; // Margin is implicit via anchored offsets above; kept for docs.

            return (effectsStrip, listFrame.Client, rowsRoot, emptyText, filterInput, deleteButton);
        }

        // ----- Rows -------------------------------------------------------------

        void SyncSpellRows(SpellWindowEntryViewModel[] entries)
        {
            while (_spellRows.Count < entries.Length)
                _spellRows.Add(CreateSpellRow(_rowsRoot, _spellRows.Count));

            float rowHeight = RuntimeClassicUiMetrics.Ui(SpellRowHeight);
            for (int i = 0; i < _spellRows.Count; i++)
            {
                bool visible = i < entries.Length;
                var row = _spellRows[i];
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                {
                    RuntimeUiPopupUtility.SetTooltip(row.Root.gameObject, null);
                    continue;
                }

                var entry = entries[i];
                row.EntryIndex = entry.SpellIndex;
                row.Entry = entry;
                if (entry.SpellTooltip != null)
                    RuntimeUiPopupUtility.SetSpellTooltip(row.Root.gameObject, entry.SpellTooltip);
                else
                    RuntimeUiPopupUtility.SetTooltip(row.Root.gameObject, BuildSpellTooltip(entry));
                row.Root.anchoredPosition = new Vector2(0f, -rowHeight * i);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Button.interactable = !entry.IsGroupHeader;
                row.Separator.gameObject.SetActive(entry.IsGroupHeader && entry.HasGroupSeparator);
                row.Name.Text = string.IsNullOrWhiteSpace(entry.Name) ? "--" : entry.Name.Trim();
                row.Cost.Text = FormatCost(entry);
                bool showChargeBar = entry.ShowChargeBar && !entry.IsGroupHeader;
                row.ChargeRoot.gameObject.SetActive(showChargeBar);
                if (showChargeBar)
                    row.ChargeFill.anchorMax = new Vector2(Mathf.Clamp01(entry.ChargeFillNormalized), 1f);
                row.Background.color = entry.IsGroupHeader
                    ? new Color(0f, 0f, 0f, 0.42f)
                    : entry.Selected
                    ? RowSelectedColor
                    : (i % 2 == 0 ? RowAltColor : RowBaseColor);
                Color textColor = entry.IsGroupHeader
                    ? new Color(0.98f, 0.82f, 0.46f)
                    : entry.Selected
                    ? SelectedTextColor
                    : entry.Active
                    ? BodyTextColor
                    : DimTextColor;
                row.Name.color = textColor;
                row.Cost.color = entry.Selected ? SelectedTextColor : entry.Active ? DimTextColor : SubtleTextColor;
            }

            _rowsRoot.sizeDelta = new Vector2(0f, rowHeight * entries.Length);
        }

        static string BuildSpellTooltip(SpellWindowEntryViewModel entry)
        {
            if (entry == null)
                return null;

            var lines = new List<string>(3);
            lines.Add(string.IsNullOrWhiteSpace(entry.Name) ? "--" : entry.Name.Trim());
            if (!string.IsNullOrWhiteSpace(entry.TypeText))
                lines.Add(entry.TypeText.Trim());
            if (!string.IsNullOrWhiteSpace(entry.CostText))
                lines.Add($"Cost: {entry.CostText.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.EffectTooltipText))
                lines.Add(entry.EffectTooltipText.Trim());
            return string.Join("\n", lines);
        }

        static string FormatCost(SpellWindowEntryViewModel entry)
        {
            // Vanilla right column typically shows just the cost number. Some entry types
            // (Power / Enchanted Item charge) pack the type text in there too; we keep the
            // VM's existing semantics - if CostText is empty, fall back to TypeText so the
            // row isn't visually naked.
            if (!string.IsNullOrWhiteSpace(entry.CostText))
                return entry.CostText.Trim();
            if (!string.IsNullOrWhiteSpace(entry.TypeText))
                return entry.TypeText.Trim();
            return string.Empty;
        }

        SpellRow CreateSpellRow(RectTransform parent, int index)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(SpellRowHeight);
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"SpellRow_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, rowHeight));
            root.pivot = new Vector2(0f, 1f);

            var background = RuntimeUiFactory.CreateImage("Background", root, RowBaseColor);
            RuntimeUiFactory.Stretch(background.rectTransform);

            var separator = RuntimeUiFactory.CreateImage("Separator", root, new Color(0.68f, 0.55f, 0.28f, 0.9f));
            separator.rectTransform.anchorMin = new Vector2(0f, 1f);
            separator.rectTransform.anchorMax = new Vector2(1f, 1f);
            separator.rectTransform.pivot = new Vector2(0.5f, 1f);
            separator.rectTransform.anchoredPosition = Vector2.zero;
            separator.rectTransform.sizeDelta = new Vector2(0f, RuntimeClassicUiMetrics.Ui(1f));
            separator.raycastTarget = false;
            separator.gameObject.SetActive(false);

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var name = RuntimeUiFactory.CreateBitmapText(
                "Name",
                root,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Left);
            name.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            name.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            name.raycastTarget = false;
            RuntimeUiFactory.SetInset(name.rectTransform, RuntimeClassicUiMetrics.Ui(8f), 0f, -RuntimeClassicUiMetrics.Ui(92f), 0f);

            var cost = RuntimeUiFactory.CreateBitmapText(
                "Cost",
                root,
                _theme?.DefaultFont,
                1f,
                DimTextColor,
                BitmapTextAlignment.Right);
            cost.PixelHeight = RuntimeClassicUiMetrics.Ui(CostTextPixelHeight);
            cost.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            cost.raycastTarget = false;
            RuntimeUiFactory.SetInset(cost.rectTransform, RuntimeClassicUiMetrics.Ui(150f), 0f, -RuntimeClassicUiMetrics.Ui(8f), 0f);

            var chargeTrackRoot = RuntimeUiFactory.CreateAnchorRect(
                "ChargeBar",
                root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-RuntimeClassicUiMetrics.Ui(68f), RuntimeClassicUiMetrics.Ui(3f)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(8f), RuntimeClassicUiMetrics.Ui(7f)));
            var chargeTrack = RuntimeUiFactory.CreateImage("Track", chargeTrackRoot, ChargeTrackColor);
            RuntimeUiFactory.Stretch(chargeTrack.rectTransform);
            chargeTrack.raycastTarget = false;

            var chargeFillImage = RuntimeUiFactory.CreateImage("Fill", chargeTrackRoot, ChargeFillColor);
            chargeFillImage.sprite = _theme?.LoadingBarFillSprite;
            if (chargeFillImage.sprite != null)
                chargeFillImage.type = Image.Type.Sliced;
            chargeFillImage.raycastTarget = false;
            RuntimeUiFactory.Stretch(chargeFillImage.rectTransform);
            chargeTrackRoot.gameObject.SetActive(false);

            var row = new SpellRow
            {
                EntryIndex = index,
                Root = root,
                Background = background,
                Separator = separator,
                Name = name,
                Cost = cost,
                ChargeRoot = chargeTrackRoot,
                ChargeTrack = chargeTrack,
                ChargeFill = chargeFillImage.rectTransform,
                Button = button,
            };

            button.onClick.AddListener(() => HandleRowClicked(row.Entry));
            return row;
        }

        void HandleRowClicked(SpellWindowEntryViewModel entry)
        {
            if (entry == null || entry.IsGroupHeader)
                return;

            var keyboard = Keyboard.current;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (shift && entry.SourceKind != RuntimeMagicSourceKind.EnchantedItem)
            {
                _onDeleteEntry?.Invoke(entry);
                return;
            }

            _onSelectionChanged?.Invoke(entry);
        }

        void SyncFilterText(string filterText)
        {
            string value = filterText ?? string.Empty;
            if (_filterInput.InputField.text == value)
                return;

            _suppressFieldEvents = true;
            _filterInput.InputField.SetTextWithoutNotify(value);
            _suppressFieldEvents = false;
        }

        void OnFilterValueChanged(string value)
        {
            if (_suppressFieldEvents)
                return;

            _onFilterChanged?.Invoke(value);
        }
    }

    sealed class RuntimeMagicEffectIconStripView
    {
        sealed class IconCell
        {
            public RectTransform Root;
            public Image Image;
        }

        readonly RectTransform _root;
        readonly RuntimeInventoryIconService _iconService;
        readonly float _iconSize;
        readonly float _spacing;
        readonly float _paddingX;
        readonly float _paddingY;
        readonly bool _rightAnchored;
        readonly List<IconCell> _cells = new();

        public RuntimeMagicEffectIconStripView(
            RectTransform parent,
            RuntimeInventoryIconService iconService,
            float iconSize,
            float spacing,
            float paddingX,
            float paddingY,
            bool rightAnchored)
        {
            _iconService = iconService;
            _iconSize = Mathf.Max(1f, iconSize);
            _spacing = Mathf.Max(0f, spacing);
            _paddingX = Mathf.Max(0f, paddingX);
            _paddingY = Mathf.Max(0f, paddingY);
            _rightAnchored = rightAnchored;
            _root = RuntimeUiFactory.CreateStretchRect("MagicEffectIconStrip", parent);
        }

        public void Sync(RuntimeMagicEffectIconViewModel[] effects, bool collapseRoot)
        {
            effects ??= Array.Empty<RuntimeMagicEffectIconViewModel>();
            while (_cells.Count < effects.Length)
                _cells.Add(CreateCell(_cells.Count));

            float width = effects.Length == 0
                ? 0f
                : _paddingX * 2f + effects.Length * _iconSize + Mathf.Max(0, effects.Length - 1) * _spacing;
            if (collapseRoot)
                _root.sizeDelta = new Vector2(width, _root.sizeDelta.y);

            for (int i = 0; i < _cells.Count; i++)
            {
                bool visible = i < effects.Length;
                var cell = _cells[i];
                SetActiveIfChanged(cell.Root.gameObject, visible);
                if (!visible)
                {
                    RuntimeUiPopupUtility.SetTooltip(cell.Root.gameObject, null);
                    continue;
                }

                var effect = effects[i];
                float x = _rightAnchored
                    ? -_paddingX - _iconSize - i * (_iconSize + _spacing)
                    : _paddingX + i * (_iconSize + _spacing);
                cell.Root.anchorMin = _rightAnchored ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
                cell.Root.anchorMax = cell.Root.anchorMin;
                cell.Root.pivot = _rightAnchored ? new Vector2(0f, 0.5f) : new Vector2(0f, 0.5f);
                cell.Root.anchoredPosition = new Vector2(x, 0f);
                cell.Root.sizeDelta = new Vector2(_iconSize, _iconSize);
                cell.Image.sprite = _iconService?.GetMagicEffectSprite(effect.IconPath);
                cell.Image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(effect.Alpha <= 0f ? 1f : effect.Alpha));
                cell.Image.preserveAspect = true;
                RuntimeUiPopupUtility.SetMagicEffectTooltip(cell.Root.gameObject, effect.Tooltip);
            }

            if (collapseRoot)
                SetActiveIfChanged(_root.gameObject, effects.Length > 0);
        }

        IconCell CreateCell(int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"MagicEffectIcon_{index}",
                _root,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(_paddingX + index * (_iconSize + _spacing), 0f),
                new Vector2(_iconSize, _iconSize));
            root.pivot = new Vector2(0f, 0.5f);
            var image = RuntimeUiFactory.CreateImage("Icon", root, Color.white);
            image.type = Image.Type.Simple;
            image.raycastTarget = true;
            RuntimeUiFactory.Stretch(image.rectTransform);
            return new IconCell
            {
                Root = root,
                Image = image,
            };
        }

        static void SetActiveIfChanged(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }
    }
}
