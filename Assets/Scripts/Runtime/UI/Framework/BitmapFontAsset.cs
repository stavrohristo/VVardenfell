using System.Collections.Generic;
using UnityEngine;

namespace VVardenfell.Runtime.UI.Framework
{
    /// <summary>
    /// Per-glyph metrics + atlas UVs. Unity's dynamic font atlas can pack glyphs rotated
    /// to save space, which means the UV for each quad corner is NOT a straightforward
    /// axis-aligned rect — we store all four corners explicitly so
    /// <see cref="BitmapTextGraphic"/> can map vertex → UV without losing orientation
    /// (otherwise rotated atlas glyphs render mirrored or upside-down).
    /// </summary>
    public readonly struct BitmapGlyph
    {
        /// <summary>
        /// Construct from an axis-aligned UV rect. Glyph is assumed to sit upright in
        /// the atlas (TopLeft = upper-left corner, BottomRight = lower-right). Use this
        /// overload for baked bitmap fonts where glyphs are packed at a fixed
        /// orientation.
        /// </summary>
        public BitmapGlyph(Rect uvRect, float width, float height, float advance, float bearingX, float bearingY)
            : this(
                uvTopLeft: new Vector2(uvRect.xMin, uvRect.yMax),
                uvTopRight: new Vector2(uvRect.xMax, uvRect.yMax),
                uvBottomLeft: new Vector2(uvRect.xMin, uvRect.yMin),
                uvBottomRight: new Vector2(uvRect.xMax, uvRect.yMin),
                width, height, advance, bearingX, bearingY)
        {
        }

        /// <summary>
        /// Construct from explicit per-corner UVs. Use this when the source font
        /// (e.g. Unity's dynamic font via <c>CharacterInfo.uvTopLeft</c> etc.) may
        /// store the glyph rotated on the atlas; the four corners then carry the
        /// orientation that a single rect cannot express.
        /// </summary>
        public BitmapGlyph(
            Vector2 uvTopLeft,
            Vector2 uvTopRight,
            Vector2 uvBottomLeft,
            Vector2 uvBottomRight,
            float width,
            float height,
            float advance,
            float bearingX,
            float bearingY)
        {
            UvTopLeft = uvTopLeft;
            UvTopRight = uvTopRight;
            UvBottomLeft = uvBottomLeft;
            UvBottomRight = uvBottomRight;
            Width = width;
            Height = height;
            Advance = advance;
            BearingX = bearingX;
            BearingY = bearingY;
        }

        public Vector2 UvTopLeft { get; }
        public Vector2 UvTopRight { get; }
        public Vector2 UvBottomLeft { get; }
        public Vector2 UvBottomRight { get; }
        public float Width { get; }
        public float Height { get; }
        public float Advance { get; }
        public float BearingX { get; }
        public float BearingY { get; }

        /// <summary>
        /// Axis-aligned UV bounding rect. Correct for non-rotated glyphs; useful for
        /// diagnostics and code paths that don't care about rotation.
        /// </summary>
        public Rect UvRect => Rect.MinMaxRect(
            Mathf.Min(Mathf.Min(UvTopLeft.x, UvTopRight.x), Mathf.Min(UvBottomLeft.x, UvBottomRight.x)),
            Mathf.Min(Mathf.Min(UvTopLeft.y, UvTopRight.y), Mathf.Min(UvBottomLeft.y, UvBottomRight.y)),
            Mathf.Max(Mathf.Max(UvTopLeft.x, UvTopRight.x), Mathf.Max(UvBottomLeft.x, UvBottomRight.x)),
            Mathf.Max(Mathf.Max(UvTopLeft.y, UvTopRight.y), Mathf.Max(UvBottomLeft.y, UvBottomRight.y)));
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
