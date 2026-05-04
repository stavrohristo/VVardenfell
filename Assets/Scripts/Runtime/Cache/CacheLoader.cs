using System;
using System.Collections;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Dds;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Pathfinding;
using Debug = UnityEngine.Debug;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Cache
{
    /// <summary>
    /// Reads the baked cache and produces the runtime-side Unity resources:
    /// Mesh[], Texture2D[], Material[]. Runs once at boot.
    /// </summary>
    public sealed class CacheLoader
    {
        const uint SupportedMeshPayloadFlags =
            CacheFormat.MeshFlagHasNormals |
            CacheFormat.MeshFlagHasUVs |
            CacheFormat.MeshFlagIndex32;
        const int MeshBatchSize = 256;
        const int TextureBatchSize = 128;
        const int BucketYieldStride = 512;

        static readonly ProfilerMarker k_Load = new("VV.CacheLoader.Load");
        static readonly ProfilerMarker k_Manifest = new("VV.CacheLoader.Manifest");
        static readonly ProfilerMarker k_Meshes = new("VV.CacheLoader.Meshes");
        static readonly ProfilerMarker k_MeshPayloadRead = new("VV.CacheLoader.MeshPayloadRead");
        static readonly ProfilerMarker k_MeshUpload = new("VV.CacheLoader.MeshUpload");
        static readonly ProfilerMarker k_Textures = new("VV.CacheLoader.Textures");
        static readonly ProfilerMarker k_RefBuckets = new("VV.CacheLoader.RefBuckets");
        static readonly ProfilerMarker k_TerrainLayers = new("VV.CacheLoader.TerrainLayers");
        static readonly ProfilerMarker k_GameplayContent = new("VV.CacheLoader.GameplayContent");

        public BakeManifest Manifest { get; private set; }
        public Mesh[] Meshes { get; private set; }
        public string[] MeshNames { get; private set; }
        public Material[] Materials { get; private set; }
        public ModelPrefabCatalogData ModelPrefabCatalog { get; private set; }
        public MorrowindVfxCatalogData VfxCatalog { get; private set; }
        public ActorAnimationCatalogData ActorAnimationCatalog { get; private set; }
        public Texture2D[] Textures { get; private set; }
        public TerrainLayers TerrainLayers { get; private set; }
        public MaterialRegistry Registry { get; private set; }
        public RuntimeContentDatabase ContentDatabase { get; private set; }

        Dictionary<string, int> _textureIndexByPath;
        Dictionary<ulong, int> _actorVisualRecipesByActorAndView;
        Dictionary<ulong, int> _equipmentVisualsByItemRigViewAndVariant;

        public bool TryLoad(out string error)
        {
            try
            {
                using var _ = k_Load.Auto();
                RuntimeCoroutinePump.RunToCompletion(LoadIncremental(new RuntimeLoadProgress()));
                error = null;
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public IEnumerator LoadIncremental(RuntimeLoadProgress progress)
        {
            Task<ActorAnimationCatalogData> actorAnimationCatalogTask = null;
            Task<MeshPayload[]> meshPayloadTask = null;
            Task<TexturePayload[]> texturePayloadTask = null;
            TexturePayloadReadState texturePayloadState = null;
            string[] textureHashes = null;

            progress?.BeginStage("Manifest + metadata", "Reading manifest", 1);
            k_Manifest.Begin();
            try
            {
                if (!BakeManifest.TryRead(CachePaths.Manifest, out var manifest))
                    throw new InvalidDataException("manifest.bin unreadable");
                if (manifest.FormatVersion != CacheFormat.FormatVersion)
                    throw new InvalidDataException($"manifest.bin format mismatch (found {manifest.FormatVersion}, expected {CacheFormat.FormatVersion}); rebake required.");
                Manifest = manifest;
                MeshNames = Importer.Bake.MeshBakery.ReadNames(CachePaths.MeshNames);
                meshPayloadTask = Task.Run(ReadMeshPayloadsOnWorker);
                textureHashes = TextureBakeryReadOrder(CachePaths.TexturesIndex);
                BuildTexturePathLookup(textureHashes);
                texturePayloadState = new TexturePayloadReadState(textureHashes.Length);
                texturePayloadTask = StartLongRunningTask(() => ReadTexturePayloadsOnWorker(textureHashes, texturePayloadState));
                if (!ModelPrefabFile.TryRead(CachePaths.ModelPrefabs, out var modelPrefabCatalog) || modelPrefabCatalog?.Records == null)
                    throw new InvalidDataException("model_prefabs.bin unreadable");
                ModelPrefabCatalog = modelPrefabCatalog;
                if (!MorrowindVfxFile.TryRead(CachePaths.VfxEffects, out var vfxCatalog) || vfxCatalog?.Effects == null)
                    throw new InvalidDataException("vfx_effects.bin unreadable; rebake required.");
                VfxCatalog = vfxCatalog;
                actorAnimationCatalogTask = Task.Run(ReadActorAnimationCatalogOnWorker);
            }
            finally
            {
                k_Manifest.End();
            }
            progress?.Report("Manifest ready", 1, 1);
            progress?.CompleteStage();
            yield return null;

            progress?.BeginStage("Gameplay content", "Loading gameplay content database", 1);
            k_GameplayContent.Begin();
            try
            {
                ContentDatabase = RuntimeContentDatabase.LoadFromCache();
                if (WorldResources.PathGridNavigation.IsCreated)
                    WorldResources.PathGridNavigation.Dispose();
                WorldResources.PathGridNavigation = PathGridNavigationWorld.Create(ContentDatabase);
            }
            finally
            {
                k_GameplayContent.End();
            }
            progress?.Report(
                $"Gameplay content ready: {ContentDatabase.ActorCount} actors, {ContentDatabase.LightCount} lights, {ContentDatabase.SoundCount} sounds, {ContentDatabase.PathGridNavigationNodeCount} path nodes",
                1,
                1);
            progress?.CompleteStage();
            yield return null;

            progress?.BeginStage("Texture decode/load", "Loading textures", 0);
            yield return LoadTexturesIncremental(progress, textureHashes, texturePayloadTask, texturePayloadState);

            progress?.BeginStage("Mesh deserialization", "Preparing meshes", 0);
            yield return ReadMeshesIncremental(progress, meshPayloadTask);

            progress?.BeginStage("Ref materials", "Building reference texture buckets", 0);
            yield return BuildReferenceMaterialBucketsIncremental(progress);

            progress?.BeginStage("Terrain layer arrays", "Preparing terrain layers", 1);
            if (File.Exists(CachePaths.TerrainLayers))
            {
                k_TerrainLayers.Begin();
                try
                {
                    TerrainLayers = new TerrainLayers();
                    yield return TerrainLayers.BuildIncremental(CachePaths.TerrainLayers, Textures, progress);
                }
                finally
                {
                    k_TerrainLayers.End();
                }
            }
            else
            {
                progress?.Report("terrain_layers.bin missing", 1, 1);
                progress?.CompleteStage();
                yield return null;
            }

            progress?.BeginStage("Actor animation metadata", "Waiting for actor animation catalog", 1);
            while (actorAnimationCatalogTask != null && !actorAnimationCatalogTask.IsCompleted)
            {
                progress?.Report("Waiting for actor animation catalog", 0, 1);
                yield return null;
            }

            var actorAnimationCatalog = actorAnimationCatalogTask?.GetAwaiter().GetResult();
            if (actorAnimationCatalog == null)
                Debug.LogWarning($"[VVardenfell] actor animation cache '{CachePaths.ActorAnimations}' is missing or version-mismatched; actor presentations will not render until actor_animations.bin is rebaked.");
            ActorAnimationCatalog = actorAnimationCatalog ?? new ActorAnimationCatalogData();
            BuildActorVisualLookup();
            progress?.Report("Actor animation catalog ready", 1, 1);
            progress?.CompleteStage("Actor animation catalog ready");
            yield return null;
        }

        static ActorAnimationCatalogData ReadActorAnimationCatalogOnWorker()
        {
            bool loaded = ActorAnimationFile.TryRead(CachePaths.ActorAnimations, out var actorAnimationCatalog);
            return loaded ? actorAnimationCatalog : null;
        }

        public bool TryGetActorVisualRecipe(ContentId actorContentId, bool firstPerson, out ActorVisualRecipeDef recipe)
        {
            recipe = null;
            if (_actorVisualRecipesByActorAndView == null)
                return false;

            ulong key = BuildActorVisualRecipeKey(actorContentId.Value, firstPerson);
            if (!_actorVisualRecipesByActorAndView.TryGetValue(key, out int index))
                return false;

            var recipes = ActorAnimationCatalog?.ActorVisualRecipes ?? Array.Empty<ActorVisualRecipeDef>();
            if ((uint)index >= (uint)recipes.Length)
                return false;

            recipe = recipes[index];
            return recipe != null && recipe.RigFamilyIndex >= 0;
        }

        public bool TryGetEquipmentVisual(
            ContentId itemContentId,
            int rigFamilyIndex,
            bool firstPerson,
            ActorVisualBodyVariant bodyVariant,
            out ActorEquipmentVisualDef visual)
        {
            visual = null;
            if (_equipmentVisualsByItemRigViewAndVariant == null)
                return false;

            ulong key = BuildEquipmentVisualKey(itemContentId.Value, rigFamilyIndex, firstPerson, bodyVariant);
            if (!_equipmentVisualsByItemRigViewAndVariant.TryGetValue(key, out int index))
                return false;

            var values = ActorAnimationCatalog?.EquipmentVisuals ?? Array.Empty<ActorEquipmentVisualDef>();
            if ((uint)index >= (uint)values.Length)
                return false;

            visual = values[index];
            return visual != null;
        }

        public bool TryGetTextureByPath(string sourcePath, out Texture2D texture)
        {
            texture = null;
            if (!TryGetTextureIndexByPath(sourcePath, out int index))
                return false;

            if ((uint)index >= (uint)(Textures?.Length ?? 0))
                return false;

            texture = Textures[index];
            return texture != null;
        }

        public bool TryGetTextureIndexByPath(string sourcePath, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(sourcePath) || _textureIndexByPath == null)
                return false;

            string normalized = NormalizeTexturePath(sourcePath);
            return _textureIndexByPath.TryGetValue(normalized, out index)
                   || _textureIndexByPath.TryGetValue(ChangeExtension(normalized, ".dds"), out index);
        }

        void BuildActorVisualLookup()
        {
            var recipes = ActorAnimationCatalog?.ActorVisualRecipes ?? Array.Empty<ActorVisualRecipeDef>();
            _actorVisualRecipesByActorAndView = new Dictionary<ulong, int>(recipes.Length);
            for (int i = 0; i < recipes.Length; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || recipe.ActorContentId.Value == 0UL)
                    continue;

                ulong key = BuildActorVisualRecipeKey(recipe.ActorContentId.Value, recipe.FirstPerson != 0);
                if (!_actorVisualRecipesByActorAndView.ContainsKey(key))
                    _actorVisualRecipesByActorAndView[key] = i;
            }

            var visuals = ActorAnimationCatalog?.EquipmentVisuals ?? Array.Empty<ActorEquipmentVisualDef>();
            _equipmentVisualsByItemRigViewAndVariant = new Dictionary<ulong, int>(visuals.Length);
            for (int i = 0; i < visuals.Length; i++)
            {
                var visual = visuals[i];
                if (visual == null || visual.ItemContentId.Value == 0UL || visual.RigFamilyIndex < 0)
                    continue;

                ulong key = BuildEquipmentVisualKey(
                    visual.ItemContentId.Value,
                    visual.RigFamilyIndex,
                    visual.FirstPerson != 0,
                    visual.BodyVariant);
                if (!_equipmentVisualsByItemRigViewAndVariant.ContainsKey(key))
                    _equipmentVisualsByItemRigViewAndVariant[key] = i;
            }
        }

        static ulong BuildActorVisualRecipeKey(ulong contentId, bool firstPerson)
            => contentId ^ (firstPerson ? 0x9E3779B97F4A7C15UL : 0UL);

        static ulong BuildEquipmentVisualKey(ulong contentId, int rigFamilyIndex, bool firstPerson, ActorVisualBodyVariant bodyVariant)
        {
            ulong hash = 14695981039346656037UL;
            Mix(ref hash, contentId);
            Mix(ref hash, (uint)rigFamilyIndex);
            Mix(ref hash, firstPerson ? 1UL : 0UL);
            Mix(ref hash, (byte)bodyVariant);
            return hash;
        }

        static void Mix(ref ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        private IEnumerator ReadMeshesIncremental(
            RuntimeLoadProgress progress,
            Task<MeshPayload[]> meshPayloadTask)
        {
            if (meshPayloadTask == null)
                throw new InvalidDataException("mesh payload task was not scheduled");

            while (!meshPayloadTask.IsCompleted)
            {
                progress?.Report("Waiting for mesh payload read", 0, 1);
                yield return null;
            }

            var payloads = meshPayloadTask.GetAwaiter().GetResult();
            Meshes = new Mesh[payloads.Length];
            progress?.Report("Uploading meshes", 0, payloads.Length);
            for (int i = 0; i < payloads.Length; i++)
            {
                k_Meshes.Begin();
                try
                {
                    Meshes[i] = UploadMeshPayload(payloads[i]);
                }
                finally
                {
                    k_Meshes.End();
                    payloads[i].Dispose();
                }

                int completed = i + 1;
                if (completed == payloads.Length || (completed % MeshBatchSize) == 0)
                {
                    progress?.Report($"Uploading meshes {completed}/{payloads.Length}", completed, payloads.Length);
                    yield return null;
                }
            }

            progress?.CompleteStage("Meshes ready");
        }

        private IEnumerator LoadTexturesIncremental(
            RuntimeLoadProgress progress,
            string[] texHashes,
            Task<TexturePayload[]> texturePayloadTask,
            TexturePayloadReadState texturePayloadState)
        {
            if (texHashes == null)
                throw new InvalidDataException("texture hash table was not loaded");
            if (texturePayloadTask == null)
                throw new InvalidDataException("texture payload task was not scheduled");

            while (!texturePayloadTask.IsCompleted)
            {
                int completed = texturePayloadState != null ? Volatile.Read(ref texturePayloadState.Completed) : 0;
                int total = texturePayloadState != null ? texturePayloadState.Total : texHashes.Length;
                progress?.Report($"Reading texture payloads {completed}/{total}", completed, total);
                yield return null;
            }

            var payloads = texturePayloadTask.GetAwaiter().GetResult();
            if (payloads.Length != texHashes.Length)
                throw new InvalidDataException($"texture payload count mismatch ({payloads.Length} != {texHashes.Length}).");

            Textures = new Texture2D[texHashes.Length];
            progress?.Report("Loading textures", 0, texHashes.Length);

            for (int i = 0; i < texHashes.Length; i++)
            {
                k_Textures.Begin();
                try
                {
                    Textures[i] = LoadTexture(payloads[i]);
                }
                finally
                {
                    k_Textures.End();
                }

                int completed = i + 1;
                if (completed == texHashes.Length || (completed % TextureBatchSize) == 0)
                {
                    progress?.Report($"Loading textures {completed}/{texHashes.Length}", completed, texHashes.Length);
                    yield return null;
                }
            }

            progress?.CompleteStage("Textures ready");
        }

        void BuildTexturePathLookup(string[] texHashes)
        {
            _textureIndexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var indexByHash = new Dictionary<string, int>(texHashes?.Length ?? 0, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; texHashes != null && i < texHashes.Length; i++)
            {
                string hash = texHashes[i];
                if (!string.IsNullOrEmpty(hash) && !indexByHash.ContainsKey(hash))
                    indexByHash[hash] = i;
            }

            var catalog = Importer.Bake.TextureBakery.ReadCatalog(CachePaths.TextureCatalog);
            for (int i = 0; i < catalog.Length; i++)
            {
                if (!indexByHash.TryGetValue(catalog[i].HashHex, out int textureIndex))
                    continue;

                RegisterTexturePath(catalog[i].ResolvedPath, textureIndex);
            }
        }

        void RegisterTexturePath(string path, int textureIndex)
        {
            string normalized = NormalizeTexturePath(path);
            if (string.IsNullOrEmpty(normalized))
                return;

            _textureIndexByPath[normalized] = textureIndex;
            _textureIndexByPath[normalized.Replace('\\', '/')] = textureIndex;
            RegisterTextureAlias(ChangeExtension(normalized, ".tga"), textureIndex);
            RegisterTextureAlias(ChangeExtension(normalized, ".dds"), textureIndex);

            int slash = normalized.LastIndexOf('\\');
            if (slash >= 0 && slash + 1 < normalized.Length)
                _textureIndexByPath[normalized.Substring(slash + 1)] = textureIndex;
        }

        void RegisterTextureAlias(string normalizedPath, int textureIndex)
        {
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            _textureIndexByPath[normalizedPath] = textureIndex;
            _textureIndexByPath[normalizedPath.Replace('\\', '/')] = textureIndex;

            int slash = normalizedPath.LastIndexOf('\\');
            if (slash >= 0 && slash + 1 < normalizedPath.Length)
                _textureIndexByPath[normalizedPath.Substring(slash + 1)] = textureIndex;
        }

        static string NormalizeTexturePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string p = path.Trim().ToLowerInvariant().Replace('/', '\\');
            while (p.Contains("\\\\", StringComparison.Ordinal))
                p = p.Replace("\\\\", "\\");
            if (p.StartsWith("\\", StringComparison.Ordinal))
                p = p.Substring(1);
            if (!p.StartsWith("textures\\", StringComparison.Ordinal) && !p.StartsWith("bookart\\", StringComparison.Ordinal))
                p = "textures\\" + p;

            return p;
        }

        static string ChangeExtension(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(0, dot) + extension : path + extension;
        }

        private IEnumerator BuildReferenceMaterialBucketsIncremental(RuntimeLoadProgress progress)
        {
            var matRecords = Importer.Bake.MaterialBakery.ReadAll(CachePaths.Materials);
            var refShader = Shader.Find("VVardenfell/MwRef");
            if (refShader == null)
                throw new InvalidDataException("VVardenfell/MwRef shader missing");

#if UNITY_EDITOR
            Registry = MaterialRegistry.LoadOrCreate();
#endif

            const int fallbackBucketKey = (1 << 16) | 1; // 0x00010001
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < Textures.Length; i++)
            {
                int w = Textures[i] != null ? Textures[i].width : 1;
                int h = Textures[i] != null ? Textures[i].height : 1;
                int key = (w << 16) | (h & 0xFFFF);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<int>();
                list.Add(i);
            }

            if (groups.Count == 0)
                groups[(1 << 16) | 1] = new List<int>();

            if (!groups.ContainsKey(fallbackBucketKey))
                groups[fallbackBucketKey] = new List<int>();

            var keys = new int[groups.Count];
            groups.Keys.CopyTo(keys, 0);
            System.Array.Sort(keys);
            int fallbackBucket = -1;

            int totalTextureOps = 0;
            foreach (var key in keys)
                totalTextureOps += groups[key].Count + 1;
            int totalMaterialOps = keys.Length * matRecords.Length;
            int totalOps = System.Math.Max(1, totalTextureOps + totalMaterialOps);
            progress?.Report("Grouping textures into buckets", 0, totalOps);

            RenderTexture[] rts = new RenderTexture[keys.Length];
            Material[] materials = new Material[keys.Length * matRecords.Length];
            var texBucketInfo = new NativeArray<int2>(
                Textures.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            var white = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false, linear: false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            int completed = 0;
            int yieldedAt = 0;
            int fallbackSlice = 0;

            bool success = false;
            try
            {
                for (int b = 0; b < keys.Length; b++)
                {
                    int key = keys[b];
                    if (key == fallbackBucketKey)
                        fallbackBucket = b;

                    int w = key >> 16;
                    int h = key & 0xFFFF;
                    var list = groups[key];
                    int sliceCount = list.Count + 1;

                    k_RefBuckets.Begin();
                    try
                    {
                        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                        {
                            name = $"VV:RefBaseArray[{w}x{h}][{sliceCount}]",
                            dimension = TextureDimension.Tex2DArray,
                            volumeDepth = sliceCount,
                            useMipMap = true,
                            autoGenerateMips = false,
                            filterMode = FilterMode.Trilinear,
                            wrapMode = TextureWrapMode.Repeat,
                            anisoLevel = 8,
                        };
                        rt.Create();
                        rts[b] = rt;

                        for (int s = 0; s < list.Count; s++)
                        {
                            int globalTex = list[s];
                            var source = Textures[globalTex] != null ? (Texture)Textures[globalTex] : white;
                            Graphics.Blit(source, rt, 0, s);
                            texBucketInfo[globalTex] = new int2(b, s);
                            completed++;

                            if ((completed - yieldedAt) >= BucketYieldStride)
                            {
                                progress?.Report($"Uploading ref textures {completed}/{totalOps}", completed, totalOps);
                                yieldedAt = completed;
                                yield return null;
                            }
                        }

                        int whiteSliceInBucket = list.Count;
                        Graphics.Blit(white, rt, 0, whiteSliceInBucket);
                        rt.GenerateMips();
                        completed++;

                        for (int mi = 0; mi < matRecords.Length; mi++)
                        {
                            var record = matRecords[mi];
                            var material = new Material(refShader)
                            {
                                name = $"{MaterialNameForFlags(record.Flags, mi)}[b{b}:{w}x{h}]",
                                enableInstancing = true,
                                doubleSidedGI = true,
                            };
                            material.SetTexture("_BaseArray", rt);
                            ApplyAlpha(material, record.Flags);
                            materials[b * matRecords.Length + mi] = material;
                            completed++;
                        }

                        if (b == fallbackBucket)
                            fallbackSlice = whiteSliceInBucket;
                    }
                    finally
                    {
                        k_RefBuckets.End();
                    }

                    if ((completed - yieldedAt) >= BucketYieldStride || b == keys.Length - 1)
                    {
                        progress?.Report($"Built ref bucket {b + 1}/{keys.Length}", completed, totalOps);
                        yieldedAt = completed;
                        yield return null;
                    }
                }

                if (fallbackBucket < 0)
                    throw new InvalidDataException($"runtime texture bucket map missing fallback key 0x{fallbackBucketKey:X8}.");

                WorldResources.RefBaseArrays = rts;
                WorldResources.TexBucketInfo = texBucketInfo;
                WorldResources.FallbackBucketSlice = new int2(fallbackBucket, fallbackSlice);
                WorldResources.BlendVariantCount = matRecords.Length;
                Materials = materials;

                progress?.CompleteStage("Reference materials ready");
                success = true;
            }
            finally
            {
                if (!success)
                {
                    for (int i = 0; i < rts.Length; i++)
                    {
                        var rt = rts[i];
                        if (rt != null)
                        {
                            rt.Release();
                            UnityEngine.Object.Destroy(rt);
                        }
                    }

                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null)
                            UnityEngine.Object.Destroy(materials[i]);
                    }

                    if (texBucketInfo.IsCreated)
                        texBucketInfo.Dispose();
                }

                UnityEngine.Object.Destroy(white);
            }

        }

        private static string MaterialNameForFlags(uint flags, int index)
        {
            bool blend = (flags & CacheFormat.MatFlagAlphaBlend) != 0;
            bool clip = (flags & CacheFormat.MatFlagAlphaClip) != 0;
            string kind = blend ? "AlphaBlend" : clip ? "AlphaTest" : "Opaque";
            return $"VV:Mat{index}({kind})";
        }

        private static void ApplyAlpha(Material m, uint flags)
        {
            bool blend = (flags & CacheFormat.MatFlagAlphaBlend) != 0;
            bool clip = (flags & CacheFormat.MatFlagAlphaClip) != 0;
            byte thr = CacheFormat.UnpackAlphaThreshold(flags);

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

        static Task<T> StartLongRunningTask<T>(Func<T> action)
            => Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

        static TexturePayload[] ReadTexturePayloadsOnWorker(
            string[] texHashes,
            TexturePayloadReadState state)
        {
            var payloads = new TexturePayload[texHashes?.Length ?? 0];
            if (state != null)
                Volatile.Write(ref state.Total, payloads.Length);
            for (int i = 0; i < payloads.Length; i++)
            {
                string hashHex = texHashes[i];
                string path = CachePaths.TextureFile(hashHex);
                if (!File.Exists(path))
                {
                    payloads[i] = new TexturePayload
                    {
                        HashHex = hashHex,
                        Error = "file missing",
                    };
                    if (state != null)
                        Volatile.Write(ref state.Completed, i + 1);
                    continue;
                }

                try
                {
                    payloads[i] = new TexturePayload
                    {
                        HashHex = hashHex,
                        Bytes = File.ReadAllBytes(path),
                    };
                }
                catch (System.Exception ex)
                {
                    payloads[i] = new TexturePayload
                    {
                        HashHex = hashHex,
                        Error = ex.Message,
                    };
                }

                if (state != null)
                    Volatile.Write(ref state.Completed, i + 1);
            }

            return payloads;
        }

        private static Texture2D LoadTexture(TexturePayload payload)
        {
            if (payload == null)
                return null;
            if (!string.IsNullOrEmpty(payload.Error))
            {
                Debug.LogWarning($"[VVardenfell] tex {payload.HashHex} load failed: {payload.Error}");
                return null;
            }
            if (payload.Bytes == null)
                return null;

            try
            {
                return DdsTexture.Load(payload.Bytes, payload.HashHex);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VVardenfell] tex {payload.HashHex} load failed: {ex.Message}");
                return null;
            }
        }

        sealed class TexturePayload
        {
            public string HashHex;
            public byte[] Bytes;
            public string Error;
        }

        sealed class TexturePayloadReadState
        {
            public TexturePayloadReadState(int total)
            {
                Total = total;
            }

            public int Total;
            public int Completed;
        }

        static MeshPayload[] ReadMeshPayloadsOnWorker()
        {
            if (!File.Exists(CachePaths.Meshes))
                throw new InvalidDataException("meshes.bin missing");

            using var fs = File.OpenRead(CachePaths.Meshes);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Importer.Bake.MeshBakery.MagicMesh)
                throw new InvalidDataException("meshes.bin magic mismatch");

            uint count = r.ReadUInt32();
            if (count > int.MaxValue)
                throw new InvalidDataException($"meshes.bin mesh count {count} exceeds supported limits.");

            var offsets = new ulong[count];
            for (int i = 0; i < count; i++)
                offsets[i] = r.ReadUInt64();

            var payloads = new MeshPayload[count];
            try
            {
                for (int i = 0; i < payloads.Length; i++)
                {
                    fs.Position = (long)offsets[i];
                    payloads[i] = ReadOneMeshPayload(r, $"BakedMesh{i}");
                }
            }
            catch
            {
                for (int i = 0; i < payloads.Length; i++)
                    payloads[i]?.Dispose();
                throw;
            }

            return payloads;
        }

        private static MeshPayload ReadOneMeshPayload(BinaryReader r, string name)
        {
            uint vertexCount = r.ReadUInt32();
            uint indexCount = r.ReadUInt32();
            uint flags = r.ReadUInt32();
            if ((flags & ~SupportedMeshPayloadFlags) != 0)
                throw new InvalidDataException($"Mesh '{name}' has unsupported flags 0x{flags:X8}.");

            bool hasNormals = (flags & CacheFormat.MeshFlagHasNormals) != 0;
            bool hasUVs = (flags & CacheFormat.MeshFlagHasUVs) != 0;
            bool index32 = (flags & CacheFormat.MeshFlagIndex32) != 0;
            if (!hasNormals)
                throw new InvalidDataException($"Mesh '{name}' is missing baked normals.");
            if (vertexCount == 0 || indexCount == 0)
                throw new InvalidDataException($"Mesh '{name}' has invalid empty buffers.");

            var bc = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var be = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            uint vertexDataBytes = r.ReadUInt32();
            uint indexDataBytes = r.ReadUInt32();

            int vertexStride = GetVertexStride(hasNormals, hasUVs);
            long expectedVertexBytes = (long)vertexCount * vertexStride;
            int indexStride = index32 ? 4 : 2;
            long expectedIndexBytes = (long)indexCount * indexStride;
            if (vertexDataBytes != expectedVertexBytes)
            {
                throw new InvalidDataException(
                    $"Mesh '{name}' vertex bytes mismatch. Expected {expectedVertexBytes}, got {vertexDataBytes}.");
            }
            if (indexDataBytes != expectedIndexBytes)
            {
                throw new InvalidDataException(
                    $"Mesh '{name}' index bytes mismatch. Expected {expectedIndexBytes}, got {indexDataBytes}.");
            }
            if (vertexDataBytes > int.MaxValue || indexDataBytes > int.MaxValue)
                throw new InvalidDataException($"Mesh '{name}' buffer sizes exceed supported limits.");

            byte[] vertexBytes = null;
            byte[] indexBytes = null;
            try
            {
                using (k_MeshPayloadRead.Auto())
                {
                    vertexBytes = ArrayPool<byte>.Shared.Rent((int)vertexDataBytes);
                    indexBytes = ArrayPool<byte>.Shared.Rent((int)indexDataBytes);
                    ReadExactly(r, vertexBytes, (int)vertexDataBytes);
                    ReadExactly(r, indexBytes, (int)indexDataBytes);
                    return new MeshPayload
                    {
                        Name = name,
                        VertexCount = (int)vertexCount,
                        IndexCount = (int)indexCount,
                        HasNormals = hasNormals,
                        HasUvs = hasUVs,
                        Index32 = index32,
                        BoundsCenter = bc,
                        BoundsExtents = be,
                        VertexBytes = vertexBytes,
                        VertexByteCount = (int)vertexDataBytes,
                        IndexBytes = indexBytes,
                        IndexByteCount = (int)indexDataBytes,
                    };
                }
            }
            catch (EndOfStreamException ex)
            {
                if (vertexBytes != null)
                    ArrayPool<byte>.Shared.Return(vertexBytes);
                if (indexBytes != null)
                    ArrayPool<byte>.Shared.Return(indexBytes);
                throw new InvalidDataException($"Mesh '{name}' payload is truncated.", ex);
            }
            catch
            {
                if (vertexBytes != null)
                    ArrayPool<byte>.Shared.Return(vertexBytes);
                if (indexBytes != null)
                    ArrayPool<byte>.Shared.Return(indexBytes);
                throw;
            }
        }

        private static Mesh UploadMeshPayload(MeshPayload payload)
        {
            if (payload == null)
                throw new InvalidDataException("Mesh payload is null.");

            try
            {
                using (k_MeshUpload.Auto())
                {
                    var mesh = new Mesh
                    {
                        name = payload.Name,
                        indexFormat = payload.Index32 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    };

                    var meshDataArray = Mesh.AllocateWritableMeshData(1);
                    try
                    {
                        var meshData = meshDataArray[0];
                        var descriptors = GetVertexLayout(payload.HasNormals, payload.HasUvs);
                        meshData.SetVertexBufferParams(payload.VertexCount, descriptors);
                        meshData.SetIndexBufferParams(payload.IndexCount, payload.Index32 ? IndexFormat.UInt32 : IndexFormat.UInt16);

                        var vertexData = meshData.GetVertexData<byte>(0);
                        var indexData = meshData.GetIndexData<byte>();
                        if (vertexData.Length != payload.VertexByteCount || indexData.Length != payload.IndexByteCount)
                        {
                            throw new InvalidDataException(
                                $"Mesh '{payload.Name}' writable buffer size mismatch ({vertexData.Length}/{indexData.Length}).");
                        }

                        NativeArray<byte>.Copy(payload.VertexBytes, 0, vertexData, 0, payload.VertexByteCount);
                        NativeArray<byte>.Copy(payload.IndexBytes, 0, indexData, 0, payload.IndexByteCount);

                        meshData.subMeshCount = 1;
                        meshData.SetSubMesh(
                            0,
                            new SubMeshDescriptor(0, payload.IndexCount, MeshTopology.Triangles),
                            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                        Mesh.ApplyAndDisposeWritableMeshData(
                            meshDataArray,
                            mesh,
                            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                        mesh.bounds = new Bounds(payload.BoundsCenter, payload.BoundsExtents * 2f);
                        mesh.UploadMeshData(true);
                        return mesh;
                    }
                    catch
                    {
                        meshDataArray.Dispose();
                        throw;
                    }
                }
            }
            finally
            {
                payload.Dispose();
            }
        }

        sealed class MeshPayload
        {
            public string Name;
            public int VertexCount;
            public int IndexCount;
            public bool HasNormals;
            public bool HasUvs;
            public bool Index32;
            public Vector3 BoundsCenter;
            public Vector3 BoundsExtents;
            public byte[] VertexBytes;
            public int VertexByteCount;
            public byte[] IndexBytes;
            public int IndexByteCount;

            public void Dispose()
            {
                if (VertexBytes != null)
                {
                    ArrayPool<byte>.Shared.Return(VertexBytes);
                    VertexBytes = null;
                }

                if (IndexBytes != null)
                {
                    ArrayPool<byte>.Shared.Return(IndexBytes);
                    IndexBytes = null;
                }
            }
        }

        private static void ReadExactly(BinaryReader r, byte[] buffer, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = r.Read(buffer, offset, length - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
        }

        private static VertexAttributeDescriptor[] GetVertexLayout(bool hasNormals, bool hasUVs)
        {
            int count = 1 + (hasNormals ? 1 : 0) + (hasUVs ? 1 : 0);
            var descriptors = new VertexAttributeDescriptor[count];
            int i = 0;
            descriptors[i++] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            if (hasNormals)
                descriptors[i++] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0);
            if (hasUVs)
                descriptors[i] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0);
            return descriptors;
        }

        private static int GetVertexStride(bool hasNormals, bool hasUVs)
        {
            int stride = 12;
            if (hasNormals)
                stride += 12;
            if (hasUVs)
                stride += 8;
            return stride;
        }
    }
}
