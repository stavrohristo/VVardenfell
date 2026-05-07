using System.Collections;
using UnityEngine;
using VVardenfell.Importer.Bake;
using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Loads the baked runtime-ready terrain layer Texture2DArray.
    /// </summary>
    public sealed class TerrainLayers
    {
        const int BatchSize = 32;

        public Texture2DArray Array { get; private set; }
        public int LayerCount { get; private set; }

        public void Build(string terrainLayersPath)
        {
            RuntimeCoroutinePump.RunToCompletion(BuildIncremental(terrainLayersPath, null));
        }

        public IEnumerator BuildIncremental(string terrainLayersPath, RuntimeLoadProgress progress)
        {
            TerrainLayerPayload payload = TerrainLayerBakery.ReadPayload(terrainLayersPath);
            LayerCount = payload.Layers?.Length ?? 0;
            if (LayerCount == 0)
            {
                progress?.BeginStage("Terrain layer arrays", "No terrain layers", 1);
                progress?.Report("No terrain layers", 1, 1);
                progress?.CompleteStage();
                yield break;
            }

            progress?.Report("Allocating baked terrain layer array", 0, LayerCount);
            Array = new Texture2DArray(payload.Width, payload.Height, LayerCount, payload.Format, payload.MipCount, linear: false)
            {
                name = "VV:TerrainLayers",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };

            bool success = false;
            try
            {
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    for (int mip = 0; mip < payload.MipCount; mip++)
                        Array.SetPixelData(payload.Layers[layer][mip], mip, layer);

                    int completed = layer + 1;
                    if (completed == LayerCount || (completed % BatchSize) == 0)
                    {
                        progress?.Report($"Uploading baked terrain layers {completed}/{LayerCount}", completed, LayerCount);
                        yield return null;
                    }
                }

                Array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                progress?.CompleteStage("Terrain layers ready");
                success = true;
            }
            finally
            {
                if (!success && Array != null)
                {
                    Object.Destroy(Array);
                    Array = null;
                    LayerCount = 0;
                }
            }
        }

        public void Dispose()
        {
            if (Array != null)
                Object.Destroy(Array);
            Array = null;
            LayerCount = 0;
        }
    }
}
