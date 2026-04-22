using System.Collections.Generic;
using UnityEngine;

namespace VVardenfell.Runtime.UI
{
    public readonly struct BitmapGlyph
    {
        public BitmapGlyph(Rect uvRect, float width, float height, float advance, float bearingX, float bearingY)
        {
            UvRect = uvRect;
            Width = width;
            Height = height;
            Advance = advance;
            BearingX = bearingX;
            BearingY = bearingY;
        }

        public Rect UvRect { get; }
        public float Width { get; }
        public float Height { get; }
        public float Advance { get; }
        public float BearingX { get; }
        public float BearingY { get; }
    }

    public sealed class BitmapFontAsset
    {
        readonly Dictionary<int, BitmapGlyph> _glyphs;

        public BitmapFontAsset(string id, Texture2D atlas, float lineHeight, Dictionary<int, BitmapGlyph> glyphs)
        {
            Id = id;
            Atlas = atlas;
            LineHeight = lineHeight;
            _glyphs = glyphs ?? new Dictionary<int, BitmapGlyph>();
        }

        public string Id { get; }
        public Texture2D Atlas { get; }
        public float LineHeight { get; }

        public bool TryGetGlyph(int codepoint, out BitmapGlyph glyph)
        {
            if (_glyphs.TryGetValue(codepoint, out glyph))
                return true;

            if (_glyphs.TryGetValue('?', out glyph))
                return true;

            glyph = default;
            return false;
        }
    }
}
