using System;
using System.Collections;
using System.IO;
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
        {
            return WorldBakeService.Bake(config, progress);
        }

        public static IEnumerator BakeUiOnly(MorrowindConfig config, BakeProgress progress)
        {
            var bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(bsaPath))
            {
                progress.Error = "Morrowind.bsa missing under the configured install path.";
                progress.Done = true;
                yield break;
            }

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
                progress.Done = true;
                yield break;
            }

            yield return null;

            try
            {
                using var bsa = BsaArchive.Open(bsaPath);
                UiAssetBakery.Bake(config, bsa, progress);
                progress.Current = 2;
                progress.Label = "Presentation cache ready";
                progress.Done = true;
            }
            catch (Exception ex)
            {
                progress.Error = $"Failed to bake UI cache: {ex.Message}";
                progress.Done = true;
            }
        }
    }
}
