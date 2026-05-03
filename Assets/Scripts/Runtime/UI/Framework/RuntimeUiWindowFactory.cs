using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    public static partial class RuntimeUiFactory
    {
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

        public static RuntimeUiProgressBarView CreateProgressBar(
            string name,
            Transform parent,
            RuntimeUiTheme theme,
            Color centerColor,
            Color fillColor)
        {
            var root = CreateStretchRect(name, parent);
            var frame = CreateBorderFrame("Frame", root, ResolveThinFrame(theme), centerColor);
            Stretch(frame.Root);

            var fillRect = CreateAnchoredRect(
                "FillRect",
                root,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.SetAsFirstSibling();

            var fill = CreateImage("Fill", fillRect, fillColor);
            fill.sprite = theme?.LoadingBarFillSprite;
            fill.raycastTarget = false;
            Stretch(fill.rectTransform);

            return new RuntimeUiProgressBarView
            {
                Root = root,
                Frame = frame,
                FillRect = fillRect,
                Fill = fill,
            };
        }

        public static void SetProgressBarFill(RuntimeUiProgressBarView view, float fraction)
        {
            if (view?.FillRect == null)
                return;

            float clamped = Mathf.Clamp01(fraction);
            RectTransform frameRect = view.Frame?.Root;
            float totalWidth = frameRect != null ? frameRect.rect.width : view.Root.rect.width;
            float fillWidth = Mathf.Max(0f, totalWidth * clamped);
            view.FillRect.sizeDelta = new Vector2(fillWidth, 0f);
        }

        public static MorrowindWindowView CreateMorrowindWindow(
            string name,
            Transform parent,
            RuntimeUiTheme theme,
            string title,
            float captionHeight,
            float clientInset,
            float backgroundOpacity,
            float titlePixelHeight,
            Color titleColor,
            bool withPinButton = false)
        {
            var root = CreateRect(name, parent);

            // Full-window background. BlackBG in the MyGUI skin: covers even the area
            // under the borders; the sprites overlay it.
            var background = CreateImage("Background", root, new Color(0f, 0f, 0f, backgroundOpacity));
            background.raycastTarget = true;
            Stretch(background.rectTransform);

            // Outer border: 4 edges + 4 corners at the window perimeter. CreateBorderFrame's
            // Center rect becomes the working area inside the outer edges; we use it as the
            // parent of the caption + inner frame below, and pass Color.clear so the Center
            // image doesn't double-paint the background.
            var outerFrame = CreateBorderFrame(
                "OuterBorder",
                root,
                ResolveThickFrame(theme),
                Color.clear);
            Stretch(outerFrame.Root);

            // Caption band: anchored to the top of the working area, spans the full width
            // between outer borders, captionHeight tall.
            var captionRoot = CreateAnchoredRect(
                "CaptionRoot",
                outerFrame.Client,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, captionHeight));
            captionRoot.pivot = new Vector2(0.5f, 1f);

            // Caption is assembled as three zones:
            //   [ LeftFiligree - flex ][ TitleBackdrop - sized to title text ][ RightFiligree - flex ]
            // Both filigree panels carry their own HB_ALL nine-patch decoration, giving
            // proper end-caps on each side of the title. The backdrop auto-sizes to the
            // title text (via RuntimeCaptionLayout) so the filigree panels slide outward
            // as the title grows/shrinks, matching vanilla's flex caption shape.
            var headBlockSet = ResolveHeadBlock(theme);

            var leftFiligree = CreateRect("LeftFiligree", captionRoot);
            AttachHeadBlockFrame(leftFiligree, headBlockSet);

            var rightFiligree = CreateRect("RightFiligree", captionRoot);
            AttachHeadBlockFrame(rightFiligree, headBlockSet);

            // Title backdrop sits on top of the (absent) filigree in the middle zone.
            // Vanilla paints this solid dark so the title text reads against the gold
            // filigree. The layout helper re-sizes it whenever the title changes.
            var titleBackdrop = CreateImage("CaptionTextBackdrop", captionRoot, new Color(0f, 0f, 0f, 0.92f));
            titleBackdrop.raycastTarget = false;

            // Title text overlays the backdrop, centered. PixelHeight is set directly
            // so callers can think in actual pixel sizes instead of scale multipliers.
            var titleText = CreateBitmapText(
                "CaptionText",
                captionRoot,
                theme?.DefaultFont,
                1f,
                titleColor,
                BitmapTextAlignment.Center);
            if (titlePixelHeight > 0f)
                titleText.PixelHeight = titlePixelHeight;
            titleText.Text = title ?? string.Empty;
            titleText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            titleText.rectTransform.anchorMin = new Vector2(0f, 0f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            titleText.rectTransform.offsetMin = Vector2.zero;
            titleText.rectTransform.offsetMax = Vector2.zero;
            titleText.raycastTarget = false;

            // Install the caption layout helper that flexes the backdrop + filigree
            // panels to the current title text width. Padding/minimum width are scaled
            // off captionHeight so the proportions look the same whatever the caller's
            // chosen caption size.
            var captionLayout = captionRoot.gameObject.AddComponent<RuntimeCaptionLayout>();
            captionLayout.Bind(
                leftFiligree,
                rightFiligree,
                titleBackdrop.rectTransform,
                titleText,
                horizontalPadding: captionHeight * 0.6f,
                minBackdropWidth: captionHeight * 2.5f);

            // Drag surface covers the full caption so dragging anywhere on the caption moves
            // the window - filigree and title don't block the raycast because they have
            // raycastTarget=false.
            var dragSurface = CreateImage("DragSurface", captionRoot, new Color(1f, 1f, 1f, 0f));
            dragSurface.raycastTarget = true;
            Stretch(dragSurface.rectTransform);

            // Inner border: wraps the body area, immediately below the caption.
            var innerBorderRoot = CreateAnchorRect(
                "InnerBorderRoot",
                outerFrame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(0f, -captionHeight));
            var innerFrame = CreateBorderFrame(
                "InnerBorder",
                innerBorderRoot,
                ResolveThickFrame(theme),
                Color.clear);
            Stretch(innerFrame.Root);

            // Client rect: inset inside the inner border by clientInset, with RectMask2D
            // clipping so overflowing content never escapes the frame.
            var client = CreateAnchorRect(
                "ClientRoot",
                innerFrame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(clientInset, clientInset),
                new Vector2(-clientInset, -clientInset));
            client.gameObject.AddComponent<RectMask2D>();

            var result = new MorrowindWindowView
            {
                Root = root,
                Frame = outerFrame,
                CaptionRoot = captionRoot,
                Title = titleText,
                DragSurface = dragSurface,
                Client = client,
            };

            if (withPinButton)
                result.PinButton = AttachPinButton(result, theme, captionHeight);

            return result;
        }

        /// <summary>
        /// Install the MW_Window_Pinnable pin button at the top-right of the
        /// window's caption. Matches the OpenMW skin: a 19×19 button inset 4px
        /// from the caption's right edge and vertically centered. The button
        /// rebinds to the drag surface so drag still works around it (the
        /// button itself swallows its own clicks).
        /// </summary>
        public static MorrowindPinButtonView AttachPinButton(
            MorrowindWindowView window,
            RuntimeUiTheme theme,
            float captionHeight)
        {
            if (window?.CaptionRoot == null)
                return null;

            // Button sizing — taken from MW_Window_Pinnable's 19×19 PinUp skin.
            // Slightly inset from the caption edges so the filigree remains
            // readable beside it.
            float buttonSize = Mathf.Max(12f, captionHeight - 6f);
            float edgeInset = 4f;

            var buttonRect = CreateAnchoredRect(
                "PinButton",
                window.CaptionRoot,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-(edgeInset + buttonSize * 0.5f), 0f),
                new Vector2(buttonSize, buttonSize));
            buttonRect.pivot = new Vector2(0.5f, 0.5f);

            var frame = CreateBorderFrame(
                "PinFrame",
                buttonRect,
                ResolveThinFrame(theme),
                new Color(0.10f, 0.08f, 0.06f, 0.88f));
            Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            // Placeholder pin glyph — a small square inside the button that
            // tints to indicate state. Replaced later by baked
            // menu_rightbutton{up,down}.dds sprites through the importer.
            var glyph = CreateImage("PinGlyph", frame.Client, new Color(0.92f, 0.80f, 0.44f, 1f));
            glyph.sprite = theme?.LoadingBarFillSprite;
            glyph.type = Image.Type.Simple;
            glyph.raycastTarget = false;
            float glyphSize = Mathf.Max(4f, buttonSize * 0.4f);
            glyph.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            glyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            glyph.rectTransform.anchoredPosition = Vector2.zero;
            glyph.rectTransform.sizeDelta = new Vector2(glyphSize, glyphSize);

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            return new MorrowindPinButtonView
            {
                Root = buttonRect,
                Button = button,
                Frame = frame,
                PinGlyph = glyph,
            };
        }

        /// <summary>
        /// Builds a nine-patch head-block (HB_ALL) filigree decoration onto <paramref name="root"/>.
        /// Mirrors the HB_ALL resource in <c>openmw_windows.skin.xml</c>: tileable Middle
        /// interior, tileable Top/Bottom/Left/Right edges, and fixed-size corner sprites.
        /// </summary>
        public static void AttachHeadBlockFrame(RectTransform root, HeadBlockTextureSet set)
        {
            if (!set.IsValid)
                return;

            float left = set.Left?.rect.width ?? 0f;
            float right = set.Right?.rect.width ?? 0f;
            float top = set.Top?.rect.height ?? 0f;
            float bottom = set.Bottom?.rect.height ?? 0f;

            var middle = CreateImage("HeadMiddle", root, Color.white);
            middle.sprite = set.Middle;
            middle.type = Image.Type.Tiled;
            middle.raycastTarget = false;
            middle.rectTransform.anchorMin = Vector2.zero;
            middle.rectTransform.anchorMax = Vector2.one;
            middle.rectTransform.offsetMin = new Vector2(left, bottom);
            middle.rectTransform.offsetMax = new Vector2(-right, -top);

            var topImg = CreateEdgeImage("HeadTop", root, set.Top, Image.Type.Tiled);
            topImg.rectTransform.anchorMin = new Vector2(0f, 1f);
            topImg.rectTransform.anchorMax = new Vector2(1f, 1f);
            topImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            topImg.rectTransform.anchoredPosition = Vector2.zero;
            topImg.rectTransform.sizeDelta = new Vector2(-(left + right), top);

            var bottomImg = CreateEdgeImage("HeadBottom", root, set.Bottom, Image.Type.Tiled);
            bottomImg.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottomImg.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottomImg.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottomImg.rectTransform.anchoredPosition = Vector2.zero;
            bottomImg.rectTransform.sizeDelta = new Vector2(-(left + right), bottom);

            var leftImg = CreateEdgeImage("HeadLeft", root, set.Left, Image.Type.Tiled);
            leftImg.rectTransform.anchorMin = new Vector2(0f, 0f);
            leftImg.rectTransform.anchorMax = new Vector2(0f, 1f);
            leftImg.rectTransform.pivot = new Vector2(0f, 0.5f);
            leftImg.rectTransform.anchoredPosition = Vector2.zero;
            leftImg.rectTransform.sizeDelta = new Vector2(left, -(top + bottom));

            var rightImg = CreateEdgeImage("HeadRight", root, set.Right, Image.Type.Tiled);
            rightImg.rectTransform.anchorMin = new Vector2(1f, 0f);
            rightImg.rectTransform.anchorMax = new Vector2(1f, 1f);
            rightImg.rectTransform.pivot = new Vector2(1f, 0.5f);
            rightImg.rectTransform.anchoredPosition = Vector2.zero;
            rightImg.rectTransform.sizeDelta = new Vector2(right, -(top + bottom));

            CreateCornerImage("HeadTopLeft", root, set.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f));
            CreateCornerImage("HeadTopRight", root, set.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f));
            CreateCornerImage("HeadBottomLeft", root, set.BottomLeft, new Vector2(0f, 0f), new Vector2(0f, 0f));
            CreateCornerImage("HeadBottomRight", root, set.BottomRight, new Vector2(1f, 0f), new Vector2(1f, 0f));
        }

        public static HeadBlockTextureSet ResolveHeadBlock(RuntimeUiTheme theme)
        {
            return theme?.HeadBlock ?? default;
        }

        public static MorrowindButtonView CreateMorrowindButton(
            string name,
            Transform parent,
            RuntimeUiTheme theme,
            string label,
            float scale,
            Color textColor,
            Color centerColor)
        {
            var root = CreateRect(name, parent);
            var frame = CreateBorderFrame("Frame", root, ResolveThinFrame(theme), centerColor);
            Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var labelText = CreateBitmapText(
                "Label",
                frame.Client,
                theme?.DefaultFont,
                scale,
                textColor,
                BitmapTextAlignment.Center);
            labelText.Text = label ?? string.Empty;
            CenterSingleLineText(labelText.rectTransform, labelText, 4f, 1.2f);

            return new MorrowindButtonView
            {
                Root = root,
                Frame = frame,
                Button = button,
                Label = labelText,
            };
        }

        public static RuntimeUiSpriteButtonView CreateSpriteButton(
            string name,
            Transform parent,
            Sprite normal,
            Sprite highlighted,
            Sprite pressed,
            bool enabled = true,
            bool preserveAspect = true,
            bool flipVertical = false,
            float disabledOpacity = 0.58f)
        {
            var root = CreateRect(name, parent);
            var image = CreateImage("Image", root, enabled ? Color.white : new Color(1f, 1f, 1f, disabledOpacity));
            Stretch(image.rectTransform);
            image.sprite = normal;
            image.type = Image.Type.Simple;
            image.preserveAspect = preserveAspect;
            if (flipVertical)
                image.rectTransform.localScale = new Vector3(1f, -1f, 1f);

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.SpriteSwap;
            button.targetGraphic = image;
            button.spriteState = new SpriteState
            {
                highlightedSprite = highlighted ?? normal,
                pressedSprite = pressed ?? highlighted ?? normal,
                selectedSprite = highlighted ?? normal,
                disabledSprite = normal,
            };
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = enabled;

            return new RuntimeUiSpriteButtonView
            {
                Root = root,
                Image = image,
                Button = button,
            };
        }

        public static BorderTextureSet ResolveThinFrame(RuntimeUiTheme theme)
        {
            return theme?.ThinFrame ?? default;
        }

        public static BorderTextureSet ResolveThickFrame(RuntimeUiTheme theme)
        {
            return theme?.ThickFrame ?? default;
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
