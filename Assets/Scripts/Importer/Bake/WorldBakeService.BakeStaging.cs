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
        {
            string esmPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(esmPath) || !File.Exists(bsaPath))
            {
                progress.Error = "Morrowind.esm or Morrowind.bsa missing under the configured install path.";
                progress.Done = true;
                yield break;
            }

            CachePaths.Warmup();
            CachePaths.EnsureExists();
            var gameplayContentLookup = BuildGameplayContentLookup(gameplayContent);

            progress.Stage = "Source Indexing";
            progress.Label = "Opening archives";
            progress.Current = 0;
            progress.Total = 5;
            yield return null;

            using var sharedBsa = BsaArchive.Open(bsaPath);

            progress.Label = "Building record index";
            progress.Current = 1;
            yield return null;
            RecordIndex recordIndex;
            using (var esm = new EsmReader(esmPath))
                recordIndex = RecordIndex.Build(esm);

            progress.Label = "Enumerating cells";
            progress.Current = 2;
            yield return null;
            var exteriorCells = new List<CellHeader>(2048);
            var interiorCells = new List<CellHeader>(2048);
            using (var esm = new EsmReader(esmPath))
            {
                foreach (var cell in CellIndex.Enumerate(esm))
                {
                    if (cell.IsInterior)
                        interiorCells.Add(cell);
                    else
                        exteriorCells.Add(cell);
                }
            }

            progress.Label = "Indexing terrain";
            progress.Current = 3;
            yield return null;
            Dictionary<(int, int), long> landOffsets;
            using (var esm = new EsmReader(esmPath))
                landOffsets = LandIndex.BuildOffsetMap(esm);

            progress.Label = "Indexing land textures";
            progress.Current = 4;
            yield return null;
            Dictionary<int, string> ltexMap;
            using (var esm = new EsmReader(esmPath))
                ltexMap = LtexIndex.Build(esm);

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

            var textureResolver = new TexturePathResolver(sharedBsa);
            var bakeryMeshes = new MeshBakery();
            bakeryMeshes.TryLoadExisting(CachePaths.MeshCatalog, CachePaths.Meshes);
            var bakeryMaterials = new MaterialBakery();
            bakeryMaterials.TryLoadExisting(CachePaths.MaterialCatalog);
            var bakeryTextures = new TextureBakery(sharedBsa, textureResolver);
            bakeryTextures.TryLoadExisting(CachePaths.TextureCatalog);
            int defaultTexIdx = bakeryTextures.AddOrGet(LtexIndex.DefaultTexturePath);
            SeedSkyWeatherTextures(gameplayContent, bakeryTextures);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);
            bakeryLayers.TryLoadExisting(CachePaths.TerrainLayers);
            var bakeryCollisions = new CollisionBakery();
            bakeryCollisions.TryLoadExisting(CachePaths.CollisionCatalog);
            var bakeryModelPrefabs = new ModelPrefabBakery();
            var bakeryActorAnimations = new ActorAnimationBakery();
            var bakeryObjectAnimations = new ObjectAnimationBakery(gameplayContent);
            var modelCache = new ConcurrentDictionary<string, Lazy<ModelSource>>(StringComparer.OrdinalIgnoreCase);

            progress.Current = 1;
            yield return null;

            var bsaByName = new Dictionary<string, BsaEntry>(sharedBsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in sharedBsa.Entries)
                bsaByName[entry.Name] = entry;

            SeedRuntimeSpawnableModels(gameplayContent, sharedBsa, bsaByName, modelCache);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var workItems = new List<CellBakeWorkItem>(exteriorCells.Count + interiorCells.Count);
            for (int i = 0; i < exteriorCells.Count; i++)
            {
                var cell = exteriorCells[i];
                landOffsets.TryGetValue((cell.GridX, cell.GridY), out var landOffset);
                var cellOrigin = new Vector3(cell.GridX * cellMeters, 0f, cell.GridY * cellMeters);
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    false,
                    landOffset,
                    BuildExteriorKey(cell.GridX, cell.GridY),
                    CachePaths.CellFile(cell.GridX, cell.GridY),
                    cellOrigin));
            }

            for (int i = 0; i < interiorCells.Count; i++)
            {
                var cell = interiorCells[i];
                string interiorId = cell.Name ?? string.Empty;
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    true,
                    0,
                    BuildInteriorKey(interiorId),
                    CachePaths.InteriorCellFile(interiorId),
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
                        () => new WorkerContext(esmPath),
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
                                    sharedBsa,
                                    bsaByName,
                                    ltexMap,
                                    previousStateByKey,
                                    modelCache,
                                    false);
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
            var expectedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirtyCells = new List<StagedCellData>();

            for (int i = 0; i < stagedCells.Length; i++)
            {
                var staged = stagedCells[i];
                expectedOutputs.Add(staged.WorkItem.OutputPath);
                if (staged.NeedsWrite)
                    dirtyCells.Add(staged);

                cellStates[i] = BuildCellState(staged);
                progress.Current = i + 1;
                if (((i + 1) & 7) == 0)
                    yield return null;
            }

            yield return PrepareDirtyCellsIncremental(dirtyCells, progress, ltexMap);
            bakeryActorAnimations.ConfigureCreatureAnimationSources(gameplayContent, bsaByName);
            yield return BuildModelPrefabsIncremental(modelCache, progress, bakeryModelPrefabs, bakeryActorAnimations, bakeryObjectAnimations, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryCollisions, sharedBsa, bsaByName, gameplayContent);
            bakeryActorAnimations.BuildVisualRecipes(gameplayContent, bsaByName);
            yield return ResolveDirtyCellIndicesIncremental(dirtyCells, progress, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryLayers, bakeryCollisions, bakeryModelPrefabs);
            FlushDroppedBakeRefWarnings();

            for (int i = 0; i < stagedCells.Length; i++)
                cellStates[i] = BuildCellState(stagedCells[i]);

            yield return WriteDirtyCellsIncremental(dirtyCells, progress, cellMeters);

            progress.Stage = "Writing";
            progress.Current = 0;
            progress.Total = 12;

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
                ModelPrefabFile.Write(CachePaths.ModelPrefabs, bakeryModelPrefabs.BuildCatalog());

            progress.Label = "actor_animations.bin";
            progress.Current = 4;
            yield return null;
            if (bakeryActorAnimations.Modified || !ActorAnimationFile.IsCurrentVersion(CachePaths.ActorAnimations))
                ActorAnimationFile.Write(CachePaths.ActorAnimations, bakeryActorAnimations.BuildCatalog());

            progress.Label = "textures.bin";
            progress.Current = 5;
            yield return null;
            if (bakeryTextures.Modified || !File.Exists(CachePaths.TexturesIndex) || !File.Exists(CachePaths.TextureCatalog))
            {
                bakeryTextures.WriteIndex(CachePaths.TexturesIndex);
                bakeryTextures.WriteCatalog(CachePaths.TextureCatalog);
            }

            progress.Label = "terrain_layers.bin";
            progress.Current = 6;
            yield return null;
            if (bakeryLayers.Modified || !File.Exists(CachePaths.TerrainLayers))
                bakeryLayers.WriteTo(CachePaths.TerrainLayers);

            progress.Label = "collisions.bin";
            progress.Current = 7;
            yield return null;
            if (bakeryCollisions.Modified || !File.Exists(CachePaths.Collisions) || !File.Exists(CachePaths.CollisionCatalog))
            {
                bakeryCollisions.WriteTo(CachePaths.Collisions);
                bakeryCollisions.WriteCatalog(CachePaths.CollisionCatalog);
            }

            progress.Label = "Pruning stale cells";
            progress.Current = 8;
            yield return null;
            PruneOrphans(CachePaths.CellsDir, expectedOutputs);
            PruneOrphans(CachePaths.InteriorCellsDir, expectedOutputs);

            progress.Label = "mesh_cache_report.txt";
            progress.Current = 9;
            yield return null;

            progress.Label = "world_collision_validation.txt";
            progress.Current = 10;
            yield return null;

            progress.Label = "ui.bin";
            progress.Current = 11;
            yield return null;
            UiAssetBakery.Bake(config, sharedBsa, progress);

            progress.Label = "manifest.bin";
            progress.Current = 12;
            yield return null;
            var manifest = BakeManifest.FromCurrentSources(
                esmPath,
                bsaPath,
                InstalledContentSources.ResolveGameplayRecordSources(config.InstallPath));
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


        private static StagedCellData StageCell(
            WorkerContext worker,
            CellBakeWorkItem workItem,
            RecordIndex recordIndex,
            GameplayContentData gameplayContent,
            Dictionary<string, ContentReference> gameplayContentLookup,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            Dictionary<int, string> ltexMap,
            Dictionary<string, BakeManifest.BakedCellState> previousStateByKey,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            bool forceRebuild)
        {
            List<CellReference> refs;
            try
            {
                refs = CellReader.ReadReferences(worker.RefsReader, workItem.Cell);
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
                    land = LandIndex.ReadAt(worker.LandReader, workItem.LandOffset);
                }
                catch
                {
                    land = null;
                }
            }

            string fingerprint = ComputeFingerprint(workItem, refs, land, recordIndex, ltexMap);
            bool hasPrevious = previousStateByKey.TryGetValue(workItem.Key, out var previousState);
            bool canReuse = !forceRebuild
                && hasPrevious
                && previousState.PipelineVersion == CacheFormat.WorldBakePipelineVersion
                && string.Equals(previousState.Fingerprint, fingerprint, StringComparison.Ordinal)
                && File.Exists(workItem.OutputPath)
                && TryValidateCellFile(workItem.OutputPath, workItem.IsInterior, workItem.Cell.Name, out _);

            var staged = new StagedCellData
            {
                WorkItem = workItem,
                PreviousState = hasPrevious ? previousState : null,
                Fingerprint = fingerprint,
                Environment = workItem.Cell.Environment,
                Land = land,
                NeedsWrite = !canReuse,
                PlacedRefs = canReuse ? null : new List<StagedPlacedRefData>(refs.Count),
                PendingRefs = canReuse ? null : new List<StagedRefData>(refs.Count),
                DoorEntries = canReuse ? null : new List<DoorRefEntry>(),
                CapturedSouls = canReuse ? null : new List<PlacedRefSoulEntry>(),
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

                CellBakery.ToUnityTransform(reference, out var pos, out var rot);
                bool hasBaseRecord = recordIndex.TryGet(reference.BaseId, out var rec);
                bool isDoorRecord = hasBaseRecord && rec.Tag == DoorTag;
                bool isStat = hasBaseRecord && rec.Tag == StatTag;
                var contentReference = ResolveGameplayContentReference(gameplayContentLookup, reference.BaseId);
                if (contentReference.Kind == ContentReferenceKind.Actor)
                {
                    staged.CollisionNoColliderCount++;
                    staged.PlacedRefs.Add(new StagedPlacedRefData(
                        string.Empty,
                        reference.FormId,
                        -1,
                        contentReference,
                        pos,
                        rot,
                        reference.Scale));
                    continue;
                }

                var model = hasBaseRecord ? EnsureModelSource(rec, sharedBsa, bsaByName, modelCache) : null;

                if (model == null || model.Meshes.Length == 0)
                {
                    if (IsLogicalOnlyWorldRef(contentReference.Kind))
                    {
                        staged.CollisionNoColliderCount++;
                        staged.PlacedRefs.Add(new StagedPlacedRefData(
                            string.Empty,
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
                        hasBaseRecord ? "model produced no renderable meshes" : "missing base record");
                    staged.CollisionNoColliderCount++;
                    continue;
                }

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

                PlacedRefCollisionAssignment collisionAssignment = ClassifyPlacedRefCollision(isStat, collisionPayload);
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
                    reference.FormId,
                    doorMetaIndex,
                    contentReference,
                    pos,
                    rot,
                    reference.Scale,
                    collisionAssignment == PlacedRefCollisionAssignment.PerPlacedRef));
            }

            staged.StaticCollision = staticVerts.Count > 0
                ? new CellBakery.StaticCollision(staticVerts.ToArray(), staticIndices.ToArray())
                : default;

            return staged;
        }


        private static PlacedRefCollisionAssignment ClassifyPlacedRefCollision(bool isStat, in CollisionPayload collision)
        {
            if (collision.IsEmpty)
                return PlacedRefCollisionAssignment.NoCollider;

            return isStat
                ? PlacedRefCollisionAssignment.CellStaticAggregate
                : PlacedRefCollisionAssignment.PerPlacedRef;
        }


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
            return new ModelSource(
                path,
                nif,
                built.ToArray(),
                authoredCollision,
                autoVisualStaticCollision,
                collisionResult.Source,
                prefab,
                hasObjectAnimation,
                hasUnsupportedObjectControllers);
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
            Dictionary<int, string> ltexMap)
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
                                    PrepareDirtyCell(dirtyCells[index], ltexMap);
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
