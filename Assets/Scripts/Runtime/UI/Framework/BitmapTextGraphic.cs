using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VVardenfell.Runtime.UI.Framework
{
    public enum BitmapTextAlignment
    {
        Left,
        Center,
        Right,
    }

    public enum BitmapTextVerticalAlignment
    {
        Top,
        Middle,
        Bottom,
    }

    /// <summary>
    /// Wrap policy applied inside <see cref="BitmapTextGraphic.OnPopulateMesh"/>.
    /// Default is <see cref="None"/>: newline characters still break lines, but long
    /// content runs off the right edge of the rect. Set to <see cref="Word"/> on any
    /// text surface that hosts variable-length body content (dialog bodies, tooltips,
    /// notification banners, item descriptions). Truncation, if wanted, must be done
    /// explicitly by the caller (e.g. ellipsize before assigning <see cref="Text"/>).
    /// </summary>
    public enum BitmapTextWrapMode
    {
        None,
        Word,
    }

    [AddComponentMenu("UI/VV Bitmap Text")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BitmapTextGraphic : MaskableGraphic
    {
        BitmapFontAsset _font;
        [SerializeField] string _text = "";
        [SerializeField] float _fontScale = 1f;
        [SerializeField] BitmapTextAlignment _alignment = BitmapTextAlignment.Left;
        [SerializeField] BitmapTextVerticalAlignment _verticalAlignment = BitmapTextVerticalAlignment.Top;
        [SerializeField] BitmapTextWrapMode _wrapMode = BitmapTextWrapMode.None;

        readonly List<string> _lineBuffer = new();
        readonly List<string> _measureLineBuffer = new();

        public BitmapFontAsset Font
        {
            get => _font;
            set
            {
                if (_font == value)
                    return;
                _font = value;
                SetAllDirty();
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                value ??= "";
                if (_text == value)
                    return;
                _text = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Per-glyph scale applied to the atlas. Historically the only knob callers had;
        /// prefer <see cref="PixelHeight"/> when the caller knows the target line-box
        /// height in canvas pixels.
        /// </summary>
        public float FontScale
        {
            get => _fontScale;
            set
            {
                value = Mathf.Max(0.1f, value);
                if (Mathf.Approximately(_fontScale, value))
                    return;
                _fontScale = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Target line-box height in canvas pixels. Writing this computes
        /// <see cref="FontScale"/> from the font's baked <see cref="BitmapFontAsset.LineHeight"/>;
        /// reading it returns the current effective line-box height. If no font is
        /// assigned the getter falls back to <see cref="FontScale"/> as a raw value.
        /// </summary>
        public float PixelHeight
        {
            get => _font != null && _font.LineHeight > 0f ? _font.LineHeight * _fontScale : _fontScale;
            set
            {
                if (_font == null || _font.LineHeight <= 0f)
                {
                    FontScale = value;
                    return;
                }

                FontScale = value / _font.LineHeight;
            }
        }

        public BitmapTextAlignment Alignment
        {
            get => _alignment;
            set
            {
                if (_alignment == value)
                    return;
                _alignment = value;
                SetVerticesDirty();
            }
        }

        public BitmapTextVerticalAlignment VerticalAlignment
        {
            get => _verticalAlignment;
            set
            {
                if (_verticalAlignment == value)
                    return;
                _verticalAlignment = value;
                SetVerticesDirty();
            }
        }

        public BitmapTextWrapMode WrapMode
        {
            get => _wrapMode;
            set
            {
                if (_wrapMode == value)
                    return;
                _wrapMode = value;
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture => _font?.Atlas != null ? _font.Atlas : s_WhiteTexture;

        /// <summary>
        /// Rendered width (in canvas pixels) of the widest line in <see cref="Text"/>
        /// at the current <see cref="FontScale"/>. Useful for sizing containers that
        /// flex to fit their label — e.g. caption backdrops behind a window title,
        /// button rects that hug their label, tooltip bubbles. Returns 0 when no font
        /// is assigned or the text is empty.
        /// </summary>
        public float PreferredWidth
        {
            get
            {
                if (_font == null || string.IsNullOrEmpty(_text))
                    return 0f;

                float widest = 0f;
                int start = 0;
                for (int i = 0; i <= _text.Length; i++)
                {
                    if (i != _text.Length && _text[i] != '\n')
                        continue;

                    int len = i - start;
                    if (len > 0)
                    {
                        // Inline measurement to avoid allocating a substring per line.
                        float w = 0f;
                        for (int k = start; k < i; k++)
                        {
                            if (_font.TryGetGlyph(_text[k], out var glyph))
                                w += glyph.Advance * _fontScale;
                        }
                        if (w > widest) widest = w;
                    }
                    start = i + 1;
                }

                return widest;
            }
        }

        public float MeasureHeightForWidth(float availableWidth)
        {
            if (_font == null || string.IsNullOrEmpty(_text))
                return 0f;

            BuildLines(_text, availableWidth, _measureLineBuffer);
            return _measureLineBuffer.Count * _font.LineHeight * _fontScale;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_font == null || _font.Atlas == null || string.IsNullOrEmpty(_text))
                return;

            var rect = rectTransform.rect;
            BuildLines(_text, rect.width, _lineBuffer);
            if (_lineBuffer.Count == 0)
                return;

            float lineHeight = _font.LineHeight * _fontScale;
            float totalHeight = _lineBuffer.Count * lineHeight;
            bool flipUvX = transform.lossyScale.x < 0f;
            bool flipUvY = transform.lossyScale.y < 0f;

            // Compute the top edge of the first line-box. For Middle/Bottom we clamp to
            // rect.yMax so overflow (content taller than rect) never renders ABOVE the
            // parent rect - it just extends downward and gets clipped by any mask the
            // caller has in place. Without the clamp, centering a line-box taller than
            // the rect spills glyphs above yMax, which historically caused HUD and
            // modal text to render outside their container.
            float firstLineTop = _verticalAlignment switch
            {
                BitmapTextVerticalAlignment.Middle =>
                    Mathf.Min(rect.yMax, rect.yMin + (rect.height + totalHeight) * 0.5f),
                BitmapTextVerticalAlignment.Bottom =>
                    Mathf.Min(rect.yMax, rect.yMin + totalHeight),
                _ => rect.yMax,
            };

            for (int lineIndex = 0; lineIndex < _lineBuffer.Count; lineIndex++)
            {
                string line = _lineBuffer[lineIndex];
                float lineWidth = MeasureLineWidth(line);
                float penX = _alignment switch
                {
                    BitmapTextAlignment.Center => rect.xMin + (rect.width - lineWidth) * 0.5f,
                    BitmapTextAlignment.Right => rect.xMax - lineWidth,
                    _ => rect.xMin,
                };

                float lineTop = firstLineTop - lineIndex * lineHeight;
                for (int i = 0; i < line.Length; i++)
                {
                    if (!_font.TryGetGlyph(line[i], out var glyph))
                        continue;

                    float x0 = penX + glyph.BearingX * _fontScale;
                    float x1 = x0 + glyph.Width * _fontScale;
                    float y1 = lineTop - glyph.BearingY * _fontScale;
                    float y0 = y1 - glyph.Height * _fontScale;

                    if (glyph.Width > 0f && glyph.Height > 0f)
                        AddQuad(vh, x0, y0, x1, y1, in glyph, flipUvX, flipUvY);

                    penX += glyph.Advance * _fontScale;
                }
            }
        }

        // Base Graphic.OnRectTransformDimensionsChange already calls SetVerticesDirty,
        // which reflows our mesh whenever the RectTransform is resized. No override
        // needed - word-wrap picks up new widths automatically on the next rebuild.

        void BuildLines(string text, float availableWidth, List<string> destination)
        {
            destination.Clear();
            if (_font == null || string.IsNullOrEmpty(text))
                return;

            // Split on explicit line breaks first. Each paragraph wraps independently.
            string[] paragraphs = text.Replace("\r", "").Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p];
                if (_wrapMode == BitmapTextWrapMode.None || availableWidth <= 0f)
                {
                    destination.Add(paragraph);
                    continue;
                }

                AppendWrappedParagraph(paragraph, availableWidth, destination);
            }
        }

        void AppendWrappedParagraph(string paragraph, float maxWidth, List<string> destination)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                destination.Add(string.Empty);
                return;
            }

            string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                destination.Add(string.Empty);
                return;
            }

            // Greedy word-wrap. A single word wider than maxWidth gets its own line
            // (overflow preserved - callers that need mid-word breaks can pre-process).
            string currentLine = words[0];
            for (int i = 1; i < words.Length; i++)
            {
                string candidate = currentLine.Length == 0 ? words[i] : currentLine + ' ' + words[i];
                if (MeasureLineWidth(candidate) <= maxWidth)
                {
                    currentLine = candidate;
                }
                else
                {
                    destination.Add(currentLine);
                    currentLine = words[i];
                }
            }

            destination.Add(currentLine);
        }

        float MeasureLineWidth(string line)
        {
            if (string.IsNullOrEmpty(line) || _font == null)
                return 0f;

            float width = 0f;
            for (int i = 0; i < line.Length; i++)
            {
                if (_font.TryGetGlyph(line[i], out var glyph))
                    width += glyph.Advance * _fontScale;
            }

            return width;
        }

        void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, in BitmapGlyph glyph, bool flipUvX, bool flipUvY)
        {
            // Map each vertex to its explicit UV corner. For a typical upright glyph
            // (e.g. baked bitmap font) this is equivalent to the old rect-min/max path;
            // for dynamic fonts (Unity atlas) that pack glyphs rotated, the four UV
            // corners carry the orientation that a single rect cannot express — without
            // this, rotated glyphs render upside-down or mirrored.
            Vector2 uvBL = glyph.UvBottomLeft;
            Vector2 uvTL = glyph.UvTopLeft;
            Vector2 uvTR = glyph.UvTopRight;
            Vector2 uvBR = glyph.UvBottomRight;

            // Local flip flags apply globally across all four corners. A horizontal flip
            // swaps left↔right; vertical flip swaps top↔bottom; these respect the
            // parent transform's lossyScale so mirrored scene transforms still render
            // readable text.
            if (flipUvX)
            {
                (uvBL, uvBR) = (uvBR, uvBL);
                (uvTL, uvTR) = (uvTR, uvTL);
            }
            if (flipUvY)
            {
                (uvBL, uvTL) = (uvTL, uvBL);
                (uvBR, uvTR) = (uvTR, uvBR);
            }

            int start = vh.currentVertCount;
            var vert = UIVertex.simpleVert;
            vert.color = color;

            // Quad vertices in order: bottom-left, top-left, top-right, bottom-right.
            vert.position = new Vector3(x0, y0);
            vert.uv0 = uvBL;
            vh.AddVert(vert);

            vert.position = new Vector3(x0, y1);
            vert.uv0 = uvTL;
            vh.AddVert(vert);

            vert.position = new Vector3(x1, y1);
            vert.uv0 = uvTR;
            vh.AddVert(vert);

            vert.position = new Vector3(x1, y0);
            vert.uv0 = uvBR;
            vh.AddVert(vert);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
