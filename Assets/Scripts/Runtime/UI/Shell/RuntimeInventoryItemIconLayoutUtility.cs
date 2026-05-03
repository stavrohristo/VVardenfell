using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    static class RuntimeInventoryItemIconLayoutUtility
    {
        static readonly Vector2 ItemSize = new(32f, 32f);
        static readonly Vector2 ItemPosition = new(5f, 5f);
        static readonly Vector2 ShadowPosition = new(9f, 9f);

        public static Image CreateItemImage(string name, Transform parent, Color color, bool shadow, bool flipVertical)
        {
            var image = RuntimeUiFactory.CreateImage(name, parent, color);
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.rectTransform.anchorMin = new Vector2(0f, 1f);
            image.rectTransform.anchorMax = new Vector2(0f, 1f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Vector2 position = shadow ? ShadowPosition : ItemPosition;
            image.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(ToAnchoredCenter(position, ItemSize));
            image.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(ItemSize);
            if (flipVertical)
                image.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            return image;
        }

        public static void SyncSprite(Image icon, Image shadow, Sprite sprite)
        {
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.preserveAspect = true;
            }

            if (shadow != null)
            {
                shadow.sprite = sprite;
                shadow.enabled = sprite != null;
                shadow.preserveAspect = true;
            }
        }

        public static void BringBorderToFront(BorderFrameView frame)
        {
            if (frame == null)
                return;

            frame.Top?.rectTransform.SetAsLastSibling();
            frame.Bottom?.rectTransform.SetAsLastSibling();
            frame.Left?.rectTransform.SetAsLastSibling();
            frame.Right?.rectTransform.SetAsLastSibling();
            frame.TopLeft?.rectTransform.SetAsLastSibling();
            frame.TopRight?.rectTransform.SetAsLastSibling();
            frame.BottomLeft?.rectTransform.SetAsLastSibling();
            frame.BottomRight?.rectTransform.SetAsLastSibling();
        }

        public static void ApplyCountRect(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = RuntimeClassicUiMetrics.Ui(ToAnchored(new Vector2(5f, 19f)));
            rect.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(32f, 18f));
        }

        static Vector2 ToAnchored(Vector2 topLeft)
            => new(topLeft.x, -topLeft.y);

        static Vector2 ToAnchoredCenter(Vector2 topLeft, Vector2 size)
            => new(topLeft.x + size.x * 0.5f, -(topLeft.y + size.y * 0.5f));
    }
}
