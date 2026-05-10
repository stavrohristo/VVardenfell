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
        public Texture2D LayerMeta0 { get; private set; }
        public Texture2D LayerMeta1 { get; private set; }
        public int LayerCount { get; private set; }

        public void Build(string terrainLayersPath)
        {
            RuntimeCoroutinePump.RunToCompletion(BuildIncremental(terrainLayersPath, null));
        }

        public IEnumerator BuildIncremental(string terrainLayersPath, RuntimeLoadProgress progress)
        {
            DestroyTextures();
            LayerCount = 0;

            TerrainLayerPayload payload = TerrainLayerBakery.ReadPayload(terrainLayersPath);
            LayerCount = payload.Layers?.Length ?? 0;
            if (LayerCount == 0 || payload.Pages == null || payload.Pages.Length == 0)
            {
                progress?.BeginStage("Terrain layer arrays", "No terrain layers", 1);
                progress?.Report("No terrain layers", 1, 1);
                progress?.CompleteStage();
                yield break;
            }

            if (payload.TextureIndices == null
                || payload.TextureIndices.Length != LayerCount
                || payload.PageCount != payload.Pages.Length)
                throw new System.IO.InvalidDataException("Terrain layer atlas metadata is incomplete; rebake required.");

            progress?.Report("Allocating baked terrain atlas", 0, payload.PageCount);
            Array = new Texture2DArray(payload.PageSize, payload.PageSize, payload.PageCount, payload.Format, true, false)
            {
                name = "VV:TerrainLayerAtlas",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
            LayerMeta0 = new Texture2D(LayerCount, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "VV:TerrainLayerMeta0",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            LayerMeta1 = new Texture2D(LayerCount, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "VV:TerrainLayerMeta1",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            bool success = false;
            try
            {
                int expectedPageLength = checked(payload.PageSize * payload.PageSize * 4);
                for (int page = 0; page < payload.PageCount; page++)
                {
                    byte[] pageBytes = payload.Pages[page];
                    if (pageBytes == null || pageBytes.Length != expectedPageLength)
                        throw new System.IO.InvalidDataException($"Terrain atlas page {page} is missing or truncated; rebake required.");

                    Array.SetPixelData(pageBytes, 0, page);

                    int completed = page + 1;
                    if (completed == payload.PageCount || (completed % BatchSize) == 0)
                    {
                        progress?.Report($"Uploading baked terrain atlas pages {completed}/{payload.PageCount}", completed, payload.PageCount);
                        yield return null;
                    }
                }

                var meta0 = new Vector4[LayerCount];
                var meta1 = new Vector4[LayerCount];
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    TerrainLayerAtlasEntry entry = payload.Layers[layer];
                    meta0[layer] = new Vector4(entry.PageIndex, entry.RectMinX, entry.RectMinY, 0f);
                    meta1[layer] = new Vector4(entry.RectSizeX, entry.RectSizeY, entry.SourceWidth, entry.SourceHeight);
                }

                LayerMeta0.SetPixelData(meta0, 0);
                LayerMeta1.SetPixelData(meta1, 0);
                Array.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                LayerMeta0.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                LayerMeta1.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                progress?.CompleteStage("Terrain layers ready");
                success = true;
            }
            finally
            {
                if (!success)
                {
                    DestroyTextures();
                    LayerCount = 0;
                }
            }
        }

        public void Dispose()
        {
            DestroyTextures();
            LayerCount = 0;
        }

        void DestroyTextures()
        {
            if (Array != null)
                Object.Destroy(Array);
            if (LayerMeta0 != null)
                Object.Destroy(LayerMeta0);
            if (LayerMeta1 != null)
                Object.Destroy(LayerMeta1);
            Array = null;
            LayerMeta0 = null;
            LayerMeta1 = null;
        }
    }
}
