using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind Stats window.
    ///
    /// Layout mirrors <c>openmw_stats_window.layout</c> exactly (see
    /// <c>docs/ui-reference/openmw-ui-skins.md</c>). At the reference 500x342 size:
    /// <list type="bullet">
    ///   <item>Left pane 220 px wide (44%):
    ///     <list type="bullet">
    ///       <item>Vitals MW_Box at (8, 8, 212, 62) - Health/Magicka/Fatigue bars.</item>
    ///       <item>Identity MW_Box at (8, 78, 212, 62) - Level/Race/Class.</item>
    ///       <item>Attributes MW_Box at (8, 148, 212, stretch) - 8 attribute rows.</item>
    ///     </list>
    ///   </item>
    ///   <item>Right pane 280 px wide (56%):
    ///     <list type="bullet">
    ///       <item>Skills MW_Box at (8, 8, 248, stretch) - scrollable skill list with
    ///         Major/Minor/Misc sections, each with a colored header and per-row values.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// All row heights are 18 px, section box padding is 4 px, section margin is 8 px.
    ///
    /// Bars follow MW_Progress_Red/Blue/Green: tinted <c>menu_bar_gray.dds</c> fill,
    /// MW_Box thin frame outline, centered value overlay text.
    /// </summary>
    sealed class StatsWindowView
    {
        // Palette eyeballed from the vanilla reference screenshot.
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SectionHeaderColor = new(0.96f, 0.82f, 0.44f);
        static readonly Color BarOverlayColor = new(0.98f, 0.94f, 0.84f);
        static readonly Color SectionBoxCenterColor = new(0f, 0f, 0f, 0.0f);
        static readonly Color BarFrameCenterColor = new(0f, 0f, 0f, 0.35f);
        static readonly Color HealthFillColor = new(0.70f, 0.22f, 0.16f, 0.96f);
        static readonly Color MagickaFillColor = new(0.17f, 0.33f, 0.68f, 0.96f);
        static readonly Color FatigueFillColor = new(0.28f, 0.58f, 0.28f, 0.96f);

        // Pixel heights for each text role - what you see is what the number says.
        const float CaptionPixelHeight = 14f;
        const float SectionHeaderPixelHeight = 14f;
        const float BodyTextPixelHeight = 13f;
        const float BarTextPixelHeight = 12f;

        // Window geometry (reference pixels, matches openmw_stats_window.layout "0 0 500 342").
        const float DefaultWindowWidth = 500f;
        const float DefaultWindowHeight = 342f;
        const float MinWindowWidth = 244f;
        const float MinWindowHeight = 180f;
        const float CaptionHeight = 20f;
        const float ClientInset = 4f;
        const float LeftPaneWidth = 220f;

        // Section box geometry.
        const float SectionMargin = 8f;         // space between sections & between section and pane edge
        const float SectionPadding = 4f;        // MW_Box content inset
        const float VitalsBoxHeight = 62f;      // 3 bar rows x 18 + 4+4 padding
        const float IdentityBoxHeight = 62f;    // 3 text rows x 18 + 4+4 padding
        const float RowHeight = 18f;

        // Column widths within a row (sum to 204 = MW_Box interior width at 500-wide default).
        const float LabelColumnWidth = 70f;
        const float BarGap = 4f;                // gap between label and bar start
        const float BarWidth = 130f;            // 70 + 4 + 130 = 204

        // Section header + internal gap inside the skills scroll.
        const float SectionHeaderHeight = 22f;
        const float SectionGap = 6f;

        // Horizontal divider between section groups inside the skills scroll.
        const float DividerThickness = 1f;
        const float DividerVerticalPadding = 5f;
        const float DividerHorizontalPadding = 6f;
        static readonly Color DividerColor = new(0.62f, 0.48f, 0.22f, 0.85f);

        sealed class BarRow
        {
            public RectTransform Fill;
            public BitmapTextGraphic Overlay;
        }

        sealed class ValueRow
        {
            public BitmapTextGraphic Value;
        }

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _viewport;
        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;

        readonly BarRow _healthBar;
        readonly BarRow _magickaBar;
        readonly BarRow _fatigueBar;
        readonly ValueRow _levelRow;
        readonly ValueRow _raceRow;
        readonly ValueRow _classRow;
        readonly ValueRow[] _attributeRows;

        readonly RectTransform _skillsContent;

        // Skill sections (Major / Minor / Misc).
        readonly RectTransform _majorHeader;
        readonly RectTransform _majorRowsRoot;
        readonly RectTransform _minorHeader;
        readonly RectTransform _minorRowsRoot;
        readonly RectTransform _miscHeader;
        readonly RectTransform _miscRowsRoot;
        readonly List<ValueRow> _majorSkillRowPool = new();
        readonly List<ValueRow> _minorSkillRowPool = new();
        readonly List<ValueRow> _miscSkillRowPool = new();

        // Faction / Sign / Reputation sections.
        readonly RectTransform _factionHeader;
        readonly RectTransform _factionRowsRoot;
        readonly List<ValueRow> _factionRowPool = new();
        readonly RectTransform _signHeader;
        readonly RectTransform _signRowsRoot;
        readonly BitmapTextGraphic _signValueText;
        readonly RectTransform _reputationHeader;
        readonly RectTransform _reputationRowsRoot;
        readonly BitmapTextGraphic _reputationValueText;

        // Dividers rendered between section groups. _majorDivider sits under the Major
        // section, etc. No divider between Faction and Sign - the user's spec groups
        // them together with no separator.
        readonly RectTransform _majorDivider;
        readonly RectTransform _minorDivider;
        readonly RectTransform _miscDivider;
        readonly RectTransform _signDivider;

        public StatsWindowView(RectTransform parent, RectTransform viewport, RuntimeUiTheme theme, Action onRectChanged, Action onPinToggled = null)
        {
            _theme = theme;
            _viewport = viewport;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "StatsWindow",
                parent,
                theme,
                "--",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.88f,
                titlePixelHeight: RuntimeClassicUiMetrics.Ui(CaptionPixelHeight),
                new Color(0.96f, 0.82f, 0.44f),
                withPinButton: true);
            if (onPinToggled != null && _window.PinButton?.Button != null)
                _window.PinButton.Button.onClick.AddListener(() => onPinToggled());
            _window.Root.anchorMin = new Vector2(0f, 1f);
            _window.Root.anchorMax = new Vector2(0f, 1f);
            _window.Root.pivot = new Vector2(0f, 1f);
            _window.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(DefaultWindowWidth, DefaultWindowHeight));

            _dragHandle = _window.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _dragHandle.Initialize(_window.Root, viewport, onRectChanged);
            _resizeHandle = RuntimeWindowSurfaceUtility.AttachResizeHandle(
                _window,
                viewport,
                RuntimeClassicUiMetrics.Ui(new Vector2(MinWindowWidth, MinWindowHeight)),
                onRectChanged);

            (RectTransform leftPane, RectTransform rightPane) = BuildPanes(_window.Client);
            (_healthBar, _magickaBar, _fatigueBar) = BuildVitalsBox(leftPane);
            (_levelRow, _raceRow, _classRow) = BuildIdentityBox(leftPane);
            _attributeRows = BuildAttributesBox(leftPane);
            var skills = BuildSkillsBox(rightPane);
            _skillsContent = skills.content;
            _majorHeader = skills.majorHeader;
            _majorRowsRoot = skills.majorRoot;
            _minorHeader = skills.minorHeader;
            _minorRowsRoot = skills.minorRoot;
            _miscHeader = skills.miscHeader;
            _miscRowsRoot = skills.miscRoot;
            _factionHeader = skills.factionHeader;
            _factionRowsRoot = skills.factionRoot;
            _signHeader = skills.signHeader;
            _signRowsRoot = skills.signRoot;
            _signValueText = skills.signValueText;
            _reputationHeader = skills.reputationHeader;
            _reputationRowsRoot = skills.reputationRoot;
            _reputationValueText = skills.reputationValueText;
            _majorDivider = skills.majorDivider;
            _minorDivider = skills.minorDivider;
            _miscDivider = skills.miscDivider;
            _signDivider = skills.signDivider;
        }

        public RectTransform Root => _window.Root;

        public bool IsInteracting => _dragHandle.IsDragging || _resizeHandle.IsDragging;

        public void SetVisible(bool visible)
        {
            _window.Root.gameObject.SetActive(visible);
        }

        public void Sync(StatsWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.CharacterName) ? "--" : model.CharacterName.Trim();
            _window.PinButton?.SetPinned(model.Pinned);
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            ApplyBar(_healthBar, model.HealthFillNormalized, model.HealthText);
            ApplyBar(_magickaBar, model.MagickaFillNormalized, model.MagickaText);
            ApplyBar(_fatigueBar, model.FatigueFillNormalized, model.FatigueText);

            _levelRow.Value.Text = model.LevelText ?? "--";
            _raceRow.Value.Text = model.RaceText ?? "--";
            _classRow.Value.Text = model.ClassText ?? "--";

            var attrs = model.Attributes ?? Array.Empty<StatsWindowAttributeRow>();
            for (int i = 0; i < _attributeRows.Length; i++)
            {
                string value = i < attrs.Length ? attrs[i].Value : "--";
                _attributeRows[i].Value.Text = string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
            }

            SyncSkillSection("Major Skills", _majorHeader, _majorRowsRoot, _majorSkillRowPool, model.MajorSkills);
            SyncSkillSection("Minor Skills", _minorHeader, _minorRowsRoot, _minorSkillRowPool, model.MinorSkills);
            SyncSkillSection("Misc Skills", _miscHeader, _miscRowsRoot, _miscSkillRowPool, model.MiscSkills);
            SyncFactionSection(model.Factions);
            SyncSingleValueSection(_signHeader, _signRowsRoot, _signValueText, "Sign", model.BirthSignName);
            SyncSingleValueSection(_reputationHeader, _reputationRowsRoot, _reputationValueText, "Reputation", model.ReputationText);

            LayoutSkillsContent(model);
        }

        // ----- Panes ----------------------------------------------------------

        (RectTransform left, RectTransform right) BuildPanes(RectTransform client)
        {
            // Left pane: anchor to full height, fixed width at the left.
            var leftPane = RuntimeUiFactory.CreateAnchoredRect(
                "LeftPane",
                client,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                new Vector2(RuntimeClassicUiMetrics.Ui(LeftPaneWidth), 0f));
            leftPane.pivot = new Vector2(0f, 0.5f);

            // Right pane: fills everything to the right of the left pane.
            var rightPane = RuntimeUiFactory.CreateAnchorRect(
                "RightPane",
                client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(LeftPaneWidth), 0f),
                Vector2.zero);

            return (leftPane, rightPane);
        }

        // ----- Left pane: Vitals box -------------------------------------------

        (BarRow health, BarRow magicka, BarRow fatigue) BuildVitalsBox(RectTransform leftPane)
        {
            RectTransform contentRoot = BuildSectionBox(leftPane, "VitalsBox", SectionMargin, VitalsBoxHeight);

            var health = BuildBarRow(contentRoot, "Health", 0f, HealthFillColor);
            var magicka = BuildBarRow(contentRoot, "Magicka", RowHeight, MagickaFillColor);
            var fatigue = BuildBarRow(contentRoot, "Fatigue", RowHeight * 2f, FatigueFillColor);
            return (health, magicka, fatigue);
        }

        BarRow BuildBarRow(RectTransform contentRoot, string labelText, float yOffset, Color fillColor)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            var row = RuntimeUiFactory.CreateAnchoredRect(
                $"{labelText}Row",
                contentRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeClassicUiMetrics.Ui(yOffset)),
                new Vector2(0f, rowHeight));
            row.pivot = new Vector2(0f, 1f);

            CreateLabel(row, labelText, alignLeft: true);

            // Bar at right portion of the row.
            var barRect = RuntimeUiFactory.CreateAnchoredRect(
                $"{labelText}Bar",
                row,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(RuntimeClassicUiMetrics.Ui(LabelColumnWidth + BarGap), 0f),
                new Vector2(RuntimeClassicUiMetrics.Ui(BarWidth), 0f));
            barRect.pivot = new Vector2(0f, 0.5f);

            // MW_Box thin frame around the bar.
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                barRect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);

            // Fill anchored left, grown rightward by normalized value.
            var fill = RuntimeUiFactory.CreateImage("Fill", frame.Client, fillColor);
            fill.sprite = _theme?.LoadingBarFillSprite;
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = Vector2.zero;

            // Overlay text centered over the whole bar (value text).
            var overlay = RuntimeUiFactory.CreateBitmapText(
                "Value",
                frame.Client,
                _theme?.DefaultFont,
                1f,
                BarOverlayColor,
                BitmapTextAlignment.Center);
            overlay.PixelHeight = RuntimeClassicUiMetrics.Ui(BarTextPixelHeight);
            overlay.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(overlay.rectTransform);

            return new BarRow { Fill = fill.rectTransform, Overlay = overlay };
        }

        // ----- Left pane: Identity box -----------------------------------------

        (ValueRow level, ValueRow race, ValueRow klass) BuildIdentityBox(RectTransform leftPane)
        {
            float y = SectionMargin + VitalsBoxHeight + SectionMargin;
            RectTransform contentRoot = BuildSectionBox(leftPane, "IdentityBox", y, IdentityBoxHeight);

            var level = BuildValueRow(contentRoot, "Level", 0f);
            var race = BuildValueRow(contentRoot, "Race", RowHeight);
            var klass = BuildValueRow(contentRoot, "Class", RowHeight * 2f);
            return (level, race, klass);
        }

        // ----- Left pane: Attributes box ---------------------------------------

        ValueRow[] BuildAttributesBox(RectTransform leftPane)
        {
            float y = SectionMargin + VitalsBoxHeight + SectionMargin + IdentityBoxHeight + SectionMargin;
            RectTransform contentRoot = BuildStretchingSectionBox(leftPane, "AttributesBox", y);

            string[] names = { "Strength", "Intelligence", "Willpower", "Agility", "Speed", "Endurance", "Personality", "Luck" };
            var rows = new ValueRow[names.Length];
            for (int i = 0; i < names.Length; i++)
                rows[i] = BuildValueRow(contentRoot, names[i], i * RowHeight);
            return rows;
        }

        // ----- Right pane: Skills box -----------------------------------------

        SkillsBoxRefs BuildSkillsBox(RectTransform rightPane)
        {
            RectTransform contentRoot = BuildStretchingSectionBox(rightPane, "SkillsBox", SectionMargin);

            // ScrollRect with its own mask viewport.
            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", contentRoot);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f); // near-invisible, required for scroll raycasts
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);

            var scroll = contentRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            // Section order matches the user's spec:
            //   Major / --- / Minor / --- / Misc / --- / Faction / Sign / --- / Reputation
            // Every visible section keeps its own header + rowsRoot. Dividers only exist
            // where the spec calls for them (after Major, Minor, Misc, Sign) - there's
            // intentionally no divider between Faction and Sign.
            var (majorHeader, majorRoot) = BuildSkillSectionHeader(content, "MajorSkills");
            var majorDivider = BuildDividerLine(content, "MajorDivider");
            var (minorHeader, minorRoot) = BuildSkillSectionHeader(content, "MinorSkills");
            var minorDivider = BuildDividerLine(content, "MinorDivider");
            var (miscHeader, miscRoot) = BuildSkillSectionHeader(content, "MiscSkills");
            var miscDivider = BuildDividerLine(content, "MiscDivider");
            var (factionHeader, factionRoot) = BuildSkillSectionHeader(content, "Factions");
            // No divider between Faction and Sign - they're a single "social" group.
            var (signHeader, signRoot, signValue) = BuildSingleValueSection(content, "Sign");
            var signDivider = BuildDividerLine(content, "SignDivider");
            var (reputationHeader, reputationRoot, reputationValue) = BuildSingleValueSection(content, "Reputation");

            return new SkillsBoxRefs
            {
                content = content,
                majorHeader = majorHeader,
                majorRoot = majorRoot,
                minorHeader = minorHeader,
                minorRoot = minorRoot,
                miscHeader = miscHeader,
                miscRoot = miscRoot,
                factionHeader = factionHeader,
                factionRoot = factionRoot,
                signHeader = signHeader,
                signRoot = signRoot,
                signValueText = signValue,
                reputationHeader = reputationHeader,
                reputationRoot = reputationRoot,
                reputationValueText = reputationValue,
                majorDivider = majorDivider,
                minorDivider = minorDivider,
                miscDivider = miscDivider,
                signDivider = signDivider,
            };
        }

        struct SkillsBoxRefs
        {
            public RectTransform content;
            public RectTransform majorHeader, majorRoot;
            public RectTransform minorHeader, minorRoot;
            public RectTransform miscHeader, miscRoot;
            public RectTransform factionHeader, factionRoot;
            public RectTransform signHeader, signRoot;
            public BitmapTextGraphic signValueText;
            public RectTransform reputationHeader, reputationRoot;
            public BitmapTextGraphic reputationValueText;
            public RectTransform majorDivider, minorDivider, miscDivider, signDivider;
        }

        /// <summary>A horizontal gold divider line that sits between section groups in the
        /// skills scroll. Caller positions it via anchoredPosition.</summary>
        RectTransform BuildDividerLine(RectTransform parent, string name)
        {
            var divider = RuntimeUiFactory.CreateImage(name, parent, DividerColor);
            divider.raycastTarget = false;
            divider.rectTransform.anchorMin = new Vector2(0f, 1f);
            divider.rectTransform.anchorMax = new Vector2(1f, 1f);
            divider.rectTransform.pivot = new Vector2(0f, 1f);
            divider.rectTransform.sizeDelta = new Vector2(
                -RuntimeClassicUiMetrics.Ui(DividerHorizontalPadding * 2f),
                RuntimeClassicUiMetrics.Ui(DividerThickness));
            divider.rectTransform.anchoredPosition = new Vector2(RuntimeClassicUiMetrics.Ui(DividerHorizontalPadding), 0f);
            return divider.rectTransform;
        }

        /// <summary>Section with a header and a single-row text value (used for Sign and
        /// Reputation - each shows one centered value rather than a list of rows).</summary>
        (RectTransform header, RectTransform rowsRoot, BitmapTextGraphic valueText)
            BuildSingleValueSection(RectTransform parent, string name)
        {
            var (header, rowsRoot) = BuildSkillSectionHeader(parent, name);

            // Single-row content inside rowsRoot - we use a row rect that matches our
            // standard row height so it lines up with skill/faction rows dimensionally.
            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            var row = RuntimeUiFactory.CreateAnchoredRect(
                $"{name}Row",
                rowsRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, rowHeight));
            row.pivot = new Vector2(0f, 1f);

            var value = RuntimeUiFactory.CreateBitmapText(
                "Value",
                row,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Center);
            value.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            value.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(value.rectTransform);

            return (header, rowsRoot, value);
        }

        // ----- Section box helpers --------------------------------------------

        /// <summary>MW_Box thin frame at a fixed Y offset with a fixed height. Returns the
        /// interior content rect (inset by section padding).</summary>
        RectTransform BuildSectionBox(RectTransform pane, string name, float yOffset, float height)
        {
            var box = RuntimeUiFactory.CreateAnchoredRect(
                name,
                pane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(RuntimeClassicUiMetrics.Ui(SectionMargin), -RuntimeClassicUiMetrics.Ui(yOffset)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(SectionMargin * 2f), RuntimeClassicUiMetrics.Ui(height)));
            box.pivot = new Vector2(0f, 1f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                box,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SectionBoxCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var content = RuntimeUiFactory.CreateAnchorRect(
                "Content",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(SectionPadding), RuntimeClassicUiMetrics.Ui(SectionPadding)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(SectionPadding), -RuntimeClassicUiMetrics.Ui(SectionPadding)));

            return content;
        }

        /// <summary>MW_Box at a fixed Y offset that stretches to fill the rest of the pane.
        /// Returns the interior content rect.</summary>
        RectTransform BuildStretchingSectionBox(RectTransform pane, string name, float yOffset)
        {
            var box = RuntimeUiFactory.CreateAnchorRect(
                name,
                pane,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(SectionMargin), RuntimeClassicUiMetrics.Ui(SectionMargin)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(SectionMargin), -RuntimeClassicUiMetrics.Ui(yOffset)));

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                box,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SectionBoxCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var content = RuntimeUiFactory.CreateAnchorRect(
                "Content",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.Ui(SectionPadding), RuntimeClassicUiMetrics.Ui(SectionPadding)),
                new Vector2(-RuntimeClassicUiMetrics.Ui(SectionPadding), -RuntimeClassicUiMetrics.Ui(SectionPadding)));

            return content;
        }

        // ----- Row helpers ----------------------------------------------------

        /// <summary>Simple "label : value" row - label at left, value right-aligned.</summary>
        ValueRow BuildValueRow(RectTransform contentRoot, string label, float yOffset)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            var row = RuntimeUiFactory.CreateAnchoredRect(
                $"{label}Row",
                contentRoot,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -RuntimeClassicUiMetrics.Ui(yOffset)),
                new Vector2(0f, rowHeight));
            row.pivot = new Vector2(0f, 1f);

            CreateLabel(row, label, alignLeft: true);
            var value = CreateLabel(row, string.Empty, alignLeft: false);
            return new ValueRow { Value = value };
        }

        /// <summary>Shared label/value text graphic. Left or right anchored.</summary>
        BitmapTextGraphic CreateLabel(RectTransform row, string text, bool alignLeft)
        {
            var label = RuntimeUiFactory.CreateBitmapText(
                alignLeft ? "Label" : "Value",
                row,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                alignLeft ? BitmapTextAlignment.Left : BitmapTextAlignment.Right);
            label.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            label.Text = text;

            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return label;
        }

        // ----- Skills section construction + sync -----------------------------

        (RectTransform header, RectTransform rowsRoot) BuildSkillSectionHeader(RectTransform parent, string name)
        {
            var header = RuntimeUiFactory.CreateAnchoredRect(
                $"{name}Header",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, RuntimeClassicUiMetrics.Ui(SectionHeaderHeight)));
            header.pivot = new Vector2(0f, 1f);

            var headerText = RuntimeUiFactory.CreateBitmapText(
                "Label",
                header,
                _theme?.DefaultFont,
                1f,
                SectionHeaderColor,
                BitmapTextAlignment.Center);
            headerText.PixelHeight = RuntimeClassicUiMetrics.Ui(SectionHeaderPixelHeight);
            headerText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            RuntimeUiFactory.Stretch(headerText.rectTransform);

            var rowsRoot = RuntimeUiFactory.CreateAnchoredRect(
                $"{name}Rows",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            rowsRoot.pivot = new Vector2(0f, 1f);
            return (header, rowsRoot);
        }

        void SyncSkillSection(string title, RectTransform header, RectTransform rowsRoot, List<ValueRow> pool, StatsWindowSkillRow[] skills)
        {
            int count = skills?.Length ?? 0;

            if (header.childCount > 0 && header.GetChild(0).TryGetComponent<BitmapTextGraphic>(out var headerText))
                headerText.Text = title;

            bool visible = count > 0;
            header.gameObject.SetActive(visible);
            rowsRoot.gameObject.SetActive(visible);
            if (!visible)
                return;

            while (pool.Count < count)
                pool.Add(BuildValueRow(rowsRoot, string.Empty, pool.Count * RowHeight));

            for (int i = 0; i < pool.Count; i++)
            {
                bool rowVisible = i < count;
                var rowRect = (RectTransform)pool[i].Value.transform.parent;
                rowRect.gameObject.SetActive(rowVisible);
                if (!rowVisible)
                    continue;

                var row = skills[i];
                if (rowRect.GetChild(0).TryGetComponent<BitmapTextGraphic>(out var nameText))
                    nameText.Text = string.IsNullOrWhiteSpace(row?.Name) ? "--" : row.Name.Trim();
                pool[i].Value.Text = string.IsNullOrWhiteSpace(row?.Value) ? "--" : row.Value.Trim();
            }
        }

        void LayoutSkillsContent(StatsWindowViewModel model)
        {
            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            float headerHeight = RuntimeClassicUiMetrics.Ui(SectionHeaderHeight);
            float sectionGap = RuntimeClassicUiMetrics.Ui(SectionGap);
            float y = 0f;

            // Skill sections: divider after each, only if a later section is visible.
            bool majorVisible = (model.MajorSkills?.Length ?? 0) > 0;
            bool minorVisible = (model.MinorSkills?.Length ?? 0) > 0;
            bool miscVisible = (model.MiscSkills?.Length ?? 0) > 0;
            bool factionVisible = (model.Factions?.Length ?? 0) > 0;
            bool signVisible = !string.IsNullOrWhiteSpace(model.BirthSignName);
            bool reputationVisible = !string.IsNullOrWhiteSpace(model.ReputationText);

            y = LayoutSkillSection(_majorHeader, _majorRowsRoot, _majorSkillRowPool, model.MajorSkills, headerHeight, rowHeight, sectionGap, y);
            y = LayoutDivider(_majorDivider, majorVisible && (minorVisible || miscVisible || factionVisible || signVisible || reputationVisible), y);

            y = LayoutSkillSection(_minorHeader, _minorRowsRoot, _minorSkillRowPool, model.MinorSkills, headerHeight, rowHeight, sectionGap, y);
            y = LayoutDivider(_minorDivider, minorVisible && (miscVisible || factionVisible || signVisible || reputationVisible), y);

            y = LayoutSkillSection(_miscHeader, _miscRowsRoot, _miscSkillRowPool, model.MiscSkills, headerHeight, rowHeight, sectionGap, y);
            y = LayoutDivider(_miscDivider, miscVisible && (factionVisible || signVisible || reputationVisible), y);

            // Faction section.
            y = LayoutFactionSection(model.Factions, headerHeight, rowHeight, sectionGap, y);

            // Sign (single row), no divider between it and Faction.
            y = LayoutSingleValueSection(_signHeader, _signRowsRoot, signVisible, headerHeight, rowHeight, sectionGap, y);
            y = LayoutDivider(_signDivider, signVisible && reputationVisible, y);

            // Reputation (single row), no divider after (last section).
            y = LayoutSingleValueSection(_reputationHeader, _reputationRowsRoot, reputationVisible, headerHeight, rowHeight, sectionGap, y);

            _skillsContent.sizeDelta = new Vector2(0f, Mathf.Max(0f, y));
        }

        float LayoutSkillSection(RectTransform header, RectTransform rowsRoot, List<ValueRow> pool, StatsWindowSkillRow[] skills, float headerHeight, float rowHeight, float sectionGap, float y)
        {
            int count = skills?.Length ?? 0;
            if (count == 0)
                return y;

            header.anchoredPosition = new Vector2(0f, -y);
            y += headerHeight;

            rowsRoot.anchoredPosition = new Vector2(0f, -y);
            rowsRoot.sizeDelta = new Vector2(0f, count * rowHeight);
            for (int i = 0; i < count; i++)
            {
                var rowRect = (RectTransform)pool[i].Value.transform.parent;
                rowRect.anchoredPosition = new Vector2(0f, -i * rowHeight);
            }

            y += count * rowHeight + sectionGap;
            return y;
        }

        float LayoutFactionSection(StatsWindowFactionRow[] factions, float headerHeight, float rowHeight, float sectionGap, float y)
        {
            int count = factions?.Length ?? 0;
            if (count == 0)
                return y;

            _factionHeader.anchoredPosition = new Vector2(0f, -y);
            y += headerHeight;

            _factionRowsRoot.anchoredPosition = new Vector2(0f, -y);
            _factionRowsRoot.sizeDelta = new Vector2(0f, count * rowHeight);
            for (int i = 0; i < count; i++)
            {
                var rowRect = (RectTransform)_factionRowPool[i].Value.transform.parent;
                rowRect.anchoredPosition = new Vector2(0f, -i * rowHeight);
            }

            y += count * rowHeight + sectionGap;
            return y;
        }

        float LayoutSingleValueSection(RectTransform header, RectTransform rowsRoot, bool visible, float headerHeight, float rowHeight, float sectionGap, float y)
        {
            if (!visible)
                return y;

            header.anchoredPosition = new Vector2(0f, -y);
            y += headerHeight;

            rowsRoot.anchoredPosition = new Vector2(0f, -y);
            rowsRoot.sizeDelta = new Vector2(0f, rowHeight);
            // The single row inside rowsRoot is a direct child at anchoredPosition (0, 0).
            // It was created by BuildSingleValueSection - no per-frame positioning needed.

            y += rowHeight + sectionGap;
            return y;
        }

        float LayoutDivider(RectTransform divider, bool shouldShow, float y)
        {
            if (!shouldShow)
            {
                divider.gameObject.SetActive(false);
                return y;
            }

            divider.gameObject.SetActive(true);
            float pad = RuntimeClassicUiMetrics.Ui(DividerVerticalPadding);
            y += pad;
            divider.anchoredPosition = new Vector2(RuntimeClassicUiMetrics.Ui(DividerHorizontalPadding), -y);
            y += RuntimeClassicUiMetrics.Ui(DividerThickness) + pad;
            return y;
        }

        void SyncFactionSection(StatsWindowFactionRow[] factions)
        {
            int count = factions?.Length ?? 0;

            bool visible = count > 0;
            _factionHeader.gameObject.SetActive(visible);
            _factionRowsRoot.gameObject.SetActive(visible);
            if (_factionHeader.childCount > 0 && _factionHeader.GetChild(0).TryGetComponent<BitmapTextGraphic>(out var headerText))
                headerText.Text = "Faction";
            if (!visible)
                return;

            while (_factionRowPool.Count < count)
                _factionRowPool.Add(BuildValueRow(_factionRowsRoot, string.Empty, _factionRowPool.Count * RowHeight));

            for (int i = 0; i < _factionRowPool.Count; i++)
            {
                bool rowVisible = i < count;
                var rowRect = (RectTransform)_factionRowPool[i].Value.transform.parent;
                rowRect.gameObject.SetActive(rowVisible);
                if (!rowVisible)
                    continue;

                var row = factions[i];
                if (rowRect.GetChild(0).TryGetComponent<BitmapTextGraphic>(out var nameText))
                    nameText.Text = string.IsNullOrWhiteSpace(row?.Name) ? "--" : row.Name.Trim();
                _factionRowPool[i].Value.Text = string.IsNullOrWhiteSpace(row?.Rank) ? "--" : row.Rank.Trim();
            }
        }

        void SyncSingleValueSection(RectTransform header, RectTransform rowsRoot, BitmapTextGraphic valueText, string headerLabel, string value)
        {
            bool visible = !string.IsNullOrWhiteSpace(value);
            header.gameObject.SetActive(visible);
            rowsRoot.gameObject.SetActive(visible);
            if (header.childCount > 0 && header.GetChild(0).TryGetComponent<BitmapTextGraphic>(out var headerTextGraphic))
                headerTextGraphic.Text = headerLabel;
            if (visible)
                valueText.Text = value.Trim();
        }

        // ----- Bar sync --------------------------------------------------------

        void ApplyBar(BarRow row, float normalizedFill, string text)
        {
            var parentRect = (RectTransform)row.Fill.parent;
            float width = parentRect.rect.width * Mathf.Clamp01(normalizedFill);
            row.Fill.sizeDelta = new Vector2(width, 0f);
            row.Overlay.Text = string.IsNullOrWhiteSpace(text) ? "0/0" : text.Trim();
        }
    }
}
