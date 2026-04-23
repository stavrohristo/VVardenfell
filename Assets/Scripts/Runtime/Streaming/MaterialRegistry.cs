using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Persistent registry of the Materials the streaming pipeline wants user-tweakable.
    ///
    /// As of FormatVersion 13 every ref samples a shared <c>Texture2DArray</c> via one of
    /// three runtime-built blend-variant Materials (Opaque / AlphaTest / AlphaBlend), so
    /// per-texture Material assets no longer make sense. The <see cref="RefMaterials"/>
    /// list is kept as a serialized field purely so existing registry <c>.asset</c> files
    /// don't lose their data, but it is **not populated or consulted anymore**.
    ///
    /// What remains user-editable:
    ///   * <see cref="TerrainTemplate"/> — cloned per-cell; <c>_Splat</c> (per-cell) and
    ///     <c>_LayerArray</c> (runtime-built) are injected after the clone. Edit shader /
    ///     tile scales on the asset.
    ///   * <see cref="TerrainFallback"/> — shared across cells whose LAND has no VTEX.
    /// </summary>
    [CreateAssetMenu(menuName = "VVardenfell/Material Registry", fileName = "VVMaterialRegistry")]
    public sealed class MaterialRegistry : ScriptableObject
    {
        [Serializable]
        public struct RefEntry
        {
            public string Key;
            public Material Material;
        }

        [Obsolete("Dormant since FormatVersion 13. Refs now share one Texture2DArray with 3 runtime-built materials; this list is kept only so old registry assets don't lose serialized data.")]
        [Tooltip("DORMANT — dead data carried forward from pre-FormatVersion-13 registries. Not populated or read anymore; per-texture ref materials were replaced by a shared Texture2DArray.")]
        public List<RefEntry> RefMaterials = new();

        [Tooltip("Cloned per-cell. _Splat (per-cell) and _LayerArray (runtime-built) are injected after the clone. Edit to tweak shader / _TileScale / _SplatSize across all terrain.")]
        public Material TerrainTemplate;

        [Tooltip("Used as-is for cells whose LAND has no VTEX. Shared across every such cell.")]
        public Material TerrainFallback;

#if UNITY_EDITOR
        public const string GeneratedDir = "Assets/Generated";
        public const string RegistryPath = "Assets/Generated/VVMaterialRegistry.asset";
        public const string TerrainTemplatePath = "Assets/Generated/VV_TerrainTemplate.mat";
        public const string TerrainFallbackPath = "Assets/Generated/VV_TerrainFallback.mat";

        /// <summary>
        /// Load the registry asset from a fixed path, creating it (and the containing
        /// folder) if missing. Editor-only.
        /// </summary>
        public static MaterialRegistry LoadOrCreate()
        {
            var reg = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(RegistryPath);
            if (reg != null) return reg;

            Directory.CreateDirectory(GeneratedDir);
            reg = CreateInstance<MaterialRegistry>();
            AssetDatabase.CreateAsset(reg, RegistryPath);
            return reg;
        }

        /// <summary>Get existing template or build + save a new one from the MW terrain shader.</summary>
        public Material GetOrCreateTerrainTemplate(Shader shader)
        {
            if (TerrainTemplate != null) return TerrainTemplate;
            Directory.CreateDirectory(GeneratedDir);

            var m = new Material(shader) { name = "VV:TerrainTemplate", enableInstancing = true };
            // Default values — user can tweak on the asset.
            m.SetFloat("_TileScale", 16f);
            m.SetFloat("_SplatSize", 16f);
            AssetDatabase.CreateAsset(m, TerrainTemplatePath);
            TerrainTemplate = m;
            EditorUtility.SetDirty(this);
            return m;
        }

        /// <summary>Get existing fallback or build + save a new one (URP/Lit, greenish).</summary>
        public Material GetOrCreateTerrainFallback(Shader fallbackShader)
        {
            if (TerrainFallback != null) return TerrainFallback;
            Directory.CreateDirectory(GeneratedDir);

            var m = new Material(fallbackShader) { name = "VV:TerrainFallback", color = new Color(0.35f, 0.42f, 0.30f) };
            AssetDatabase.CreateAsset(m, TerrainFallbackPath);
            TerrainFallback = m;
            EditorUtility.SetDirty(this);
            return m;
        }
#endif
    }
}
