using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
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
        /// <summary>Optional MW_Window_Pinnable pin button installed at the
        /// caption's top-right corner. Null for windows that don't pin
        /// (Options, Save/Load, Container, modal dialogs).</summary>
        public MorrowindPinButtonView PinButton;
    }

    /// <summary>
    /// Pin toggle button that lives at the top-right of an MW_Window_Pinnable
    /// caption. <see cref="SetPinned"/> swaps the two-state visual (amber
    /// center + MW_Box thin frame when pinned, dark center when unpinned) so
    /// the view can reflect the underlying ECS Pinned byte without rebuilding.
    /// </summary>
    public sealed class MorrowindPinButtonView
    {
        public RectTransform Root;
        public Button Button;
        public BorderFrameView Frame;
        public Image PinGlyph;

        public void SetPinned(bool pinned)
        {
            if (Frame != null && Frame.Center != null)
                Frame.Center.color = pinned ? PinnedCenterColor : UnpinnedCenterColor;
            if (PinGlyph != null)
                PinGlyph.color = pinned ? PinnedGlyphColor : UnpinnedGlyphColor;
        }

        static readonly Color PinnedCenterColor = new(0.54f, 0.40f, 0.14f, 0.96f);
        static readonly Color UnpinnedCenterColor = new(0.10f, 0.08f, 0.06f, 0.88f);
        static readonly Color PinnedGlyphColor = new(0.14f, 0.10f, 0.06f, 1f);
        static readonly Color UnpinnedGlyphColor = new(0.92f, 0.80f, 0.44f, 1f);
    }

    public sealed class MorrowindButtonView
    {
        public RectTransform Root;
        public BorderFrameView Frame;
        public Button Button;
        public BitmapTextGraphic Label;
    }

    public sealed class RuntimeUiCanvasView
    {
        public Canvas Canvas;
        public CanvasScaler Scaler;
        public GraphicRaycaster Raycaster;
        public RectTransform Root;
        public EventSystem EventSystem;
    }

    public sealed class RuntimeUiProgressBarView
    {
        public RectTransform Root;
        public BorderFrameView Frame;
        public RectTransform FillRect;
        public Image Fill;
    }

    public sealed class RuntimeUiSpriteButtonView
    {
        public RectTransform Root;
        public Image Image;
        public Button Button;
    }

    public sealed class RuntimeUiTextInputView
    {
        public RectTransform Root;
        public BorderFrameView Frame;
        public RectTransform InputRoot;
        public InputField InputField;
        public Text HiddenText;
        public Text HiddenPlaceholder;
        public BitmapTextGraphic OverlayText;
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
}
