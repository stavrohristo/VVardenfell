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
        const uint Version = 2;
        const int LayerSize = 256;
        const int LayerMipCount = 9;

        private readonly object _gate = new object();
        private readonly int _defaultTextureIndex;
        private readonly Dictionary<int, ushort> _layerByTextureIndex = new Dictionary<int, ushort>();
        private readonly List<int> _textureIndexByLayer = new List<int>();

        public int Count => _textureIndexByLayer.Count;
        public bool Modified { get; private set; }

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
                var idx = (ushort)_textureIndexByLayer.Count;
                _textureIndexByLayer.Add(textureIndex);
                _layerByTextureIndex[textureIndex] = idx;
                Modified = true;
                return idx;
            }
        }

        public ushort[] BuildCellGrid(
            ushort[] vtexIndices,
            Dictionary<int, string> ltexMap,
            TextureBakery textureBakery,
            Dictionary<ushort, ushort> vtexToLayerCache)
        {
            var grid = new ushort[LandRecord.NumTextures];
            for (int i = 0; i < LandRecord.NumTextures; i++)
            {
                ushort vtex = vtexIndices[i];
                if (vtexToLayerCache.TryGetValue(vtex, out var cached))
                {
                    grid[i] = cached;
                    continue;
                }

                string path = LtexIndex.ResolveVtex(vtex, ltexMap);
                int texIdx = textureBakery.AddOrGet(path);
                ushort layer = AddOrGet(texIdx);
                vtexToLayerCache[vtex] = layer;
                grid[i] = layer;
            }

            return grid;
        }

        public void WriteTo(string path, TextureBakery textureBakery)
        {
            if (textureBakery == null)
                throw new System.ArgumentNullException(nameof(textureBakery));

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicLayers);
            w.Write(Version);
            w.Write((uint)_textureIndexByLayer.Count);
            foreach (var texIdx in _textureIndexByLayer)
                w.Write(texIdx);

            w.Write(LayerSize);
            w.Write(LayerSize);
            w.Write(LayerMipCount);
            w.Write((int)TextureFormat.RGBA32);

            for (int i = 0; i < _textureIndexByLayer.Count; i++)
            {
                byte[][] mips = BuildLayerMips(textureBakery, _textureIndexByLayer[i], i);
                for (int mip = 0; mip < LayerMipCount; mip++)
                {
                    w.Write(mips[mip].Length);
                    w.Write(mips[mip]);
                }
            }

            Modified = false;
        }

        public void TryLoadExisting(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var existing = ReadAll(path);
                if (existing == null || existing.Length == 0)
                    return;

                _layerByTextureIndex.Clear();
                _textureIndexByLayer.Clear();
                for (int i = 0; i < existing.Length; i++)
                {
                    _textureIndexByLayer.Add(existing[i]);
                    _layerByTextureIndex[existing[i]] = (ushort)i;
                }
                Modified = false;
            }
            catch
            {
                ResetToDefaultLayer();
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
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicLayers)
                throw new InvalidDataException($"Bad magic in {path}");
            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported terrain layer version {version} in {path}; rebake required.");

            uint n = r.ReadUInt32();
            var arr = new int[n];
            for (int i = 0; i < n; i++)
                arr[i] = r.ReadInt32();
            return arr;
        }

        public static TerrainLayerPayload ReadPayload(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicLayers)
                throw new InvalidDataException($"Bad magic in {path}");
            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported terrain layer version {version} in {path}; rebake required.");

            int layerCount = checked((int)r.ReadUInt32());
            var textureIndices = new int[layerCount];
            for (int i = 0; i < layerCount; i++)
                textureIndices[i] = r.ReadInt32();

            int width = r.ReadInt32();
            int height = r.ReadInt32();
            int mipCount = r.ReadInt32();
            var format = (TextureFormat)r.ReadInt32();
            if (width != LayerSize || height != LayerSize || mipCount != LayerMipCount || format != TextureFormat.RGBA32)
                throw new InvalidDataException($"Invalid terrain layer payload metadata in {path}.");

            var layers = new byte[layerCount][][];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new byte[mipCount][];
                for (int mip = 0; mip < mipCount; mip++)
                {
                    int mipSize = System.Math.Max(1, LayerSize >> mip);
                    int expectedLength = mipSize * mipSize * 4;
                    int length = r.ReadInt32();
                    if (length != expectedLength)
                        throw new InvalidDataException($"Invalid terrain layer {i} mip {mip} payload length {length} in {path}.");
                    layers[i][mip] = r.ReadBytes(length);
                    if (layers[i][mip].Length != length)
                        throw new EndOfStreamException($"Truncated terrain layer {i} mip {mip} in {path}.");
                }
            }

            if (fs.Position != fs.Length)
                throw new InvalidDataException($"Unexpected trailing data in {path} at offset {fs.Position}/{fs.Length}.");

            return new TerrainLayerPayload
            {
                TextureIndices = textureIndices,
                Width = width,
                Height = height,
                MipCount = mipCount,
                Format = format,
                Layers = layers,
            };
        }

        static byte[][] BuildLayerMips(TextureBakery textureBakery, int textureIndex, int layer)
        {
            byte[] topMip;
            if (textureIndex < 0)
            {
                topMip = BuildMagentaFallback();
            }
            else
            {
                DdsTexture.Payload source = textureBakery.GetRgba32Payload(textureIndex);
                if (source.Width != LayerSize || source.Height != LayerSize)
                    throw new InvalidDataException($"Terrain layer {layer} texture {textureIndex} is {source.Width}x{source.Height}; expected {LayerSize}x{LayerSize}.");
                topMip = source.Mips[0];
            }

            var mips = new byte[LayerMipCount][];
            mips[0] = topMip;
            int previousSize = LayerSize;
            for (int mip = 1; mip < LayerMipCount; mip++)
            {
                int size = System.Math.Max(1, LayerSize >> mip);
                mips[mip] = DownsampleRgba32(mips[mip - 1], previousSize, size);
                previousSize = size;
            }

            return mips;
        }

        static byte[] BuildMagentaFallback()
        {
            var bytes = new byte[LayerSize * LayerSize * 4];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i + 0] = 255;
                bytes[i + 1] = 0;
                bytes[i + 2] = 255;
                bytes[i + 3] = 255;
            }
            return bytes;
        }

        static byte[] DownsampleRgba32(byte[] source, int sourceSize, int targetSize)
        {
            var result = new byte[targetSize * targetSize * 4];
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    int srcX = x * 2;
                    int srcY = y * 2;
                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int oy = 0; oy < 2; oy++)
                    {
                        int py = System.Math.Min(sourceSize - 1, srcY + oy);
                        for (int ox = 0; ox < 2; ox++)
                        {
                            int px = System.Math.Min(sourceSize - 1, srcX + ox);
                            int offset = ((py * sourceSize) + px) * 4;
                            r += source[offset + 0];
                            g += source[offset + 1];
                            b += source[offset + 2];
                            a += source[offset + 3];
                            count++;
                        }
                    }

                    int dst = ((y * targetSize) + x) * 4;
                    result[dst + 0] = (byte)(r / count);
                    result[dst + 1] = (byte)(g / count);
                    result[dst + 2] = (byte)(b / count);
                    result[dst + 3] = (byte)(a / count);
                }
            }

            return result;
        }
    }

    public sealed class TerrainLayerPayload
    {
        public int[] TextureIndices;
        public int Width;
        public int Height;
        public int MipCount;
        public TextureFormat Format;
        public byte[][][] Layers;
    }
}
