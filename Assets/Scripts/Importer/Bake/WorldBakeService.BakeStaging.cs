using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{

    internal static partial class WorldBakeService
    {
        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress, GameplayContentData gameplayContent = null)
            => Bake(config, config?.CreateVanillaContentProfile(), progress, gameplayContent);

        public static IEnumerator Bake(MorrowindConfig config, MorrowindContentProfile profile, BakeProgress progress, GameplayContentData gameplayContent = null)
        {
            profile ??= config?.CreateVanillaContentProfile();
            string profileError = null;
            if (profile == null || !profile.IsValid(out profileError))
            {
                progress.Error = profileError ?? "Content profile is invalid.";
                progress.Done = true;
                yield break;
            }

            string[] sourcePaths = profile.ContentFiles ?? Array.Empty<string>();
            string esmPath = sourcePaths.Length > 0 ? sourcePaths[0] : string.Empty;
            string bsaPath = (profile.Archives != null && profile.Archives.Length > 0) ? profile.Archives[0] : string.Empty;

            CachePaths.Warmup();
            CachePaths.EnsureExists();
            var gameplayContentLookup = BuildGameplayContentLookup(gameplayContent);
            var combinedStaticExclusions = BuildCombinedStaticExclusionData(gameplayContent);

            progress.Stage = "Source Indexing";
            progress.Label = "Opening archives";
            progress.Current = 0;
            progress.Total = 5;
            yield return null;

            using var assetResolver = ContentAssetResolver.Open(profile);
            var sharedBsa = assetResolver.PrimaryArchive;

            progress.Label = "Building record index";
            progress.Current = 1;
            yield return null;
            RecordIndex recordIndex = RecordIndex.Build(sourcePaths);

            progress.Label = "Enumerating cells";
            progress.Current = 2;
            yield return null;
            var exteriorCellGroups = new Dictionary<string, List<CellHeader>>(StringComparer.OrdinalIgnoreCase);
            var interiorCellGroups = new Dictionary<string, List<CellHeader>>(StringComparer.OrdinalIgnoreCase);
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                using var esm = new EsmReader(sourcePaths[sourceIndex]);
                foreach (var cell in CellIndex.Enumerate(esm, sourceIndex))
                {
                    var key = cell.IsInterior ? BuildInteriorKey(cell.Name ?? string.Empty) : BuildExteriorKey(cell.GridX, cell.GridY);
                    var groups = cell.IsInterior ? interiorCellGroups : exteriorCellGroups;
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<CellHeader>();
                    list.Add(cell);
                }
            }

            var exteriorCells = new List<CellHeader>(exteriorCellGroups.Count);
            var interiorCells = new List<CellHeader>(interiorCellGroups.Count);
            var cellRecordGroups = new Dictionary<string, CellHeader[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in exteriorCellGroups)
            {
                var records = pair.Value.ToArray();
                cellRecordGroups[pair.Key] = records;
                var cell = records[^1];
                exteriorCells.Add(cell);
            }
            foreach (var pair in interiorCellGroups)
            {
                var records = pair.Value.ToArray();
                cellRecordGroups[pair.Key] = records;
                var cell = records[^1];
                interiorCells.Add(cell);
            }

            exteriorCells.Sort((a, b) =>
            {
                int x = a.GridX.CompareTo(b.GridX);
                return x != 0 ? x : a.GridY.CompareTo(b.GridY);
            });
            interiorCells.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            progress.Label = "Indexing terrain";
            progress.Current = 3;
            yield return null;
            var landOffsets = new Dictionary<(int, int), (string SourcePath, long Offset)>();
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                using var esm = new EsmReader(sourcePaths[sourceIndex]);
                var sourceLandOffsets = LandIndex.BuildOffsetMap(esm);
                foreach (var pair in sourceLandOffsets)
                    landOffsets[pair.Key] = (sourcePaths[sourceIndex], pair.Value);
            }

            progress.Label = "Indexing land textures";
            progress.Current = 4;
            yield return null;
            var ltexMapsBySource = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                using var esm = new EsmReader(sourcePaths[sourceIndex]);
                ltexMapsBySource[sourcePaths[sourceIndex]] = LtexIndex.Build(esm);
            }

            progress.Stage = "Dependency Snapshot";
            progress.Label = "Loading previous cache state";
            progress.Current = 0;
            progress.Total = 1;
            yield return null;

            BakeManifest.TryRead(CachePaths.Manifest, out var existingManifest);
            var previousStateByKey = new Dictionary<string, BakeManifest.BakedCellState>(StringComparer.OrdinalIgnoreCase);
            if (existingManifest?.CellStates != null)
            {
                for (int i = 0; i < existingManifest.CellStates.Length; i++)
                {
                    var state = existingManifest.CellStates[i];
                    if (state != null && !string.IsNullOrEmpty(state.Key))
                        previousStateByKey[state.Key] = state;
                }
            }

            var textureResolver = new TexturePathResolver(assetResolver.Entries);
            var bakeryMeshes = new MeshBakery();
            bakeryMeshes.TryLoadExisting(CachePaths.MeshCatalog, CachePaths.Meshes);
            var bakeryMaterials = new MaterialBakery();
            bakeryMaterials.TryLoadExisting(CachePaths.MaterialCatalog);
            var bakeryTextures = new TextureBakery(assetResolver, textureResolver);
            bakeryTextures.TryLoadExisting(CachePaths.TextureCatalog);
            int defaultTexIdx = bakeryTextures.AddOrGetRequired(LtexIndex.DefaultTexturePath, "Default terrain texture");
            SeedSkyWeatherTextures(gameplayContent, bakeryTextures);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);
            bakeryLayers.TryLoadExisting(CachePaths.TerrainLayers);
            var bakerySplats = new TerrainSplatBakery();
            bakerySplats.TryLoadExisting(CachePaths.TerrainSplats);
            bool forceCellRebuild = bakeryLayers.ExistingCacheInvalid
                                    || bakerySplats.ExistingCacheInvalid
                                    || !File.Exists(CachePaths.TerrainSplats);
            var bakeryCollisions = new CollisionBakery();
            bakeryCollisions.TryLoadExisting(CachePaths.CollisionCatalog);
            var bakeryModelPrefabs = new ModelPrefabBakery();
            var bakeryVfxEffects = new VfxEffectBakery();
            var bakeryActorAnimations = new ActorAnimationBakery();
            var bakeryObjectAnimations = new ObjectAnimationBakery(gameplayContent);
            var modelCache = new ConcurrentDictionary<string, Lazy<ModelSource>>(StringComparer.OrdinalIgnoreCase);
            var requiredVfxModels = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            progress.Current = 1;
            yield return null;

            var bsaByName = assetResolver.CopyEntryMap();

            SeedRequiredVfxAssets(gameplayContent, sharedBsa, bsaByName, modelCache, bakeryTextures, requiredVfxModels);
            SeedRuntimeSpawnableModels(gameplayContent, sharedBsa, bsaByName, modelCache);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var workItems = new List<CellBakeWorkItem>(exteriorCells.Count + interiorCells.Count);
            for (int i = 0; i < exteriorCells.Count; i++)
            {
                var cell = exteriorCells[i];
                landOffsets.TryGetValue((cell.GridX, cell.GridY), out var landLocation);
                var cellOrigin = new Vector3(cell.GridX * cellMeters, 0f, cell.GridY * cellMeters);
                string key = BuildExteriorKey(cell.GridX, cell.GridY);
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    false,
                    landLocation.Offset,
                    landLocation.SourcePath,
                    cellRecordGroups[key],
                    key,
                    cellOrigin));
            }

            for (int i = 0; i < interiorCells.Count; i++)
            {
                var cell = interiorCells[i];
                string interiorId = cell.Name ?? string.Empty;
                string key = BuildInteriorKey(interiorId);
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    true,
                    0,
                    cell.SourcePath,
                    cellRecordGroups[key],
                    key,
                    Vector3.zero));
            }

            progress.Stage = "Per-Cell Planning";
            progress.Label = "Staging cells";
            progress.Current = 0;
            progress.Total = workItems.Count;
            yield return null;

            var stagedCells = new StagedCellData[workItems.Count];
            int plannedCount = 0;
            Exception stageFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            var stageTask = Task.Run(() =>
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, workItems.Count),
                        options,
                        () => new WorkerContext(),
                        (range, _, worker) =>
                        {
                            for (int i = range.Item1; i < range.Item2; i++)
                            {
                                stagedCells[i] = StageCell(
                                    worker,
                                    workItems[i],
                                    recordIndex,
                                    gameplayContent,
                                    gameplayContentLookup,
                                    combinedStaticExclusions,
                                    sharedBsa,
                                    bsaByName,
                                    ltexMapsBySource,
                                    previousStateByKey,
                                    modelCache,
                                    config.BakeCombinedCellRenderChunks,
                                    forceCellRebuild);
                                Interlocked.Increment(ref plannedCount);
                            }
                            return worker;
                        },
                        worker => worker.Dispose());
                }
                catch (Exception ex)
                {
                    stageFailure = ex;
                }
            });

            while (!stageTask.IsCompleted)
            {
                progress.Current = plannedCount;
                yield return null;
            }

            if (stageFailure != null)
                throw stageFailure;
            FlushAnimatedStaticRefWarnings();
            FlushUnsupportedObjectControllerWarnings();
            FlushDroppedBakeRefWarnings();

            progress.Current = workItems.Count;
            yield return null;

            progress.Stage = "Finalizing";
            progress.Label = "Collecting dirty cells";
            progress.Current = 0;
            progress.Total = workItems.Count;
            yield return null;

            var cellStates = new BakeManifest.BakedCellState[stagedCells.Length];
            var expectedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirtyCells = new List<StagedCellData>();

            for (int i = 0; i < stagedCells.Length; i++)
            {
                var staged = stagedCells[i];
                expectedSections.Add(staged.WorkItem.IsInterior
                    ? CachePaths.InteriorCellSectionFile(staged.WorkItem.Cell.Name ?? string.Empty)
                    : CachePaths.ExteriorCellSectionFile(staged.WorkItem.Cell.GridX, staged.WorkItem.Cell.GridY));
                if (staged.NeedsWrite)
                    dirtyCells.Add(staged);

                cellStates[i] = BuildCellState(staged);
                progress.Current = i + 1;
                if (((i + 1) & 7) == 0)
                    yield return null;
            }

            yield return PrepareDirtyCellsIncremental(dirtyCells, progress, ltexMapsBySource);
            bakeryActorAnimations.ConfigureCreatureAnimationSources(gameplayContent, bsaByName);
            yield return BuildModelPrefabsIncremental(modelCache, progress, bakeryModelPrefabs, bakeryVfxEffects, bakeryActorAnimations, bakeryObjectAnimations, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryCollisions, sharedBsa, bsaByName, gameplayContent, requiredVfxModels);
            bakeryActorAnimations.BuildVisualRecipes(gameplayContent, bsaByName);
            yield return ResolveDirtyCellIndicesIncremental(dirtyCells, progress, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryLayers, bakerySplats, bakeryCollisions, bakeryModelPrefabs);
            FlushDroppedBakeRefWarnings();

            for (int i = 0; i < stagedCells.Length; i++)
                cellStates[i] = BuildCellState(stagedCells[i]);

            var modelPrefabCatalog = bakeryModelPrefabs.BuildCatalog();
            yield return WriteDirtyCellsIncremental(dirtyCells, progress, cellMeters, modelPrefabCatalog, bakeryTextures, bakeryMaterials, gameplayContent, bakeryCollisions);

            progress.Stage = "Writing";
            progress.Current = 0;
            progress.Total = 18;

            progress.Label = "meshes.bin";
            progress.Current = 1;
            yield return null;
            if (bakeryMeshes.Modified || !File.Exists(CachePaths.Meshes) || !File.Exists(CachePaths.MeshCatalog) || !File.Exists(CachePaths.MeshNames))
            {
                bakeryMeshes.WriteTo(CachePaths.Meshes);
                bakeryMeshes.WriteCatalog(CachePaths.MeshCatalog);
                bakeryMeshes.WriteNames(CachePaths.MeshNames);
            }

            progress.Label = "materials.bin";
            progress.Current = 2;
            yield return null;
            if (bakeryMaterials.Modified || !File.Exists(CachePaths.Materials) || !File.Exists(CachePaths.MaterialCatalog))
            {
                bakeryMaterials.WriteTo(CachePaths.Materials);
                bakeryMaterials.WriteCatalog(CachePaths.MaterialCatalog);
            }

            progress.Label = "model_prefabs.bin";
            progress.Current = 3;
            yield return null;
            if (bakeryModelPrefabs.Modified || bakeryObjectAnimations.Modified || !File.Exists(CachePaths.ModelPrefabs))
                ModelPrefabFile.Write(CachePaths.ModelPrefabs, modelPrefabCatalog);

            progress.Label = "runtime_spawn_prefabs.entities";
            progress.Current = 4;
            yield return null;
            if (bakeryModelPrefabs.Modified
                || bakeryObjectAnimations.Modified
                || bakeryTextures.Modified
                || bakeryMaterials.Modified
                || bakeryCollisions.Modified
                || !RuntimeSpawnPrefabBakery.IsCurrent(CachePaths.RuntimeSpawnPrefabs, modelPrefabCatalog.Records?.Length ?? 0)
                || !File.Exists(CachePaths.RuntimeSpawnPrefabs))
            {
                RuntimeSpawnPrefabBakery.Write(CachePaths.RuntimeSpawnPrefabs, modelPrefabCatalog, bakeryTextures, bakeryMaterials, bakeryCollisions);
            }

            progress.Label = "vfx_effects.bin";
            progress.Current = 5;
            yield return null;
            if (bakeryVfxEffects.Modified || !File.Exists(CachePaths.VfxEffects))
                MorrowindVfxFile.Write(CachePaths.VfxEffects, bakeryVfxEffects.BuildCatalog());

            progress.Label = "actor_animations.bin";
            progress.Current = 6;
            yield return null;
            if (bakeryActorAnimations.Modified || !ActorAnimationFile.IsCurrentVersion(CachePaths.ActorAnimations))
                ActorAnimationFile.Write(CachePaths.ActorAnimations, bakeryActorAnimations.BuildCatalog());

            progress.Label = "textures.bin";
            progress.Current = 7;
            yield return null;
            bool texturesNeedWrite = bakeryTextures.Modified || !File.Exists(CachePaths.TexturesIndex) || !File.Exists(CachePaths.TextureCatalog);
            if (texturesNeedWrite)
            {
                bakeryTextures.WriteIndex(CachePaths.TexturesIndex);
                bakeryTextures.WriteCatalog(CachePaths.TextureCatalog);
            }

            progress.Label = "ref_texture_buckets.bin";
            progress.Current = 8;
            yield return null;
            if (texturesNeedWrite || !File.Exists(CachePaths.RefTextureBuckets))
                RefTextureBucketFile.Write(CachePaths.RefTextureBuckets, bakeryTextures.BuildRefTextureBuckets());

            progress.Label = "terrain_layers.bin";
            progress.Current = 9;
            yield return null;
            if (texturesNeedWrite || bakeryLayers.Modified || !File.Exists(CachePaths.TerrainLayers))
                bakeryLayers.WriteTo(CachePaths.TerrainLayers, bakeryTextures);

            progress.Label = "terrain_splats.bin";
            progress.Current = 10;
            yield return null;
            if (bakerySplats.Modified || !File.Exists(CachePaths.TerrainSplats))
                bakerySplats.WriteTo(CachePaths.TerrainSplats);

            progress.Label = "collisions.bin";
            progress.Current = 11;
            yield return null;
            if (bakeryCollisions.Modified || !File.Exists(CachePaths.Collisions) || !File.Exists(CachePaths.CollisionCatalog))
            {
                bakeryCollisions.WriteTo(CachePaths.Collisions);
                bakeryCollisions.WriteCatalog(CachePaths.CollisionCatalog);
            }

            progress.Label = "Pruning stale cells";
            progress.Current = 12;
            yield return null;
            PruneOrphans(CachePaths.LegacyExteriorCellsDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            PruneOrphans(CachePaths.LegacyInteriorCellsDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            PruneOrphans(CachePaths.ExteriorCellSectionsDir, expectedSections);
            PruneOrphans(CachePaths.InteriorCellSectionsDir, expectedSections);
            PruneLegacyTextureFiles(CachePaths.TexturesDir);

            progress.Label = "world_cells.blob";
            progress.Current = 13;
            yield return null;
            RuntimeWorldCellBlobBakery.Write(CachePaths.WorldCells, cellStates);

            progress.Label = "runtime_distant_terrain.entities";
            progress.Current = 14;
            yield return null;
            WriteRuntimeDistantTerrain(CachePaths.RuntimeDistantTerrain, stagedCells);

            progress.Label = "mesh_cache_report.txt";
            progress.Current = 15;
            yield return null;

            progress.Label = "world_collision_validation.txt";
            progress.Current = 16;
            yield return null;

            progress.Label = "ui.bin";
            progress.Current = 17;
            yield return null;
            UiAssetBakery.Bake(config, sharedBsa, progress);

            progress.Label = "manifest.bin";
            progress.Current = 18;
            yield return null;
            var manifest = BakeManifest.FromCurrentSources(
                esmPath,
                bsaPath,
                InstalledContentSources.ResolveGameplayDependencySources(profile));
            manifest.MeshCount = bakeryMeshes.Count;
            manifest.MaterialCount = bakeryMaterials.Count;
            manifest.TextureCount = bakeryTextures.Count;
            manifest.CollisionCount = bakeryCollisions.Count;
            manifest.CellCount = exteriorCells.Count;
            manifest.CellGrid = new (int, int)[exteriorCells.Count];
            for (int i = 0; i < exteriorCells.Count; i++)
                manifest.CellGrid[i] = (exteriorCells[i].GridX, exteriorCells[i].GridY);
            manifest.InteriorCellCount = interiorCells.Count;
            manifest.InteriorCellIds = new string[interiorCells.Count];
            for (int i = 0; i < interiorCells.Count; i++)
                manifest.InteriorCellIds[i] = interiorCells[i].Name ?? string.Empty;
            manifest.CellStates = cellStates;
            manifest.Write(CachePaths.Manifest);

            progress.Stage = "Done";
            int dirtyCount = 0;
            for (int i = 0; i < stagedCells.Length; i++)
            {
                if (stagedCells[i].NeedsWrite)
                    dirtyCount++;
            }
            progress.Label = $"{dirtyCount}/{stagedCells.Length} cells rebuilt, {bakeryMeshes.Count} meshes, {bakeryMaterials.Count} mats, {bakeryTextures.Count} textures, {bakeryLayers.Count} terrain layers, {bakeryCollisions.Count} collisions, {bakeryActorAnimations.SkeletonCount} actor skeletons, {bakeryActorAnimations.ClipCount} actor clips";
            if (config.BakeCombinedCellRenderChunks)
                LogCombinedCellRenderBakeStats(stagedCells);
            progress.Done = true;
        }


        static void SeedSkyWeatherTextures(GameplayContentData gameplayContent, TextureBakery textures)
        {
            if (gameplayContent == null || textures == null)
                return;

            var visual = gameplayContent.SkyWeatherVisualSettings;
            AddTexture(textures, visual.SunTexture);
            AddTexture(textures, visual.SunGlareTexture);
            AddTexture(textures, visual.StarTexture);
            AddTexture(textures, visual.MasserShadowTexture);
            AddTexture(textures, visual.SecundaShadowTexture);
            AddTexture(textures, visual.RainDropTexture);
            AddTextures(textures, visual.MasserPhaseTextures);
            AddTextures(textures, visual.SecundaPhaseTextures);
            AddTextures(textures, visual.CloudTextures);
            AddTextures(textures, visual.PrecipitationTextures);

            var weather = gameplayContent.WeatherDefinitions ?? Array.Empty<WeatherDefinitionDef>();
            for (int i = 0; i < weather.Length; i++)
                AddTexture(textures, weather[i].CloudTexture);
        }


        static void AddTextures(TextureBakery textures, string[] paths)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
                AddTexture(textures, paths[i]);
        }


        static void AddTexture(TextureBakery textures, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                textures.AddOrGet(path);
        }


        static void SeedRequiredVfxAssets(
            GameplayContentData gameplayContent,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            TextureBakery textures,
            ConcurrentDictionary<string, byte> requiredVfxModels)
        {
            if (gameplayContent == null)
                return;

            for (int i = 0; i <= 2; i++)
            {
                string model = "meshes/" + RequireGameplayGameSettingString(gameplayContent, $"Blood_Model_{i}");
                EnsureRequiredModelSource(
                    model,
                    sharedBsa,
                    bsaByName,
                    modelCache,
                    $"Blood VFX model seed Blood_Model_{i}");
                MarkRequiredVfxModel(requiredVfxModels, model);
            }

            EnsureRequiredTexture(textures, RequireGameplayGameSettingString(gameplayContent, "Blood_Texture_0"), "Blood_Texture_0");
            for (int i = 1; i <= 2; i++)
            {
                string texture = RequireGameplayGameSettingStringAllowEmpty(gameplayContent, $"Blood_Texture_{i}");
                if (!string.IsNullOrWhiteSpace(texture))
                    EnsureRequiredTexture(textures, texture, $"Blood_Texture_{i}");
            }

            SeedMagicVfxAssets(gameplayContent, sharedBsa, bsaByName, modelCache, requiredVfxModels);
        }


        static void SeedMagicVfxAssets(
            GameplayContentData gameplayContent,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            ConcurrentDictionary<string, byte> requiredVfxModels)
        {
            var effects = gameplayContent.MagicEffects ?? Array.Empty<MagicEffectDef>();
            for (int i = 0; i < effects.Length; i++)
            {
                SeedMagicVfxObject(gameplayContent, sharedBsa, bsaByName, modelCache, requiredVfxModels, effects[i].CastingObjectId, effects[i].Index, "CastingObjectId");
                SeedMagicVfxObject(gameplayContent, sharedBsa, bsaByName, modelCache, requiredVfxModels, effects[i].HitObjectId, effects[i].Index, "HitObjectId");
                SeedMagicVfxObject(gameplayContent, sharedBsa, bsaByName, modelCache, requiredVfxModels, effects[i].AreaObjectId, effects[i].Index, "AreaObjectId");
                SeedMagicVfxObject(gameplayContent, sharedBsa, bsaByName, modelCache, requiredVfxModels, effects[i].BoltObjectId, effects[i].Index, "BoltObjectId");
            }
        }


        static void SeedMagicVfxObject(
            GameplayContentData gameplayContent,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            ConcurrentDictionary<string, byte> requiredVfxModels,
            string objectId,
            int effectIndex,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            string model = ResolveRequiredVfxObjectModel(gameplayContent, objectId, $"Magic effect {effectIndex} {fieldName}");
            EnsureRequiredModelSource(
                model,
                sharedBsa,
                bsaByName,
                modelCache,
                $"Magic effect {effectIndex} {fieldName} '{objectId}'");
            MarkRequiredVfxModel(requiredVfxModels, model);
        }


        static string ResolveRequiredVfxObjectModel(GameplayContentData gameplayContent, string objectId, string context)
        {
            if (TryResolveVfxModel(gameplayContent.Activators, objectId, out string model)
                || TryResolveVfxModel(gameplayContent.Statics, objectId, out model)
                || TryResolveVfxModel(gameplayContent.Lights, objectId, out model)
                || TryResolveVfxModel(gameplayContent.Items, objectId, out model))
            {
                return model;
            }

            throw new InvalidOperationException($"Required VFX object '{objectId}' for {context} is missing or has no model.");
        }


        static bool TryResolveVfxModel(BaseDef[] records, string id, out string model)
        {
            model = null;
            var values = records ?? Array.Empty<BaseDef>();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i].Id, id, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(values[i].Model))
                {
                    model = values[i].Model;
                    return true;
                }
            }

            return false;
        }


        static bool TryResolveVfxModel(GenericRecordDef[] records, string id, out string model)
        {
            model = null;
            var values = records ?? Array.Empty<GenericRecordDef>();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i].Id, id, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(values[i].Model))
                {
                    model = values[i].Model;
                    return true;
                }
            }

            return false;
        }


        static bool TryResolveVfxModel(LightDef[] records, string id, out string model)
        {
            model = null;
            var values = records ?? Array.Empty<LightDef>();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i].Id, id, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(values[i].Model))
                {
                    model = values[i].Model;
                    return true;
                }
            }

            return false;
        }


        static void MarkRequiredVfxModel(ConcurrentDictionary<string, byte> requiredVfxModels, string model)
        {
            if (requiredVfxModels == null)
                return;

            string normalized = NormalizeModelPath(model);
            if (!string.IsNullOrWhiteSpace(normalized))
                requiredVfxModels.TryAdd(normalized, 0);
        }


        static string RequireGameplayGameSettingString(GameplayContentData gameplayContent, string id)
        {
            if (TryGetGameplayGameSettingString(gameplayContent, id, out string value))
                return value;

            throw new InvalidOperationException($"Required GMST '{id}' is missing or not a string.");
        }


        static string RequireGameplayGameSettingStringAllowEmpty(GameplayContentData gameplayContent, string id)
        {
            var settings = gameplayContent?.GameSettings ?? Array.Empty<GenericRecordDef>();
            for (int i = 0; i < settings.Length; i++)
            {
                if (!string.Equals(settings[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (settings[i].ValueKind != GenericRecordValueKind.String)
                    throw new InvalidOperationException($"Required GMST '{id}' is not a string.");
                return settings[i].Text ?? string.Empty;
            }

            throw new InvalidOperationException($"Required GMST '{id}' is missing.");
        }


        static bool TryGetGameplayGameSettingString(GameplayContentData gameplayContent, string id, out string value)
        {
            var settings = gameplayContent?.GameSettings ?? Array.Empty<GenericRecordDef>();
            for (int i = 0; i < settings.Length; i++)
            {
                if (string.Equals(settings[i].Id, id, StringComparison.OrdinalIgnoreCase)
                    && settings[i].ValueKind == GenericRecordValueKind.String
                    && !string.IsNullOrWhiteSpace(settings[i].Text))
                {
                    value = settings[i].Text;
                    return true;
                }
            }

            value = null;
            return false;
        }


        static void EnsureRequiredTexture(TextureBakery textures, string path, string context)
        {
            if (textures == null)
                throw new InvalidOperationException($"{context} cannot be seeded without a texture bakery.");
            if (textures.AddOrGet(path) < 0)
                throw new InvalidOperationException($"{context} required texture '{path}' is missing from the archive.");
        }

        static List<CellReference> ReadMergedReferences(WorkerContext worker, CellBakeWorkItem workItem)
        {
            var result = new List<CellReference>();
            var indexByFormId = new Dictionary<uint, int>();
            var records = workItem.CellRecords ?? Array.Empty<CellHeader>();
            for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
            {
                var cell = records[recordIndex];
                var refs = CellReader.ReadReferences(worker.GetReader(cell.SourcePath), cell);
                for (int i = 0; i < refs.Count; i++)
                {
                    var reference = refs[i];
                    if (reference.FormId == 0u)
                        continue;

                    if (reference.Deleted)
                    {
                        if (indexByFormId.TryGetValue(reference.FormId, out int existingIndex))
                        {
                            result.RemoveAt(existingIndex);
                            indexByFormId.Clear();
                            for (int rebuild = 0; rebuild < result.Count; rebuild++)
                                indexByFormId[result[rebuild].FormId] = rebuild;
                        }
                        continue;
                    }

                    if (indexByFormId.TryGetValue(reference.FormId, out int replaceIndex))
                    {
                        result[replaceIndex] = reference;
                    }
                    else
                    {
                        indexByFormId[reference.FormId] = result.Count;
                        result.Add(reference);
                    }
                }
            }

            return result;
        }


        private static StagedCellData StageCell(
            WorkerContext worker,
            CellBakeWorkItem workItem,
            RecordIndex recordIndex,
            GameplayContentData gameplayContent,
            Dictionary<string, ContentReference> gameplayContentLookup,
            CombinedStaticExclusionData combinedStaticExclusions,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            Dictionary<string, Dictionary<int, string>> ltexMapsBySource,
            Dictionary<string, BakeManifest.BakedCellState> previousStateByKey,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            bool bakeCombinedCellRenderChunks,
            bool forceRebuild)
        {
            List<CellReference> refs;
            try
            {
                refs = ReadMergedReferences(worker, workItem);
            }
            catch
            {
                refs = new List<CellReference>();
            }

            LandRecord land = null;
            if (!workItem.IsInterior && workItem.LandOffset != 0)
            {
                try
                {
                    land = LandIndex.ReadAt(worker.GetReader(workItem.LandSourcePath), workItem.LandOffset);
                }
                catch
                {
                    land = null;
                }
            }

            string fingerprint = ComputeFingerprint(workItem, refs, land, recordIndex, ltexMapsBySource, bakeCombinedCellRenderChunks, combinedStaticExclusions);
            bool hasPrevious = previousStateByKey.TryGetValue(workItem.Key, out var previousState);
            string sectionPath = workItem.IsInterior
                ? CachePaths.InteriorCellSectionFile(workItem.Cell.Name ?? string.Empty)
                : CachePaths.ExteriorCellSectionFile(workItem.Cell.GridX, workItem.Cell.GridY);
            bool canReuse = !forceRebuild
                && hasPrevious
                && previousState.PipelineVersion == CacheFormat.WorldBakePipelineVersion
                && string.Equals(previousState.Fingerprint, fingerprint, StringComparison.Ordinal)
                && File.Exists(sectionPath);

            var staged = new StagedCellData
            {
                WorkItem = workItem,
                PreviousState = hasPrevious ? previousState : null,
                Fingerprint = fingerprint,
                Environment = workItem.Cell.Environment,
                Land = land,
                BakeCombinedCellRenderChunks = bakeCombinedCellRenderChunks,
                NeedsWrite = !canReuse,
                PlacedRefs = canReuse ? null : new List<StagedPlacedRefData>(refs.Count),
                PendingRefs = canReuse ? null : new List<StagedRefData>(refs.Count),
                DoorEntries = canReuse ? null : new List<DoorRefEntry>(),
                CapturedSouls = canReuse ? null : new List<PlacedRefSoulEntry>(),
                LockStates = canReuse ? null : new List<PlacedRefLockEntry>(),
                CombinedRenderChunks = canReuse ? null : new List<CombinedCellRenderChunkDef>(),
                CollisionMissingPayloadSamples = canReuse ? null : new List<string>(4),
            };

            if (canReuse)
                return staged;

            var duplicatePlacedRefCounts = new Dictionary<uint, int>();
            for (int i = 0; i < refs.Count; i++)
            {
                var reference = refs[i];
                if (reference.Deleted || reference.FormId == 0u)
                    continue;

                duplicatePlacedRefCounts.TryGetValue(reference.FormId, out int duplicateCount);
                duplicatePlacedRefCounts[reference.FormId] = duplicateCount + 1;
            }

            var staticVerts = new List<Vector3>();
            var staticIndices = new List<int>();
            for (int i = 0; i < refs.Count; i++)
            {
                var reference = refs[i];
                if (reference.Deleted)
                    continue;

                if (!string.IsNullOrWhiteSpace(reference.SoulId))
                {
                    staged.CapturedSouls.Add(new PlacedRefSoulEntry
                    {
                        PlacedRefId = reference.FormId,
                        SoulId = reference.SoulId.Trim(),
                    });
                }

                if (reference.LockLevel != 0
                    || !string.IsNullOrWhiteSpace(reference.KeyId)
                    || !string.IsNullOrWhiteSpace(reference.TrapId))
                {
                    staged.LockStates.Add(new PlacedRefLockEntry
                    {
                        PlacedRefId = reference.FormId,
                        LockLevel = reference.LockLevel,
                        Locked = (byte)((reference.LockLevel > 0 || !string.IsNullOrWhiteSpace(reference.KeyId)) ? 1 : 0),
                        KeyId = reference.KeyId?.Trim() ?? string.Empty,
                        TrapId = reference.TrapId?.Trim() ?? string.Empty,
                    });
                }

                CellBakery.ToUnityTransform(reference, out var pos, out var rot);
                bool hasBaseRecord = recordIndex.TryGet(reference.BaseId, out var rec);
                bool isDoorRecord = hasBaseRecord && rec.Tag == DoorTag;
                bool isStat = hasBaseRecord && rec.Tag == StatTag;
                var contentReference = ResolveGameplayContentReference(gameplayContentLookup, reference.BaseId);
                if (IsOpenMwHiddenMarker(reference.BaseId))
                {
                    int hiddenMarkerDoorMetaIndex = -1;
                    if (isDoorRecord)
                    {
                        hiddenMarkerDoorMetaIndex = staged.DoorEntries.Count;
                        BuildDoorEntry(reference, out var doorEntry);
                        staged.DoorEntries.Add(doorEntry);
                    }

                    staged.CollisionNoColliderCount++;
                    staged.PlacedRefs.Add(new StagedPlacedRefData(
                        string.Empty,
                        null,
                        reference.FormId,
                        hiddenMarkerDoorMetaIndex,
                        contentReference,
                        pos,
                        rot,
                        reference.Scale));
                    continue;
                }

                if (contentReference.Kind == ContentReferenceKind.Actor)
                {
                    staged.CollisionNoColliderCount++;
                    staged.PlacedRefs.Add(new StagedPlacedRefData(
                        string.Empty,
                        null,
                        reference.FormId,
                        -1,
                        contentReference,
                        pos,
                        rot,
                        reference.Scale));
                    continue;
                }

                var model = hasBaseRecord ? EnsureModelSource(rec, sharedBsa, bsaByName, modelCache) : null;

                if (model == null)
                {
                    if (IsLogicalOnlyWorldRef(contentReference.Kind))
                    {
                        staged.CollisionNoColliderCount++;
                        staged.PlacedRefs.Add(new StagedPlacedRefData(
                            string.Empty,
                            null,
                            reference.FormId,
                            -1,
                            contentReference,
                            pos,
                            rot,
                            reference.Scale));
                        continue;
                    }

                    RecordDroppedBakeRef(
                        workItem,
                        reference.FormId,
                        reference.BaseId,
                        hasBaseRecord ? rec.Model : string.Empty,
                        contentReference,
                        hasBaseRecord ? "model could not be loaded" : "missing base record");
                    staged.CollisionNoColliderCount++;
                    continue;
                }

                bool hasRenderableMeshes = model.Meshes.Length > 0;

                int doorMetaIndex = -1;
                if (isDoorRecord)
                {
                    doorMetaIndex = staged.DoorEntries.Count;
                    BuildDoorEntry(reference, out var doorEntry);
                    staged.DoorEntries.Add(doorEntry);
                }

                CollisionPayload collisionPayload = model.Collision;
                bool usedAutoVisualStaticCollision = false;
                if (isStat && collisionPayload.IsEmpty && !model.AutoVisualStaticCollision.IsEmpty)
                {
                    collisionPayload = model.AutoVisualStaticCollision;
                    usedAutoVisualStaticCollision = true;
                }

                if (!hasRenderableMeshes && collisionPayload.IsEmpty)
                {
                    if (IsLogicalOnlyWorldRef(contentReference.Kind))
                    {
                        staged.CollisionNoColliderCount++;
                        staged.PlacedRefs.Add(new StagedPlacedRefData(
                            string.Empty,
                            null,
                            reference.FormId,
                            -1,
                            contentReference,
                            pos,
                            rot,
                            reference.Scale));
                        continue;
                    }

                    RecordDroppedBakeRef(
                        workItem,
                        reference.FormId,
                        reference.BaseId,
                        rec.Model,
                        contentReference,
                        "model produced no renderable meshes or collision");
                    staged.CollisionNoColliderCount++;
                    continue;
                }

                bool combinedStaticEligible = IsCombinedStaticRefEligible(
                    staged,
                    isStat,
                    contentReference,
                    reference.FormId,
                    reference.BaseId,
                    model,
                    combinedStaticExclusions);
                PlacedRefCollisionAssignment collisionAssignment = ClassifyPlacedRefCollision(isStat, combinedStaticEligible, collisionPayload);
                if (isStat)
                    staged.CollisionStaticCandidateCount++;

                switch (collisionAssignment)
                {
                    case PlacedRefCollisionAssignment.CellStaticAggregate:
                        AppendTransformed(collisionPayload, pos, rot, reference.Scale, workItem.CellOrigin, staticVerts, staticIndices);
                        staged.CollisionStaticAggregateCount++;
                        staged.CollisionStaticAggregateTriangleCount += collisionPayload.TriangleCount;
                        if (usedAutoVisualStaticCollision)
                            staged.CollisionAutoVisualStaticCount++;
                        else
                            staged.CollisionAuthoredRootCount++;
                        break;
                    case PlacedRefCollisionAssignment.PerPlacedRef:
                        staged.CollisionAuthoredRootCount++;
                        staged.CollisionPerPlacedRefCount++;
                        break;
                    default:
                        if (model.CollisionSource == CollisionExtractionSource.ExplicitNoCollision)
                            staged.CollisionExplicitNoCollisionCount++;
                        else if (isStat && collisionPayload.IsEmpty)
                        {
                            staged.CollisionMissingPayloadCount++;
                            AddMissingCollisionSample(staged, model.ModelPath);
                        }

                        staged.CollisionNoColliderCount++;
                        break;
                }

                if (!hasRenderableMeshes && collisionAssignment == PlacedRefCollisionAssignment.CellStaticAggregate)
                    continue;

                if (isStat && model.HasObjectAnimation)
                    RecordAnimatedStaticRef(model.ModelPath);
                if (model.HasObjectAnimation
                    && model.HasUnsupportedObjectControllers
                    && IsObjectAnimationEligibleContent(contentReference.Kind))
                {
                    RecordUnsupportedObjectControllerRef(model.ModelPath);
                }

                staged.PlacedRefs.Add(new StagedPlacedRefData(
                    model.ModelPath,
                    model,
                    reference.FormId,
                    doorMetaIndex,
                    contentReference,
                    pos,
                    rot,
                    reference.Scale,
                    collisionAssignment == PlacedRefCollisionAssignment.PerPlacedRef,
                    combinedStaticEligible));
            }

            staged.StaticCollision = staticVerts.Count > 0
                ? new CellBakery.StaticCollision(staticVerts.ToArray(), staticIndices.ToArray())
                : default;

            return staged;
        }


        private static PlacedRefCollisionAssignment ClassifyPlacedRefCollision(bool isStat, bool combinedStaticEligible, in CollisionPayload collision)
        {
            if (collision.IsEmpty)
                return PlacedRefCollisionAssignment.NoCollider;

            return isStat && combinedStaticEligible
                ? PlacedRefCollisionAssignment.CellStaticAggregate
                : PlacedRefCollisionAssignment.PerPlacedRef;
        }


        private static bool IsCombinedStaticRefEligible(
            StagedCellData staged,
            bool isStat,
            ContentReference contentReference,
            uint placedRefId,
            string baseId,
            ModelSource model,
            CombinedStaticExclusionData exclusions)
        {
            if (!staged.BakeCombinedCellRenderChunks || staged.WorkItem.IsInterior || !isStat)
                return false;
            if (contentReference.Kind != ContentReferenceKind.Static)
                return false;
            if (IsMutableStaticRef(contentReference, placedRefId, baseId, exclusions))
                return false;
            return IsCombinedCellRenderEligibleStaticGraph(model?.ModelPath ?? string.Empty, model);
        }


        private static bool IsMutableStaticRef(
            ContentReference contentReference,
            uint placedRefId,
            string baseId,
            CombinedStaticExclusionData exclusions)
        {
            if (exclusions == null)
                return false;
            if (placedRefId != 0u
                && exclusions.MutablePlacedRefIds != null
                && exclusions.MutablePlacedRefIds.Contains(placedRefId))
                return true;
            if (contentReference.Kind != ContentReferenceKind.Static)
                return false;
            return IsScriptedStaticBaseId(baseId, exclusions);
        }


        private static bool IsScriptedStaticBaseId(string baseId, CombinedStaticExclusionData exclusions)
        {
            string normalized = ContentId.NormalizeId(baseId ?? string.Empty);
            return !string.IsNullOrEmpty(normalized)
                   && exclusions?.ScriptedStaticIds != null
                   && exclusions.ScriptedStaticIds.Contains(normalized);
        }


        private static bool IsMutablePlacedOrScriptedStaticRef(uint placedRefId, string baseId, CombinedStaticExclusionData exclusions)
        {
            if (exclusions == null)
                return false;
            return (placedRefId != 0u
                    && exclusions.MutablePlacedRefIds != null
                    && exclusions.MutablePlacedRefIds.Contains(placedRefId))
                   || IsScriptedStaticBaseId(baseId, exclusions);
        }


        private static CombinedStaticExclusionData BuildCombinedStaticExclusionData(GameplayContentData gameplayContent)
        {
            var mutablePlacedRefIds = new HashSet<uint>();
            var scriptedStaticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gameplayContent == null)
                return new CombinedStaticExclusionData(mutablePlacedRefIds, scriptedStaticIds);

            var statics = gameplayContent.Statics ?? Array.Empty<GenericRecordDef>();
            for (int i = 0; i < statics.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(statics[i].ScriptId))
                    continue;
                string normalized = ContentId.NormalizeId(statics[i].Id ?? string.Empty);
                if (!string.IsNullOrEmpty(normalized))
                    scriptedStaticIds.Add(normalized);
            }

            var instructions = gameplayContent.MorrowindScriptInstructions ?? Array.Empty<MorrowindScriptInstructionDef>();
            for (int i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                if (!IsPlacedRefMutationOpcode((MorrowindScriptOpcode)instruction.Opcode))
                    continue;
                if ((MorrowindScriptRefTargetMode)instruction.Operand0 != MorrowindScriptRefTargetMode.PlacedRef)
                    continue;
                uint placedRefId = unchecked((uint)instruction.Int0);
                if (placedRefId != 0u)
                    mutablePlacedRefIds.Add(placedRefId);
            }

            return new CombinedStaticExclusionData(mutablePlacedRefIds, scriptedStaticIds);
        }


        private static bool IsPlacedRefMutationOpcode(MorrowindScriptOpcode opcode)
            => opcode is MorrowindScriptOpcode.RequestSetDisabled
                or MorrowindScriptOpcode.Rotate
                or MorrowindScriptOpcode.SetAngle
                or MorrowindScriptOpcode.PositionCell
                or MorrowindScriptOpcode.Position
                or MorrowindScriptOpcode.SetPos
                or MorrowindScriptOpcode.MoveWorld
                or MorrowindScriptOpcode.Move
                or MorrowindScriptOpcode.SetAtStart;


        private static void AddMissingCollisionSample(StagedCellData staged, string modelPath)
        {
            if (staged.CollisionMissingPayloadSamples == null || staged.CollisionMissingPayloadSamples.Count >= 5)
                return;

            if (!string.IsNullOrWhiteSpace(modelPath) && !staged.CollisionMissingPayloadSamples.Contains(modelPath))
                staged.CollisionMissingPayloadSamples.Add(modelPath);
        }


        private static bool TryComputeAggregateWorldBounds(
            ModelSource model,
            Vector3 position,
            Quaternion rotation,
            float scale,
            out Bounds aggregateBounds)
        {
            aggregateBounds = default;
            if (model == null || model.Meshes == null || model.Meshes.Length == 0)
                return false;

            bool hasBounds = false;
            for (int i = 0; i < model.Meshes.Length; i++)
            {
                Bounds worldBounds = TransformBounds(model.Meshes[i].LocalBounds, position, rotation, scale);
                if (!hasBounds)
                {
                    aggregateBounds = worldBounds;
                    hasBounds = true;
                }
                else
                {
                    aggregateBounds.Encapsulate(worldBounds.min);
                    aggregateBounds.Encapsulate(worldBounds.max);
                }
            }

            return hasBounds;
        }


        private static Bounds TransformBounds(Bounds localBounds, Vector3 position, Quaternion rotation, float scale)
        {
            float absoluteScale = Mathf.Abs(scale);
            Vector3 scaledCenter = localBounds.center * scale;
            Vector3 scaledExtents = localBounds.extents * absoluteScale;
            Vector3 worldCenter = position + rotation * scaledCenter;

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 worldExtents = new(
                Mathf.Abs(right.x) * scaledExtents.x + Mathf.Abs(up.x) * scaledExtents.y + Mathf.Abs(forward.x) * scaledExtents.z,
                Mathf.Abs(right.y) * scaledExtents.x + Mathf.Abs(up.y) * scaledExtents.y + Mathf.Abs(forward.y) * scaledExtents.z,
                Mathf.Abs(right.z) * scaledExtents.x + Mathf.Abs(up.z) * scaledExtents.y + Mathf.Abs(forward.z) * scaledExtents.z);

            return new Bounds(worldCenter, worldExtents * 2f);
        }


        private static ModelSource EnsureModelSource(
            BaseRecord rec,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            return EnsureModelSource(rec.Model, sharedBsa, bsaByName, modelCache);
        }


        private static ModelSource EnsureModelSource(
            string modelPath,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            if (string.IsNullOrEmpty(modelPath))
                return null;

            string nifPath = NormalizeModelPath(modelPath);
            var lazy = modelCache.GetOrAdd(
                nifPath,
                path => new Lazy<ModelSource>(() =>
                {
                    if (!bsaByName.TryGetValue(path, out var entry))
                        return null;
                    try { return CreateModelSource(path, sharedBsa, entry); }
                    catch { return null; }
                }, LazyThreadSafetyMode.ExecutionAndPublication));

            return lazy.Value;
        }


        private static ModelSource EnsureRequiredModelSource(
            string modelPath,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            string context)
        {
            string nifPath = NormalizeModelPath(modelPath);
            if (string.IsNullOrEmpty(nifPath))
                throw new InvalidOperationException($"{context} has an empty model path.");
            if (sharedBsa == null || bsaByName == null || !bsaByName.TryGetValue(nifPath, out var entry))
                throw new InvalidOperationException($"{context} required model '{nifPath}' is missing from the archive.");

            try
            {
                var source = CreateModelSource(nifPath, sharedBsa, entry);
                modelCache[nifPath] = new Lazy<ModelSource>(() => source, LazyThreadSafetyMode.ExecutionAndPublication);
                return source;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{context} required model '{nifPath}' could not be loaded.", ex);
            }
        }


        private static ModelSource CreateModelSource(string path, BsaArchive sharedBsa, BsaEntry entry)
        {
            var nif = NifFile.Parse(path, sharedBsa.Read(entry));
            var built = NifMeshBuilder.BuildRaw(nif);
            var prefab = NifModelPrefabBuilder.Build(nif);
            var collisionResult = NifCollisionExtractor.Extract(nif);
            CollisionPayload authoredCollision = collisionResult.Source == CollisionExtractionSource.AuthoredRootCollision
                ? collisionResult.Payload
                : default;
            CollisionPayload autoVisualStaticCollision = collisionResult.Source == CollisionExtractionSource.AutoVisualStatic
                ? collisionResult.Payload
                : default;
            bool hasObjectAnimation = NifObjectAnimationAnalysis.HasSupportedObjectAnimation(nif);
            bool hasUnsupportedObjectControllers = NifObjectAnimationAnalysis.HasUnsupportedObjectControllers(nif);
            float effectControllerStopTime = NifEffectControllerAnalysis.ResolveMaxControllerStopTime(nif);
            return new ModelSource(
                path,
                nif,
                built.ToArray(),
                authoredCollision,
                autoVisualStaticCollision,
                collisionResult.Source,
                prefab,
                hasObjectAnimation,
                hasUnsupportedObjectControllers,
                effectControllerStopTime);
        }


        private static bool IsObjectAnimationEligibleContent(ContentReferenceKind kind)
        {
            return kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Door
                or ContentReferenceKind.Container
                or ContentReferenceKind.Light;
        }


        private static bool IsLogicalOnlyWorldRef(ContentReferenceKind kind)
        {
            return kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Light
                or ContentReferenceKind.LeveledItem
                or ContentReferenceKind.LeveledCreature;
        }


        private static bool IsOpenMwHiddenMarker(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            string marker = id.Trim();
            return string.Equals(marker, "prisonmarker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(marker, "divinemarker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(marker, "templemarker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(marker, "northmarker", StringComparison.OrdinalIgnoreCase);
        }


        private static void RecordAnimatedStaticRef(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return;

            s_AnimatedStaticRefCounts.AddOrUpdate(modelPath, 1, (_, count) => count + 1);
        }


        private static void RecordUnsupportedObjectControllerRef(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return;

            s_UnsupportedObjectControllerRefCounts.AddOrUpdate(modelPath, 1, (_, count) => count + 1);
        }


        private static void RecordDroppedBakeRef(
            CellBakeWorkItem workItem,
            uint placedRefId,
            string baseId,
            string modelPath,
            ContentReference contentReference,
            string reason)
        {
            string cellLabel = FormatCellBakeLabel(workItem);
            string message =
                $"[VVardenfell][DroppedRefBake] {cellLabel} ref {placedRefId:X8} base='{baseId ?? string.Empty}' "
                + $"model='{modelPath ?? string.Empty}' content={contentReference.Kind}:{contentReference.HandleValue} "
                + $"was not baked: {reason}.";
            s_DroppedBakeRefWarnings.TryAdd(message, 0);
        }

        private static void FlushAnimatedStaticRefWarnings()
        {
            if (s_AnimatedStaticRefCounts.IsEmpty)
                return;

            foreach (var pair in s_AnimatedStaticRefCounts)
            {
                UnityEngine.Debug.LogWarning(
                    $"[VVardenfell][ObjectAnimation] static model '{pair.Key}' has embedded object animation on {pair.Value} placed refs; detected but disabled for OpenMW class-gated parity.");
            }

            s_AnimatedStaticRefCounts.Clear();
        }


        private static void FlushUnsupportedObjectControllerWarnings()
        {
            if (s_UnsupportedObjectControllerRefCounts.IsEmpty)
                return;

            foreach (var pair in s_UnsupportedObjectControllerRefCounts)
            {
                UnityEngine.Debug.LogWarning(
                    $"[VVardenfell][ObjectAnimation] model '{pair.Key}' has unsupported object-animation controller families on {pair.Value} placed refs; transform/visibility tracks remain the only simulated controller families in V1.");
            }

            s_UnsupportedObjectControllerRefCounts.Clear();
        }


        private static void FlushDroppedBakeRefWarnings()
        {
            if (s_DroppedBakeRefWarnings.IsEmpty)
                return;

            foreach (var pair in s_DroppedBakeRefWarnings)
                UnityEngine.Debug.LogWarning(pair.Key);

            s_DroppedBakeRefWarnings.Clear();
        }


        private static string FormatCellBakeLabel(CellBakeWorkItem workItem)
        {
            return workItem.IsInterior
                ? $"interior '{workItem.Cell.Name ?? string.Empty}'"
                : $"exterior ({workItem.Cell.GridX},{workItem.Cell.GridY})";
        }


        static string NormalizeModelPath(string modelPath)
            => ActorVisualContentRules.NormalizeModelPath(modelPath);


        static string ResolveActorAnimationModelPath(string modelPath, Dictionary<string, BsaEntry> bsaByName)
        {
            string model = NormalizeModelPath(modelPath);
            if (string.IsNullOrEmpty(model))
                return model;

            string corrected = BuildPrefixedActorModelPath(model);
            string correctedKf = BuildCompanionKfPath(corrected);
            if (bsaByName == null || !bsaByName.ContainsKey(correctedKf))
                return model;

            if (!bsaByName.ContainsKey(corrected))
                throw new InvalidOperationException(
                    $"Actor model '{model}' resolves animation source '{correctedKf}' but required actor model '{corrected}' is missing.");

            return corrected;
        }


        static string BuildPrefixedActorModelPath(string modelPath)
            => ActorVisualContentRules.BuildPrefixedActorModelPath(modelPath);


        static string BuildCompanionKfPath(string modelPath)
            => ActorVisualContentRules.BuildCompanionKfPath(modelPath);


        private static void SeedRuntimeSpawnableModels(
            GameplayContentData gameplayContent,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            if (gameplayContent == null)
                return;

            var actors = gameplayContent.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Kind != ActorDefKind.Creature)
                    continue;

                EnsureModelSource(actors[i].Model, sharedBsa, bsaByName, modelCache);
                string actorModel = ResolveActorAnimationModelPath(actors[i].Model, bsaByName);
                if (!string.IsNullOrEmpty(actorModel)
                    && !string.Equals(actorModel, NormalizeModelPath(actors[i].Model), StringComparison.OrdinalIgnoreCase))
                {
                    EnsureRequiredModelSource(
                        actorModel,
                        sharedBsa,
                        bsaByName,
                        modelCache,
                        $"Creature actor '{actors[i].Id}' animation model seed");
                }
            }

            var items = gameplayContent.Items ?? Array.Empty<BaseDef>();
            for (int i = 0; i < items.Length; i++)
                EnsureModelSource(items[i].Model, sharedBsa, bsaByName, modelCache);

            var lights = gameplayContent.Lights ?? Array.Empty<LightDef>();
            for (int i = 0; i < lights.Length; i++)
                EnsureModelSource(lights[i].Model, sharedBsa, bsaByName, modelCache);

            SeedDefaultNpcAnimationModels(sharedBsa, bsaByName, modelCache);

            var bodyParts = gameplayContent.ActorBodyParts ?? Array.Empty<ActorBodyPartDef>();
            for (int i = 0; i < bodyParts.Length; i++)
                EnsureModelSource(bodyParts[i].Model, sharedBsa, bsaByName, modelCache);
        }


        static void SeedDefaultNpcAnimationModels(
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            string[] models =
            {
                "meshes\\base_anim.nif",
                "meshes\\base_anim_female.nif",
                "meshes\\base_animkna.nif",
                "meshes\\base_anim_female.1st.nif",
                "meshes\\base_animkna.1st.nif",
                "meshes\\xbase_anim.1st.nif",
            };

            for (int i = 0; i < models.Length; i++)
                EnsureRequiredModelSource(
                    models[i],
                    sharedBsa,
                    bsaByName,
                    modelCache,
                    "NPC animation model seed");
        }


        private static IEnumerator PrepareDirtyCellsIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            Dictionary<string, Dictionary<int, string>> ltexMapsBySource)
        {
            progress.Stage = "Preparing dirty cells";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to prepare" : $"Preparing dirty cells 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
            {
                yield break;
            }

            int completed = 0;
            Exception prepareFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var task = Task.Run(() =>
            {
                try
                {
                    int nextIndex = 0;
                    int failureSignaled = 0;
                    var workers = new Task[maxWorkers];
                    for (int worker = 0; worker < maxWorkers; worker++)
                    {
                        workers[worker] = Task.Factory.StartNew(() =>
                        {
                            while (Volatile.Read(ref failureSignaled) == 0)
                            {
                                int index = Interlocked.Increment(ref nextIndex) - 1;
                                if (index >= dirtyCells.Count)
                                    break;

                                try
                                {
                                    PrepareDirtyCell(dirtyCells[index], ltexMapsBySource);
                                    Interlocked.Increment(ref completed);
                                }
                                catch (Exception ex)
                                {
                                    if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                        prepareFailure = ex;
                                    break;
                                }
                            }
                        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }

                    Task.WaitAll(workers);
                }
                catch (Exception ex)
                {
                    prepareFailure ??= ex;
                }
                finally
                {
                }
            });

            while (!task.IsCompleted)
            {
                int current = completed;
                progress.Current = current;
                progress.Label = $"Preparing dirty cells {current}/{dirtyCells.Count}";
                yield return null;
            }

            if (prepareFailure != null)
            {
                throw prepareFailure;
            }

            progress.Current = dirtyCells.Count;
            progress.Label = $"Preparing dirty cells {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;
        }


        }
    }
