using System;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind Map window.
    ///
    /// Mirrors <c>openmw_map_window.layout</c> (see <c>docs/ui-reference/openmw-ui-skins.md</c>)
    /// at the reference 300x300 size:
    /// <list type="bullet">
    ///   <item>Local map panel - MW_ScrollView-equivalent fills the full client area
    ///     (minus a thin footer for the toggle button). Shows the zoomed local map tiles
    ///     around the player's position.</item>
    ///   <item>Global map panel - same rect, overlaid. Toggled via the World button.
    ///     Only one of the two panels is visible at a time.</item>
    ///   <item>Compass - 32x32 <c>textures\compass.dds</c> in the top-left corner of the
    ///     active map panel (RotatingSkin in MyGUI; rotated by the player's heading).</item>
    ///   <item>World / Local toggle - AutoSizedButton MW_Button pinned bottom-right.
    ///     Caption flips between "World" and "Local" based on the active panel. Hidden
    ///     while in an interior cell (no world map available).</item>
    /// </list>
    ///
    /// Both map panels are reserved but render no tiles yet - the map-tile pipeline
    /// lands later. The compass and toggle button are wired; the MW_Box backdrop
    /// stands in for the future map image. Cell/region names surface through tooltips
    /// in vanilla; we drop the old summary/region/cell/streaming text lines entirely.
    /// </summary>
    sealed class MapWindowView
    {
        // Palette.
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color MapPanelCenterColor = new(0.02f, 0.02f, 0.02f, 0.86f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);

        // Pixel heights.
        const float CaptionPixelHeight = 14f;
        const float BodyTextPixelHeight = 13f;

        // Window geometry (matches openmw_map_window.layout position "0 0 300 300").
        const float DefaultWindowWidth = 300f;
        const float DefaultWindowHeight = 300f;
        const float MinWindowWidth = 240f;
        const float MinWindowHeight = 220f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;

        // Compass: vanilla compass.dds is 32x32.
        const float CompassSize = 32f;

        // World button: AutoSizedButton position="213 233 61 22" -> 61w 22h right-anchored.
        const float ToggleButtonWidth = 61f;
        const float ToggleButtonHeight = 22f;
        const float ToggleButtonMargin = 4f;      // small gap from panel edge

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _viewport;
        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;

        readonly RectTransform _localMapRoot;
        readonly RectTransform _globalMapRoot;
        readonly Image _localCompass;
        readonly Image _globalCompass;
        readonly MorrowindButtonView _toggleButton;

        // True while the Global (world) panel is active; otherwise Local.
        bool _globalActive;

        public MapWindowView(RectTransform parent, RectTransform viewport, RuntimeUiTheme theme, Action onRectChanged, Action onPinToggled = null)
        {
            _theme = theme;
            _viewport = viewport;

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "MapWindow",
                parent,
                theme,
                "Map",
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

            (_localMapRoot, _globalMapRoot, _localCompass, _globalCompass, _toggleButton) = BuildClient();
            ApplyPanelVisibility();
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

        public void Sync(MapWindowViewModel model)
        {
            if (model == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Map" : model.Title.Trim();
            _window.PinButton?.SetPinned(model.Pinned);
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            // Toggle button caption: VM drives ("World" / "Local" depending on active panel).
            string toggleLabel = string.IsNullOrWhiteSpace(model.ToggleButtonText) ? "World" : model.ToggleButtonText.Trim();
            _toggleButton.Label.Text = toggleLabel;

            // Interior cells hide the world-map toggle entirely (no global map to show).
            _toggleButton.Root.gameObject.SetActive(!model.InteriorActive);

            // If we're forced into an interior, fall back to the local panel.
            if (model.InteriorActive && _globalActive)
            {
                _globalActive = false;
                ApplyPanelVisibility();
            }
        }

        // ----- Build ------------------------------------------------------------

        (RectTransform localRoot, RectTransform globalRoot, Image localCompass, Image globalCompass, MorrowindButtonView toggleButton) BuildClient()
        {
            // Map area fills the full client - vanilla overlays the World button on top
            // of the map panel, not below it. Compass and toggle button both live inside
            // the map panel stack and float above the tiles.
            var local = BuildMapPanel("LocalMap");
            var global = BuildMapPanel("GlobalMap");

            var localCompass = BuildCompass("CompassLocal", local);
            var globalCompass = BuildCompass("CompassGlobal", global);

            // World / Local toggle - bottom-right of the window client. The layout pins
            // it at (213, 233) inside a 300x300 window, which is margin-from-bottom-right
            // of (300-213-61, 300-233-22) = (26, 45) — but vanilla MW renders it right
            // up against the bottom-right corner in-engine. We split the difference and
            // use a small 4 px margin so the filigree border isn't touched.
            float btnWidth = RuntimeClassicUiMetrics.Ui(ToggleButtonWidth);
            float btnHeight = RuntimeClassicUiMetrics.Ui(ToggleButtonHeight);
            float btnMargin = RuntimeClassicUiMetrics.Ui(ToggleButtonMargin);

            var buttonRect = RuntimeUiFactory.CreateAnchoredRect(
                "WorldButtonRect",
                _window.Client,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-btnMargin - btnWidth, btnMargin),
                new Vector2(btnWidth, btnHeight));

            var toggleButton = RuntimeUiFactory.CreateMorrowindButton(
                "Button",
                buttonRect,
                _theme,
                "World",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(toggleButton.Root);
            toggleButton.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            toggleButton.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            toggleButton.Button.transition = Selectable.Transition.ColorTint;
            toggleButton.Button.onClick.AddListener(OnToggleClicked);

            return (local, global, localCompass, globalCompass, toggleButton);
        }

        RectTransform BuildMapPanel(string name)
        {
            // Full-client panel - the MW_MapView scrollview in vanilla. The inner frame
            // uses the same thin MW_Box border used throughout, with a nearly-opaque
            // black center that stands in for the map tiles until the tile pipeline is
            // online (reserve space, draw nothing inside).
            var root = RuntimeUiFactory.CreateAnchorRect(
                name,
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                MapPanelCenterColor);
            RuntimeUiFactory.Stretch(frame.Root);
            frame.Center.raycastTarget = false;

            return root;
        }

        Image BuildCompass(string name, RectTransform panel)
        {
            float size = RuntimeClassicUiMetrics.Ui(CompassSize);
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                panel,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
                new Vector2(size, size));
            rect.pivot = new Vector2(0f, 1f);

            var image = RuntimeUiFactory.CreateImage("Image", rect, Color.white);
            RuntimeUiFactory.Stretch(image.rectTransform);
            image.sprite = _theme?.CompassSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            // Vanilla wraps the compass in RotatingSkin - rotated by the player's heading
            // via the bridge. We leave the transform un-rotated until the heading wire
            // lands; the compass then just shows north-up like a static icon.

            return image;
        }

        // ----- Toggle -----------------------------------------------------------

        void OnToggleClicked()
        {
            _globalActive = !_globalActive;
            ApplyPanelVisibility();
        }

        void ApplyPanelVisibility()
        {
            _localMapRoot.gameObject.SetActive(!_globalActive);
            _globalMapRoot.gameObject.SetActive(_globalActive);
            // Caption flips to match the *other* panel (vanilla convention: button shows
            // what you'll switch *to*). The view model's ToggleButtonText is authoritative
            // when present, but we nudge the local state so the caption reads sensibly
            // until the next Sync.
            _toggleButton.Label.Text = _globalActive ? "Local" : "World";

            // Keep compass references live (used by future heading-sync code).
            _ = _localCompass;
            _ = _globalCompass;
        }
    }
}
