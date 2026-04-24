using System;
using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.UI.Assets;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    /// <summary>
    /// Nine-patch sprite set for the decorative "caption block" at the top of every
    /// Morrowind window (the gold filigree HB_ALL skin in OpenMW's <c>openmw_windows.skin.xml</c>).
    /// Unlike <see cref="BorderTextureSet"/>, the head block has an explicit <see cref="Middle"/>
    /// sprite that tiles across the caption interior rather than a flat colour fill.
    /// </summary>
    public readonly struct HeadBlockTextureSet
    {
        public HeadBlockTextureSet(
            Sprite middle,
            Sprite top,
            Sprite bottom,
            Sprite left,
            Sprite right,
            Sprite topLeft,
            Sprite topRight,
            Sprite bottomLeft,
            Sprite bottomRight)
        {
            Middle = middle;
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
            TopLeft = topLeft;
            TopRight = topRight;
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
        }

        public Sprite Middle { get; }
        public Sprite Top { get; }
        public Sprite Bottom { get; }
        public Sprite Left { get; }
        public Sprite Right { get; }
        public Sprite TopLeft { get; }
        public Sprite TopRight { get; }
        public Sprite BottomLeft { get; }
        public Sprite BottomRight { get; }

        public bool IsValid => Middle != null && Top != null && Bottom != null && Left != null && Right != null;
    }

    public abstract class RuntimeUiTheme : IDisposable
    {
        public abstract BitmapFontAsset DefaultFont { get; }
        public abstract BorderTextureSet ThinFrame { get; }
        public abstract BorderTextureSet ThickFrame { get; }
        public abstract HeadBlockTextureSet HeadBlock { get; }
        public abstract Sprite LoadingBarFillSprite { get; }
        /// <summary>Vanilla HUD crosshair sprite (baked from <c>Textures/target.dds</c>).</summary>
        public abstract Sprite CrosshairSprite { get; }
        /// <summary>Vanilla minimap compass sprite (baked from <c>Textures/compass.dds</c>).
        /// Intended to be rotated to match the player's facing at runtime.</summary>
        public abstract Sprite CompassSprite { get; }
        /// <summary>Vanilla HUD sneak indicator icon (baked from
        /// <c>Icons/k/stealth_sneak.dds</c>). Shown in the sneak quick-slot box while
        /// the player is sneaking.</summary>
        public abstract Sprite StealthSneakSprite { get; }
        public abstract UiImageAsset MenuBackground { get; }
        public abstract IReadOnlyList<UiImageAsset> SplashImages { get; }

        public abstract UiImageAsset GetImage(string id);
        public abstract UiMovieRuntimeInfo GetMovie(string slot);
        public abstract Sprite GetBootstrapSprite(string key);
        public abstract void Dispose();

        public static RuntimeUiTheme FromAssets(UiRuntimeAssets assets)
        {
            return new CachedRuntimeUiTheme(assets);
        }

        public static RuntimeUiTheme CreateEmbeddedFallback()
        {
            return new EmbeddedRuntimeUiTheme();
        }
    }

    sealed class CachedRuntimeUiTheme : RuntimeUiTheme
    {
        readonly UiRuntimeAssets _assets;
        readonly BorderTextureSet _thinFrame;
        readonly BorderTextureSet _thickFrame;
        readonly HeadBlockTextureSet _headBlock;

        public CachedRuntimeUiTheme(UiRuntimeAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _thinFrame = new BorderTextureSet(
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderTop),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderBottom),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderTopLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderTopRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderBottomLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThinBorderBottomRight));
            _thickFrame = new BorderTextureSet(
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderTop),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderBottom),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderTopLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderTopRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderBottomLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.ThickBorderBottomRight));
            _headBlock = new HeadBlockTextureSet(
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockMiddle),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockTop),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockBottom),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockTopLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockTopRight),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockBottomLeft),
                GetBootstrapSprite(UiBootstrapAssetKeys.HeadBlockBottomRight));
        }

        public override BitmapFontAsset DefaultFont => _assets.DefaultFont;
        public override BorderTextureSet ThinFrame => _thinFrame;
        public override BorderTextureSet ThickFrame => _thickFrame;
        public override HeadBlockTextureSet HeadBlock => _headBlock;
        public override Sprite LoadingBarFillSprite => GetBootstrapSprite(UiBootstrapAssetKeys.LoadingBarGray);
        public override Sprite CrosshairSprite => GetBootstrapSprite(UiBootstrapAssetKeys.HudCrosshair);
        public override Sprite CompassSprite => GetBootstrapSprite(UiBootstrapAssetKeys.HudCompass);
        public override Sprite StealthSneakSprite => GetBootstrapSprite(UiBootstrapAssetKeys.HudStealthSneakIcon);
        public override UiImageAsset MenuBackground => _assets.MenuBackground;
        public override IReadOnlyList<UiImageAsset> SplashImages => _assets.SplashImages;

        public override UiImageAsset GetImage(string id) => _assets.GetImage(id);
        public override UiMovieRuntimeInfo GetMovie(string slot) => _assets.GetMovie(slot);
        public override Sprite GetBootstrapSprite(string key) => _assets.GetBootstrapImage(key)?.Sprite;

        public override void Dispose()
        {
            _assets.Dispose();
        }
    }

    sealed class EmbeddedRuntimeUiTheme : RuntimeUiTheme
    {
        readonly List<Texture2D> _ownedTextures = new();
        readonly List<Sprite> _ownedSprites = new();
        readonly BitmapFontAsset _defaultFont;
        readonly BorderTextureSet _thinFrame;
        readonly BorderTextureSet _thickFrame;
        readonly HeadBlockTextureSet _headBlock;
        readonly Sprite _loadingBarFillSprite;
        readonly Sprite _crosshairSprite;
        readonly Sprite _compassSprite;
        readonly Sprite _stealthSneakSprite;
        readonly UiImageAsset _menuBackground;

        public EmbeddedRuntimeUiTheme()
        {
            _defaultFont = EmbeddedBitmapFontBuilder.BuildDefaultFont();
            _thinFrame = CreateFrame("Thin", 1, new Color32(156, 131, 90, 255));
            _thickFrame = CreateFrame("Thick", 2, new Color32(188, 157, 108, 255));
            // Fallback head-block is a flat gold rectangle - no filigree texture available
            // when the real MW assets haven't been baked yet (bootstrap / missing install path).
            var headEdge = CreateSolidSprite("HeadBlockEdge", 2, 2, new Color32(188, 157, 108, 255));
            var headMiddle = CreateSolidSprite("HeadBlockMiddle", 4, 4, new Color32(120, 90, 50, 255));
            _headBlock = new HeadBlockTextureSet(headMiddle, headEdge, headEdge, headEdge, headEdge, headEdge, headEdge, headEdge, headEdge);
            _loadingBarFillSprite = CreateSolidSprite("LoadingBarFill", 2, 2, new Color32(0, 208, 209, 255));
            // Fallback crosshair is a tiny opaque white square - visible but obviously
            // placeholder. The real crosshair is target.dds from the MW install, loaded
            // through the baked UI asset pipeline once the player has pointed the launcher
            // at their install folder.
            _crosshairSprite = CreateSolidSprite("Crosshair", 2, 2, new Color32(240, 230, 210, 255));
            _compassSprite = CreateSolidSprite("Compass", 2, 2, new Color32(240, 215, 155, 255));
            _stealthSneakSprite = CreateSolidSprite("StealthSneak", 2, 2, new Color32(200, 200, 200, 255));
            _menuBackground = CreateImageAsset("FallbackBackground", 2, 2, new[]
            {
                new Color32(15, 11, 8, 255),
                new Color32(22, 16, 12, 255),
                new Color32(18, 13, 10, 255),
                new Color32(10, 7, 5, 255),
            });
        }

        public override BitmapFontAsset DefaultFont => _defaultFont;
        public override BorderTextureSet ThinFrame => _thinFrame;
        public override BorderTextureSet ThickFrame => _thickFrame;
        public override HeadBlockTextureSet HeadBlock => _headBlock;
        public override Sprite LoadingBarFillSprite => _loadingBarFillSprite;
        public override Sprite CrosshairSprite => _crosshairSprite;
        public override Sprite CompassSprite => _compassSprite;
        public override Sprite StealthSneakSprite => _stealthSneakSprite;
        public override UiImageAsset MenuBackground => _menuBackground;
        public override IReadOnlyList<UiImageAsset> SplashImages => Array.Empty<UiImageAsset>();

        public override UiImageAsset GetImage(string id)
        {
            return string.Equals(id, _menuBackground.Id, StringComparison.OrdinalIgnoreCase)
                ? _menuBackground
                : null;
        }

        public override UiMovieRuntimeInfo GetMovie(string slot)
        {
            return null;
        }

        public override Sprite GetBootstrapSprite(string key)
        {
            return key switch
            {
                UiBootstrapAssetKeys.LoadingBarGray => _loadingBarFillSprite,
                UiBootstrapAssetKeys.ThinBorderTop or UiBootstrapAssetKeys.ThinBorderBottom
                    or UiBootstrapAssetKeys.ThinBorderLeft or UiBootstrapAssetKeys.ThinBorderRight
                    or UiBootstrapAssetKeys.ThinBorderTopLeft or UiBootstrapAssetKeys.ThinBorderTopRight
                    or UiBootstrapAssetKeys.ThinBorderBottomLeft or UiBootstrapAssetKeys.ThinBorderBottomRight => _thinFrame.Top,
                UiBootstrapAssetKeys.ThickBorderTop or UiBootstrapAssetKeys.ThickBorderBottom
                    or UiBootstrapAssetKeys.ThickBorderLeft or UiBootstrapAssetKeys.ThickBorderRight
                    or UiBootstrapAssetKeys.ThickBorderTopLeft or UiBootstrapAssetKeys.ThickBorderTopRight
                    or UiBootstrapAssetKeys.ThickBorderBottomLeft or UiBootstrapAssetKeys.ThickBorderBottomRight => _thickFrame.Top,
                UiBootstrapAssetKeys.HeadBlockMiddle => _headBlock.Middle,
                UiBootstrapAssetKeys.HeadBlockTop or UiBootstrapAssetKeys.HeadBlockBottom
                    or UiBootstrapAssetKeys.HeadBlockLeft or UiBootstrapAssetKeys.HeadBlockRight
                    or UiBootstrapAssetKeys.HeadBlockTopLeft or UiBootstrapAssetKeys.HeadBlockTopRight
                    or UiBootstrapAssetKeys.HeadBlockBottomLeft or UiBootstrapAssetKeys.HeadBlockBottomRight => _headBlock.Top,
                UiBootstrapAssetKeys.HudCrosshair => _crosshairSprite,
                UiBootstrapAssetKeys.HudCompass => _compassSprite,
                UiBootstrapAssetKeys.HudStealthSneakIcon => _stealthSneakSprite,
                _ => null,
            };
        }

        public override void Dispose()
        {
            for (int i = 0; i < _ownedSprites.Count; i++)
            {
                if (_ownedSprites[i] != null)
                    Object.Destroy(_ownedSprites[i]);
            }

            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                    Object.Destroy(_ownedTextures[i]);
            }

            _ownedSprites.Clear();
            _ownedTextures.Clear();
        }

        BorderTextureSet CreateFrame(string name, int thickness, Color32 color)
        {
            var edge = CreateSolidSprite($"{name}Edge", thickness, thickness, color);
            var corner = CreateSolidSprite($"{name}Corner", thickness, thickness, color);
            return new BorderTextureSet(edge, edge, edge, edge, corner, corner, corner, corner);
        }

        UiImageAsset CreateImageAsset(string id, int width, int height, Color32[] pixels)
        {
            var texture = CreateTexture(id, width, height, pixels);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = id;
            _ownedSprites.Add(sprite);
            return new UiImageAsset
            {
                Id = id,
                Texture = texture,
                Sprite = sprite,
            };
        }

        Sprite CreateSolidSprite(string id, int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            var texture = CreateTexture(id, width, height, pixels);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = id;
            _ownedSprites.Add(sprite);
            return sprite;
        }

        Texture2D CreateTexture(string id, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _ownedTextures.Add(texture);
            return texture;
        }
    }

    static class EmbeddedBitmapFontBuilder
    {
        const int FontPointSize = 32;

        public static BitmapFontAsset BuildDefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (font == null)
                return new BitmapFontAsset("embedded.bootstrap.empty", Texture2D.whiteTexture, FontPointSize, new Dictionary<int, BitmapGlyph>());

            string charset = BuildCharacterSet();
            font.RequestCharactersInTexture(charset, FontPointSize, FontStyle.Normal);
            var atlas = font.material != null ? font.material.mainTexture as Texture2D : null;
            if (atlas == null)
                atlas = Texture2D.whiteTexture;

            var glyphs = new Dictionary<int, BitmapGlyph>(charset.Length);
            for (int i = 0; i < charset.Length; i++)
            {
                char ch = charset[i];
                if (!font.GetCharacterInfo(ch, out var info, FontPointSize, FontStyle.Normal))
                    continue;

                // Unity reports dynamic font quads relative to the text baseline; the
                // bitmap renderer wants a top-left line-box bearing. Normalize the Y
                // bearing here so mixed ascenders/descenders share one visual baseline.
                float lineHeight = font.lineHeight > 0 ? font.lineHeight : FontPointSize;
                float ascent = font.ascent > 0 ? font.ascent : lineHeight;
                float bearingY = Mathf.Max(0f, ascent - info.maxY);

                // Unity's dynamic font atlas can pack glyphs rotated to save space, so
                // the per-corner UVs are NOT interchangeable with a min/max rect — each
                // corner maps to a specific quad vertex. Passing all four corners through
                // preserves orientation; collapsing them into a rect flips glyphs
                // upside-down or sideways on the atlases that rotate.
                float width = Mathf.Max(0f, info.maxX - info.minX);
                float height = Mathf.Max(0f, info.maxY - info.minY);
                glyphs[ch] = new BitmapGlyph(
                    uvTopLeft: info.uvTopLeft,
                    uvTopRight: info.uvTopRight,
                    uvBottomLeft: info.uvBottomLeft,
                    uvBottomRight: info.uvBottomRight,
                    width,
                    height,
                    Mathf.Max(0f, info.advance),
                    info.minX,
                    bearingY);
            }

            float defaultLineHeight = font.lineHeight > 0 ? font.lineHeight : FontPointSize;
            return new BitmapFontAsset("embedded.bootstrap.default", atlas, defaultLineHeight, glyphs);
        }

        static string BuildCharacterSet()
        {
            const string extras = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
                                  " !?.,:;+-=*/\\\\|_()[]{}<>\"'`~@#$%^&";
            var chars = new HashSet<char>(extras);
            for (char ch = ' '; ch <= '~'; ch++)
                chars.Add(ch);

            var sorted = new List<char>(chars);
            sorted.Sort();
            return new string(sorted.ToArray());
        }
    }
}
