using System.IO;
using UnityEngine;

namespace VVardenfell.Core.Cache
{
    /// <summary>
    /// Centralised paths for the baked DOTS cache.
    /// </summary>
    public static class CachePaths
    {
        public const string RootFolderName = "vvardenfell-cache";

        public static string Root => Path.Combine(Application.persistentDataPath, RootFolderName);
        public static string Manifest => Path.Combine(Root, "manifest.bin");
        public static string Meshes => Path.Combine(Root, "meshes.bin");
        public static string Materials => Path.Combine(Root, "materials.bin");
        public static string MeshNames => Path.Combine(Root, "meshnames.bin");
        public static string TexturesIndex => Path.Combine(Root, "textures.bin");
        public static string TerrainLayers => Path.Combine(Root, "terrain_layers.bin");
        public static string TexturesDir => Path.Combine(Root, "textures");
        public static string CellsDir => Path.Combine(Root, "cells");

        public static string TextureFile(string hashHex) => Path.Combine(TexturesDir, hashHex + ".dds");
        public static string CellFile(int gridX, int gridY) => Path.Combine(CellsDir, $"{gridX}_{gridY}.bin");

        public static void EnsureExists()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(TexturesDir);
            Directory.CreateDirectory(CellsDir);
        }
    }
}
