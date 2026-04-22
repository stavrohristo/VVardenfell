using System.Collections;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;
using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Loads terrain_layers.bin and materialises a single Texture2DArray that the terrain
    /// shader samples. Each slice is a 256x256 RGBA32 copy of one LTEX texture, produced
    /// by blitting the already-decoded runtime Texture2D through one reusable temporary
    /// RenderTexture and CPU Texture2D scratch buffer.
    /// </summary>
    public sealed class TerrainLayers
    {
        const int BatchSize = 32;

        public const int LayerSize = 256;

        public Texture2DArray Array { get; private set; }
        public int LayerCount { get; private set; }

        public void Build(string terrainLayersPath, Texture2D[] textures)
        {
            RuntimeCoroutinePump.RunToCompletion(BuildIncremental(terrainLayersPath, textures, null));
        }

        public IEnumerator BuildIncremental(string terrainLayersPath, Texture2D[] textures, RuntimeLoadProgress progress)
        {
            int[] texIdxByLayer = TerrainLayerBakery.ReadAll(terrainLayersPath);
            LayerCount = texIdxByLayer.Length;
            if (LayerCount == 0)
            {
                progress?.BeginStage("Terrain layer arrays", "No terrain layers", 1);
                progress?.Report("No terrain layers", 1, 1);
                progress?.CompleteStage();
                yield break;
            }

            progress?.Report("Allocating terrain layer array", 0, LayerCount);

            Array = new Texture2DArray(LayerSize, LayerSize, LayerCount, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "VV:TerrainLayers",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };

            var rt = RenderTexture.GetTemporary(LayerSize, LayerSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;

            var tmp = new Texture2D(LayerSize, LayerSize, TextureFormat.RGBA32, mipChain: false, linear: false);
            var prevActive = RenderTexture.active;

            bool success = false;
            try
            {
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    int texIdx = texIdxByLayer[layer];
                    Texture src = (texIdx >= 0 && texIdx < textures.Length) ? textures[texIdx] : null;
                    if (src == null)
                        src = Texture2D.whiteTexture;

                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, LayerSize, LayerSize), 0, 0, recalculateMipMaps: false);
                    tmp.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    Graphics.CopyTexture(tmp, 0, 0, Array, layer, 0);

                    int completed = layer + 1;
                    if (completed == LayerCount || (completed % BatchSize) == 0)
                    {
                        progress?.Report($"Uploading terrain layers {completed}/{LayerCount}", completed, LayerCount);
                        yield return null;
                    }
                }

                Array.Apply(updateMipmaps: true, makeNoLongerReadable: true);
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
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tmp);
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
