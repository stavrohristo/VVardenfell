using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Importer.Dds;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Collects every LTEX texture that any cell's VTEX grid references, dedupes them, and
    /// assigns each a dense layer index that fits in a 16-bit splatmap slot.
    /// </summary>
    public sealed class TerrainLayerBakery
    {
        public const uint MagicLayers = 0x5259_4C54u; // 'TLYR'
        const uint Version = 3;
        const int PreferredPageSize = 4096;
        const int MaxPageSize = 8192;
        const int GutterPixels = 16;
        const int BytesPerPixel = 4;

        private readonly object _gate = new object();
        private readonly int _defaultTextureIndex;
        private readonly Dictionary<int, ushort> _layerByTextureIndex = new Dictionary<int, ushort>();
        private readonly List<int> _textureIndexByLayer = new List<int>();

        public int Count => _textureIndexByLayer.Count;
        public bool Modified { get; private set; }
        public bool ExistingCacheInvalid { get; private set; }

        public TerrainLayerBakery(int defaultTextureIndex)
        {
            _defaultTextureIndex = defaultTextureIndex;
            ResetToDefaultLayer();
        }

        public ushort AddOrGet(int textureIndex)
        {
            lock (_gate)
            {
                if (_layerByTextureIndex.TryGetValue(textureIndex, out var existing))
                    return existing;
                if (_textureIndexByLayer.Count >= ushort.MaxValue)
                    throw new InvalidOperationException($"Terrain layer count exceeded {ushort.MaxValue}; cannot encode additional LTEX texture index {textureIndex} in a ushort splat layer.");

                var idx = (ushort)_textureIndexByLayer.Count;
                _textureIndexByLayer.Add(textureIndex);
                _layerByTextureIndex[textureIndex] = idx;
                Modified = true;
                return idx;
            }
        }

        public ushort[] BuildCellGrid(
            ushort[] vtexIndices,
            Dictionary<string, Dictionary<int, string>> ltexMapsBySource,
            string landSourcePath,
            TextureBakery textureBakery,
            Dictionary<string, ushort> vtexToLayerCache)
        {
            var grid = new ushort[LandRecord.NumTextures];
            for (int i = 0; i < LandRecord.NumTextures; i++)
            {
                ushort vtex = vtexIndices[i];
                string cacheKey = (landSourcePath ?? string.Empty) + "\0" + vtex;
                if (vtexToLayerCache.TryGetValue(cacheKey, out var cached))
                {
                    grid[i] = cached;
                    continue;
                }

                string path = LtexIndex.ResolveVtexRequired(vtex, ltexMapsBySource, landSourcePath, $"Terrain layer grid slot {i}");
                int texIdx = textureBakery.AddOrGetRequired(path, $"Terrain layer grid slot {i}");
                ushort layer = AddOrGet(texIdx);
                vtexToLayerCache[cacheKey] = layer;
                grid[i] = layer;
            }

            return grid;
        }

        public void WriteTo(string path, TextureBakery textureBakery)
        {
            if (textureBakery == null)
                throw new ArgumentNullException(nameof(textureBakery));

            TerrainLayerPayload payload = BuildAtlasPayload(textureBakery);
            string tempPath = path + ".tmp";
            try
            {
                using (var fs = File.Create(tempPath))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(MagicLayers);
                    w.Write(Version);
                    w.Write((uint)payload.TextureIndices.Length);
                    foreach (var texIdx in payload.TextureIndices)
                        w.Write(texIdx);

                    w.Write(payload.PageSize);
                    w.Write(payload.PageCount);
                    w.Write(payload.GutterPixels);
                    w.Write((int)payload.Format);

                    for (int i = 0; i < payload.Layers.Length; i++)
                    {
                        TerrainLayerAtlasEntry layer = payload.Layers[i];
                        w.Write(layer.PageIndex);
                        w.Write(layer.SourceWidth);
                        w.Write(layer.SourceHeight);
                        w.Write(layer.InnerX);
                        w.Write(layer.InnerY);
                        w.Write(layer.InnerWidth);
                        w.Write(layer.InnerHeight);
                        w.Write(layer.RectMinX);
                        w.Write(layer.RectMinY);
                        w.Write(layer.RectSizeX);
                        w.Write(layer.RectSizeY);
                    }

                    int expectedPageLength = checked(payload.PageSize * payload.PageSize * BytesPerPixel);
                    for (int i = 0; i < payload.Pages.Length; i++)
                    {
                        if (payload.Pages[i] == null || payload.Pages[i].Length != expectedPageLength)
                            throw new InvalidDataException($"Terrain atlas page {i} has invalid byte length {payload.Pages[i]?.Length ?? 0}; expected {expectedPageLength}.");
                        w.Write(payload.Pages[i].Length);
                        w.Write(payload.Pages[i]);
                    }
                }

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
                Modified = false;
                ExistingCacheInvalid = false;
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        public void TryLoadExisting(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var payload = ReadPayload(path);
                var existing = payload.TextureIndices;
                if (existing == null || existing.Length == 0)
                    return;

                _layerByTextureIndex.Clear();
                _textureIndexByLayer.Clear();
                for (int i = 0; i < existing.Length; i++)
                {
                    if (i >= ushort.MaxValue)
                        throw new InvalidDataException($"Terrain layer cache '{path}' contains more than {ushort.MaxValue} layers.");
                    _textureIndexByLayer.Add(existing[i]);
                    _layerByTextureIndex[existing[i]] = (ushort)i;
                }
                Modified = false;
                ExistingCacheInvalid = false;
            }
            catch
            {
                ResetToDefaultLayer();
                ExistingCacheInvalid = true;
                Debug.LogWarning($"[VVardenfell][TerrainLayerCacheInvalid] Existing terrain layer cache '{path}' is invalid or incomplete; rebuilding terrain layer indices.");
            }
        }

        void ResetToDefaultLayer()
        {
            _layerByTextureIndex.Clear();
            _textureIndexByLayer.Clear();
            _textureIndexByLayer.Add(_defaultTextureIndex);
            _layerByTextureIndex[_defaultTextureIndex] = 0;
            Modified = true;
        }

        public static int[] ReadAll(string path)
            => ReadPayload(path).TextureIndices;

        public static TerrainLayerPayload ReadPayload(string path)
        {
            try
            {
                return ReadPayloadUnchecked(path);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"Truncated terrain layer atlas payload in {path}; rebake required.", ex);
            }
        }

        static TerrainLayerPayload ReadPayloadUnchecked(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicLayers)
                throw new InvalidDataException($"Bad magic in {path}");
            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported terrain layer version {version} in {path}; rebake required.");

            int layerCount = checked((int)r.ReadUInt32());
            if (layerCount <= 0 || layerCount > ushort.MaxValue)
                throw new InvalidDataException($"Invalid terrain layer count {layerCount} in {path}; rebake required.");

            var textureIndices = new int[layerCount];
            for (int i = 0; i < layerCount; i++)
                textureIndices[i] = r.ReadInt32();

            int pageSize = r.ReadInt32();
            int pageCount = r.ReadInt32();
            int gutter = r.ReadInt32();
            var format = (TextureFormat)r.ReadInt32();
            ValidateAtlasHeader(path, pageSize, pageCount, gutter, format);

            var layers = new TerrainLayerAtlasEntry[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                var layer = new TerrainLayerAtlasEntry
                {
                    PageIndex = r.ReadInt32(),
                    SourceWidth = r.ReadInt32(),
                    SourceHeight = r.ReadInt32(),
                    InnerX = r.ReadInt32(),
                    InnerY = r.ReadInt32(),
                    InnerWidth = r.ReadInt32(),
                    InnerHeight = r.ReadInt32(),
                    RectMinX = r.ReadSingle(),
                    RectMinY = r.ReadSingle(),
                    RectSizeX = r.ReadSingle(),
                    RectSizeY = r.ReadSingle(),
                };
                ValidateLayerEntry(path, i, layer, pageSize, pageCount);
                layers[i] = layer;
            }

            int expectedPageLength = checked(pageSize * pageSize * BytesPerPixel);
            var pages = new byte[pageCount][];
            for (int i = 0; i < pageCount; i++)
            {
                int length = r.ReadInt32();
                if (length != expectedPageLength)
                    throw new InvalidDataException($"Invalid terrain atlas page {i} payload length {length} in {path}; expected {expectedPageLength}.");
                pages[i] = r.ReadBytes(length);
                if (pages[i].Length != length)
                    throw new EndOfStreamException($"Truncated terrain atlas page {i} in {path}.");
            }

            if (fs.Position != fs.Length)
                throw new InvalidDataException($"Unexpected trailing data in {path} at offset {fs.Position}/{fs.Length}.");

            return new TerrainLayerPayload
            {
                TextureIndices = textureIndices,
                PageSize = pageSize,
                PageCount = pageCount,
                GutterPixels = gutter,
                Format = format,
                Layers = layers,
                Pages = pages,
            };
        }

        TerrainLayerPayload BuildAtlasPayload(TextureBakery textureBakery)
        {
            var sources = new List<TerrainAtlasSource>(_textureIndexByLayer.Count);
            for (int layer = 0; layer < _textureIndexByLayer.Count; layer++)
                sources.Add(LoadSource(textureBakery, _textureIndexByLayer[layer], layer));

            if (!TryPack(sources, PreferredPageSize, out var placements))
                if (!TryPack(sources, MaxPageSize, out placements))
                    throw new InvalidDataException($"Terrain atlas could not pack {sources.Count} layers into {MaxPageSize}x{MaxPageSize} RGBA32 pages with {GutterPixels}px wrapped gutters.");

            int pageSize = placements.PageSize;
            var pages = new byte[placements.PageCount][];
            int pageLength = checked(pageSize * pageSize * BytesPerPixel);
            for (int i = 0; i < pages.Length; i++)
                pages[i] = new byte[pageLength];

            var layerEntries = new TerrainLayerAtlasEntry[sources.Count];
            foreach (TerrainAtlasSource source in sources)
            {
                TerrainAtlasPlacement placement = placements.ByLayer[source.LayerIndex];
                WriteWrappedTexture(pages[placement.PageIndex], pageSize, source, placement.X, placement.Y);
                int innerX = placement.X + GutterPixels;
                int innerY = placement.Y + GutterPixels;
                layerEntries[source.LayerIndex] = new TerrainLayerAtlasEntry
                {
                    PageIndex = placement.PageIndex,
                    SourceWidth = source.Width,
                    SourceHeight = source.Height,
                    InnerX = innerX,
                    InnerY = innerY,
                    InnerWidth = source.Width,
                    InnerHeight = source.Height,
                    RectMinX = innerX / (float)pageSize,
                    RectMinY = innerY / (float)pageSize,
                    RectSizeX = source.Width / (float)pageSize,
                    RectSizeY = source.Height / (float)pageSize,
                };
            }

            return new TerrainLayerPayload
            {
                TextureIndices = _textureIndexByLayer.ToArray(),
                PageSize = pageSize,
                PageCount = pages.Length,
                GutterPixels = GutterPixels,
                Format = TextureFormat.RGBA32,
                Layers = layerEntries,
                Pages = pages,
            };
        }

        TerrainAtlasSource LoadSource(TextureBakery textureBakery, int textureIndex, int layer)
        {
            if (textureIndex < 0)
                throw new InvalidDataException($"Terrain layer {layer} has unresolved texture index {textureIndex}; native terrain atlas requires a valid LTEX texture.");

            DdsTexture.Payload source = textureBakery.GetRgba32Payload(textureIndex);
            if (source == null || source.Mips == null || source.Mips.Length == 0 || source.Mips[0] == null)
                throw new InvalidDataException($"Terrain layer {layer} texture {textureIndex} has no source pixels.");

            if (!IsPowerOfTwo(source.Width) || !IsPowerOfTwo(source.Height))
            {
                throw new InvalidDataException(
                    $"Terrain layer {layer} texture {textureIndex} is {source.Width}x{source.Height}; native atlas requires power-of-two dimensions and a maximum padded size of {MaxPageSize}x{MaxPageSize}.");
            }

            int expectedLength = checked(source.Width * source.Height * BytesPerPixel);
            if (source.Mips[0].Length != expectedLength)
                throw new InvalidDataException($"Terrain layer {layer} texture {textureIndex} top mip has {source.Mips[0].Length} bytes; expected {expectedLength}.");

            int paddedWidth = checked(source.Width + (GutterPixels * 2));
            int paddedHeight = checked(source.Height + (GutterPixels * 2));
            if (paddedWidth > MaxPageSize || paddedHeight > MaxPageSize)
            {
                throw new InvalidDataException(
                    $"Terrain layer {layer} texture {textureIndex} is {source.Width}x{source.Height}; padded atlas entry {paddedWidth}x{paddedHeight} exceeds maximum page size {MaxPageSize}.");
            }

            return new TerrainAtlasSource(layer, textureIndex, source.Width, source.Height, source.Mips[0]);
        }

        static bool TryPack(List<TerrainAtlasSource> sources, int pageSize, out TerrainAtlasPacking packing)
        {
            var ordered = new List<TerrainAtlasSource>(sources);
            ordered.Sort(CompareSourcesForPacking);

            var pages = new List<TerrainAtlasPageState>();
            var byLayer = new TerrainAtlasPlacement[sources.Count];
            foreach (TerrainAtlasSource source in ordered)
            {
                if (source.PaddedWidth > pageSize || source.PaddedHeight > pageSize)
                {
                    packing = null;
                    return false;
                }

                bool placed = false;
                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages[i].TryPlace(source, pageSize, out var placement))
                    {
                        byLayer[source.LayerIndex] = placement;
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    var page = new TerrainAtlasPageState(pages.Count);
                    if (!page.TryPlace(source, pageSize, out var placement))
                    {
                        packing = null;
                        return false;
                    }
                    pages.Add(page);
                    byLayer[source.LayerIndex] = placement;
                }
            }

            packing = new TerrainAtlasPacking(pageSize, pages.Count, byLayer);
            return true;
        }

        static int CompareSourcesForPacking(TerrainAtlasSource a, TerrainAtlasSource b)
        {
            int cmp = b.PaddedHeight.CompareTo(a.PaddedHeight);
            if (cmp != 0)
                return cmp;
            cmp = b.PaddedWidth.CompareTo(a.PaddedWidth);
            if (cmp != 0)
                return cmp;
            cmp = a.TextureIndex.CompareTo(b.TextureIndex);
            if (cmp != 0)
                return cmp;
            return a.LayerIndex.CompareTo(b.LayerIndex);
        }

        static void WriteWrappedTexture(byte[] page, int pageSize, TerrainAtlasSource source, int outerX, int outerY)
        {
            int paddedWidth = source.PaddedWidth;
            int paddedHeight = source.PaddedHeight;
            for (int y = 0; y < paddedHeight; y++)
            {
                int sourceY = Mod(y - GutterPixels, source.Height);
                int sourceRow = sourceY * source.Width;
                int destRow = (outerY + y) * pageSize;
                for (int x = 0; x < paddedWidth; x++)
                {
                    int sourceX = Mod(x - GutterPixels, source.Width);
                    int src = ((sourceRow + sourceX) * BytesPerPixel);
                    int dst = ((destRow + outerX + x) * BytesPerPixel);
                    page[dst + 0] = source.Pixels[src + 0];
                    page[dst + 1] = source.Pixels[src + 1];
                    page[dst + 2] = source.Pixels[src + 2];
                    page[dst + 3] = source.Pixels[src + 3];
                }
            }
        }

        static void ValidateAtlasHeader(string path, int pageSize, int pageCount, int gutter, TextureFormat format)
        {
            if ((pageSize != PreferredPageSize && pageSize != MaxPageSize)
                || pageCount <= 0
                || gutter != GutterPixels
                || format != TextureFormat.RGBA32)
            {
                throw new InvalidDataException($"Invalid terrain atlas metadata in {path}; rebake required.");
            }
        }

        static void ValidateLayerEntry(string path, int index, TerrainLayerAtlasEntry layer, int pageSize, int pageCount)
        {
            if (layer.PageIndex < 0 || layer.PageIndex >= pageCount
                || layer.SourceWidth <= 0 || layer.SourceHeight <= 0
                || layer.SourceWidth != layer.InnerWidth || layer.SourceHeight != layer.InnerHeight
                || layer.InnerX < GutterPixels || layer.InnerY < GutterPixels
                || layer.InnerX + layer.InnerWidth + GutterPixels > pageSize
                || layer.InnerY + layer.InnerHeight + GutterPixels > pageSize
                || !IsPowerOfTwo(layer.SourceWidth) || !IsPowerOfTwo(layer.SourceHeight)
                || layer.RectMinX < 0f || layer.RectMinY < 0f
                || layer.RectSizeX <= 0f || layer.RectSizeY <= 0f
                || layer.RectMinX + layer.RectSizeX > 1f
                || layer.RectMinY + layer.RectSizeY > 1f)
            {
                throw new InvalidDataException($"Invalid terrain atlas layer {index} metadata in {path}; rebake required.");
            }
        }

        static bool IsPowerOfTwo(int value)
            => value > 0 && (value & (value - 1)) == 0;

        static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        sealed class TerrainAtlasSource
        {
            public readonly int LayerIndex;
            public readonly int TextureIndex;
            public readonly int Width;
            public readonly int Height;
            public readonly byte[] Pixels;

            public TerrainAtlasSource(int layerIndex, int textureIndex, int width, int height, byte[] pixels)
            {
                LayerIndex = layerIndex;
                TextureIndex = textureIndex;
                Width = width;
                Height = height;
                Pixels = pixels;
            }

            public int PaddedWidth => Width + (GutterPixels * 2);
            public int PaddedHeight => Height + (GutterPixels * 2);
        }

        sealed class TerrainAtlasPlacement
        {
            public readonly int PageIndex;
            public readonly int X;
            public readonly int Y;

            public TerrainAtlasPlacement(int pageIndex, int x, int y)
            {
                PageIndex = pageIndex;
                X = x;
                Y = y;
            }
        }

        sealed class TerrainAtlasPageState
        {
            private readonly int _pageIndex;
            private int _shelfY;
            private int _shelfX;
            private int _shelfHeight;

            public TerrainAtlasPageState(int pageIndex)
            {
                _pageIndex = pageIndex;
            }

            public bool TryPlace(TerrainAtlasSource source, int pageSize, out TerrainAtlasPlacement placement)
            {
                int width = source.PaddedWidth;
                int height = source.PaddedHeight;
                if (width > pageSize || height > pageSize)
                {
                    placement = null;
                    return false;
                }

                if (_shelfHeight == 0)
                {
                    _shelfHeight = height;
                    _shelfX = width;
                    placement = new TerrainAtlasPlacement(_pageIndex, 0, 0);
                    return true;
                }

                if (_shelfX + width <= pageSize && _shelfY + height <= pageSize)
                {
                    placement = new TerrainAtlasPlacement(_pageIndex, _shelfX, _shelfY);
                    _shelfX += width;
                    if (height > _shelfHeight)
                        _shelfHeight = height;
                    return true;
                }

                int nextShelfY = _shelfY + _shelfHeight;
                if (nextShelfY + height > pageSize)
                {
                    placement = null;
                    return false;
                }

                _shelfY = nextShelfY;
                _shelfX = width;
                _shelfHeight = height;
                placement = new TerrainAtlasPlacement(_pageIndex, 0, _shelfY);
                return true;
            }
        }

        sealed class TerrainAtlasPacking
        {
            public readonly int PageSize;
            public readonly int PageCount;
            public readonly TerrainAtlasPlacement[] ByLayer;

            public TerrainAtlasPacking(int pageSize, int pageCount, TerrainAtlasPlacement[] byLayer)
            {
                PageSize = pageSize;
                PageCount = pageCount;
                ByLayer = byLayer;
            }
        }
    }

    public sealed class TerrainLayerPayload
    {
        public int[] TextureIndices;
        public int PageSize;
        public int PageCount;
        public int GutterPixels;
        public TextureFormat Format;
        public TerrainLayerAtlasEntry[] Layers;
        public byte[][] Pages;
    }

    public sealed class TerrainLayerAtlasEntry
    {
        public int PageIndex;
        public int SourceWidth;
        public int SourceHeight;
        public int InnerX;
        public int InnerY;
        public int InnerWidth;
        public int InnerHeight;
        public float RectMinX;
        public float RectMinY;
        public float RectSizeX;
        public float RectSizeY;
    }
}
