using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.UI
{
    public sealed class BorderFrameView
    {
        public RectTransform Root;
        public RectTransform Client;
        public Image Center;
        public Image Top;
        public Image Bottom;
        public Image Left;
        public Image Right;
        public Image TopLeft;
        public Image TopRight;
        public Image BottomLeft;
        public Image BottomRight;
    }

    public sealed class MorrowindWindowView
    {
        public RectTransform Root;
        public BorderFrameView Frame;
        public RectTransform CaptionRoot;
        public BitmapTextGraphic Title;
        public Image DragSurface;
        public RectTransform Client;
    }

    public sealed class MorrowindButtonView
    {
        public RectTransform Root;
        public BorderFrameView Frame;
        public Button Button;
        public BitmapTextGraphic Label;
    }

    public readonly struct BorderTextureSet
    {
        public BorderTextureSet(Sprite top, Sprite bottom, Sprite left, Sprite right, Sprite topLeft, Sprite topRight, Sprite bottomLeft, Sprite bottomRight)
        {
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
            TopLeft = topLeft;
            TopRight = topRight;
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
        }

        public Sprite Top { get; }
        public Sprite Bottom { get; }
        public Sprite Left { get; }
        public Sprite Right { get; }
        public Sprite TopLeft { get; }
        public Sprite TopRight { get; }
        public Sprite BottomLeft { get; }
        public Sprite BottomRight { get; }
    }

    public static class RuntimeUiFactory
    {
        public static EventSystem EnsureEventSystem()
        {
            var eventSystem = Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
                return eventSystem;

            var go = new GameObject("VVardenfell.EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(go);
            return go.GetComponent<EventSystem>();
        }

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
            rect.anchoredPosition = Vector2.zero;
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

        public static BitmapTextGraphic CreateBitmapText(
            string name,
            Transform parent,
            BitmapFontAsset font,
            float scale,
            Color color,
            BitmapTextAlignment alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(BitmapTextGraphic));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<BitmapTextGraphic>();
            text.Font = font;
            text.FontScale = scale;
            text.color = color;
            text.Alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        public static BorderFrameView CreateBorderFrame(string name, Transform parent, BorderTextureSet set, Color centerColor)
        {
            var root = CreateStretchRect(name, parent);
            var result = new BorderFrameView
            {
                Root = root,
            };

            float left = set.Left?.rect.width ?? 0f;
            float right = set.Right?.rect.width ?? 0f;
            float top = set.Top?.rect.height ?? 0f;
            float bottom = set.Bottom?.rect.height ?? 0f;

            result.Center = CreateImage("Center", root, centerColor);
            result.Center.raycastTarget = false;
            result.Center.rectTransform.anchorMin = Vector2.zero;
            result.Center.rectTransform.anchorMax = Vector2.one;
            result.Center.rectTransform.offsetMin = new Vector2(left, bottom);
            result.Center.rectTransform.offsetMax = new Vector2(-right, -top);
            result.Client = result.Center.rectTransform;

            result.Top = CreateEdgeImage("Top", root, set.Top, Image.Type.Simple);
            result.Top.rectTransform.anchorMin = new Vector2(0f, 1f);
            result.Top.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.Top.rectTransform.pivot = new Vector2(0.5f, 1f);
            result.Top.rectTransform.anchoredPosition = Vector2.zero;
            result.Top.rectTransform.sizeDelta = new Vector2(-(left + right), top);

            result.Bottom = CreateEdgeImage("Bottom", root, set.Bottom, Image.Type.Simple);
            result.Bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.Bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            result.Bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            result.Bottom.rectTransform.anchoredPosition = Vector2.zero;
            result.Bottom.rectTransform.sizeDelta = new Vector2(-(left + right), bottom);

            result.Left = CreateEdgeImage("Left", root, set.Left, Image.Type.Simple);
            result.Left.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.Left.rectTransform.anchorMax = new Vector2(0f, 1f);
            result.Left.rectTransform.pivot = new Vector2(0f, 0.5f);
            result.Left.rectTransform.anchoredPosition = Vector2.zero;
            result.Left.rectTransform.sizeDelta = new Vector2(left, -(top + bottom));

            result.Right = CreateEdgeImage("Right", root, set.Right, Image.Type.Simple);
            result.Right.rectTransform.anchorMin = new Vector2(1f, 0f);
            result.Right.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.Right.rectTransform.pivot = new Vector2(1f, 0.5f);
            result.Right.rectTransform.anchoredPosition = Vector2.zero;
            result.Right.rectTransform.sizeDelta = new Vector2(right, -(top + bottom));

            result.TopLeft = CreateCornerImage("TopLeft", root, set.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f));
            result.TopRight = CreateCornerImage("TopRight", root, set.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f));
            result.BottomLeft = CreateCornerImage("BottomLeft", root, set.BottomLeft, new Vector2(0f, 0f), new Vector2(0f, 0f));
            result.BottomRight = CreateCornerImage("BottomRight", root, set.BottomRight, new Vector2(1f, 0f), new Vector2(1f, 0f));
            return result;
        }

        public static MorrowindWindowView CreateMorrowindWindow(
            string name,
            Transform parent,
            UiRuntimeAssets assets,
            BitmapFontAsset titleFont,
            string title,
            float captionHeight,
            float clientInset,
            float captionInset,
            float backgroundOpacity,
            float titleScale,
            Color titleColor)
        {
            var root = CreateRect(name, parent);
            var frame = CreateBorderFrame(
                "WindowFrame",
                root,
                ResolveThickFrame(assets),
                new Color(0f, 0f, 0f, backgroundOpacity));
            Stretch(frame.Root);

            var captionRoot = CreateAnchoredRect(
                "CaptionRoot",
                frame.Client,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -captionInset),
                new Vector2(-captionInset * 2f, captionHeight));
            captionRoot.pivot = new Vector2(0.5f, 1f);

            var captionFrame = CreateBorderFrame(
                "CaptionFrame",
                captionRoot,
                ResolveThinFrame(assets),
                new Color(0f, 0f, 0f, Mathf.Clamp01(backgroundOpacity + 0.04f)));
            Stretch(captionFrame.Root);

            var dragSurface = CreateImage("DragSurface", captionRoot, new Color(1f, 1f, 1f, 0f));
            dragSurface.raycastTarget = true;
            Stretch(dragSurface.rectTransform);

            var titleText = CreateBitmapText(
                "CaptionText",
                captionRoot,
                titleFont,
                titleScale,
                titleColor,
                BitmapTextAlignment.Center);
            titleText.Text = title ?? string.Empty;
            Stretch(titleText.rectTransform);

            var client = CreateAnchorRect(
                "ClientRoot",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(clientInset, clientInset),
                new Vector2(-clientInset, -(captionHeight + clientInset + captionInset)));

            return new MorrowindWindowView
            {
                Root = root,
                Frame = frame,
                CaptionRoot = captionRoot,
                Title = titleText,
                DragSurface = dragSurface,
                Client = client,
            };
        }

        public static MorrowindButtonView CreateMorrowindButton(
            string name,
            Transform parent,
            UiRuntimeAssets assets,
            BitmapFontAsset font,
            string label,
            float scale,
            Color textColor,
            Color centerColor)
        {
            var root = CreateRect(name, parent);
            var frame = CreateBorderFrame("Frame", root, ResolveThinFrame(assets), centerColor);
            Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var labelText = CreateBitmapText(
                "Label",
                root,
                font,
                scale,
                textColor,
                BitmapTextAlignment.Center);
            labelText.Text = label ?? string.Empty;
            Stretch(labelText.rectTransform);

            return new MorrowindButtonView
            {
                Root = root,
                Frame = frame,
                Button = button,
                Label = labelText,
            };
        }

        public static BorderTextureSet ResolveThinFrame(UiRuntimeAssets assets)
        {
            return new BorderTextureSet(
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTop)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottom)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderRight)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTopLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTopRight)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottomLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottomRight)?.Sprite);
        }

        public static BorderTextureSet ResolveThickFrame(UiRuntimeAssets assets)
        {
            return new BorderTextureSet(
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTop)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottom)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderRight)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTopLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTopRight)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottomLeft)?.Sprite,
                assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottomRight)?.Sprite);
        }

        public static float MeasureLineWidth(BitmapFontAsset font, string text, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0f;

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (font.TryGetGlyph(text[i], out var glyph))
                    width += glyph.Advance * scale;
            }

            return width;
        }

        public static string WrapText(BitmapFontAsset font, string text, float scale, float maxWidth)
        {
            if (font == null || string.IsNullOrWhiteSpace(text) || maxWidth <= 0f)
                return text ?? string.Empty;

            var paragraphs = text.Replace("\r", "").Split('\n');
            var wrapped = new List<string>(paragraphs.Length * 2);
            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
            {
                string paragraph = paragraphs[paragraphIndex];
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    wrapped.Add(string.Empty);
                    continue;
                }

                string[] words = paragraph.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                string currentLine = words.Length > 0 ? words[0] : string.Empty;
                for (int wordIndex = 1; wordIndex < words.Length; wordIndex++)
                {
                    string candidate = $"{currentLine} {words[wordIndex]}";
                    if (MeasureLineWidth(font, candidate, scale) <= maxWidth)
                    {
                        currentLine = candidate;
                    }
                    else
                    {
                        wrapped.Add(currentLine);
                        currentLine = words[wordIndex];
                    }
                }

                if (!string.IsNullOrEmpty(currentLine))
                    wrapped.Add(currentLine);
            }

            return string.Join("\n", wrapped);
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void SetInset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image CreateEdgeImage(string name, Transform parent, Sprite sprite, Image.Type type)
        {
            var image = CreateImage(name, parent, Color.white);
            image.sprite = sprite;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        static Image CreateCornerImage(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 pivot)
        {
            var image = CreateImage(name, parent, Color.white);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = anchor;
            image.rectTransform.anchorMax = anchor;
            image.rectTransform.pivot = pivot;
            image.rectTransform.anchoredPosition = Vector2.zero;
            image.rectTransform.sizeDelta = sprite != null ? sprite.rect.size : Vector2.zero;
            return image;
        }
    }
}
