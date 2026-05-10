using System;
using System.Collections;
using System.IO;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// UI-facing bake coordinator. World baking is delegated to <see cref="WorldBakeService"/>
    /// so the bootstrap flow stays thin and reusable.
    /// </summary>
    public static class BakeCoordinator
    {
        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress)
            => Bake(config, config?.CreateVanillaContentProfile(), progress);

        public static IEnumerator Bake(MorrowindConfig config, MorrowindContentProfile profile, BakeProgress progress)
        {
            progress.Done = false;
            yield return GameplayContentBakery.Bake(config, profile, progress, markDone: false);
            if (!string.IsNullOrEmpty(progress.Error))
                yield break;

            progress.Done = false;
            var gameplayContent = GameplayContentFile.Read(CachePaths.GameplayContent);
            yield return WorldBakeService.Bake(config, profile, progress, gameplayContent);
        }

        public static IEnumerator BakeUiOnly(MorrowindConfig config, BakeProgress progress)
        {
            yield return BakeUiOnlyInternal(config, progress, markDone: true);
        }

        public static IEnumerator BakeGameplayOnly(MorrowindConfig config, BakeProgress progress)
            => BakeGameplayOnly(config, config?.CreateVanillaContentProfile(), progress);

        public static IEnumerator BakeGameplayOnly(MorrowindConfig config, MorrowindContentProfile profile, BakeProgress progress)
        {
            yield return GameplayContentBakery.Bake(config, profile, progress);
        }

        public static IEnumerator BakeUiAndGameplayOnly(MorrowindConfig config, BakeProgress progress)
            => BakeUiAndGameplayOnly(config, config?.CreateVanillaContentProfile(), progress);

        public static IEnumerator BakeUiAndGameplayOnly(MorrowindConfig config, MorrowindContentProfile profile, BakeProgress progress)
        {
            yield return BakeUiOnlyInternal(config, progress, markDone: false);
            if (!string.IsNullOrEmpty(progress.Error))
                yield break;

            progress.Done = false;
            yield return GameplayContentBakery.Bake(config, profile, progress);
        }

        static IEnumerator BakeUiOnlyInternal(MorrowindConfig config, BakeProgress progress, bool markDone)
        {
            var bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(bsaPath))
            {
                progress.Error = "Morrowind.bsa missing under the configured install path.";
                progress.Done = markDone;
                yield break;
            }

            progress.Done = false;
            progress.Error = null;
            progress.Stage = "UI";
            progress.Label = "Opening archives";
            progress.Current = 0;
            progress.Total = 2;
            yield return null;

            try
            {
                using var bsa = BsaArchive.Open(bsaPath);
                progress.Label = "Baking presentation assets";
                progress.Current = 1;
            }
            catch (Exception ex)
            {
                progress.Error = $"Failed to open BSA: {ex.Message}";
                progress.Done = markDone;
                yield break;
            }

            yield return null;

            try
            {
                using var bsa = BsaArchive.Open(bsaPath);
                UiAssetBakery.Bake(config, bsa, progress);
                progress.Current = 2;
                progress.Label = "Presentation cache ready";
                progress.Done = markDone;
            }
            catch (Exception ex)
            {
                progress.Error = $"Failed to bake UI cache: {ex.Message}";
                progress.Done = markDone;
            }
        }
    }
}
