using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class RuntimeHudView
    {
        // Cluster sizes are specified in the HUD canvas' reference pixels. The HUD
        // lives on its own canvas whose reference resolution is driven by
        // HudScale (ref = 1920×1080 / HudScale), so these nominal widths become
        // "clusterWidth × HudScale" screen pixels at runtime. Values below are
        // half of their screen-pixel equivalents at the default HudScale = 2.
        const float BottomLeftClusterWidth = 260f;
        const float BottomLeftClusterHeight = 120f;
        const float BottomRightClusterWidth = 260f;
        const float BottomRightClusterHeight = 120f;
        const float FocusWidth = 280f;
        const float NotificationWidth = 360f;
        const float SubtitleWidth = 520f;

        // Vanilla MW bar tint palette. Matches the fontcolour=health/magic/fatigue values
        // used by MW_BarTrack_Red/Blue/Green in openmw_hud_energybar.skin.xml and the
        // HealthFillColor/MagickaFillColor/FatigueFillColor consts used by StatsWindowView,
        // so in-world HUD bars and paper-sheet bars read the same color.
        static readonly Color HealthFillColor = new(0.70f, 0.22f, 0.16f, 0.96f);
        static readonly Color MagickaFillColor = new(0.17f, 0.33f, 0.68f, 0.96f);
        static readonly Color FatigueFillColor = new(0.28f, 0.58f, 0.28f, 0.96f);
        static readonly Color EnemyHealthFillColor = new(0.96f, 0.88f, 0.20f, 0.96f);
        static readonly Color BarFrameCenterColor = new(0f, 0f, 0f, 0.82f);
        // Weapon/spell quick-slot status bar tints. Matches vanilla's
        // fontcolour=weapon_fill (warm gold) and fontcolour=magic_fill (violet).
        static readonly Color WeaponStatusFillColor = new(0.80f, 0.66f, 0.22f, 0.95f);
        static readonly Color SpellStatusFillColor = new(0.60f, 0.46f, 0.88f, 0.95f);

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _root;
        readonly RectTransform _bottomLeftCluster;
        readonly RectTransform _bottomRightCluster;
        readonly RectTransform _centerCluster;
        readonly RectTransform _topCenterCluster;
        readonly Image _crosshairImage;
        readonly BitmapTextGraphic _weaponSpellText;
        readonly BitmapTextGraphic _cellNameText;
        readonly BitmapTextGraphic _focusText;
        readonly BitmapTextGraphic _notificationText;
        readonly BitmapTextGraphic _subtitleText;
        readonly Image _healthFill;
        readonly Image _magickaFill;
        readonly Image _fatigueFill;
        readonly Image _enemyHealthFill;
        readonly RectTransform _healthFillParent;
        readonly RectTransform _magickaFillParent;
        readonly RectTransform _fatigueFillParent;
        readonly RectTransform _enemyHealthFillParent;
        readonly RectTransform _enemyHealthRow;
        readonly Image _weaponStatusFill;
        readonly Image _spellStatusFill;
        readonly RectTransform _weaponStatusFillParent;
        readonly RectTransform _spellStatusFillParent;
        readonly Image _spellIcon;
        readonly RectTransform _spellSlotRoot;
        readonly RectTransform _sneakSlotRow;
        readonly RectTransform _effectBoxRoot;
        readonly RuntimeMagicEffectIconStripView _activeEffectStrip;
        GameObject _rootGo;
        GameObject _crosshairGo;
        GameObject _focusAnchorGo;
        GameObject _notificationAnchorGo;
        GameObject _subtitleAnchorGo;
        GameObject _spellIconGo;
        GameObject _enemyHealthRowGo;
        GameObject _sneakSlotRowGo;
        GameObject _effectBoxGo;
        LocalMapTileGridView _miniMapGrid;

        public RuntimeHudView(RectTransform parent, RuntimeUiTheme theme, RuntimeInventoryIconService iconService = null)
        {
            _theme = theme;
            _iconService = iconService;
            _root = RuntimeUiFactory.CreateStretchRect("HudRoot", parent);
            // Cluster corner offsets are also in HUD-canvas reference pixels —
            // halved from their legacy screen-pixel constants so they land at
            // the same physical screen positions under the new HudScale=2
            // default ref=960×540 canvas.
            _bottomLeftCluster = CreateCluster(
                "BottomLeftHudCluster",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(11f, 9f),
                new Vector2(BottomLeftClusterWidth, BottomLeftClusterHeight));
            _bottomRightCluster = CreateCluster(
                "BottomRightHudCluster",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-9f, 9f),
                new Vector2(BottomRightClusterWidth, BottomRightClusterHeight));
            _centerCluster = CreateCluster(
                "CenterHudCluster",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            _topCenterCluster = CreateCluster(
                "TopCenterHudCluster",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -52f),
                Vector2.zero);

            (_healthFill, _magickaFill, _fatigueFill, _enemyHealthFill, _enemyHealthRow) = BuildStatBars();
            (_weaponSpellText, _weaponStatusFill, _spellStatusFill, _spellIcon, _spellSlotRoot, _sneakSlotRow) = BuildQuickSlots();
            (_cellNameText, _effectBoxRoot, _activeEffectStrip) = BuildMapCluster();
            (_focusText, _notificationText, _subtitleText) = BuildMessages();
            _rootGo = _root.gameObject;
            _healthFillParent = (RectTransform)_healthFill.transform.parent;
            _magickaFillParent = (RectTransform)_magickaFill.transform.parent;
            _fatigueFillParent = (RectTransform)_fatigueFill.transform.parent;
            _enemyHealthFillParent = (RectTransform)_enemyHealthFill.transform.parent;
            _weaponStatusFillParent = (RectTransform)_weaponStatusFill.transform.parent;
            _spellStatusFillParent = (RectTransform)_spellStatusFill.transform.parent;
            _focusAnchorGo = _focusText.transform.parent.gameObject;
            _notificationAnchorGo = _notificationText.transform.parent.gameObject;
            _subtitleAnchorGo = _subtitleText.transform.parent.gameObject;
            _spellIconGo = _spellIcon.gameObject;
            _enemyHealthRowGo = _enemyHealthRow.gameObject;
            _sneakSlotRowGo = _sneakSlotRow.gameObject;
            _effectBoxGo = _effectBoxRoot.gameObject;

            // Vanilla HUD crosshair - baked sprite from Textures/target.dds, sized to its
            // native 32x32 at reference resolution (matches openmw_hud.layout "Crosshair").
            _crosshairImage = RuntimeUiFactory.CreateImage("Crosshair", _centerCluster, Color.white);
            _crosshairImage.sprite = _theme.CrosshairSprite;
            _crosshairImage.type = Image.Type.Simple;
            _crosshairImage.preserveAspect = true;
            _crosshairImage.raycastTarget = false;
            _crosshairImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _crosshairImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _crosshairImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _crosshairImage.rectTransform.anchoredPosition = Vector2.zero;
            _crosshairImage.rectTransform.sizeDelta = RuntimeClassicUiMetrics.HudLayout(new Vector2(32f, 32f));
            _crosshairGo = _crosshairImage.gameObject;
        }

        public void SetVisible(bool visible)
        {
            SetActiveIfChanged(_rootGo, visible);
        }

        public void Sync(RuntimeHudViewModel model)
        {
            if (model == null || !model.Visible)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            SetActiveIfChanged(_crosshairGo, model.ShowCrosshair);
            SetActiveIfChanged(_focusAnchorGo, !string.IsNullOrWhiteSpace(model.FocusText));
            SetActiveIfChanged(_notificationAnchorGo, !string.IsNullOrWhiteSpace(model.NotificationText));
            SetActiveIfChanged(_subtitleAnchorGo, !string.IsNullOrWhiteSpace(model.SubtitleText));
            _focusText.Text = model.FocusText ?? string.Empty;
            _notificationText.Text = model.NotificationText ?? string.Empty;
            _subtitleText.Text = model.SubtitleText ?? string.Empty;
            _weaponSpellText.Text = model.WeaponSpellText ?? string.Empty;
            _cellNameText.Text = model.CellNameText ?? string.Empty;

            SetBarFill(_healthFill, _healthFillParent, model.HealthFillNormalized);
            SetBarFill(_magickaFill, _magickaFillParent, model.MagickaFillNormalized);
            SetBarFill(_fatigueFill, _fatigueFillParent, model.FatigueFillNormalized);
            SetBarFill(_weaponStatusFill, _weaponStatusFillParent, model.WeaponStatusNormalized);
            SetBarFill(_spellStatusFill, _spellStatusFillParent, model.SpellStatusNormalized);
            bool hasSpellIcon = !string.IsNullOrWhiteSpace(model.SelectedSpellIconPath);
            SetActiveIfChanged(_spellIconGo, hasSpellIcon);
            if (hasSpellIcon)
                _spellIcon.sprite = _iconService?.GetMagicEffectSprite(model.SelectedSpellIconPath);
            RuntimeUiPopupUtility.SetTooltip(_spellSlotRoot.gameObject, model.SelectedSpellTooltip);
            SetActiveIfChanged(_enemyHealthRowGo, model.ShowEnemyHealth);
            if (model.ShowEnemyHealth)
                SetBarFill(_enemyHealthFill, _enemyHealthFillParent, model.EnemyHealthFillNormalized);
            SetActiveIfChanged(_sneakSlotRowGo, model.ShowSneakIndicator);
            SyncActiveEffects(model.ActiveEffects);
            _miniMapGrid.Sync(model.LocalMap);
        }

        // Vanilla stacks the bars at y = 176 / 161 / 146 / 131 (fatigue / magicka / health / enemy)
        // in openmw_hud.layout - 15 px between bars. We preserve the spacing ratio but
        // express it in the HUD cluster's bottom-up coordinate space (higher y = higher on
        // screen): Fatigue at y=12 (lowest), Magicka at y=27, Health at y=42, Enemy at y=57.
        // Enemy bar is hidden by default (shown only while targeting a hostile actor).
        (Image health, Image magicka, Image fatigue, Image enemyHealth, RectTransform enemyRow) BuildStatBars()
        {
            Image fatigue = CreateHudStatBar("FatigueFrame", new Vector2(13f, 12f), FatigueFillColor, out _);
            Image magicka = CreateHudStatBar("MagickaFrame", new Vector2(13f, 27f), MagickaFillColor, out _);
            Image health = CreateHudStatBar("HealthFrame", new Vector2(13f, 42f), HealthFillColor, out _);
            Image enemyHealth = CreateHudStatBar("EnemyHealthFrame", new Vector2(13f, 57f), EnemyHealthFillColor, out RectTransform enemyRow);
            enemyRow.gameObject.SetActive(false); // vanilla default: hidden until targeting
            return (health, magicka, fatigue, enemyHealth, enemyRow);
        }

        /// <summary>
        /// Builds a vanilla MW energy bar: <c>HUD_Box</c> chrome (BlackBG + thin MW_Box frame)
        /// wrapping a tinted <c>menu_bar_gray.dds</c> track fill. The returned Image is the
        /// left-anchored fill whose width is driven by normalized value at sync time. Also
        /// emits the root row rect via <paramref name="row"/> so callers can hide the whole
        /// bar cleanly (used by the enemy health bar's default-hidden state).
        /// </summary>
        Image CreateHudStatBar(string name, Vector2 localPosition, Color fillColor, out RectTransform row)
        {
            row = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _bottomLeftCluster,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                RuntimeClassicUiMetrics.HudLayout(localPosition),
                RuntimeClassicUiMetrics.HudLayout(new Vector2(65f, 12f)));
            row.pivot = new Vector2(0f, 0f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                row,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);

            var fill = RuntimeUiFactory.CreateImage("Fill", frame.Client, fillColor);
            fill.sprite = _theme?.LoadingBarFillSprite;
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = Vector2.zero;
            return fill;
        }

        // Vanilla HUD quick-slot row (openmw_hud.layout):
        //   WeapBox  at x=82,  size 36x41  (icon box 36x36 + status bar 36x6)
        //   SpellBox at x=122, size 36x41  (same structure, magic tint on status bar)
        //   SneakBox at x=162, size 36x36  (just an icon, no status bar, hidden by default)
        // Boxes use HUD_Box: thin MW_Box frame + BlackBG interior. The icon child in each
        // weapon/spell box is left empty here - the item/spell icon pipeline will drop an
        // Image in when equipment wiring lands (openmw's ItemWidget/SpellWidget).
        (BitmapTextGraphic nameText, Image weaponStatus, Image spellStatus, Image spellIcon, RectTransform spellSlotRoot, RectTransform sneakSlotRow) BuildQuickSlots()
        {
            var nameText = RuntimeUiFactory.CreateBitmapText(
                "WeaponSpellName",
                _bottomLeftCluster,
                _theme.DefaultFont,
                RuntimeClassicUiMetrics.HudText(0.58f),
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Left);
            nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameText.rectTransform.anchorMax = new Vector2(0f, 0f);
            nameText.rectTransform.pivot = new Vector2(0f, 0f);
            nameText.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.HudLayout(new Vector2(13f, 58f));
            nameText.rectTransform.sizeDelta = RuntimeClassicUiMetrics.HudLayout(new Vector2(270f, 24f));
            nameText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var weaponSlot = CreateHudEquipmentSlot("WeaponBox", new Vector2(82f, 13f), WeaponStatusFillColor);
            var spellSlot = CreateHudEquipmentSlot("SpellBox", new Vector2(122f, 13f), SpellStatusFillColor);
            RectTransform sneakSlotRow = CreateHudSneakSlot("SneakBox", new Vector2(162f, 13f));
            sneakSlotRow.gameObject.SetActive(false); // vanilla default: hidden until sneaking
            return (nameText, weaponSlot.StatusFill, spellSlot.StatusFill, spellSlot.Icon, spellSlot.Root, sneakSlotRow);
        }

        /// <summary>
        /// Vanilla equipment slot (weapon or spell). Builds a 36x41 root whose top 36x36
        /// is an empty HUD_Box icon cell and whose bottom 36x6 is a tinted status bar
        /// (weapon condition / spell cast readiness). Icon cell stays empty until the
        /// equipped-item icon pipeline lands.
        /// </summary>
        (RectTransform Root, Image Icon, Image StatusFill) CreateHudEquipmentSlot(string name, Vector2 localPosition, Color statusTint)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _bottomLeftCluster,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                RuntimeClassicUiMetrics.HudLayout(localPosition),
                RuntimeClassicUiMetrics.HudLayout(new Vector2(36f, 41f)));
            root.pivot = new Vector2(0f, 0f);

            var boxRoot = RuntimeUiFactory.CreateAnchoredRect(
                "BoxRoot",
                root,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.HudLayout(new Vector2(36f, 36f)));
            boxRoot.pivot = new Vector2(0f, 1f);

            var boxFrame = RuntimeUiFactory.CreateBorderFrame(
                "BoxFrame",
                boxRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(boxFrame.Root);

            var icon = RuntimeUiFactory.CreateImage("Icon", boxFrame.Client, Color.white);
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RuntimeUiFactory.SetInset(
                icon.rectTransform,
                RuntimeClassicUiMetrics.HudLayout(2f),
                RuntimeClassicUiMetrics.HudLayout(2f),
                -RuntimeClassicUiMetrics.HudLayout(2f),
                -RuntimeClassicUiMetrics.HudLayout(2f));
            icon.gameObject.SetActive(false);

            var statusRoot = RuntimeUiFactory.CreateAnchoredRect(
                "StatusRoot",
                root,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                Vector2.zero,
                RuntimeClassicUiMetrics.HudLayout(new Vector2(36f, 6f)));
            statusRoot.pivot = new Vector2(0f, 0f);

            var statusFrame = RuntimeUiFactory.CreateBorderFrame(
                "StatusFrame",
                statusRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(statusFrame.Root);

            var statusFill = RuntimeUiFactory.CreateImage("StatusFill", statusFrame.Client, statusTint);
            statusFill.sprite = _theme?.LoadingBarFillSprite;
            statusFill.type = Image.Type.Simple;
            statusFill.raycastTarget = false;
            statusFill.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            statusFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            statusFill.rectTransform.anchoredPosition = Vector2.zero;
            statusFill.rectTransform.sizeDelta = Vector2.zero;
            return (root, icon, statusFill);
        }

        /// <summary>
        /// Vanilla sneak indicator slot. 36x36 HUD_Box with the stealth_sneak sprite
        /// inset 2px from each edge (per openmw_hud.layout "SneakImage"). No status bar.
        /// Hidden at build time; caller toggles visibility per <c>ShowSneakIndicator</c>.
        /// Returns the row rect for visibility toggling.
        /// </summary>
        RectTransform CreateHudSneakSlot(string name, Vector2 localPosition)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _bottomLeftCluster,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                RuntimeClassicUiMetrics.HudLayout(localPosition),
                RuntimeClassicUiMetrics.HudLayout(new Vector2(36f, 36f)));
            root.pivot = new Vector2(0f, 0f);

            var boxFrame = RuntimeUiFactory.CreateBorderFrame(
                "BoxFrame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                BarFrameCenterColor);
            RuntimeUiFactory.Stretch(boxFrame.Root);

            var icon = RuntimeUiFactory.CreateImage("SneakIcon", boxFrame.Client, Color.white);
            icon.sprite = _theme?.StealthSneakSprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RuntimeUiFactory.SetInset(
                icon.rectTransform,
                RuntimeClassicUiMetrics.HudLayout(2f),
                RuntimeClassicUiMetrics.HudLayout(2f),
                -RuntimeClassicUiMetrics.HudLayout(2f),
                -RuntimeClassicUiMetrics.HudLayout(2f));
            return root;
        }

        (BitmapTextGraphic cellName, RectTransform effectBoxRoot, RuntimeMagicEffectIconStripView activeEffectStrip) BuildMapCluster()
        {
            var cellName = RuntimeUiFactory.CreateBitmapText(
                "CellName",
                _bottomRightCluster,
                _theme.DefaultFont,
                RuntimeClassicUiMetrics.HudText(0.56f),
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Right);
            cellName.rectTransform.anchorMin = new Vector2(0f, 0f);
            cellName.rectTransform.anchorMax = new Vector2(1f, 0f);
            cellName.rectTransform.pivot = new Vector2(1f, 0f);
            cellName.rectTransform.anchoredPosition = new Vector2(0f, RuntimeClassicUiMetrics.HudLayout(87f));
            cellName.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.HudLayout(-12f), RuntimeClassicUiMetrics.HudLayout(24f));
            cellName.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var effectBox = RuntimeUiFactory.CreateAnchoredRect(
                "EffectBox",
                _bottomRightCluster,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-RuntimeClassicUiMetrics.HudLayout(89f), RuntimeClassicUiMetrics.HudLayout(12f)),
                RuntimeClassicUiMetrics.HudLayout(new Vector2(20f, 20f)));
            effectBox.pivot = new Vector2(1f, 0f);
            var effectFrame = RuntimeUiFactory.CreateBorderFrame(
                "EffectFrame",
                effectBox,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.64f));
            RuntimeUiFactory.Stretch(effectFrame.Root);
            var activeEffectStrip = new RuntimeMagicEffectIconStripView(
                effectFrame.Client,
                _iconService,
                RuntimeClassicUiMetrics.HudLayout(16f),
                0f,
                RuntimeClassicUiMetrics.HudLayout(2f),
                RuntimeClassicUiMetrics.HudLayout(2f),
                rightAnchored: true);
            effectBox.gameObject.SetActive(false);

            var miniMapRoot = RuntimeUiFactory.CreateAnchoredRect(
                "MiniMapBox",
                _bottomRightCluster,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(BottomRightClusterWidth - RuntimeClassicUiMetrics.HudLayout(65f), RuntimeClassicUiMetrics.HudLayout(12f)),
                RuntimeClassicUiMetrics.HudLayout(new Vector2(65f, 65f)));
            miniMapRoot.pivot = new Vector2(0f, 0f);
            var miniMapFrame = RuntimeUiFactory.CreateBorderFrame(
                "MiniMapFrame",
                miniMapRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.82f));
            RuntimeUiFactory.Stretch(miniMapFrame.Root);
            var miniMapFace = RuntimeUiFactory.CreateAnchorRect(
                "MiniMapFace",
                miniMapFrame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(RuntimeClassicUiMetrics.HudLayout(2f), RuntimeClassicUiMetrics.HudLayout(2f)),
                new Vector2(-RuntimeClassicUiMetrics.HudLayout(2f), -RuntimeClassicUiMetrics.HudLayout(2f)));
            _miniMapGrid = new LocalMapTileGridView(
                "MiniMapGrid",
                miniMapFace,
                _theme,
                RuntimeClassicUiMetrics.HudLayout(new Vector2(32f, 32f)),
                new Color(0.16f, 0.17f, 0.12f, 0.92f));
            return (cellName, effectBox, activeEffectStrip);
        }

        (BitmapTextGraphic focusText, BitmapTextGraphic notificationText, BitmapTextGraphic subtitleText) BuildMessages()
        {
            // Vanilla MW shows the raycast-target label as floating text with no backdrop
            // or border — just the name of the focused object painted below the crosshair.
            // We keep the transparent image as a layout + visibility anchor (Sync toggles
            // its parent active state) but its alpha is 0 so nothing renders.
            var focusBackdrop = RuntimeUiFactory.CreateImage("FocusAnchor", _centerCluster, new Color(0f, 0f, 0f, 0f));
            focusBackdrop.raycastTarget = false;
            focusBackdrop.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            focusBackdrop.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            focusBackdrop.rectTransform.pivot = new Vector2(0.5f, 0f);
            focusBackdrop.rectTransform.anchoredPosition = new Vector2(0f, RuntimeClassicUiMetrics.HudLayout(21f));
            focusBackdrop.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.HudLayout(FocusWidth), RuntimeClassicUiMetrics.HudLayout(15f));

            var focusText = RuntimeUiFactory.CreateBitmapText(
                "FocusText",
                focusBackdrop.transform,
                _theme.DefaultFont,
                RuntimeClassicUiMetrics.OverlayText(0.72f),
                new Color(0.96f, 0.93f, 0.85f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(focusText.rectTransform);
            focusText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var notificationBackdrop = RuntimeUiFactory.CreateImage("NotificationBackdrop", _topCenterCluster, new Color(0f, 0f, 0f, 0.62f));
            notificationBackdrop.raycastTarget = false;
            notificationBackdrop.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.pivot = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.anchoredPosition = Vector2.zero;
            notificationBackdrop.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.HudLayout(NotificationWidth), RuntimeClassicUiMetrics.HudLayout(17f));

            var notificationText = RuntimeUiFactory.CreateBitmapText(
                "NotificationText",
                notificationBackdrop.transform,
                _theme.DefaultFont,
                RuntimeClassicUiMetrics.OverlayText(0.74f),
                new Color(0.97f, 0.94f, 0.86f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(notificationText.rectTransform);
            notificationText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var subtitleBackdrop = RuntimeUiFactory.CreateImage("SubtitleBackdrop", _root, new Color(0f, 0f, 0f, 0.66f));
            subtitleBackdrop.raycastTarget = false;
            subtitleBackdrop.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            subtitleBackdrop.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            subtitleBackdrop.rectTransform.pivot = new Vector2(0.5f, 0f);
            subtitleBackdrop.rectTransform.anchoredPosition = new Vector2(0f, RuntimeClassicUiMetrics.HudLayout(108f));
            subtitleBackdrop.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.HudLayout(SubtitleWidth), RuntimeClassicUiMetrics.HudLayout(38f));

            var subtitleText = RuntimeUiFactory.CreateBitmapText(
                "SubtitleText",
                subtitleBackdrop.transform,
                _theme.DefaultFont,
                RuntimeClassicUiMetrics.OverlayText(0.78f),
                new Color(0.97f, 0.94f, 0.86f),
                BitmapTextAlignment.Center);
            subtitleText.WrapMode = BitmapTextWrapMode.Word;
            RuntimeUiFactory.Stretch(subtitleText.rectTransform);
            subtitleText.rectTransform.offsetMin = new Vector2(RuntimeClassicUiMetrics.HudLayout(8f), RuntimeClassicUiMetrics.HudLayout(4f));
            subtitleText.rectTransform.offsetMax = new Vector2(-RuntimeClassicUiMetrics.HudLayout(8f), -RuntimeClassicUiMetrics.HudLayout(4f));
            subtitleText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            return (focusText, notificationText, subtitleText);
        }

        RectTransform CreateCluster(string name, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            // HUD canvas owns the HudScale → ref-resolution mapping, so cluster
            // geometry flows through HudLayout (identity today) rather than Ui,
            // keeping the whole HUD under a single scale knob.
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _root,
                anchor,
                anchor,
                RuntimeClassicUiMetrics.HudLayout(anchoredPosition),
                RuntimeClassicUiMetrics.HudLayout(size));
            rect.pivot = pivot;
            return rect;
        }

        static void SetBarFill(Image fill, RectTransform parentRect, float normalized)
        {
            float width = Mathf.Max(0f, parentRect.rect.width * Mathf.Clamp01(normalized));
            fill.rectTransform.sizeDelta = new Vector2(width, 0f);
        }

        void SyncActiveEffects(RuntimeMagicEffectIconViewModel[] activeEffects)
        {
            activeEffects ??= System.Array.Empty<RuntimeMagicEffectIconViewModel>();
            bool hasEffects = activeEffects.Length > 0;
            SetActiveIfChanged(_effectBoxGo, hasEffects);
            if (!hasEffects)
            {
                _activeEffectStrip.Sync(activeEffects, collapseRoot: false);
                return;
            }

            float iconSize = RuntimeClassicUiMetrics.HudLayout(16f);
            float padding = RuntimeClassicUiMetrics.HudLayout(2f);
            float width = padding * 2f + activeEffects.Length * iconSize;
            _effectBoxRoot.sizeDelta = new Vector2(width, RuntimeClassicUiMetrics.HudLayout(20f));
            _activeEffectStrip.Sync(activeEffects, collapseRoot: false);
        }

        static void SetActiveIfChanged(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }
    }
}
