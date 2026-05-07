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
        private static IEnumerator BuildModelPrefabsIncremental(
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            BakeProgress progress,
            ModelPrefabBakery modelPrefabs,
            VfxEffectBakery vfxEffects,
            ActorAnimationBakery actorAnimations,
            ObjectAnimationBakery objectAnimations,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            CollisionBakery collisions,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            GameplayContentData gameplayContent,
            ConcurrentDictionary<string, byte> requiredVfxModels)
        {
            var keys = new List<string>(modelCache.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var actorBodyPartModels = BuildActorBodyPartModelSet(gameplayContent, bsaByName);

            progress.Stage = "Model Prefabs";
            progress.Total = Math.Max(1, keys.Count);
            progress.Current = 0;
            progress.Label = keys.Count == 0 ? "No model prefabs to bake" : $"Building model prefabs 0/{keys.Count}";
            yield return null;

            if (keys.Count == 0)
                yield break;

            var skinReferenceSkeletons = BuildActorBodyPartSkinReferenceSkeletonMap(gameplayContent, bsaByName);
            var extractedReferenceSkeletons = new Dictionary<string, ActorSkeletonDef>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                if (modelCache.TryGetValue(keys[i], out var lazy))
                {
                    var source = lazy.Value;
                    if (source?.Prefab != null)
                    {
                        string normalizedModelPath = NormalizeModelPath(source.ModelPath);
                        bool forceActorModel = actorBodyPartModels.Contains(normalizedModelPath);
                        ActorAnimationBakery.Assignment actorAnimation = default;
                        bool actorAnimationAssigned = false;
                        if (forceActorModel
                            && skinReferenceSkeletons.TryGetValue(normalizedModelPath, out var skinReferenceSkeletonPaths)
                            && skinReferenceSkeletonPaths.Count > 0)
                        {
                            var sortedReferenceSkeletonPaths = new List<string>(skinReferenceSkeletonPaths);
                            sortedReferenceSkeletonPaths.Sort(StringComparer.OrdinalIgnoreCase);
                            for (int referenceIndex = 0; referenceIndex < sortedReferenceSkeletonPaths.Count; referenceIndex++)
                            {
                                string skinReferenceSkeletonPath = sortedReferenceSkeletonPaths[referenceIndex];
                                ActorSkeletonDef skinReferenceSkeleton = GetOrExtractActorSkeleton(
                                    skinReferenceSkeletonPath,
                                    sharedBsa,
                                    bsaByName,
                                    modelCache,
                                    extractedReferenceSkeletons);

                                var referenceAssignment = actorAnimations.GetOrAddModel(
                                    source.ModelPath,
                                    source.Nif,
                                    source.Prefab,
                                    meshes,
                                    materials,
                                    textures,
                                    sharedBsa,
                                    bsaByName,
                                    forceActorModel,
                                    skinReferenceSkeleton,
                                    skinReferenceSkeletonPath);

                                if (!actorAnimationAssigned || !actorAnimation.HasValue)
                                    actorAnimation = referenceAssignment;
                                actorAnimationAssigned = true;
                            }
                        }
                        else
                        {
                            actorAnimation = actorAnimations.GetOrAddModel(
                                source.ModelPath,
                                source.Nif,
                                source.Prefab,
                                meshes,
                                materials,
                                textures,
                                sharedBsa,
                                bsaByName,
                                forceActorModel);
                            actorAnimationAssigned = true;
                        }

                        var objectAnimation = objectAnimations.GetOrAddModel(source.ModelPath, source.Nif, source.Prefab);
                        vfxEffects.GetOrAddModel(
                            source.ModelPath,
                            source.Nif,
                            requiredVfxModels != null && requiredVfxModels.ContainsKey(NormalizeModelPath(source.ModelPath)),
                            textures);
                        modelPrefabs.GetOrAdd(
                            source.ModelPath,
                            source.Prefab,
                            meshes,
                            materials,
                            textures,
                            collisions,
                            actorAnimation,
                            objectAnimation,
                            source.EffectControllerStopTime);
                    }
                }

                progress.Current = i + 1;
                progress.Label = $"Building model prefabs {i + 1}/{keys.Count}";
                if (((i + 1) & 31) == 0 || i + 1 == keys.Count)
                    yield return null;
            }

            EnsureDefaultNpcAnimationAssignments(
                actorAnimations,
                modelCache,
                sharedBsa,
                bsaByName,
                meshes,
                materials,
                textures);
        }


        static void EnsureDefaultNpcAnimationAssignments(
            ActorAnimationBakery actorAnimations,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures)
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
            {
                string normalized = NormalizeModelPath(models[i]);
                var source = EnsureRequiredModelSource(
                    normalized,
                    sharedBsa,
                    bsaByName,
                    modelCache,
                    "NPC animation assignment");

                actorAnimations.GetOrAddModel(
                    source.ModelPath,
                    source.Nif,
                    source.Prefab,
                    meshes,
                    materials,
                    textures,
                    sharedBsa,
                    bsaByName,
                    forceActorModel: true);
            }
        }


        static HashSet<string> BuildActorBodyPartModelSet(GameplayContentData gameplayContent, Dictionary<string, BsaEntry> bsaByName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDefaultNpcAnimationModels(result);
            var bodyParts = gameplayContent?.ActorBodyParts ?? Array.Empty<ActorBodyPartDef>();
            for (int i = 0; i < bodyParts.Length; i++)
            {
                string model = NormalizeModelPath(bodyParts[i].Model);
                if (!string.IsNullOrEmpty(model))
                    result.Add(model);
            }

            var actors = gameplayContent?.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Kind != ActorDefKind.Creature)
                    continue;

                string actorModel = ResolveActorAnimationModelPath(actors[i].Model, bsaByName);
                if (!string.IsNullOrEmpty(actorModel))
                    result.Add(actorModel);
            }

            return result;
        }


        static void AddDefaultNpcAnimationModels(HashSet<string> result)
        {
            if (result == null)
                return;

            result.Add("meshes\\base_anim.nif");
            result.Add("meshes\\base_anim_female.nif");
            result.Add("meshes\\base_animkna.nif");
            result.Add("meshes\\base_anim_female.1st.nif");
            result.Add("meshes\\base_animkna.1st.nif");
            result.Add("meshes\\xbase_anim.1st.nif");
        }


        static Dictionary<string, HashSet<string>> BuildActorBodyPartSkinReferenceSkeletonMap(
            GameplayContentData gameplayContent,
            Dictionary<string, BsaEntry> bsaByName)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var races = BuildRaceLookup(gameplayContent);
            var bodyParts = gameplayContent?.ActorBodyParts ?? Array.Empty<ActorBodyPartDef>();

            for (int i = 0; i < bodyParts.Length; i++)
            {
                var part = bodyParts[i];
                if (part.Type != ActorBodyPartMeshType.Skin
                    && part.Type != ActorBodyPartMeshType.Clothing
                    && part.Type != ActorBodyPartMeshType.Armor)
                    continue;

                if (part.Type != ActorBodyPartMeshType.Skin)
                {
                    if (part.FirstPerson != 0)
                        AddFirstPersonEquipmentSkinReferenceTargets(result, part, bsaByName);
                    else
                        AddEquipmentSkinReferenceTargets(result, part, bsaByName);
                    continue;
                }

                if (part.FirstPerson != 0)
                    continue;

                string targetSkeleton = ResolveNpcSkeletonModel(
                    part.FirstPerson != 0,
                    part.Female != 0,
                    IsBeastRace(part.RaceId, races),
                    bsaByName);
                AddSkinReferenceTarget(result, part.Model, targetSkeleton, part.Id);
            }

            var bodyPartsById = BuildBodyPartLookup(bodyParts);
            var actors = gameplayContent?.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor.Kind != ActorDefKind.Npc)
                    continue;

                bool female = (actor.Flags & 0x1u) != 0;
                bool beast = IsBeastRace(actor.RaceId, races);
                string targetSkeleton = ResolveNpcSkeletonModel(firstPerson: false, female, beast, bsaByName);
                if (string.IsNullOrEmpty(targetSkeleton))
                    continue;

                AddExplicitActorBodyPartTarget(result, bodyPartsById, actor.HeadId, targetSkeleton);
                AddExplicitActorBodyPartTarget(result, bodyPartsById, actor.HairId, targetSkeleton);

                for (int partReference = (int)ActorSkinPartReferenceType.Neck;
                     partReference < (int)ActorSkinPartReferenceType.Count;
                     partReference++)
                {
                    var reference = (ActorSkinPartReferenceType)partReference;
                    if (!IsBaseSkinPartReference(reference))
                        continue;

                    if (TryResolveNpcRaceBodyPart(
                            bodyParts,
                            actor.RaceId,
                            reference,
                            female,
                            firstPerson: false,
                            out var part))
                    {
                        AddSkinReferenceTarget(result, part.Model, targetSkeleton, part.Id);
                    }
                }
            }

            return result;
        }


        static void AddEquipmentSkinReferenceTargets(
            Dictionary<string, HashSet<string>> result,
            ActorBodyPartDef part,
            Dictionary<string, BsaEntry> bsaByName)
        {
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(false, false, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(false, true, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(false, false, true, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(false, true, true, bsaByName), part.Id);

            if (!IsFirstPersonMeshPart(part.Part))
                return;

            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, false, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, true, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, false, true, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, true, true, bsaByName), part.Id);
        }


        static void AddFirstPersonEquipmentSkinReferenceTargets(
            Dictionary<string, HashSet<string>> result,
            ActorBodyPartDef part,
            Dictionary<string, BsaEntry> bsaByName)
        {
            if (!IsFirstPersonMeshPart(part.Part))
                return;

            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, false, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, true, false, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, false, true, bsaByName), part.Id);
            AddSkinReferenceTarget(result, part.Model, ResolveNpcSkeletonModel(true, true, true, bsaByName), part.Id);
        }


        static ActorSkeletonDef GetOrExtractActorSkeleton(
            string modelPath,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            Dictionary<string, ActorSkeletonDef> skeletonCache)
        {
            string normalized = NormalizeModelPath(modelPath);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (skeletonCache.TryGetValue(normalized, out var cached))
                return cached;

            var source = EnsureModelSource(normalized, sharedBsa, bsaByName, modelCache);
            var skeleton = source?.Nif != null
                ? NifActorAnimationExtractor.ExtractSkeleton(source.Nif)
                : null;
            skeletonCache[normalized] = skeleton;
            return skeleton;
        }


        static Dictionary<string, RaceDef> BuildRaceLookup(GameplayContentData gameplayContent)
        {
            var result = new Dictionary<string, RaceDef>(StringComparer.OrdinalIgnoreCase);
            var races = gameplayContent?.Races ?? Array.Empty<RaceDef>();
            for (int i = 0; i < races.Length; i++)
            {
                string id = races[i].Id;
                if (!string.IsNullOrWhiteSpace(id) && !result.ContainsKey(id))
                    result[id] = races[i];
            }

            return result;
        }


        static Dictionary<string, ActorBodyPartDef> BuildBodyPartLookup(ActorBodyPartDef[] bodyParts)
        {
            var result = new Dictionary<string, ActorBodyPartDef>(StringComparer.OrdinalIgnoreCase);
            bodyParts ??= Array.Empty<ActorBodyPartDef>();
            for (int i = 0; i < bodyParts.Length; i++)
            {
                string id = ContentId.NormalizeId(bodyParts[i].Id);
                if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                    result[id] = bodyParts[i];
            }

            return result;
        }


        static bool IsBeastRace(string raceId, Dictionary<string, RaceDef> races)
            => ActorVisualContentRules.IsBeastRace(raceId, races);


        static bool IsPlayerActor(ActorDef actor)
            => ActorVisualContentRules.IsPlayerActor(actor);


        static void AddExplicitActorBodyPartTarget(
            Dictionary<string, HashSet<string>> targets,
            Dictionary<string, ActorBodyPartDef> bodyPartsById,
            string bodyPartId,
            string targetSkeleton)
        {
            if (string.IsNullOrWhiteSpace(bodyPartId)
                || bodyPartsById == null
                || !bodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId), out var part)
                || part.Type != ActorBodyPartMeshType.Skin)
            {
                return;
            }

            AddSkinReferenceTarget(targets, part.Model, targetSkeleton, part.Id);
        }


        static void AddSkinReferenceTarget(
            Dictionary<string, HashSet<string>> targets,
            string bodyPartModel,
            string targetSkeleton,
            string contextId)
        {
            string model = NormalizeModelPath(bodyPartModel);
            if (string.IsNullOrEmpty(model))
                throw new InvalidOperationException($"Actor skin body part '{contextId}' has no model path.");
            if (string.IsNullOrEmpty(targetSkeleton))
                throw new InvalidOperationException($"Actor skin body part '{contextId}' has no target skeleton for model '{model}'.");

            targetSkeleton = NormalizeModelPath(targetSkeleton);
            if (!targets.TryGetValue(model, out var references))
            {
                references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                targets[model] = references;
            }

            references.Add(targetSkeleton);
        }


        static string ResolveNpcSkeletonModel(
            bool firstPerson,
            bool female,
            bool beast,
            Dictionary<string, BsaEntry> bsaByName)
            => ActorVisualContentRules.ResolveNpcSkeletonModel(firstPerson, female, beast);


        static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorSkinPartReferenceType partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
            => ActorVisualContentRules.TryResolveNpcRaceBodyPart(
                bodyParts,
                raceId,
                ToVisualPartReference(partReference),
                female,
                firstPerson,
                out result);


        static int ResolveNpcRaceBodyPartScore(
            bool firstPerson,
            bool female,
            bool isFirstPersonArmPart,
            bool partFirstPerson,
            bool partFemale)
            => ActorVisualContentRules.ResolveNpcRaceBodyPartScore(
                firstPerson,
                female,
                isFirstPersonArmPart,
                partFirstPerson,
                partFemale);


        static bool IsBaseSkinPartReference(ActorSkinPartReferenceType type)
            => ActorVisualContentRules.IsBaseSkinPartReference(ToVisualPartReference(type));


        static bool IsFirstPersonPartReference(ActorSkinPartReferenceType type)
            => ActorVisualContentRules.IsFirstPersonPartReference(ToVisualPartReference(type));


        static bool IsFirstPersonMeshPart(ActorBodyPartMeshPart part)
            => ActorVisualContentRules.IsFirstPersonMeshPart(part);


        static ActorBodyPartMeshPart GetMeshPart(ActorSkinPartReferenceType type)
            => ActorVisualMappingPolicy.TryGetMeshPart(ToVisualPartReference(type), out var meshPart)
                ? meshPart
                : ActorBodyPartMeshPart.Chest;


        static ActorVisualPartReference ToVisualPartReference(ActorSkinPartReferenceType type)
            => (ActorVisualPartReference)(byte)type;


        private static void PrepareDirtyCell(StagedCellData staged, Dictionary<int, string> ltexMap)
        {
            try
            {
            if (staged.PendingRefs == null)
                return;

            if (staged.Land != null && staged.Land.VtexIndices != null)
            {
                try
                {
                    var texturePaths = new string[LandRecord.NumTextures];
                    for (int i = 0; i < LandRecord.NumTextures; i++)
                        texturePaths[i] = LtexIndex.ResolveVtex(staged.Land.VtexIndices[i], ltexMap);
                    staged.TerrainTexturePaths = texturePaths;
                }
                finally
                {
                }
            }

            staged.PreparedRefs = BuildPreparedCellRefs(staged);
            staged.DoorEntries ??= new List<DoorRefEntry>();
            }
            finally
            {
            }
        }


        private static IEnumerator ResolveDirtyCellIndicesIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            CollisionBakery collisions,
            ModelPrefabBakery modelPrefabs)
        {
            progress.Stage = "Assigning global indices";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to resolve" : $"Assigning global indices 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            int completed = 0;
            Exception resolveFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var resolveTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWorkers];
                for (int worker = 0; worker < maxWorkers; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        var materialIndexCache = new Dictionary<uint, int>();
                        var textureIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var terrainLayerCache = new Dictionary<int, ushort>();

                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= dirtyCells.Count)
                                break;

                            try
                            {
                                ResolveDirtyCellIndices(
                                    dirtyCells[index],
                                    meshes,
                                    materials,
                                    textures,
                                    terrainLayers,
                                    collisions,
                                    modelPrefabs,
                                    materialIndexCache,
                                    textureIndexCache,
                                    terrainLayerCache);
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    resolveFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!resolveTask.IsCompleted)
            {
                int current = completed;
                progress.Current = current;
                progress.Label = $"Assigning global indices {current}/{dirtyCells.Count}";
                yield return null;
            }

            if (resolveFailure != null)
                throw resolveFailure;

            progress.Current = dirtyCells.Count;
            progress.Label = $"Assigning global indices {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;
        }


        }
    }
