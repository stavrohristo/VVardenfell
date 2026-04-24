using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class LocalMapTileGridView
    {
        const int GridSide = 3;
        const int TileCount = GridSide * GridSide;
        const float DefaultTilePixelSize = 256f;
        static readonly Color DoorMarkerColor = new(0.86f, 0.68f, 0.28f, 0.95f);
        static readonly Vector2 MarkerSize = new(8f, 8f);

        readonly RectTransform _viewport;
        readonly RectTransform _content;
        readonly RawImage[] _mapTiles = new RawImage[TileCount];
        readonly RawImage[] _shroudTiles = new RawImage[TileCount];
        readonly System.Collections.Generic.List<Image> _markerPool = new();
        readonly Image _playerMarker;
        readonly Material _shroudMaterial;
        readonly Color _emptyTileColor;
        readonly float _baseTilePixelSize;
        float _lastTilePixelSize = 1f;

        public LocalMapTileGridView(
            string name,
            RectTransform parent,
            RuntimeUiTheme theme,
            Vector2 markerSize,
            Color emptyTileColor,
            float baseTilePixelSize = 0f)
        {
            _emptyTileColor = emptyTileColor;
            _baseTilePixelSize = Mathf.Max(1f, baseTilePixelSize > 0f ? baseTilePixelSize : RuntimeClassicUiMetrics.Ui(DefaultTilePixelSize));
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

            var shader = Shader.Find("VVardenfell/UI/MapShroud");
            if (shader != null)
                _shroudMaterial = new Material(shader) { name = "VV:MapShroud(UI)" };

            int index = 0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    var tileRoot = RuntimeUiFactory.CreateAnchoredRect(
                        $"Tile_{x}_{y}",
                        _content,
                        Vector2.zero,
                        Vector2.zero,
                        Vector2.zero,
                        Vector2.zero);
                    tileRoot.pivot = Vector2.zero;

                    var map = RuntimeUiFactory.CreateRawImage("Map", tileRoot, _emptyTileColor);
                    map.raycastTarget = false;
                    RuntimeUiFactory.Stretch(map.rectTransform);

                    var shroud = RuntimeUiFactory.CreateRawImage("Shroud", tileRoot, Color.black);
                    shroud.material = _shroudMaterial;
                    shroud.raycastTarget = false;
                    RuntimeUiFactory.Stretch(shroud.rectTransform);

                    _mapTiles[index] = map;
                    _shroudTiles[index] = shroud;
                    index++;
                }
            }

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

        public void Sync(LocalMapViewModel model)
        {
            bool ready = model?.Ready == true;
            _content.gameObject.SetActive(ready);
            _playerMarker.gameObject.SetActive(ready);
            if (!ready)
                return;

            CalculateLayout(model, Mathf.Clamp(model.Zoom <= 0f ? 1f : model.Zoom, 1f / 3f, 4f), out Vector2 viewportSize, out Vector2 tileSize, out Vector2 playerPosition, out Vector2 contentSize);
            float tilePixelWidth = tileSize.x;
            float tilePixelHeight = tileSize.y;
            _lastTilePixelSize = Mathf.Min(tilePixelWidth, tilePixelHeight);
            _content.sizeDelta = contentSize;
            _content.anchoredPosition = ClampContentPosition(
                new Vector2(
                    viewportSize.x * 0.5f - playerPosition.x + model.PanCellX * tilePixelWidth,
                    viewportSize.y * 0.5f - playerPosition.y + model.PanCellY * tilePixelHeight),
                contentSize,
                viewportSize);
            _playerMarker.rectTransform.anchoredPosition = playerPosition;
            _playerMarker.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f - model.PlayerHeadingDegrees);

            for (int i = 0; i < TileCount; i++)
            {
                _mapTiles[i].texture = null;
                _mapTiles[i].color = _emptyTileColor;
                _mapTiles[i].uvRect = new Rect(0f, 0f, 1f, 1f);
                _shroudTiles[i].texture = null;
                _shroudTiles[i].gameObject.SetActive(false);
            }

            var tiles = model.Tiles;
            if (tiles == null)
                return;

            for (int i = 0; i < tiles.Length; i++)
            {
                var entry = tiles[i];
                int index = ToIndex(entry.OffsetX, entry.OffsetY);
                if ((uint)index >= TileCount)
                    continue;

                RectTransform tileRect = (RectTransform)_mapTiles[index].transform.parent;
                tileRect.anchoredPosition = new Vector2((entry.OffsetX + 1) * tilePixelWidth, (entry.OffsetY + 1) * tilePixelHeight);
                tileRect.sizeDelta = new Vector2(tilePixelWidth, tilePixelHeight);

                bool hasMap = entry.HasMapTexture && entry.MapTexture != null;
                _mapTiles[index].texture = hasMap ? entry.MapTexture : null;
                _mapTiles[index].uvRect = hasMap ? entry.MapUvRect : new Rect(0f, 0f, 1f, 1f);
                _mapTiles[index].color = hasMap ? Color.white : _emptyTileColor;
                _shroudTiles[index].texture = hasMap && model.ShowShroud ? entry.ShroudTexture : null;
                _shroudTiles[index].gameObject.SetActive(hasMap && model.ShowShroud && entry.ShroudTexture != null);
            }

            SyncMarkers(model, tilePixelWidth, tilePixelHeight);
            _playerMarker.rectTransform.SetAsLastSibling();
        }

        public float LastTilePixelSize => _lastTilePixelSize;

        public bool TryCalculateCursorAnchoredPan(
            LocalMapViewModel model,
            Vector2 screenPosition,
            Camera eventCamera,
            float targetZoom,
            out float panCellX,
            out float panCellY)
        {
            panCellX = model?.PanCellX ?? 0f;
            panCellY = model?.PanCellY ?? 0f;
            if (model?.Ready != true
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screenPosition, eventCamera, out Vector2 localPoint))
            {
                return false;
            }

            Vector2 cursor = localPoint + _viewport.rect.size * 0.5f;
            Vector2 viewportSize = new Vector2(Mathf.Max(1f, _viewport.rect.width), Mathf.Max(1f, _viewport.rect.height));
            float oldZoom = Mathf.Clamp(model.Zoom <= 0f ? 1f : model.Zoom, 1f / 3f, 4f);
            float newZoom = Mathf.Clamp(targetZoom <= 0f ? oldZoom : targetZoom, 1f / 3f, 4f);
            Vector2 oldTileSize = CalculateTileSize(oldZoom);
            Vector2 newTileSize = CalculateTileSize(newZoom);
            float oldTileWidth = oldTileSize.x;
            float oldTileHeight = oldTileSize.y;
            float oldPlayerX = (1f + Mathf.Clamp01(model.PlayerCellX)) * oldTileWidth;
            float oldPlayerY = (1f + Mathf.Clamp01(model.PlayerCellY)) * oldTileHeight;
            Vector2 oldContent = new(
                viewportSize.x * 0.5f - oldPlayerX + model.PanCellX * oldTileWidth,
                viewportSize.y * 0.5f - oldPlayerY + model.PanCellY * oldTileHeight);
            oldContent = ClampContentPosition(oldContent, new Vector2(oldTileWidth * GridSide, oldTileHeight * GridSide), viewportSize);
            float contentCellX = (cursor.x - oldContent.x) / oldTileWidth;
            float contentCellY = (cursor.y - oldContent.y) / oldTileHeight;

            float newTileWidth = newTileSize.x;
            float newTileHeight = newTileSize.y;
            float newPlayerX = (1f + Mathf.Clamp01(model.PlayerCellX)) * newTileWidth;
            float newPlayerY = (1f + Mathf.Clamp01(model.PlayerCellY)) * newTileHeight;
            Vector2 newContent = new(cursor.x - contentCellX * newTileWidth, cursor.y - contentCellY * newTileHeight);
            newContent = ClampContentPosition(newContent, new Vector2(newTileWidth * GridSide, newTileHeight * GridSide), viewportSize);
            panCellX = (newContent.x - viewportSize.x * 0.5f + newPlayerX) / newTileWidth;
            panCellY = (newContent.y - viewportSize.y * 0.5f + newPlayerY) / newTileHeight;
            return true;
        }

        public bool TryClampPan(LocalMapViewModel model, float panCellX, float panCellY, float zoom, out float clampedPanCellX, out float clampedPanCellY)
        {
            clampedPanCellX = panCellX;
            clampedPanCellY = panCellY;
            if (model?.Ready != true)
                return false;

            CalculateLayout(model, zoom, out Vector2 viewportSize, out Vector2 tileSize, out Vector2 playerPosition, out Vector2 contentSize);
            Vector2 clampedContent = ClampContentPosition(
                new Vector2(
                    viewportSize.x * 0.5f - playerPosition.x + panCellX * tileSize.x,
                    viewportSize.y * 0.5f - playerPosition.y + panCellY * tileSize.y),
                contentSize,
                viewportSize);
            clampedPanCellX = (clampedContent.x - viewportSize.x * 0.5f + playerPosition.x) / tileSize.x;
            clampedPanCellY = (clampedContent.y - viewportSize.y * 0.5f + playerPosition.y) / tileSize.y;
            return true;
        }

        void CalculateLayout(LocalMapViewModel model, float zoom, out Vector2 viewportSize, out Vector2 tileSize, out Vector2 playerPosition, out Vector2 contentSize)
        {
            viewportSize = new Vector2(Mathf.Max(1f, _viewport.rect.width), Mathf.Max(1f, _viewport.rect.height));
            tileSize = CalculateTileSize(zoom);
            playerPosition = new Vector2(
                (1f + Mathf.Clamp01(model.PlayerCellX)) * tileSize.x,
                (1f + Mathf.Clamp01(model.PlayerCellY)) * tileSize.y);
            contentSize = tileSize * GridSide;
        }

        Vector2 CalculateTileSize(float zoom)
        {
            float clampedZoom = Mathf.Clamp(zoom <= 0f ? 1f : zoom, 1f / 3f, 4f);
            float size = _baseTilePixelSize * clampedZoom;
            return new Vector2(size, size);
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

        void SyncMarkers(LocalMapViewModel model, float tilePixelWidth, float tilePixelHeight)
        {
            var markers = model.ShowMarkers ? model.Markers : null;
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
                int tileIndex = ToIndex(entry.OffsetX, entry.OffsetY);
                if ((uint)tileIndex >= TileCount)
                {
                    RuntimeUiPopupUtility.SetTooltip(marker.gameObject, null);
                    marker.gameObject.SetActive(false);
                    continue;
                }

                RectTransform tileRect = (RectTransform)_mapTiles[tileIndex].transform.parent;
                if (marker.rectTransform.parent != tileRect)
                    marker.rectTransform.SetParent(tileRect, worldPositionStays: false);

                marker.rectTransform.SetSiblingIndex(1);
                marker.rectTransform.anchoredPosition = new Vector2(
                    Mathf.Clamp01(entry.CellX) * tilePixelWidth,
                    Mathf.Clamp01(entry.CellY) * tilePixelHeight);
                RuntimeUiPopupUtility.SetTooltip(marker.gameObject, entry.Label);
            }
        }

        void EnsureMarkerPool(int count)
        {
            while (_markerPool.Count < count)
            {
                var marker = RuntimeUiFactory.CreateImage($"DoorMarker_{_markerPool.Count}", _content, DoorMarkerColor);
                marker.raycastTarget = true;
                RuntimeUiPopupUtility.SetTooltip(marker.gameObject, null);
                marker.rectTransform.anchorMin = Vector2.zero;
                marker.rectTransform.anchorMax = Vector2.zero;
                marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                marker.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(MarkerSize);
                _markerPool.Add(marker);
            }
        }

        static int ToIndex(int offsetX, int offsetY)
            => (offsetY + 1) * GridSide + (offsetX + 1);
    }
}
