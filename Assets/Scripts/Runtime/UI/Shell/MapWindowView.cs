using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class MapWindowView
    {
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color MapPanelCenterColor = new(0.02f, 0.02f, 0.02f, 0.86f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color MapMarkerColor = new(0.86f, 0.68f, 0.28f, 0.95f);

        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float BodyTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float MinWindowWidth = 240f;
        const float MinWindowHeight = 220f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;
        const float ToggleButtonWidth = 61f;
        const float ToggleButtonHeight = 22f;
        const float ToggleButtonRightMargin = 10f;
        const float ToggleButtonBottomMargin = 9f;
        const float MaxMapZoom = 4f;
        const float MinGlobalMapZoom = 0.125f;
        const float MinLocalMapZoom = 1f / 3f;

        readonly RuntimeUiTheme _theme;
        readonly RectTransform _viewport;
        readonly MorrowindWindowView _window;
        readonly RuntimeWindowDragHandle _dragHandle;
        readonly RuntimeWindowResizeHandle _resizeHandle;
        readonly RectTransform _localMapRoot;
        readonly RectTransform _globalMapRoot;
        readonly MorrowindButtonView _toggleButton;
        readonly LocalMapTileGridView _localGrid;
        readonly GlobalMapView _globalMap;
        readonly BitmapTextGraphic _placeholderText;
        MapWindowViewModel _model;

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

            (_localMapRoot, _globalMapRoot, _toggleButton, _localGrid, _globalMap, _placeholderText) = BuildClient();
            _localMapRoot.gameObject.SetActive(true);
            _globalMapRoot.gameObject.SetActive(false);
            _toggleButton.Root.gameObject.SetActive(false);
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

            _model = model;
            SetVisible(true);
            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Map" : model.Title.Trim();
            _window.PinButton?.SetPinned(model.Pinned);
            if (!IsInteracting)
                RuntimeWindowSurfaceUtility.ApplyNormalizedRect(_window.Root, _viewport, model.NormalizedRect);

            bool showGlobal = model.GlobalEnabled && model.Mode == MapWindowMode.Global;
            _localMapRoot.gameObject.SetActive(!showGlobal);
            _globalMapRoot.gameObject.SetActive(showGlobal);
            _toggleButton.Root.gameObject.SetActive(model.GlobalEnabled);
            _toggleButton.Label.Text = string.IsNullOrWhiteSpace(model.ToggleButtonText) ? (showGlobal ? "Local" : "World") : model.ToggleButtonText;
            _localGrid.Sync(model.LocalMap);
            _globalMap.Sync(model.GlobalMap);
            bool ready = model.LocalMap?.Ready == true;
            _placeholderText.gameObject.SetActive(!showGlobal && !ready);
            _placeholderText.Text = string.IsNullOrWhiteSpace(model.ViewSummaryText)
                ? "Local map unavailable."
                : model.ViewSummaryText.Trim();
        }

        (RectTransform localRoot, RectTransform globalRoot, MorrowindButtonView toggleButton, LocalMapTileGridView localGrid, GlobalMapView globalMap, BitmapTextGraphic placeholderText) BuildClient()
        {
            var local = BuildMapPanel("LocalMap", out RectTransform localClient);
            var global = BuildMapPanel("GlobalMap", out RectTransform globalClient);

            var localGrid = new LocalMapTileGridView(
                "LocalMapGrid",
                localClient,
                _theme,
                RuntimeClassicUiMetrics.Ui(new Vector2(32f, 32f)),
                MapPanelCenterColor,
                RuntimeClassicUiMetrics.Ui(new Vector2(256f, 256f)).x);
            var globalMap = new GlobalMapView(
                "GlobalMapGrid",
                globalClient,
                _theme,
                RuntimeClassicUiMetrics.Ui(new Vector2(32f, 32f)));
            AttachInputSurface(localClient);
            AttachInputSurface(globalClient);

            var placeholder = RuntimeUiFactory.CreateBitmapText(
                "UnavailableText",
                localClient,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Center);
            RuntimeUiFactory.SetInsetText(
                placeholder.rectTransform,
                placeholder,
                8f,
                8f,
                -8f,
                -8f,
                BitmapTextVerticalAlignment.Middle);
            placeholder.gameObject.SetActive(false);

            float btnWidth = RuntimeClassicUiMetrics.Ui(ToggleButtonWidth);
            float btnHeight = RuntimeClassicUiMetrics.Ui(ToggleButtonHeight);
            float btnRightMargin = RuntimeClassicUiMetrics.Ui(ToggleButtonRightMargin);
            float btnBottomMargin = RuntimeClassicUiMetrics.Ui(ToggleButtonBottomMargin);

            var buttonRect = RuntimeUiFactory.CreateAnchoredRect(
                "WorldButtonRect",
                _window.Client,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-btnRightMargin, btnBottomMargin),
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
            toggleButton.Button.interactable = true;
            toggleButton.Button.onClick.AddListener(ToggleMode);

            return (local, global, toggleButton, localGrid, globalMap, placeholder);
        }

        void AttachInputSurface(RectTransform parent)
        {
            var image = RuntimeUiFactory.CreateImage("InputSurface", parent, new Color(0f, 0f, 0f, 0f));
            RuntimeUiFactory.Stretch(image.rectTransform);
            image.raycastTarget = true;
            var input = image.gameObject.AddComponent<MapInputSurface>();
            input.Initialize(OnMapDragged, OnMapScrolled);
            image.rectTransform.SetAsFirstSibling();
        }

        void ToggleMode()
        {
            if (_model == null || !_model.GlobalEnabled)
                return;
            var next = _model.Mode == MapWindowMode.Global ? MapWindowMode.Local : MapWindowMode.Global;
            if (!RuntimeShellRequestBridge.TrySetMapWindowMode(next, out string error))
                Debug.LogWarning(error);
        }

        void OnMapDragged(Vector2 delta)
        {
            if (_model == null)
                return;
            if (_model.Mode == MapWindowMode.Global && _model.GlobalMap?.Ready == true)
            {
                float zoom = ClampGlobalZoom(_model.GlobalMap.Zoom, _model.GlobalMap);
                float panX = _model.GlobalMap.PanX + delta.x / zoom;
                float panY = _model.GlobalMap.PanY + delta.y / zoom;
                if (_globalMap.TryClampPan(_model.GlobalMap, panX, panY, zoom, out float clampedPanX, out float clampedPanY))
                    RequestViewport(MapWindowMode.Global, clampedPanX, clampedPanY, zoom);
            }
            else if (_model.LocalMap?.Ready == true)
            {
                float tileSize = Mathf.Max(1f, _localGrid.LastTilePixelSize);
                float zoom = Mathf.Clamp(_model.LocalMap.Zoom, MinLocalMapZoom, MaxMapZoom);
                float panX = _model.LocalMap.PanCellX + delta.x / tileSize;
                float panY = _model.LocalMap.PanCellY + delta.y / tileSize;
                if (_localGrid.TryClampPan(_model.LocalMap, panX, panY, zoom, out float clampedPanX, out float clampedPanY))
                    RequestViewport(MapWindowMode.Local, clampedPanX, clampedPanY, zoom);
            }
        }

        void OnMapScrolled(PointerEventData eventData)
        {
            float scroll = eventData.scrollDelta.y;
            if (_model == null || Mathf.Approximately(scroll, 0f))
                return;
            float factor = scroll > 0f ? 1.08f : 1f / 1.08f;
            if (_model.Mode == MapWindowMode.Global && _model.GlobalMap?.Ready == true)
            {
                float zoom = ClampGlobalZoom(_model.GlobalMap.Zoom * factor, _model.GlobalMap);
                if (_globalMap.TryCalculateCursorAnchoredPan(_model.GlobalMap, eventData.position, eventData.pressEventCamera ?? eventData.enterEventCamera, zoom, out float panX, out float panY))
                    RequestViewport(MapWindowMode.Global, panX, panY, zoom);
                else
                    RequestViewport(MapWindowMode.Global, _model.GlobalMap.PanX, _model.GlobalMap.PanY, zoom);
            }
            else if (_model.LocalMap?.Ready == true)
            {
                float zoom = Mathf.Clamp(_model.LocalMap.Zoom * factor, MinLocalMapZoom, MaxMapZoom);
                if (_localGrid.TryCalculateCursorAnchoredPan(_model.LocalMap, eventData.position, eventData.pressEventCamera ?? eventData.enterEventCamera, zoom, out float panX, out float panY))
                    RequestViewport(MapWindowMode.Local, panX, panY, zoom);
                else
                    RequestViewport(MapWindowMode.Local, _model.LocalMap.PanCellX, _model.LocalMap.PanCellY, zoom);
            }
        }

        static void RequestViewport(MapWindowMode mode, float panX, float panY, float zoom)
        {
            if (!RuntimeShellRequestBridge.TrySetMapViewport(mode, panX, panY, zoom, out string error))
                Debug.LogWarning(error);
        }

        float ClampGlobalZoom(float zoom, GlobalMapViewModel model)
            => Mathf.Clamp(zoom <= 0f ? 1f : zoom, MinGlobalMapZoom, MaxMapZoom);

        RectTransform BuildMapPanel(string name, out RectTransform client)
        {
            var root = RuntimeUiFactory.CreateAnchorRect(
                name,
                _window.Client,
                Vector2.zero,
                Vector2.one,
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
            client = frame.Client;
            return root;
        }

        sealed class MapInputSurface : MonoBehaviour, IDragHandler, IScrollHandler
        {
            Action<Vector2> _onDrag;
            Action<PointerEventData> _onScroll;

            public void Initialize(Action<Vector2> onDrag, Action<PointerEventData> onScroll)
            {
                _onDrag = onDrag;
                _onScroll = onScroll;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                    _onDrag?.Invoke(eventData.delta);
            }

            public void OnScroll(PointerEventData eventData)
            {
                _onScroll?.Invoke(eventData);
            }
        }

        sealed class GlobalMapView
        {
            readonly RectTransform _viewport;
            readonly RectTransform _content;
            readonly RawImage _baseImage;
            readonly RawImage _overlayImage;
            readonly Image _playerMarker;
            readonly System.Collections.Generic.List<Image> _markerPool = new();
            float _lastZoom = 1f;

            public GlobalMapView(string name, RectTransform parent, RuntimeUiTheme theme, Vector2 markerSize)
            {
                _viewport = RuntimeUiFactory.CreateAnchorRect(
                    name,
                    parent,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);
                _viewport.gameObject.AddComponent<RectMask2D>();

                _content = RuntimeUiFactory.CreateAnchoredRect(
                    "Content",
                    _viewport,
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero);
                _content.pivot = Vector2.zero;

                _baseImage = RuntimeUiFactory.CreateRawImage("Base", _content, Color.white);
                _baseImage.raycastTarget = false;
                RuntimeUiFactory.Stretch(_baseImage.rectTransform);

                _overlayImage = RuntimeUiFactory.CreateRawImage("Overlay", _content, Color.white);
                _overlayImage.raycastTarget = false;
                RuntimeUiFactory.Stretch(_overlayImage.rectTransform);

                _playerMarker = RuntimeUiFactory.CreateImage("PlayerMarker", _content, Color.white);
                _playerMarker.sprite = theme?.CompassSprite;
                _playerMarker.type = Image.Type.Simple;
                _playerMarker.preserveAspect = true;
                _playerMarker.raycastTarget = false;
                _playerMarker.rectTransform.anchorMin = Vector2.zero;
                _playerMarker.rectTransform.anchorMax = Vector2.zero;
                _playerMarker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _playerMarker.rectTransform.anchoredPosition = Vector2.zero;
                _playerMarker.rectTransform.sizeDelta = markerSize;
                _playerMarker.rectTransform.SetAsLastSibling();
            }

            public float LastZoom => _lastZoom;

            public bool TryCalculateCursorAnchoredPan(
                GlobalMapViewModel model,
                Vector2 screenPosition,
                Camera eventCamera,
                float targetZoom,
                out float panX,
                out float panY)
            {
                panX = model?.PanX ?? 0f;
                panY = model?.PanY ?? 0f;
                if (model?.Ready != true
                    || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screenPosition, eventCamera, out Vector2 localPoint))
                {
                    return false;
                }

                Vector2 cursor = localPoint + _viewport.rect.size * 0.5f;
                float oldZoom = Mathf.Clamp(model.Zoom <= 0f ? 1f : model.Zoom, MinGlobalMapZoom, MaxMapZoom);
                float newZoom = Mathf.Clamp(targetZoom <= 0f ? oldZoom : targetZoom, MinGlobalMapZoom, MaxMapZoom);
                float oldPlayerX = model.PlayerX * oldZoom;
                float oldPlayerY = model.PlayerY * oldZoom;
                Vector2 oldContent = new(
                    _viewport.rect.width * 0.5f - oldPlayerX + model.PanX * oldZoom,
                    _viewport.rect.height * 0.5f - oldPlayerY + model.PanY * oldZoom);
                oldContent = ClampContentPosition(oldContent, new Vector2(model.Width * oldZoom, model.Height * oldZoom), _viewport.rect.size);
                Vector2 imagePoint = (cursor - oldContent) / oldZoom;
                Vector2 newContent = cursor - imagePoint * newZoom;
                newContent = ClampContentPosition(newContent, new Vector2(model.Width * newZoom, model.Height * newZoom), _viewport.rect.size);
                panX = (newContent.x - _viewport.rect.width * 0.5f + model.PlayerX * newZoom) / newZoom;
                panY = (newContent.y - _viewport.rect.height * 0.5f + model.PlayerY * newZoom) / newZoom;
                return true;
            }

            public bool TryClampPan(GlobalMapViewModel model, float panX, float panY, float zoom, out float clampedPanX, out float clampedPanY)
            {
                clampedPanX = panX;
                clampedPanY = panY;
                if (model?.Ready != true)
                    return false;

                float clampedZoom = Mathf.Clamp(zoom <= 0f ? 1f : zoom, MinGlobalMapZoom, MaxMapZoom);
                Vector2 viewportSize = _viewport.rect.size;
                Vector2 playerPosition = new(model.PlayerX * clampedZoom, model.PlayerY * clampedZoom);
                Vector2 contentSize = new(Mathf.Max(1f, model.Width) * clampedZoom, Mathf.Max(1f, model.Height) * clampedZoom);
                Vector2 contentPosition = ClampContentPosition(
                    new Vector2(
                        viewportSize.x * 0.5f - playerPosition.x + panX * clampedZoom,
                        viewportSize.y * 0.5f - playerPosition.y + panY * clampedZoom),
                    contentSize,
                    viewportSize);
                clampedPanX = (contentPosition.x - viewportSize.x * 0.5f + playerPosition.x) / clampedZoom;
                clampedPanY = (contentPosition.y - viewportSize.y * 0.5f + playerPosition.y) / clampedZoom;
                return true;
            }

            public void Sync(GlobalMapViewModel model)
            {
                bool ready = model?.Ready == true && model.BaseTexture != null;
                _content.gameObject.SetActive(ready);
                _playerMarker.gameObject.SetActive(ready);
                if (!ready)
                    return;

                float zoom = Mathf.Clamp(model.Zoom <= 0f ? 1f : model.Zoom, MinGlobalMapZoom, MaxMapZoom);
                _lastZoom = zoom;
                float width = Mathf.Max(1f, model.Width) * zoom;
                float height = Mathf.Max(1f, model.Height) * zoom;
                _content.sizeDelta = new Vector2(width, height);
                _baseImage.texture = model.BaseTexture;
                _overlayImage.texture = model.OverlayTexture;

                float playerX = model.PlayerX * zoom;
                float playerY = model.PlayerY * zoom;
                _content.anchoredPosition = ClampContentPosition(
                    new Vector2(
                        _viewport.rect.width * 0.5f - playerX + model.PanX * zoom,
                        _viewport.rect.height * 0.5f - playerY + model.PanY * zoom),
                    new Vector2(width, height),
                    _viewport.rect.size);
                _playerMarker.rectTransform.anchoredPosition = new Vector2(playerX, playerY);
                _playerMarker.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f - model.PlayerHeadingDegrees);
                SyncMarkers(model, zoom);
            }

            void SyncMarkers(GlobalMapViewModel model, float zoom)
            {
                var markers = model.Markers;
                int count = markers?.Length ?? 0;
                EnsureMarkerPool(count);

                for (int i = 0; i < _markerPool.Count; i++)
                {
                    var marker = _markerPool[i];
                    bool active = i < count;
                    marker.gameObject.SetActive(active);
                    if (!active)
                    {
                        RuntimeUiPopupUtility.SetTooltip(marker.gameObject, null);
                        continue;
                    }

                    var entry = markers[i];
                    float markerSize = 12f * zoom;
                    if (zoom < 1f)
                        markerSize *= Mathf.Sqrt(Mathf.Max(0, entry.AggregateWeight));
                    else if (entry.AggregateWeight > 0)
                        markerSize = 0f;

                    active = markerSize >= 6f;
                    marker.gameObject.SetActive(active);
                    if (!active)
                    {
                        RuntimeUiPopupUtility.SetTooltip(marker.gameObject, null);
                        continue;
                    }

                    marker.rectTransform.anchoredPosition = new Vector2(entry.X * zoom, entry.Y * zoom);
                    marker.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(markerSize, markerSize));
                    RuntimeUiPopupUtility.SetTooltip(marker.gameObject, entry.Label);
                    marker.rectTransform.SetAsLastSibling();
                }

                _playerMarker.rectTransform.SetAsLastSibling();
            }

            void EnsureMarkerPool(int count)
            {
                while (_markerPool.Count < count)
                {
                    var marker = RuntimeUiFactory.CreateImage($"PlaceMarker_{_markerPool.Count}", _content, MapMarkerColor);
                    marker.raycastTarget = true;
                    RuntimeUiPopupUtility.SetTooltip(marker.gameObject, null);
                    marker.rectTransform.anchorMin = Vector2.zero;
                    marker.rectTransform.anchorMax = Vector2.zero;
                    marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    marker.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(12f, 12f));
                    _markerPool.Add(marker);
                }
            }

            static Vector2 ClampContentPosition(Vector2 position, Vector2 contentSize, Vector2 viewportSize)
            {
                return new Vector2(
                    ClampAxis(position.x, contentSize.x, viewportSize.x),
                    ClampAxis(position.y, contentSize.y, viewportSize.y));
            }

            static float ClampAxis(float position, float contentSize, float viewportSize)
            {
                if (contentSize <= viewportSize)
                    return (viewportSize - contentSize) * 0.5f;
                return Mathf.Clamp(position, viewportSize - contentSize, 0f);
            }
        }
    }
}
