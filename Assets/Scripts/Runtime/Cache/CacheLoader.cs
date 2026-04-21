using System.IO;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Dds;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Reads the baked cache and produces the runtime-side Unity resources:
    /// <see cref="UnityEngine.Mesh"/>[], <see cref="Texture2D"/>[], <see cref="Material"/>[].
    /// Runs once at boot.
    /// </summary>
    public sealed class CacheLoader
    {
        public BakeManifest Manifest { get; private set; }
        public Mesh[] Meshes { get; private set; }
        public string[] MeshNames { get; private set; } // parallel to Meshes; may be null if sidecar missing
        public Material[] Materials { get; private set; }
        public Texture2D[] Textures { get; private set; }
        public TerrainLayers TerrainLayers { get; private set; }
        /// <summary>
        /// Editor-only material registry asset. Populated on first editor boot; used
        /// by <see cref="Streaming.WorldBootstrap"/> to resolve the terrain template +
        /// fallback so they share the user's tweaks too. Null in standalone builds.
        /// </summary>
        public MaterialRegistry Registry { get; private set; }

        public bool TryLoad(out string error)
        {
            error = null;

            if (!BakeManifest.TryRead(CachePaths.Manifest, out var manifest))
            { error = "manifest.bin unreadable"; return false; }
            Manifest = manifest;

            Meshes = ReadAllMeshes(CachePaths.Meshes, out var meshError);
            if (Meshes == null) { error = meshError; return false; }

            MeshNames = Importer.Bake.MeshBakery.ReadNames(CachePaths.MeshNames);

            var texHashes = TextureBakeryReadOrder(CachePaths.TexturesIndex);
            Textures = new Texture2D[texHashes.Length];
            for (int i = 0; i < texHashes.Length; i++)
                Textures[i] = LoadTexture(CachePaths.TextureFile(texHashes[i]), texHashes[i]);

            var matRecords = Importer.Bake.MaterialBakery.ReadAll(CachePaths.Materials);
            var refShader  = Shader.Find("VVardenfell/MwRef");
            if (refShader == null) { error = "VVardenfell/MwRef shader missing"; return false; }

#if UNITY_EDITOR
            // Registry still hosts the terrain template/fallback; ref materials no longer
            // round-trip through it (there are only a handful now, rebuilt each boot).
            Registry = MaterialRegistry.LoadOrCreate();
#endif

            // Group textures by (width, height) into buckets, each with its own native-sized
            // Texture2DArray + mip chain. Rationale: the previous single 256² ARGB32 array
            // upscaled 16² textures 16× (wasted VRAM + bandwidth) and had no mips (every
            // sample hit base mip level regardless of distance, thrashing the texture cache).
            // Per-dim buckets mean natively-sized slices and proper trilinear filtering.
            //
            // Each bucket gets its own RenderMeshArray + Material set so the material's
            // _BaseArray points at the bucket's RT. Entities end up in different chunks
            // (per-bucket RMA = separate shared component value) which is fine because
            // bucket count is small (~5), not per-cell.
            BuildBucketedRefArrays(Textures, matRecords, refShader, out var rts, out var rmas,
                                   out var texBucketInfo, out var fallbackBucketSlice, out var materials);
            WorldResources.RefBaseArrays       = rts;
            WorldResources.RefsRmas            = rmas;
            WorldResources.TexBucketInfo       = texBucketInfo;
            WorldResources.FallbackBucketSlice = fallbackBucketSlice;
            WorldResources.BlendVariantCount   = matRecords.Length;
            Materials = materials;
            Debug.Log($"[VVardenfell] Refs: {rts.Length} dim-buckets × {matRecords.Length} blend variants = {materials.Length} materials");

            if (File.Exists(CachePaths.TerrainLayers))
            {
                TerrainLayers = new TerrainLayers();
                TerrainLayers.Build(CachePaths.TerrainLayers, Textures);
            }

            return true;
        }

        /// <summary>
        /// Group <paramref name="src"/> textures by (width, height), build one Texture2DArray
        /// per unique dimension with a full mip chain, and produce the matching per-bucket
        /// RenderMeshArrays and materials. Each bucket's array also gets a trailing white
        /// slice for refs with <c>SliceIndex == -1</c>.
        /// </summary>
        private void BuildBucketedRefArrays(
            Texture2D[] src,
            VVardenfell.Core.Cache.MaterialRecord[] matRecords,
            Shader refShader,
            out RenderTexture[] rts,
            out RenderMeshArray[] rmas,
            out Unity.Collections.NativeArray<Unity.Mathematics.int2> texBucketInfo,
            out Unity.Mathematics.int2 fallbackBucketSlice,
            out Material[] materials)
        {
            // Group texture indices by (width, height) packed into one int.
            var groups = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
            for (int i = 0; i < src.Length; i++)
            {
                int w = src[i] != null ? src[i].width  : 1;
                int h = src[i] != null ? src[i].height : 1;
                int key = (w << 16) | (h & 0xFFFF);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new System.Collections.Generic.List<int>();
                list.Add(i);
            }
            // Stable bucket order: ascending by dim so bucket 0 is smallest (least wasteful as fallback).
            var keys = new int[groups.Count];
            groups.Keys.CopyTo(keys, 0);
            System.Array.Sort(keys);

            int nBuckets = keys.Length;
            rts  = new RenderTexture[nBuckets];
            rmas = new RenderMeshArray[nBuckets];

            texBucketInfo = new Unity.Collections.NativeArray<Unity.Mathematics.int2>(
                src.Length, Unity.Collections.Allocator.Persistent,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);

            // Solid-white 1×1 reused as fallback slot in every bucket (missing sources + the
            // trailing fallback slice for SliceIndex == -1).
            var white = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false, linear: false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            int fallbackBucket = 0;
            int fallbackSlice  = 0;

            materials = new Material[nBuckets * matRecords.Length];
            var meshes = Meshes;

            for (int b = 0; b < nBuckets; b++)
            {
                int key = keys[b];
                int w = key >> 16;
                int h = key & 0xFFFF;
                var list = groups[key];
                int sliceCount = list.Count + 1; // +1 for per-bucket white fallback

                var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name             = $"VV:RefBaseArray[{w}x{h}][{sliceCount}]",
                    dimension        = TextureDimension.Tex2DArray,
                    volumeDepth      = sliceCount,
                    useMipMap        = true,
                    autoGenerateMips = false, // we call GenerateMips() once after all Blits
                    filterMode       = FilterMode.Trilinear,
                    wrapMode         = TextureWrapMode.Repeat,
                    anisoLevel       = 8,
                };
                rt.Create();

                for (int s = 0; s < list.Count; s++)
                {
                    int globalTex = list[s];
                    var source = src[globalTex] != null ? (Texture)src[globalTex] : white;
                    Graphics.Blit(source, rt, 0, s); // resize/format-convert + write mip 0 of slice s
                    texBucketInfo[globalTex] = new Unity.Mathematics.int2(b, s);
                }
                int whiteSliceInBucket = list.Count;
                Graphics.Blit(white, rt, 0, whiteSliceInBucket);

                // Build the mip chain from the populated mip 0.
                rt.GenerateMips();

                rts[b] = rt;

                // Materials for this bucket: one per blend variant, each bound to this bucket's RT.
                var bucketMats = new Material[matRecords.Length];
                for (int mi = 0; mi < matRecords.Length; mi++)
                {
                    var r = matRecords[mi];
                    var m = new Material(refShader)
                    {
                        name = $"{MaterialNameForFlags(r.Flags, mi)}[b{b}:{w}x{h}]",
                        enableInstancing = true,
                        doubleSidedGI    = true,
                    };
                    m.SetTexture("_BaseArray", rt);
                    ApplyAlpha(m, r.Flags);
                    bucketMats[mi] = m;
                    materials[b * matRecords.Length + mi] = m;
                }
                // RMA per bucket: 3 materials × all meshes. Meshes are shared across RMAs by
                // reference — cheap, since RenderMeshArray holds managed references.
                rmas[b] = new RenderMeshArray(bucketMats, meshes);

                // Bucket 0 hosts the global fallback slice for SliceIndex == -1.
                if (b == fallbackBucket) fallbackSlice = whiteSliceInBucket;
            }

            Object.Destroy(white);
            fallbackBucketSlice = new Unity.Mathematics.int2(fallbackBucket, fallbackSlice);
        }

        private static string MaterialNameForFlags(uint flags, int index)
        {
            bool blend = (flags & CacheFormat.MatFlagAlphaBlend) != 0;
            bool clip  = (flags & CacheFormat.MatFlagAlphaClip) != 0;
            string kind = blend ? "AlphaBlend" : clip ? "AlphaTest" : "Opaque";
            return $"VV:Mat{index}({kind})";
        }

        /// <summary>
        /// Wire the requested NiAlphaProperty-derived surface on a MwRef material:
        /// opaque / alpha-clip / transparent. MwRef uses explicit <c>_SrcBlend</c>/<c>_DstBlend</c>/<c>_ZWrite</c>
        /// material properties driven by <c>[PropertyName] Blend</c> directives in the shader,
        /// plus <c>_ALPHATEST_ON</c> / <c>_SURFACE_TYPE_TRANSPARENT</c> keywords.
        /// </summary>
        private static void ApplyAlpha(Material m, uint flags)
        {
            bool blend = (flags & CacheFormat.MatFlagAlphaBlend) != 0;
            bool clip  = (flags & CacheFormat.MatFlagAlphaClip) != 0;
            byte thr   = CacheFormat.UnpackAlphaThreshold(flags);

            if (blend)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            }

            if (clip)
            {
                m.SetFloat("_Cutoff", thr / 255f);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                m.DisableKeyword("_ALPHATEST_ON");
            }
        }

        private static string[] TextureBakeryReadOrder(string indexPath)
            => Importer.Bake.TextureBakery.ReadIndex(indexPath);

        private static Texture2D LoadTexture(string path, string hashHex)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                return DdsTexture.Load(bytes, hashHex);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VVardenfell] tex {hashHex} load failed: {ex.Message}");
                return null;
            }
        }

        private static Mesh[] ReadAllMeshes(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) { error = "meshes.bin missing"; return null; }
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Importer.Bake.MeshBakery.MagicMesh) { error = "meshes.bin magic mismatch"; return null; }
            uint count = r.ReadUInt32();
            var offsets = new ulong[count];
            for (int i = 0; i < count; i++) offsets[i] = r.ReadUInt64();

            var meshes = new Mesh[count];
            for (int i = 0; i < count; i++)
            {
                fs.Position = (long)offsets[i];
                meshes[i] = ReadOneMesh(r, $"BakedMesh{i}");
            }
            return meshes;
        }

        private static Mesh ReadOneMesh(BinaryReader r, string name)
        {
            int vertexCount = r.ReadInt32();
            int indexCount = r.ReadInt32();
            uint flags = r.ReadUInt32();
            bool hasNormals = (flags & CacheFormat.MeshFlagHasNormals) != 0;
            bool hasUVs = (flags & CacheFormat.MeshFlagHasUVs) != 0;
            bool index32 = (flags & CacheFormat.MeshFlagIndex32) != 0;

            // Bounds (center + extents)
            var bc = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var be = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            var verts = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                verts[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            Vector3[] normals = null;
            if (hasNormals)
            {
                normals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    normals[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }

            Vector2[] uvs = null;
            if (hasUVs)
            {
                uvs = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    uvs[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            }

            var indices = new int[indexCount];
            if (index32) for (int i = 0; i < indexCount; i++) indices[i] = (int)r.ReadUInt32();
            else          for (int i = 0; i < indexCount; i++) indices[i] = r.ReadUInt16();

            var mesh = new Mesh { name = name };
            mesh.indexFormat = index32 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            if (normals != null) mesh.SetNormals(normals);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            if (normals == null) mesh.RecalculateNormals();
            mesh.bounds = new Bounds(bc, be * 2f);
            return mesh;
        }
    }
}
