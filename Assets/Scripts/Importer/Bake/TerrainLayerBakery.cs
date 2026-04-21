using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Collects every LTEX texture that any cell's VTEX grid references, dedupes them, and
    /// assigns each a dense layer index (0..N-1) that fits in a 16-bit splatmap slot.
    /// Layer 0 is reserved for the hardcoded default texture (<c>_land_default.dds</c>).
    ///
    /// File layout (<c>terrain_layers.bin</c>):
    ///   u32 magic 'TLYR'
    ///   u32 layerCount
    ///   i32[layerCount] textureIndex   (index into <c>textures.bin</c>; -1 if unresolvable)
    ///
    /// We do NOT duplicate DDS bytes here — textures go through the same
    /// <see cref="TextureBakery"/> as mesh textures. The runtime loader will decode those
    /// DDS files and resample them into a uniform-size <c>Texture2DArray</c>.
    /// </summary>
    public sealed class TerrainLayerBakery
    {
        public const uint MagicLayers = 0x5259_4C54u; // 'TLYR'

        private readonly Dictionary<int, ushort> _layerByTextureIndex = new Dictionary<int, ushort>();
        private readonly List<int> _textureIndexByLayer = new List<int>();

        public int Count => _textureIndexByLayer.Count;

        public TerrainLayerBakery(int defaultTextureIndex)
        {
            // Layer 0 is the default terrain texture. Kept even when textureIndex is -1 so the
            // runtime can still display *something* (magenta fallback) instead of crashing.
            _textureIndexByLayer.Add(defaultTextureIndex);
            _layerByTextureIndex[defaultTextureIndex] = 0;
        }

        /// <summary>Returns the dense layer index for a given textures.bin index.</summary>
        public ushort AddOrGet(int textureIndex)
        {
            if (_layerByTextureIndex.TryGetValue(textureIndex, out var existing)) return existing;
            var idx = (ushort)_textureIndexByLayer.Count;
            _textureIndexByLayer.Add(textureIndex);
            _layerByTextureIndex[textureIndex] = idx;
            return idx;
        }

        /// <summary>
        /// Resolve a cell's 16x16 <see cref="LandRecord.VtexIndices"/> (VTEX values as stored in
        /// the ESM, 0 = default, otherwise ltex+1) into a 16x16 dense-layer-index grid.
        /// </summary>
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
                if (vtexToLayerCache.TryGetValue(vtex, out var cached)) { grid[i] = cached; continue; }

                string path = LtexIndex.ResolveVtex(vtex, ltexMap);
                int texIdx = textureBakery.AddOrGet(path);
                ushort layer = AddOrGet(texIdx);
                vtexToLayerCache[vtex] = layer;
                grid[i] = layer;
            }
            return grid;
        }

        public void WriteTo(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicLayers);
            w.Write((uint)_textureIndexByLayer.Count);
            foreach (var texIdx in _textureIndexByLayer) w.Write(texIdx);
        }

        public static int[] ReadAll(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != MagicLayers)
                throw new InvalidDataException($"Bad magic in {path}");
            uint n = r.ReadUInt32();
            var arr = new int[n];
            for (int i = 0; i < n; i++) arr[i] = r.ReadInt32();
            return arr;
        }
    }
}
