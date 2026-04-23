using System;
using UnityEngine;
using UnityEngine.UI;

namespace VVardenfell.Runtime.UI
{
    public enum BitmapTextAlignment
    {
        Left,
        Center,
        Right,
    }

    [AddComponentMenu("UI/VV Bitmap Text")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BitmapTextGraphic : MaskableGraphic
    {
        BitmapFontAsset _font;
        [SerializeField] string _text = "";
        [SerializeField] float _fontScale = 1f;
        [SerializeField] BitmapTextAlignment _alignment = BitmapTextAlignment.Left;

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

        public override Texture mainTexture => _font?.Atlas != null ? _font.Atlas : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_font == null || _font.Atlas == null || string.IsNullOrEmpty(_text))
                return;

            var rect = rectTransform.rect;
            var lines = _text.Replace("\r", "").Split('\n');
            float lineHeight = _font.LineHeight * _fontScale;
            bool flipUvX = transform.lossyScale.x < 0f;
            bool flipUvY = transform.lossyScale.y < 0f;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                float lineWidth = MeasureLineWidth(line);
                float penX = _alignment switch
                {
                    BitmapTextAlignment.Center => rect.xMin + (rect.width - lineWidth) * 0.5f,
                    BitmapTextAlignment.Right => rect.xMax - lineWidth,
                    _ => rect.xMin,
                };

                float lineTop = rect.yMax - lineIndex * lineHeight;
                for (int i = 0; i < line.Length; i++)
                {
                    if (!_font.TryGetGlyph(line[i], out var glyph))
                        continue;

                    float x0 = penX + glyph.BearingX * _fontScale;
                    float x1 = x0 + glyph.Width * _fontScale;
                    float y1 = lineTop - glyph.BearingY * _fontScale;
                    float y0 = y1 - glyph.Height * _fontScale;

                    if (glyph.Width > 0f && glyph.Height > 0f)
                        AddQuad(vh, x0, y0, x1, y1, glyph.UvRect, flipUvX, flipUvY);

                    penX += glyph.Advance * _fontScale;
                }
            }
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

        void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Rect uv, bool flipUvX, bool flipUvY)
        {
            int start = vh.currentVertCount;
            var vert = UIVertex.simpleVert;
            vert.color = color;
            float uMin = flipUvX ? uv.xMax : uv.xMin;
            float uMax = flipUvX ? uv.xMin : uv.xMax;
            float vMin = flipUvY ? uv.yMax : uv.yMin;
            float vMax = flipUvY ? uv.yMin : uv.yMax;

            vert.position = new Vector3(x0, y0);
            vert.uv0 = new Vector2(uMin, vMin);
            vh.AddVert(vert);

            vert.position = new Vector3(x0, y1);
            vert.uv0 = new Vector2(uMin, vMax);
            vh.AddVert(vert);

            vert.position = new Vector3(x1, y1);
            vert.uv0 = new Vector2(uMax, vMax);
            vh.AddVert(vert);

            vert.position = new Vector3(x1, y0);
            vert.uv0 = new Vector2(uMax, vMin);
            vh.AddVert(vert);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
