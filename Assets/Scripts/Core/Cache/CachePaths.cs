using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VVardenfell.Core.Cache
{
    /// <summary>
    /// Centralised paths for the baked DOTS cache.
    /// </summary>
    public static class CachePaths
    {
        public const string RootFolderName = "vvardenfell-cache";
        private static string s_root;

        public static string Root => s_root ??= Path.Combine(Application.persistentDataPath, RootFolderName);
        public static string Manifest => Path.Combine(Root, "manifest.bin");
        public static string Meshes => Path.Combine(Root, "meshes.bin");
        public static string Materials => Path.Combine(Root, "materials.bin");
        public static string MeshNames => Path.Combine(Root, "meshnames.bin");
        public static string ModelPrefabs => Path.Combine(Root, "model_prefabs.bin");
        public static string ActorAnimations => Path.Combine(Root, "actor_animations.bin");
        public static string TexturesIndex => Path.Combine(Root, "textures.bin");
        public static string TerrainLayers => Path.Combine(Root, "terrain_layers.bin");
        public static string Collisions => Path.Combine(Root, "collisions.bin");
        public static string MeshCatalog => Path.Combine(Root, "mesh_catalog.bin");
        public static string MaterialCatalog => Path.Combine(Root, "material_catalog.bin");
        public static string TextureCatalog => Path.Combine(Root, "texture_catalog.bin");
        public static string CollisionCatalog => Path.Combine(Root, "collision_catalog.bin");
        public static string UiManifest => Path.Combine(Root, "ui.bin");
        public static string UiPayloads => Path.Combine(Root, "ui_payloads.bin");
        public static string GameplayContentManifest => Path.Combine(Root, "gameplay_content_manifest.bin");
        public static string GameplayContent => Path.Combine(Root, "gameplay_content.bin");
        public static string GameplayValidationReport => Path.Combine(Root, "gameplay_content_validation.txt");
        public static string WorldCollisionValidationReport => Path.Combine(Root, "world_collision_validation.txt");
        public static string MeshCacheReport => Path.Combine(Root, "mesh_cache_report.txt");
        public static string TexturesDir => Path.Combine(Root, "textures");
        public static string AudioDir => Path.Combine(Root, "audio");
        public static string CellsDir => Path.Combine(Root, "cells");
        public static string InteriorCellsDir => Path.Combine(Root, "interiors");
        public static string UiMoviesDir => Path.Combine(Root, "ui_movies");

        public static string TextureFile(string hashHex) => Path.Combine(TexturesDir, hashHex + ".dds");
        public static string CellFile(int gridX, int gridY) => Path.Combine(CellsDir, $"{gridX}_{gridY}.bin");
        public static string CellPlacementAuditFile(int gridX, int gridY) => Path.Combine(CellsDir, $"{gridX}_{gridY}.audit.bin");
        public static string InteriorCellFile(string cellId)
            => Path.Combine(InteriorCellsDir, StableHashHex(cellId) + ".bin");
        public static string InteriorCellPlacementAuditFile(string cellId)
            => Path.Combine(InteriorCellsDir, StableHashHex(cellId) + ".audit.bin");
        public static string UiMovieFile(string slotName)
            => Path.Combine(UiMoviesDir, SanitizeFileName(slotName) + ".mp4");

        /// <summary>
        /// Resolve Unity's persistent path on the main thread before any worker uses cache paths.
        /// </summary>
        public static void Warmup()
        {
            _ = Root;
        }

        public static void EnsureExists()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(TexturesDir);
            Directory.CreateDirectory(AudioDir);
            Directory.CreateDirectory(CellsDir);
            Directory.CreateDirectory(InteriorCellsDir);
            Directory.CreateDirectory(UiMoviesDir);
        }

        public static string StableHashHex(string value)
        {
            byte[] input = Encoding.UTF8.GetBytes((value ?? string.Empty).Trim().ToLowerInvariant());
            byte[] hash;
            using (var sha1 = SHA1.Create())
                hash = sha1.ComputeHash(input);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "movie";

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            return sb.ToString().Trim('_');
        }
    }
}
