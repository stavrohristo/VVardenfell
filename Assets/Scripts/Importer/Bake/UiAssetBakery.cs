using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;

namespace VVardenfell.Importer.Bake
{
    public static class UiAssetBakery
    {
        static readonly string[] MovieSlots =
        {
            "Company Logo",
            "Morrowind Logo",
            "New Game",
            "Loading",
            "Options Menu",
        };

        static readonly string[] ScriptMovieFiles =
        {
            "mw_cavern.bik",
            "mw_end.bik",
        };

        static readonly HashSet<string> SupportedSplashExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tga", ".dds", ".png", ".bmp", ".jpeg", ".jpg",
        };

        static readonly (string Key, string RelativePath, bool Required)[] BootstrapImageSources =
        {
            (UiBootstrapAssetKeys.MenuNewGameNormal, @"Textures\menu_newgame.dds", true),
            (UiBootstrapAssetKeys.MenuNewGameHighlight, @"Textures\menu_newgame_over.dds", true),
            (UiBootstrapAssetKeys.MenuNewGamePressed, @"Textures\menu_newgame_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuLoadGameNormal, @"Textures\menu_loadgame.dds", true),
            (UiBootstrapAssetKeys.MenuLoadGameHighlight, @"Textures\menu_loadgame_over.dds", true),
            (UiBootstrapAssetKeys.MenuLoadGamePressed, @"Textures\menu_loadgame_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuOptionsNormal, @"Textures\menu_options.dds", true),
            (UiBootstrapAssetKeys.MenuOptionsHighlight, @"Textures\menu_options_over.dds", true),
            (UiBootstrapAssetKeys.MenuOptionsPressed, @"Textures\menu_options_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuCreditsNormal, @"Textures\menu_credits.dds", true),
            (UiBootstrapAssetKeys.MenuCreditsHighlight, @"Textures\menu_credits_over.dds", true),
            (UiBootstrapAssetKeys.MenuCreditsPressed, @"Textures\menu_credits_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuExitGameNormal, @"Textures\menu_exitgame.dds", true),
            (UiBootstrapAssetKeys.MenuExitGameHighlight, @"Textures\menu_exitgame_over.dds", true),
            (UiBootstrapAssetKeys.MenuExitGamePressed, @"Textures\menu_exitgame_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuReturnNormal, @"Textures\menu_return.dds", true),
            (UiBootstrapAssetKeys.MenuReturnHighlight, @"Textures\menu_return_over.dds", true),
            (UiBootstrapAssetKeys.MenuReturnPressed, @"Textures\menu_return_pressed.dds", true),
            (UiBootstrapAssetKeys.MenuSaveGameNormal, @"Textures\menu_savegame.dds", true),
            (UiBootstrapAssetKeys.MenuSaveGameHighlight, @"Textures\menu_savegame_over.dds", true),
            (UiBootstrapAssetKeys.MenuSaveGamePressed, @"Textures\menu_savegame_pressed.dds", true),
            (UiBootstrapAssetKeys.LoadingBarGray, @"Textures\menu_bar_gray.dds", true),
            (UiBootstrapAssetKeys.ThinBorderTop, @"Textures\menu_thin_border_top.dds", true),
            (UiBootstrapAssetKeys.ThinBorderBottom, @"Textures\menu_thin_border_bottom.dds", true),
            (UiBootstrapAssetKeys.ThinBorderLeft, @"Textures\menu_thin_border_left.dds", true),
            (UiBootstrapAssetKeys.ThinBorderRight, @"Textures\menu_thin_border_right.dds", true),
            (UiBootstrapAssetKeys.ThinBorderTopLeft, @"Textures\menu_thin_border_top_left_corner.dds", true),
            (UiBootstrapAssetKeys.ThinBorderTopRight, @"Textures\menu_thin_border_top_right_corner.dds", true),
            (UiBootstrapAssetKeys.ThinBorderBottomLeft, @"Textures\menu_thin_border_bottom_left_corner.dds", true),
            (UiBootstrapAssetKeys.ThinBorderBottomRight, @"Textures\menu_thin_border_bottom_right_corner.dds", true),
            (UiBootstrapAssetKeys.ThickBorderTop, @"Textures\menu_thick_border_top.dds", true),
            (UiBootstrapAssetKeys.ThickBorderBottom, @"Textures\menu_thick_border_bottom.dds", true),
            (UiBootstrapAssetKeys.ThickBorderLeft, @"Textures\menu_thick_border_left.dds", true),
            (UiBootstrapAssetKeys.ThickBorderRight, @"Textures\menu_thick_border_right.dds", true),
            (UiBootstrapAssetKeys.ThickBorderTopLeft, @"Textures\menu_thick_border_top_left_corner.dds", true),
            (UiBootstrapAssetKeys.ThickBorderTopRight, @"Textures\menu_thick_border_top_right_corner.dds", true),
            (UiBootstrapAssetKeys.ThickBorderBottomLeft, @"Textures\menu_thick_border_bottom_left_corner.dds", true),
            (UiBootstrapAssetKeys.ThickBorderBottomRight, @"Textures\menu_thick_border_bottom_right_corner.dds", true),
            (UiBootstrapAssetKeys.HeadBlockMiddle, @"Textures\menu_head_block_middle.dds", true),
            (UiBootstrapAssetKeys.HeadBlockTop, @"Textures\menu_head_block_top.dds", true),
            (UiBootstrapAssetKeys.HeadBlockBottom, @"Textures\menu_head_block_bottom.dds", true),
            (UiBootstrapAssetKeys.HeadBlockLeft, @"Textures\menu_head_block_left.dds", true),
            (UiBootstrapAssetKeys.HeadBlockRight, @"Textures\menu_head_block_right.dds", true),
            (UiBootstrapAssetKeys.HeadBlockTopLeft, @"Textures\menu_head_block_top_left_corner.dds", true),
            (UiBootstrapAssetKeys.HeadBlockTopRight, @"Textures\menu_head_block_top_right_corner.dds", true),
            (UiBootstrapAssetKeys.HeadBlockBottomLeft, @"Textures\menu_head_block_bottom_left_corner.dds", true),
            (UiBootstrapAssetKeys.HeadBlockBottomRight, @"Textures\menu_head_block_bottom_right_corner.dds", true),
            (UiBootstrapAssetKeys.HudCrosshair, @"Textures\target.dds", true),
            (UiBootstrapAssetKeys.HudCompass, @"Textures\compass.dds", true),
            (UiBootstrapAssetKeys.HudStealthSneakIcon, @"Icons\k\stealth_sneak.dds", true),
            (UiBootstrapAssetKeys.JournalBookBackground, @"Textures\tx_menubook.dds", true),
            (UiBootstrapAssetKeys.JournalBookBookmark, @"Textures\tx_menubook_bookmark.dds", true),
            (UiBootstrapAssetKeys.JournalBookPrevNormal, @"Textures\tx_menubook_prev_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookPrevHighlight, @"Textures\tx_menubook_prev_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookPrevPressed, @"Textures\tx_menubook_prev_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookNextNormal, @"Textures\tx_menubook_next_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookNextHighlight, @"Textures\tx_menubook_next_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookNextPressed, @"Textures\tx_menubook_next_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookCloseNormal, @"Textures\tx_menubook_close_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookCloseHighlight, @"Textures\tx_menubook_close_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookClosePressed, @"Textures\tx_menubook_close_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookJournalNormal, @"Textures\tx_menubook_journal_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookJournalHighlight, @"Textures\tx_menubook_journal_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookJournalPressed, @"Textures\tx_menubook_journal_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookCancelNormal, @"Textures\tx_menubook_cancel_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookCancelHighlight, @"Textures\tx_menubook_cancel_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookCancelPressed, @"Textures\tx_menubook_cancel_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookTakeNormal, @"Textures\tx_menubook_take_idle.dds", true),
            (UiBootstrapAssetKeys.JournalBookTakeHighlight, @"Textures\tx_menubook_take_over.dds", true),
            (UiBootstrapAssetKeys.JournalBookTakePressed, @"Textures\tx_menubook_take_pressed.dds", true),
            (UiBootstrapAssetKeys.JournalBookOptionsNormal, @"Textures\tx_menubook_options.dds", false),
            (UiBootstrapAssetKeys.JournalBookOptionsHighlight, @"Textures\tx_menubook_options_over.dds", false),
            (UiBootstrapAssetKeys.JournalBookQuests, @"Textures\tx_menubook_quests.dds", false),
            (UiBootstrapAssetKeys.JournalBookQuestsAll, @"Textures\tx_menubook_quests_all.dds", false),
            (UiBootstrapAssetKeys.JournalBookQuestsActive, @"Textures\tx_menubook_quests_active.dds", false),
            (UiBootstrapAssetKeys.ScrollBackground, @"Textures\scroll.dds", true),
        };

        public static void Bake(MorrowindConfig config, BsaArchive bsa, BakeProgress progress = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (bsa == null)
                throw new ArgumentNullException(nameof(bsa));

            CachePaths.EnsureExists();

            string installRoot = config.InstallPath;
            string dataFilesRoot = Path.Combine(installRoot, "Data Files");
            string iniPath = Path.Combine(installRoot, "Morrowind.ini");
            var ini = MorrowindIniReader.Read(iniPath);
            UiCacheManifest.TryRead(CachePaths.UiManifest, out var previousManifest);
            var transcodeBridge = new MovieTranscodeBridge(previousManifest);
            var bsaEntries = BuildBsaMap(bsa);

            var sources = new List<UiSourceRecord>();
            var images = new List<UiImageRecord>();
            var bootstrapImages = new List<UiBootstrapImageBinding>();
            var fonts = new List<UiFontRecord>();
            var movies = new List<UiMovieRecord>();
            var splashIds = new List<string>();

            sources.Add(CreateLooseSource(iniPath));

            if (progress != null)
            {
                progress.Stage = "UI";
                progress.Label = "Baking presentation assets";
                progress.Current = 0;
                progress.Total = 5;
            }

            using (var payloadFs = File.Create(CachePaths.UiPayloads))
            using (var payloadWriter = new BinaryWriter(payloadFs))
            {
                BakeSplashes(dataFilesRoot, payloadWriter, sources, images, splashIds);
                if (progress != null)
                {
                    progress.Current = 1;
                    progress.Label = "Baked splash images";
                }

                BakeMenuBackground(bsa, bsaEntries, dataFilesRoot, payloadWriter, sources, images);
                if (progress != null)
                {
                    progress.Current = 2;
                    progress.Label = "Baked menu background";
                }

                BakeBootstrapImages(bsa, bsaEntries, dataFilesRoot, payloadWriter, sources, images, bootstrapImages);
                if (progress != null)
                {
                    progress.Current = 3;
                    progress.Label = "Baked bootstrap UI chrome";
                }

                BakeFonts(dataFilesRoot, ini, payloadWriter, sources, fonts);
                if (progress != null)
                {
                    progress.Current = 4;
                    progress.Label = "Baked bitmap fonts";
                }

                BakeMovies(dataFilesRoot, ini, transcodeBridge, sources, movies);
                if (progress != null)
                {
                    progress.Current = 5;
                    progress.Label = "Baked movies";
                }
            }

            var manifest = new UiCacheManifest
            {
                Sources = sources.ToArray(),
                Images = images.ToArray(),
                BootstrapImages = bootstrapImages.ToArray(),
                Fonts = fonts.ToArray(),
                Movies = movies.ToArray(),
                SplashImageIds = splashIds.ToArray(),
                MenuBackgroundImageId = images.Any(i => i.Id == "menu_background") ? "menu_background" : "",
                DefaultFontId = fonts.Any(f => f.Id == "font_0") ? "font_0" : (fonts.Count > 0 ? fonts[0].Id : ""),
                TitleFontId = fonts.Any(f => f.Id == "font_2") ? "font_2" : (fonts.Any(f => f.Id == "font_1") ? "font_1" : ""),
            };

            manifest.Write(CachePaths.UiManifest);
        }

        private static void BakeBootstrapImages(
            BsaArchive bsa,
            Dictionary<string, BsaEntry> bsaEntries,
            string dataFilesRoot,
            BinaryWriter payloadWriter,
            List<UiSourceRecord> sources,
            List<UiImageRecord> images,
            List<UiBootstrapImageBinding> bootstrapImages)
        {
            for (int i = 0; i < BootstrapImageSources.Length; i++)
            {
                var entry = BootstrapImageSources[i];
                if (!TryResolveLooseOrBsa(
                    bsa,
                    bsaEntries,
                    dataFilesRoot,
                    entry.RelativePath,
                    out var bytes,
                    out var sourcePath,
                    out var source))
                {
                    if (entry.Required)
                    {
                        throw new FileNotFoundException(
                            $"Required bootstrap UI texture '{entry.RelativePath}' could not be resolved from the Morrowind install.");
                    }

                    continue;
                }

                string imageId = BuildBootstrapImageId(entry.Key);
                AddImageRecord(payloadWriter, images, imageId, sourcePath, bytes);
                bootstrapImages.Add(new UiBootstrapImageBinding
                {
                    Key = entry.Key,
                    ImageId = imageId,
                });
                sources.Add(source);
            }
        }

        private static void BakeSplashes(
            string dataFilesRoot,
            BinaryWriter payloadWriter,
            List<UiSourceRecord> sources,
            List<UiImageRecord> images,
            List<string> splashIds)
        {
            string splashRoot = Path.Combine(dataFilesRoot, "Splash");
            if (!Directory.Exists(splashRoot))
                return;

            var files = Directory.EnumerateFiles(splashRoot, "*", SearchOption.AllDirectories)
                .Where(path => SupportedSplashExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string relative = NormalizeRelative(file);
                string id = "splash/" + Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                AddImageRecord(payloadWriter, images, id, relative, File.ReadAllBytes(file));
                sources.Add(CreateLooseSource(file));
                splashIds.Add(id);
            }
        }

        private static void BakeMenuBackground(
            BsaArchive bsa,
            Dictionary<string, BsaEntry> bsaEntries,
            string dataFilesRoot,
            BinaryWriter payloadWriter,
            List<UiSourceRecord> sources,
            List<UiImageRecord> images)
        {
            if (TryResolveLooseOrBsa(
                bsa, bsaEntries, dataFilesRoot, @"Textures\menu_morrowind.dds",
                out var bytes, out var sourcePath, out var source))
            {
                AddImageRecord(payloadWriter, images, "menu_background", sourcePath, bytes);
                sources.Add(source);
            }
        }

        private static void BakeFonts(
            string dataFilesRoot,
            MorrowindIniReader ini,
            BinaryWriter payloadWriter,
            List<UiSourceRecord> sources,
            List<UiFontRecord> fonts)
        {
            string fontDir = Path.Combine(dataFilesRoot, "Fonts");
            // Keep the default in-game readable font for body text, then use the larger
            // Century Gothic variant as the shared title font instead of Daedric so UI
            // captions remain legible across bootstrap and runtime shells.
            var defaultNames = new[] { "magic_cards_regular", "century_gothic_font_regular", "century_gothic_big" };

            for (int i = 0; i < 3; i++)
            {
                string configured = ini.GetValueOrDefault("Fonts", $"Font {i}", defaultNames[i]);
                string fontPath = Path.Combine(fontDir, configured + ".fnt");
                if (!File.Exists(fontPath))
                    continue;

                var record = MorrowindFontBakery.Bake($"font_{i}", fontPath, payloadWriter, out var fontSources);
                fonts.Add(record);
                for (int s = 0; s < fontSources.Length; s++)
                    sources.Add(fontSources[s]);
            }
        }

        private static void BakeMovies(
            string dataFilesRoot,
            MorrowindIniReader ini,
            MovieTranscodeBridge transcodeBridge,
            List<UiSourceRecord> sources,
            List<UiMovieRecord> movies)
        {
            for (int i = 0; i < MovieSlots.Length; i++)
            {
                string slot = MovieSlots[i];
                string configuredSource = ini.GetValueOrDefault("Movies", slot, "");
                string fallback = slot switch
                {
                    "Morrowind Logo" => "menu_background",
                    "New Game" => "menu_background",
                    "Options Menu" => "menu_background",
                    _ => "",
                };

                var record = transcodeBridge.CreateRecord(slot, configuredSource, fallback, dataFilesRoot);
                movies.Add(record);

                string fullSource = record.ResolvedSourcePath;
                if (!string.IsNullOrEmpty(fullSource) && File.Exists(fullSource))
                    sources.Add(CreateLooseSource(fullSource));
            }

            var menuMovie = transcodeBridge.CreateRecord("Menu Background", @"menu_background.bik", "menu_background", dataFilesRoot);
            movies.Add(menuMovie);
            string menuMoviePath = menuMovie.ResolvedSourcePath;
            if (File.Exists(menuMoviePath))
                sources.Add(CreateLooseSource(menuMoviePath));

            for (int i = 0; i < ScriptMovieFiles.Length; i++)
            {
                string movieFile = ScriptMovieFiles[i];
                var record = transcodeBridge.CreateRecord(movieFile, movieFile, string.Empty, dataFilesRoot);
                movies.Add(record);

                string fullSource = record.ResolvedSourcePath;
                if (!string.IsNullOrEmpty(fullSource) && File.Exists(fullSource))
                    sources.Add(CreateLooseSource(fullSource));
            }
        }

        private static void AddImageRecord(BinaryWriter payloadWriter, List<UiImageRecord> images, string id, string sourcePath, byte[] bytes)
        {
            long offset = payloadWriter.BaseStream.Position;
            payloadWriter.Write(bytes.Length);
            payloadWriter.Write(bytes);
            images.Add(new UiImageRecord
            {
                Id = id,
                SourcePath = sourcePath,
                Extension = Path.GetExtension(sourcePath ?? "") ?? "",
                PayloadOffset = offset,
                PayloadLength = checked(bytes.Length + sizeof(int)),
            });
        }

        private static Dictionary<string, BsaEntry> BuildBsaMap(BsaArchive bsa)
        {
            var map = new Dictionary<string, BsaEntry>(bsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bsa.Entries.Length; i++)
                map[bsa.Entries[i].Name] = bsa.Entries[i];
            return map;
        }

        private static bool TryResolveLooseOrBsa(
            BsaArchive bsa,
            Dictionary<string, BsaEntry> bsaEntries,
            string dataFilesRoot,
            string relativePath,
            out byte[] bytes,
            out string sourcePath,
            out UiSourceRecord source)
        {
            string normalized = relativePath.Replace('/', '\\');
            string loosePath = Path.Combine(dataFilesRoot, normalized.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(loosePath))
            {
                bytes = File.ReadAllBytes(loosePath);
                sourcePath = NormalizeRelative(loosePath);
                source = CreateLooseSource(loosePath);
                return true;
            }

            string bsaPath = normalized.ToLowerInvariant();
            if (bsaEntries.TryGetValue(bsaPath, out var entry))
            {
                bytes = bsa.Read(entry);
                sourcePath = bsaPath;
                source = new UiSourceRecord
                {
                    Kind = UiSourceKind.BsaEntry,
                    Path = bsaPath,
                    Size = entry.Size,
                    MtimeTicks = 0,
                };
                return true;
            }

            bytes = null;
            sourcePath = null;
            source = null;
            return false;
        }

        private static UiSourceRecord CreateLooseSource(string path)
        {
            var info = new FileInfo(path);
            return new UiSourceRecord
            {
                Kind = path.EndsWith("Morrowind.ini", StringComparison.OrdinalIgnoreCase) ? UiSourceKind.Ini : UiSourceKind.LooseFile,
                Path = NormalizeRelative(path),
                Size = info.Length,
                MtimeTicks = info.LastWriteTimeUtc.Ticks,
            };
        }

        private static string NormalizeRelative(string fullPath)
        {
            string normalized = fullPath.Replace('/', '\\');
            int dataFilesIndex = normalized.IndexOf("\\Data Files\\", StringComparison.OrdinalIgnoreCase);
            if (dataFilesIndex >= 0)
                return normalized.Substring(dataFilesIndex + 1);

            int iniIndex = normalized.IndexOf("\\Morrowind.ini", StringComparison.OrdinalIgnoreCase);
            if (iniIndex >= 0)
                return "Morrowind.ini";

            return normalized;
        }

        private static string BuildBootstrapImageId(string key)
        {
            return key.Replace('.', '_').Replace('/', '_').Replace('\\', '_');
        }
    }
}
