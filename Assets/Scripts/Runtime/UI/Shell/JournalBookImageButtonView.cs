using System;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class JournalBookImageButtonView
    {
        public RectTransform Root;
        public Image Image;
        public Button Button;

        public static JournalBookImageButtonView Create(
            string name,
            Transform parent,
            RuntimeUiTheme theme,
            string normalKey,
            string highlightedKey,
            string pressedKey,
            Action onClick)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            root.pivot = new Vector2(0f, 1f);

            var image = RuntimeUiFactory.CreateImage("Image", root, Color.white);
            image.sprite = RequireSprite(theme, normalKey);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = true;
            RuntimeUiFactory.Stretch(image.rectTransform);
            image.rectTransform.localScale = new Vector3(1f, -1f, 1f);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = RequireSprite(theme, highlightedKey),
                pressedSprite = RequireSprite(theme, pressedKey),
                selectedSprite = RequireSprite(theme, highlightedKey),
            };
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            return new JournalBookImageButtonView
            {
                Root = root,
                Image = image,
                Button = button,
            };
        }

        public void SetGeometry(float x, float y, float width, float height)
        {
            Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
        }

        public void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible)
                Root.gameObject.SetActive(visible);
        }

        static Sprite RequireSprite(RuntimeUiTheme theme, string key)
        {
            Sprite sprite = theme?.GetBootstrapSprite(key);
            if (sprite == null)
                throw new InvalidOperationException($"Required journal book UI texture '{key}' is missing.");
            return sprite;
        }
    }
}
