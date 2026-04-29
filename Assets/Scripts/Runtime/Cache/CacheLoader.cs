using System;
using System.Collections;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        public RenderShardCatalogData RenderShardCatalog { get; private set; }
        public ModelPrefabCatalogData ModelPrefabCatalog { get; private set; }
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
            var totalSw = Stopwatch.StartNew();

            progress?.BeginStage("Manifest + metadata", "Reading manifest", 1);
            var manifestSw = Stopwatch.StartNew();
            long metadataLastMs = 0;
            k_Manifest.Begin();
            try
            {
                if (!BakeManifest.TryRead(CachePaths.Manifest, out var manifest))
                    throw new InvalidDataException("manifest.bin unreadable");
                if (manifest.FormatVersion != CacheFormat.FormatVersion)
                    throw new InvalidDataException($"manifest.bin format mismatch (found {manifest.FormatVersion}, expected {CacheFormat.FormatVersion}); rebake required.");
                Manifest = manifest;
                LogMetadataTiming("manifest", manifestSw, ref metadataLastMs);
                MeshNames = Importer.Bake.MeshBakery.ReadNames(CachePaths.MeshNames);
                LogMetadataTiming("mesh names", manifestSw, ref metadataLastMs);
                if (!RenderShardFile.TryRead(CachePaths.RenderShards, out var renderShardCatalog) || renderShardCatalog?.Records == null)
                    throw new InvalidDataException("render_shards.bin unreadable");
                RenderShardCatalog = renderShardCatalog;
                LogMetadataTiming("render shards", manifestSw, ref metadataLastMs);
                if (!ModelPrefabFile.TryRead(CachePaths.ModelPrefabs, out var modelPrefabCatalog) || modelPrefabCatalog?.Records == null)
                    throw new InvalidDataException("model_prefabs.bin unreadable");
                ModelPrefabCatalog = modelPrefabCatalog;
                LogMetadataTiming("model prefabs", manifestSw, ref metadataLastMs);
                if (!ActorAnimationFile.TryRead(CachePaths.ActorAnimations, out var actorAnimationCatalog))
                    Debug.LogWarning($"[VVardenfell] actor animation cache '{CachePaths.ActorAnimations}' is missing or version-mismatched; actor presentations will not render until actor_animations.bin is rebaked.");
                ActorAnimationCatalog = actorAnimationCatalog ?? new ActorAnimationCatalogData();
                LogMetadataTiming("actor animation catalog", manifestSw, ref metadataLastMs);
                BuildActorVisualLookup();
                LogMetadataTiming("actor visual lookup", manifestSw, ref metadataLastMs);
            }
            finally
            {
                k_Manifest.End();
            }
            manifestSw.Stop();
            progress?.Report("Manifest ready", 1, 1);
            progress?.CompleteStage();
            yield return null;

            progress?.BeginStage("Gameplay content", "Loading gameplay content database", 1);
            var gameplaySw = Stopwatch.StartNew();
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
            gameplaySw.Stop();
            progress?.Report(
                $"Gameplay content ready: {ContentDatabase.ActorCount} actors, {ContentDatabase.LightCount} lights, {ContentDatabase.SoundCount} sounds, {ContentDatabase.PathGridNavigationNodeCount} path nodes",
                1,
                1);
            progress?.CompleteStage();
            yield return null;

            progress?.BeginStage("Mesh deserialization", "Reading meshes", 0);
            yield return ReadMeshesIncremental(progress);

            progress?.BeginStage("Texture decode/load", "Loading textures", 0);
            yield return LoadTexturesIncremental(progress);

            progress?.BeginStage("Ref shards + materials", "Building reference shards", 0);
            yield return BuildBucketedRefArraysIncremental(progress);

            progress?.BeginStage("Terrain layer arrays", "Preparing terrain layers", 1);
            var terrainSw = Stopwatch.StartNew();
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
            terrainSw.Stop();

            totalSw.Stop();
        }

        static void LogMetadataTiming(string segment, Stopwatch stopwatch, ref long lastMs)
        {
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            Debug.Log($"[VVardenfell][BootTiming] detail=ManifestMetadata segment='{segment}' deltaMs={elapsedMs - lastMs} elapsedMs={elapsedMs}");
            lastMs = elapsedMs;
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
            if (string.IsNullOrWhiteSpace(sourcePath) || _textureIndexByPath == null)
                return false;

            if (!_textureIndexByPath.TryGetValue(NormalizeTexturePath(sourcePath), out int index))
                return false;

            if ((uint)index >= (uint)(Textures?.Length ?? 0))
                return false;

            texture = Textures[index];
            return texture != null;
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

        private IEnumerator ReadMeshesIncremental(RuntimeLoadProgress progress)
        {
            var sw = Stopwatch.StartNew();
            if (!File.Exists(CachePaths.Meshes))
                throw new InvalidDataException("meshes.bin missing");

            using var fs = File.OpenRead(CachePaths.Meshes);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Importer.Bake.MeshBakery.MagicMesh)
                throw new InvalidDataException("meshes.bin magic mismatch");

            uint count = r.ReadUInt32();
            progress?.Report("Reading mesh table", 0, (int)count);

            var offsets = new ulong[count];
            for (int i = 0; i < count; i++)
                offsets[i] = r.ReadUInt64();

            Meshes = new Mesh[count];
            for (int i = 0; i < count; i++)
            {
                k_Meshes.Begin();
                try
                {
                    fs.Position = (long)offsets[i];
                    Meshes[i] = ReadOneMesh(r, $"BakedMesh{i}");
                }
                finally
                {
                    k_Meshes.End();
                }

                int completed = i + 1;
                if (completed == count || (completed % MeshBatchSize) == 0)
                {
                    progress?.Report($"Reading meshes {completed}/{count}", completed, (int)count);
                    yield return null;
                }
            }

            sw.Stop();
            progress?.CompleteStage("Meshes ready");
        }

        private IEnumerator LoadTexturesIncremental(RuntimeLoadProgress progress)
        {
            var sw = Stopwatch.StartNew();
            var texHashes = TextureBakeryReadOrder(CachePaths.TexturesIndex);
            BuildTexturePathLookup(texHashes);
            Textures = new Texture2D[texHashes.Length];
            progress?.Report("Loading textures", 0, texHashes.Length);

            for (int i = 0; i < texHashes.Length; i++)
            {
                k_Textures.Begin();
                try
                {
                    Textures[i] = LoadTexture(CachePaths.TextureFile(texHashes[i]), texHashes[i]);
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

            sw.Stop();
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

            int slash = normalized.LastIndexOf('\\');
            if (slash >= 0 && slash + 1 < normalized.Length)
                _textureIndexByPath[normalized.Substring(slash + 1)] = textureIndex;
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

        private IEnumerator BuildBucketedRefArraysIncremental(RuntimeLoadProgress progress)
        {
            var sw = Stopwatch.StartNew();
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
            var bucketMaterials = new Material[keys.Length][];
            var texBucketInfo = new NativeArray<int2>(
                Textures.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            var white = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false, linear: false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            int completed = 0;
            int yieldedAt = 0;
            int fallbackSlice = 0;
            NativeArray<int2> shardRanges = default;
            NativeArray<int> shardGlobalMeshIndices = default;

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

                        var bucketMats = new Material[matRecords.Length];
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
                            bucketMats[mi] = material;
                            materials[b * matRecords.Length + mi] = material;
                            completed++;
                        }

                        bucketMaterials[b] = bucketMats;
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

                var records = RenderShardCatalog?.Records ?? Array.Empty<RenderShardRecord>();
                if (records.Length == 0)
                    throw new InvalidDataException("render_shards.bin contains no shard records.");

                if (fallbackBucket < 0)
                    throw new InvalidDataException($"runtime texture bucket map missing fallback key 0x{fallbackBucketKey:X8}.");

                var bucketIndexByKey = new Dictionary<int, int>(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    bucketIndexByKey[keys[i]] = i;

                int remappedShardCount = 0;
                int invalidShardMeshCount = 0;
                var remappedBuckets = new HashSet<int>();
                Mesh invalidShardMeshFallback = null;

                var rmas = new RenderMeshArray[records.Length];
                shardRanges = new NativeArray<int2>(records.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                int totalShardMeshIndices = 0;
                for (int shardIndex = 0; shardIndex < records.Length; shardIndex++)
                    totalShardMeshIndices += records[shardIndex]?.GlobalMeshIndices?.Length ?? 0;
                shardGlobalMeshIndices = new NativeArray<int>(totalShardMeshIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                int shardGlobalCursor = 0;
                for (int shardIndex = 0; shardIndex < records.Length; shardIndex++)
                {
                    var record = records[shardIndex];
                    if (!bucketIndexByKey.TryGetValue(record.BucketKey, out int bucketIndex))
                    {
                        remappedShardCount++;
                        remappedBuckets.Add(record.BucketKey);
                        bucketIndex = fallbackBucket;
                    }

                    var globalMeshIndices = record.GlobalMeshIndices ?? Array.Empty<int>();
                    var shardMeshes = new Mesh[globalMeshIndices.Length];
                    shardRanges[shardIndex] = new int2(shardGlobalCursor, globalMeshIndices.Length);
                    for (int meshIndex = 0; meshIndex < globalMeshIndices.Length; meshIndex++)
                    {
                        int globalMeshIndex = globalMeshIndices[meshIndex];
                        if ((uint)globalMeshIndex >= (uint)Meshes.Length)
                        {
                            invalidShardMeshCount++;
                            invalidShardMeshFallback ??= CreateInvalidShardMeshFallback();
                            shardMeshes[meshIndex] = invalidShardMeshFallback;
                            shardGlobalMeshIndices[shardGlobalCursor++] = -1;
                            continue;
                        }

                        shardMeshes[meshIndex] = Meshes[globalMeshIndex];
                        shardGlobalMeshIndices[shardGlobalCursor++] = globalMeshIndex;
                    }

                    rmas[shardIndex] = new RenderMeshArray(bucketMaterials[bucketIndex], shardMeshes);
                }

                WorldResources.RefBaseArrays = rts;
                WorldResources.RefsRmas = rmas;
                WorldResources.RefShardMeshRanges = shardRanges;
                WorldResources.RefShardGlobalMeshIndices = shardGlobalMeshIndices;
                WorldResources.TexBucketInfo = texBucketInfo;
                WorldResources.FallbackBucketSlice = new int2(fallbackBucket, fallbackSlice);
                WorldResources.BlendVariantCount = matRecords.Length;
                Materials = materials;

                progress?.CompleteStage("Reference shards ready");
                RenderShardFile.LogShardStats(RenderShardCatalog, "runtime load");
                if (remappedShardCount > 0)
                {
                    var missingBuckets = new StringBuilder();
                    int bucketCount = 0;
                    foreach (int missing in remappedBuckets)
                    {
                        if (bucketCount > 0)
                            missingBuckets.Append(", ");
                        missingBuckets.AppendFormat("0x{0:X8}", missing);
                        bucketCount++;
                    }

                    Debug.LogWarning($"[VVardenfell] remapped {remappedShardCount} render shard(s) ({missingBuckets}) to fallback bucket due to missing bucket keys.");
                }
                if (invalidShardMeshCount > 0)
                    Debug.LogWarning($"[VVardenfell] replaced {invalidShardMeshCount} invalid render shard mesh slot(s) with a fallback mesh. Rebake the cache to remove stale shard data.");
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

                    if (shardRanges.IsCreated)
                        shardRanges.Dispose();
                    if (shardGlobalMeshIndices.IsCreated)
                        shardGlobalMeshIndices.Dispose();
                    if (texBucketInfo.IsCreated)
                        texBucketInfo.Dispose();
                }

                UnityEngine.Object.Destroy(white);
            }

            sw.Stop();
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

        private static Texture2D LoadTexture(string path, string hashHex)
        {
            if (!File.Exists(path))
                return null;

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

        private static Mesh ReadOneMesh(BinaryReader r, string name)
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
                }

                using (k_MeshUpload.Auto())
                {
                    var mesh = new Mesh
                    {
                        name = name,
                        indexFormat = index32 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    };

                    var meshDataArray = Mesh.AllocateWritableMeshData(1);
                    try
                    {
                        var meshData = meshDataArray[0];
                        var descriptors = GetVertexLayout(hasNormals, hasUVs);
                        meshData.SetVertexBufferParams((int)vertexCount, descriptors);
                        meshData.SetIndexBufferParams((int)indexCount, index32 ? IndexFormat.UInt32 : IndexFormat.UInt16);

                        var vertexData = meshData.GetVertexData<byte>(0);
                        var indexData = meshData.GetIndexData<byte>();
                        if (vertexData.Length != vertexDataBytes || indexData.Length != indexDataBytes)
                        {
                            throw new InvalidDataException(
                                $"Mesh '{name}' writable buffer size mismatch ({vertexData.Length}/{indexData.Length}).");
                        }

                        NativeArray<byte>.Copy(vertexBytes, 0, vertexData, 0, (int)vertexDataBytes);
                        NativeArray<byte>.Copy(indexBytes, 0, indexData, 0, (int)indexDataBytes);

                        meshData.subMeshCount = 1;
                        meshData.SetSubMesh(
                            0,
                            new SubMeshDescriptor(0, (int)indexCount, MeshTopology.Triangles),
                            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                        Mesh.ApplyAndDisposeWritableMeshData(
                            meshDataArray,
                            mesh,
                            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                        mesh.bounds = new Bounds(bc, be * 2f);
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
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"Mesh '{name}' payload is truncated.", ex);
            }
            finally
            {
                if (vertexBytes != null)
                    ArrayPool<byte>.Shared.Return(vertexBytes);
                if (indexBytes != null)
                    ArrayPool<byte>.Shared.Return(indexBytes);
            }
        }

        private static Mesh CreateInvalidShardMeshFallback()
        {
            var mesh = new Mesh
            {
                name = "VV:InvalidRenderShardFallback",
                indexFormat = IndexFormat.UInt16,
            };

            mesh.SetVertices(new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.001f, 0f),
                new Vector3(0.001f, 0f, 0f),
            });
            mesh.SetNormals(new[]
            {
                Vector3.up,
                Vector3.up,
                Vector3.up,
            });
            mesh.SetUVs(0, new[]
            {
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
            });
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, calculateBounds: false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
            mesh.UploadMeshData(true);
            return mesh;
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
