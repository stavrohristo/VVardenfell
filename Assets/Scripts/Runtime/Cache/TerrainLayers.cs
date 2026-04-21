using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Loads <c>terrain_layers.bin</c> and materialises a single <see cref="Texture2DArray"/>
    /// that the terrain shader samples. Each slice is a 256×256 RGBA32 copy of one LTEX texture,
    /// produced by blitting the already-decoded runtime <see cref="Texture2D"/> through a temporary
    /// <see cref="RenderTexture"/>. Slices are keyed by dense <c>layerIndex</c> (as stored in each
    /// cell's 16×16 splatmap grid).
    /// </summary>
    public sealed class TerrainLayers
    {
        public const int LayerSize = 256;

        public Texture2DArray Array { get; private set; }
        public int LayerCount { get; private set; }

        public void Build(string terrainLayersPath, Texture2D[] textures)
        {
            int[] texIdxByLayer = TerrainLayerBakery.ReadAll(terrainLayersPath);
            LayerCount = texIdxByLayer.Length;
            if (LayerCount == 0) return;

            Array = new Texture2DArray(LayerSize, LayerSize, LayerCount, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "VV:TerrainLayers",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };

            // One shared RT we reuse per slice — avoids 80 allocations.
            var rt = RenderTexture.GetTemporary(LayerSize, LayerSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;

            // Readback buffer for CopyTexture alternative (some platforms can't CopyTexture RT→Texture2DArray).
            var tmp = new Texture2D(LayerSize, LayerSize, TextureFormat.RGBA32, mipChain: false, linear: false);
            var prevActive = RenderTexture.active;

            try
            {
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    int texIdx = texIdxByLayer[layer];
                    Texture src = (texIdx >= 0 && texIdx < textures.Length) ? textures[texIdx] : null;
                    if (src == null) src = Texture2D.whiteTexture;

                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, LayerSize, LayerSize), 0, 0, recalculateMipMaps: false);
                    tmp.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                    Graphics.CopyTexture(tmp, 0, 0, Array, layer, 0);
                }
                Array.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tmp);
            }
        }

        public void Dispose()
        {
            if (Array != null) Object.Destroy(Array);
            Array = null;
        }
    }
}
