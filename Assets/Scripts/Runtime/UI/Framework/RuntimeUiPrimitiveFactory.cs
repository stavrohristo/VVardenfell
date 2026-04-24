using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    public static partial class RuntimeUiFactory
    {
        public static RectTransform CreateStretchRect(string name, Transform parent)
        {
            var rect = CreateRect(name, parent);
            Stretch(rect);
            return rect;
        }

        public static RectTransform CreateAnchoredRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        public static RectTransform CreateAnchorRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            // Intentionally do NOT set anchoredPosition here. offsetMin/offsetMax already
            // define the rect's geometry fully. Unity's anchoredPosition setter translates
            // offsetMin and offsetMax by the delta between current and target, so forcing
            // anchoredPosition = (0, 0) after asymmetric offsets (e.g. a two-pane split
            // with offsetMin.x = LeftPaneWidth) will shift the rect off to one side. The
            // previous version of this helper had exactly that bug and caused every pane
            // built with asymmetric insets to land in the wrong place.
            rect.localScale = Vector3.one;
            return rect;
        }

        public static RawImage CreateRawImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.color = color;
            return image;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
