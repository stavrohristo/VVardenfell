using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public static class UiBootstrapAssetKeys
    {
        public const string MenuNewGameNormal = "bootstrap.menu.newgame.normal";
        public const string MenuNewGameHighlight = "bootstrap.menu.newgame.highlight";
        public const string MenuNewGamePressed = "bootstrap.menu.newgame.pressed";
        public const string MenuLoadGameNormal = "bootstrap.menu.loadgame.normal";
        public const string MenuLoadGameHighlight = "bootstrap.menu.loadgame.highlight";
        public const string MenuLoadGamePressed = "bootstrap.menu.loadgame.pressed";
        public const string MenuOptionsNormal = "bootstrap.menu.options.normal";
        public const string MenuOptionsHighlight = "bootstrap.menu.options.highlight";
        public const string MenuOptionsPressed = "bootstrap.menu.options.pressed";
        public const string MenuCreditsNormal = "bootstrap.menu.credits.normal";
        public const string MenuCreditsHighlight = "bootstrap.menu.credits.highlight";
        public const string MenuCreditsPressed = "bootstrap.menu.credits.pressed";
        public const string MenuExitGameNormal = "bootstrap.menu.exitgame.normal";
        public const string MenuExitGameHighlight = "bootstrap.menu.exitgame.highlight";
        public const string MenuExitGamePressed = "bootstrap.menu.exitgame.pressed";
        public const string MenuReturnNormal = "bootstrap.menu.return.normal";
        public const string MenuReturnHighlight = "bootstrap.menu.return.highlight";
        public const string MenuReturnPressed = "bootstrap.menu.return.pressed";
        public const string MenuSaveGameNormal = "bootstrap.menu.savegame.normal";
        public const string MenuSaveGameHighlight = "bootstrap.menu.savegame.highlight";
        public const string MenuSaveGamePressed = "bootstrap.menu.savegame.pressed";
        public const string LoadingBarGray = "bootstrap.loading.bar.gray";
        public const string ThinBorderTop = "bootstrap.frame.thin.top";
        public const string ThinBorderBottom = "bootstrap.frame.thin.bottom";
        public const string ThinBorderLeft = "bootstrap.frame.thin.left";
        public const string ThinBorderRight = "bootstrap.frame.thin.right";
        public const string ThinBorderTopLeft = "bootstrap.frame.thin.topleft";
        public const string ThinBorderTopRight = "bootstrap.frame.thin.topright";
        public const string ThinBorderBottomLeft = "bootstrap.frame.thin.bottomleft";
        public const string ThinBorderBottomRight = "bootstrap.frame.thin.bottomright";
        public const string ThickBorderTop = "bootstrap.frame.thick.top";
        public const string ThickBorderBottom = "bootstrap.frame.thick.bottom";
        public const string ThickBorderLeft = "bootstrap.frame.thick.left";
        public const string ThickBorderRight = "bootstrap.frame.thick.right";
        public const string ThickBorderTopLeft = "bootstrap.frame.thick.topleft";
        public const string ThickBorderTopRight = "bootstrap.frame.thick.topright";
        public const string ThickBorderBottomLeft = "bootstrap.frame.thick.bottomleft";
        public const string ThickBorderBottomRight = "bootstrap.frame.thick.bottomright";

        public static readonly string[] RequiredImageKeys =
        {
            MenuNewGameNormal,
            MenuNewGameHighlight,
            MenuNewGamePressed,
            MenuLoadGameNormal,
            MenuLoadGameHighlight,
            MenuLoadGamePressed,
            MenuOptionsNormal,
            MenuOptionsHighlight,
            MenuOptionsPressed,
            MenuCreditsNormal,
            MenuCreditsHighlight,
            MenuCreditsPressed,
            MenuExitGameNormal,
            MenuExitGameHighlight,
            MenuExitGamePressed,
            MenuReturnNormal,
            MenuReturnHighlight,
            MenuReturnPressed,
            MenuSaveGameNormal,
            MenuSaveGameHighlight,
            MenuSaveGamePressed,
            LoadingBarGray,
            ThinBorderTop,
            ThinBorderBottom,
            ThinBorderLeft,
            ThinBorderRight,
            ThinBorderTopLeft,
            ThinBorderTopRight,
            ThinBorderBottomLeft,
            ThinBorderBottomRight,
            ThickBorderTop,
            ThickBorderBottom,
            ThickBorderLeft,
            ThickBorderRight,
            ThickBorderTopLeft,
            ThickBorderTopRight,
            ThickBorderBottomLeft,
            ThickBorderBottomRight,
        };
    }

    public enum UiSourceKind : byte
    {
        Ini = 0,
        LooseFile = 1,
        BsaEntry = 2,
    }

    public enum UiPayloadKind : byte
    {
        Image = 0,
        Font = 1,
    }

    [Flags]
    public enum UiMovieFlags : byte
    {
        None = 0,
        SourceAvailable = 1 << 0,
        TranscodedAvailable = 1 << 1,
        MissingSource = 1 << 2,
        HasAudio = 1 << 3,
    }

    public sealed class UiSourceRecord
    {
        public UiSourceKind Kind;
        public string Path;
        public long Size;
        public long MtimeTicks;
    }

    public sealed class UiImageRecord
    {
        public string Id;
        public string SourcePath;
        public string Extension;
        public long PayloadOffset;
        public int PayloadLength;
    }

    public sealed class UiBootstrapImageBinding
    {
        public string Key;
        public string ImageId;
    }

    public sealed class UiFontRecord
    {
        public string Id;
        public string SourcePath;
        public float DefaultHeight;
        public long PayloadOffset;
        public int PayloadLength;
    }

    public sealed class UiMovieRecord
    {
        public string Slot;
        public string ConfiguredSource;
        public string ResolvedSourcePath;
        public long SourceSize;
        public long SourceMtimeTicks;
        public string CachedClipPath;
        public long CachedClipSize;
        public long CachedClipMtimeTicks;
        public int TranscodeProfileVersion;
        public int Width;
        public int Height;
        public long DurationMs;
        public bool HasAudio;
        public string FallbackImageId;
        public UiMovieFlags Flags;
    }

    /// <summary>
    /// UI cache manifest. The outer cache contract is project-defined metadata in ui.bin
    /// plus raw asset payloads in ui_payloads.bin. Raster/font payloads referenced here are
    /// rebuilt into runtime UI resources on each launch, while movie clips are cached as
    /// transcoded files under ui_movies/.
    /// </summary>
    public sealed class UiCacheManifest
    {
        const uint Magic = 0x49555656u; // 'VVUI'
        const uint Version = 4;

        public const int MovieTranscodeProfileVersion = 4;

        public UiSourceRecord[] Sources = Array.Empty<UiSourceRecord>();
        public UiImageRecord[] Images = Array.Empty<UiImageRecord>();
        public UiBootstrapImageBinding[] BootstrapImages = Array.Empty<UiBootstrapImageBinding>();
        public UiFontRecord[] Fonts = Array.Empty<UiFontRecord>();
        public UiMovieRecord[] Movies = Array.Empty<UiMovieRecord>();
        public string[] SplashImageIds = Array.Empty<string>();
        public string MenuBackgroundImageId = "";
        public string DefaultFontId = "";
        public string TitleFontId = "";

        public bool SourcesMatch(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
                return false;

            for (int i = 0; i < Sources.Length; i++)
            {
                var source = Sources[i];
                if (source.Kind == UiSourceKind.BsaEntry)
                    continue;

                string fullPath = ResolveInstallRelative(installPath, source.Path);
                if (!File.Exists(fullPath))
                    return false;

                var info = new FileInfo(fullPath);
                if (info.Length != source.Size || info.LastWriteTimeUtc.Ticks != source.MtimeTicks)
                    return false;
            }

            return true;
        }

        public bool TryGetBootstrapImageId(string key, out string imageId)
        {
            imageId = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            for (int i = 0; i < BootstrapImages.Length; i++)
            {
                var binding = BootstrapImages[i];
                if (!string.Equals(binding.Key, key, StringComparison.Ordinal))
                    continue;

                imageId = binding.ImageId;
                return !string.IsNullOrWhiteSpace(imageId);
            }

            return false;
        }

        public bool HasRequiredBootstrapImages(out string error)
        {
            for (int i = 0; i < UiBootstrapAssetKeys.RequiredImageKeys.Length; i++)
            {
                string key = UiBootstrapAssetKeys.RequiredImageKeys[i];
                if (!TryGetBootstrapImageId(key, out var imageId))
                {
                    error = $"UI bootstrap asset binding '{key}' is missing.";
                    return false;
                }

                bool foundImage = false;
                for (int imageIndex = 0; imageIndex < Images.Length; imageIndex++)
                {
                    if (!string.Equals(Images[imageIndex].Id, imageId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foundImage = true;
                    break;
                }

                if (!foundImage)
                {
                    error = $"UI bootstrap asset '{key}' points to missing image '{imageId}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public void Write(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);

            w.Write(DefaultFontId ?? "");
            w.Write(TitleFontId ?? "");
            w.Write(MenuBackgroundImageId ?? "");

            w.Write(Sources.Length);
            for (int i = 0; i < Sources.Length; i++)
            {
                var source = Sources[i];
                w.Write((byte)source.Kind);
                w.Write(source.Path ?? "");
                w.Write(source.Size);
                w.Write(source.MtimeTicks);
            }

            w.Write(Images.Length);
            for (int i = 0; i < Images.Length; i++)
            {
                var image = Images[i];
                w.Write(image.Id ?? "");
                w.Write(image.SourcePath ?? "");
                w.Write(image.Extension ?? "");
                w.Write(image.PayloadOffset);
                w.Write(image.PayloadLength);
            }

            w.Write(BootstrapImages.Length);
            for (int i = 0; i < BootstrapImages.Length; i++)
            {
                var binding = BootstrapImages[i];
                w.Write(binding.Key ?? "");
                w.Write(binding.ImageId ?? "");
            }

            w.Write(Fonts.Length);
            for (int i = 0; i < Fonts.Length; i++)
            {
                var font = Fonts[i];
                w.Write(font.Id ?? "");
                w.Write(font.SourcePath ?? "");
                w.Write(font.DefaultHeight);
                w.Write(font.PayloadOffset);
                w.Write(font.PayloadLength);
            }

            w.Write(Movies.Length);
            for (int i = 0; i < Movies.Length; i++)
            {
                var movie = Movies[i];
                w.Write(movie.Slot ?? "");
                w.Write(movie.ConfiguredSource ?? "");
                w.Write(movie.ResolvedSourcePath ?? "");
                w.Write(movie.SourceSize);
                w.Write(movie.SourceMtimeTicks);
                w.Write(movie.CachedClipPath ?? "");
                w.Write(movie.CachedClipSize);
                w.Write(movie.CachedClipMtimeTicks);
                w.Write(movie.TranscodeProfileVersion);
                w.Write(movie.Width);
                w.Write(movie.Height);
                w.Write(movie.DurationMs);
                w.Write(movie.HasAudio);
                w.Write(movie.FallbackImageId ?? "");
                w.Write((byte)movie.Flags);
            }

            w.Write(SplashImageIds.Length);
            for (int i = 0; i < SplashImageIds.Length; i++)
                w.Write(SplashImageIds[i] ?? "");
        }

        public static bool TryRead(string path, out UiCacheManifest manifest)
        {
            manifest = null;
            if (!File.Exists(path))
                return false;

            try
            {
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != Magic)
                    return false;
                if (r.ReadUInt32() != Version)
                    return false;

                var result = new UiCacheManifest
                {
                    DefaultFontId = r.ReadString(),
                    TitleFontId = r.ReadString(),
                    MenuBackgroundImageId = r.ReadString(),
                };

                int sourceCount = r.ReadInt32();
                result.Sources = new UiSourceRecord[sourceCount];
                for (int i = 0; i < sourceCount; i++)
                {
                    result.Sources[i] = new UiSourceRecord
                    {
                        Kind = (UiSourceKind)r.ReadByte(),
                        Path = r.ReadString(),
                        Size = r.ReadInt64(),
                        MtimeTicks = r.ReadInt64(),
                    };
                }

                int imageCount = r.ReadInt32();
                result.Images = new UiImageRecord[imageCount];
                for (int i = 0; i < imageCount; i++)
                {
                    result.Images[i] = new UiImageRecord
                    {
                        Id = r.ReadString(),
                        SourcePath = r.ReadString(),
                        Extension = r.ReadString(),
                        PayloadOffset = r.ReadInt64(),
                        PayloadLength = r.ReadInt32(),
                    };
                }

                int bootstrapImageCount = r.ReadInt32();
                result.BootstrapImages = new UiBootstrapImageBinding[bootstrapImageCount];
                for (int i = 0; i < bootstrapImageCount; i++)
                {
                    result.BootstrapImages[i] = new UiBootstrapImageBinding
                    {
                        Key = r.ReadString(),
                        ImageId = r.ReadString(),
                    };
                }

                int fontCount = r.ReadInt32();
                result.Fonts = new UiFontRecord[fontCount];
                for (int i = 0; i < fontCount; i++)
                {
                    result.Fonts[i] = new UiFontRecord
                    {
                        Id = r.ReadString(),
                        SourcePath = r.ReadString(),
                        DefaultHeight = r.ReadSingle(),
                        PayloadOffset = r.ReadInt64(),
                        PayloadLength = r.ReadInt32(),
                    };
                }

                int movieCount = r.ReadInt32();
                result.Movies = new UiMovieRecord[movieCount];
                for (int i = 0; i < movieCount; i++)
                {
                    result.Movies[i] = new UiMovieRecord
                    {
                        Slot = r.ReadString(),
                        ConfiguredSource = r.ReadString(),
                        ResolvedSourcePath = r.ReadString(),
                        SourceSize = r.ReadInt64(),
                        SourceMtimeTicks = r.ReadInt64(),
                        CachedClipPath = r.ReadString(),
                        CachedClipSize = r.ReadInt64(),
                        CachedClipMtimeTicks = r.ReadInt64(),
                        TranscodeProfileVersion = r.ReadInt32(),
                        Width = r.ReadInt32(),
                        Height = r.ReadInt32(),
                        DurationMs = r.ReadInt64(),
                        HasAudio = r.ReadBoolean(),
                        FallbackImageId = r.ReadString(),
                        Flags = (UiMovieFlags)r.ReadByte(),
                    };
                }

                int splashCount = r.ReadInt32();
                result.SplashImageIds = new string[splashCount];
                for (int i = 0; i < splashCount; i++)
                    result.SplashImageIds[i] = r.ReadString();

                manifest = result;
                return true;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        public static string ResolveInstallRelative(string installPath, string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;
            return Path.Combine(installPath, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        }
    }
}
