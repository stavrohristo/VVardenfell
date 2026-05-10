using System;
using System.Collections.Generic;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Nif;
using UnityEngine;

namespace VVardenfell.Importer.Bake
{
    public sealed partial class ActorAnimationBakery
    {
        const uint CreatureFlagBipedal = 0x01u;


        sealed class ModelBindingBuildState
        {
            public string ModelPath;
            public string BindReferenceSkeletonPath;
            public int SkeletonIndex = -1;
            public int FirstSkinMeshIndex = -1;
            public int SkinMeshCount;
            public int FirstGraphNodeIndex = -1;
            public int GraphNodeCount;
            public int FirstClipIndex = -1;
            public int ClipCount;
            public string UnsupportedSkinBindingReason;
        }


        public readonly struct Assignment
        {
            readonly int _skeletonIndex;
            readonly int _firstSkinMeshIndex;
            readonly int _skinMeshCount;
            readonly int _firstClipIndex;
            readonly int _clipCount;

            public readonly bool HasValue;
            public int SkeletonIndex => HasValue ? _skeletonIndex : -1;
            public int FirstSkinMeshIndex => HasValue ? _firstSkinMeshIndex : -1;
            public int SkinMeshCount => HasValue ? _skinMeshCount : 0;
            public int FirstClipIndex => HasValue ? _firstClipIndex : -1;
            public int ClipCount => HasValue ? _clipCount : 0;

            public Assignment(
                int skeletonIndex,
                int firstSkinMeshIndex,
                int skinMeshCount,
                int firstClipIndex,
                int clipCount)
            {
                HasValue = true;
                _skeletonIndex = skeletonIndex;
                _firstSkinMeshIndex = firstSkinMeshIndex;
                _skinMeshCount = skinMeshCount;
                _firstClipIndex = firstClipIndex;
                _clipCount = clipCount;
            }
        }


        readonly Dictionary<string, Assignment> _assignmentsByBindingKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _bindingIndicesByBindingKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _rigFamiliesByKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _skinBindingsByKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, string> _unsupportedSkinBindingReasonsBySkinBindingIndex = new();
        readonly Dictionary<string, string> _unsupportedSkinMeshReasonsByKey = new(StringComparer.OrdinalIgnoreCase);
        readonly List<ModelBindingBuildState> _bindings = new();
        readonly List<ActorRigFamilyDef> _rigFamilies = new();
        readonly List<ActorSkinBindingDef> _skinBindings = new();
        readonly List<ActorVisualRecipeDef> _actorVisualRecipes = new();
        readonly List<ActorVisualRecipeEntryDef> _actorVisualRecipeEntries = new();
        readonly List<ActorEquipmentVisualDef> _equipmentVisuals = new();
        readonly List<ActorEquipmentVisualEntryDef> _equipmentVisualEntries = new();
        readonly List<ActorModelGraphNodeDef> _graphNodes = new();
        readonly List<ActorSkeletonDef> _skeletons = new();
        readonly List<ActorSkinMeshDef> _skinMeshes = new();
        readonly List<ActorSkinWeightDef> _skinWeights = new();
        readonly List<ActorHeadMorphTargetDef> _headMorphTargets = new();
        readonly List<ActorHeadMorphVertexDef> _headMorphVertices = new();
        readonly List<ActorAnimationClipDef> _clips = new();
        readonly List<ActorAnimationTrackDef> _tracks = new();
        readonly List<ActorAnimationKeyDef> _keys = new();
        readonly List<ActorAnimationTextKeyDef> _textKeys = new();
        readonly List<ActorAnimationTextMarkerDef> _textMarkers = new();
        readonly HashSet<string> _creatureActorModels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _bipedalCreatureActorModels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _requiredCompanionKfActorModels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _missingControllerTargetWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _unsupportedCopiedRigWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _unsupportedActorVisualWarnings = new(StringComparer.OrdinalIgnoreCase);
        public bool Modified { get; private set; }
        public int ModelBindingCount => _bindings.Count;
        public int SkeletonCount => _skeletons.Count;
        public int SkinMeshCount => _skinMeshes.Count;
        public int ClipCount => _clips.Count;


        public bool TryGetAssignment(string modelPath, out Assignment assignment)
            => _assignmentsByBindingKey.TryGetValue(BuildBindingKey(modelPath, null), out assignment);


        public void ConfigureCreatureAnimationSources(GameplayContentData gameplayContent, Dictionary<string, BsaEntry> bsaByName)
        {
            _creatureActorModels.Clear();
            _bipedalCreatureActorModels.Clear();
            _requiredCompanionKfActorModels.Clear();

            var actors = gameplayContent?.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor.Kind != ActorDefKind.Creature)
                    continue;

                string model = NormalizeModelPath(actor.Model);
                if (string.IsNullOrEmpty(model))
                    continue;

                string actorModel = ResolveCreatureActorModelPath(actor, bsaByName);
                _creatureActorModels.Add(actorModel);
                if ((actor.Flags & CreatureFlagBipedal) != 0)
                    _bipedalCreatureActorModels.Add(actorModel);

                if (!string.Equals(actorModel, model, StringComparison.OrdinalIgnoreCase))
                    _requiredCompanionKfActorModels.Add(actorModel);
            }
        }


        public Assignment GetOrAddModel(
            string modelPath,
            NifFile modelNif,
            ModelPrefabSource prefabSource,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            BsaArchive bsa,
            Dictionary<string, BsaEntry> bsaByName,
            bool forceActorModel = false,
            ActorSkeletonDef skinBindReferenceSkeleton = null,
            string skinBindReferenceSkeletonPath = null)
        {
            modelPath ??= string.Empty;
            string normalizedReferenceSkeletonPath = NormalizeModelPath(skinBindReferenceSkeletonPath);
            string bindingKey = BuildBindingKey(modelPath, normalizedReferenceSkeletonPath);
            if (_assignmentsByBindingKey.TryGetValue(bindingKey, out var existing))
                return existing;

            if (modelNif == null || !IsActorAnimationCandidate(modelNif, modelPath, bsaByName, forceActorModel))
            {
                _assignmentsByBindingKey[bindingKey] = default;
                return default;
            }

            int firstGraphNode = _graphNodes.Count;
            AppendModelGraphNodes(prefabSource, firstGraphNode);
            int graphNodeCount = _graphNodes.Count - firstGraphNode;

            int skeletonIndex = _skeletons.Count;
            bool isCreatureFullModel = skinBindReferenceSkeleton == null
                                       && _creatureActorModels.Contains(NormalizeModelPath(modelPath));
            var skeleton = isCreatureFullModel
                ? NifActorAnimationExtractor.ExtractSkeleton(prefabSource)
                : NifActorAnimationExtractor.ExtractSkeleton(modelNif);
            _skeletons.Add(skeleton);

            int firstSkinMesh = _skinMeshes.Count;
            var skinMeshes = NifActorAnimationExtractor.ExtractSkinMeshes(
                modelNif,
                skeleton,
                skeletonIndex,
                _skinWeights,
                _keys,
                _headMorphTargets,
                _headMorphVertices);
            bool remapSkinBonesToReferenceSkeleton = skinBindReferenceSkeleton != null
                && !ReferenceEquals(skinBindReferenceSkeleton, skeleton);
            AttachRenderData(
                modelPath,
                skinMeshes,
                prefabSource,
                firstGraphNode,
                graphNodeCount,
                skeleton,
                skinBindReferenceSkeleton ?? skeleton,
                remapSkinBonesToReferenceSkeleton,
                meshes,
                materials,
                textures);

            string unsupportedSkinBindingReason = null;
            if (remapSkinBonesToReferenceSkeleton && !HasRenderableSkinMeshes(skinMeshes))
            {
                unsupportedSkinBindingReason =
                    $"model remapped to reference skeleton '{normalizedReferenceSkeletonPath}' produced no renderable skin meshes";
            }

            for (int i = 0; i < skinMeshes.Length; i++)
                _skinMeshes.Add(skinMeshes[i]);

            int firstClip = _clips.Count;
            AddSharedNpcKfClips(modelPath, bsa, bsaByName);
            AddSharedCreatureKfClips(modelPath, bsa, bsaByName);
            AddCompanionKfClips(modelPath, bsa, bsaByName);
            AddClips(modelNif);

            var assignment = new Assignment(
                skeletonIndex,
                _skinMeshes.Count > firstSkinMesh ? firstSkinMesh : -1,
                _skinMeshes.Count - firstSkinMesh,
                _clips.Count > firstClip ? firstClip : -1,
                _clips.Count - firstClip);

            _assignmentsByBindingKey[bindingKey] = assignment;
            _bindings.Add(new ModelBindingBuildState
            {
                ModelPath = modelPath,
                BindReferenceSkeletonPath = normalizedReferenceSkeletonPath,
                SkeletonIndex = assignment.SkeletonIndex,
                FirstSkinMeshIndex = assignment.FirstSkinMeshIndex,
                SkinMeshCount = assignment.SkinMeshCount,
                FirstGraphNodeIndex = firstGraphNode,
                GraphNodeCount = graphNodeCount,
                FirstClipIndex = assignment.FirstClipIndex,
                ClipCount = assignment.ClipCount,
                UnsupportedSkinBindingReason = unsupportedSkinBindingReason,
            });
            _bindingIndicesByBindingKey[bindingKey] = _bindings.Count - 1;
            Modified = true;
            return assignment;
        }


        public void BuildVisualRecipes(GameplayContentData gameplayContent, Dictionary<string, BsaEntry> bsaByName)
        {
            _rigFamilies.Clear();
            _skinBindings.Clear();
            _actorVisualRecipes.Clear();
            _actorVisualRecipeEntries.Clear();
            _equipmentVisuals.Clear();
            _equipmentVisualEntries.Clear();
            _rigFamiliesByKey.Clear();
            _skinBindingsByKey.Clear();
            _unsupportedSkinBindingReasonsBySkinBindingIndex.Clear();

            gameplayContent ??= new GameplayContentData();
            var races = BuildRaceLookup(gameplayContent);
            BuildNpcRigFamilies();
            BuildCreatureRigFamilies(gameplayContent, bsaByName);
            BuildActorVisualRecipes(gameplayContent, races, bsaByName);
            BuildEquipmentVisuals(gameplayContent);

            Modified = true;
        }


        static bool HasRenderableSkinMeshes(ActorSkinMeshDef[] skinMeshes)
        {
            skinMeshes ??= Array.Empty<ActorSkinMeshDef>();
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                if (IsRenderableSkinMesh(skinMeshes[i]))
                    return true;
            }

            return false;
        }


        public ActorAnimationCatalogData BuildCatalog()
        {
            RebuildTextMarkers();
            return new()
            {
                RigFamilies = _rigFamilies.ToArray(),
                SkinBindings = _skinBindings.ToArray(),
                ActorVisualRecipes = _actorVisualRecipes.ToArray(),
                ActorVisualRecipeEntries = _actorVisualRecipeEntries.ToArray(),
                EquipmentVisuals = _equipmentVisuals.ToArray(),
                EquipmentVisualEntries = _equipmentVisualEntries.ToArray(),
                GraphNodes = _graphNodes.ToArray(),
                Skeletons = _skeletons.ToArray(),
                SkinMeshes = _skinMeshes.ToArray(),
                SkinWeights = _skinWeights.ToArray(),
                HeadMorphTargets = _headMorphTargets.ToArray(),
                HeadMorphVertices = _headMorphVertices.ToArray(),
                Clips = _clips.ToArray(),
                Tracks = _tracks.ToArray(),
                Keys = _keys.ToArray(),
                TextKeys = _textKeys.ToArray(),
                TextMarkers = _textMarkers.ToArray(),
            };
        }


        void BuildNpcRigFamilies()
        {
            AddNpcRigFamily(ActorRigFamilyKind.NpcMale, false, false, false);
            AddNpcRigFamily(ActorRigFamilyKind.NpcFemale, false, true, false);
            AddNpcRigFamily(ActorRigFamilyKind.NpcBeast, false, false, true);
            AddNpcRigFamily(ActorRigFamilyKind.NpcMaleFirstPerson, true, false, false);
            AddNpcRigFamily(ActorRigFamilyKind.NpcFemaleFirstPerson, true, true, false);
            AddNpcRigFamily(ActorRigFamilyKind.NpcBeastFirstPerson, true, false, true);
        }


        void AddNpcRigFamily(ActorRigFamilyKind kind, bool firstPerson, bool female, bool beast)
        {
            string skeletonPath = ResolveNpcSkeletonModel(firstPerson, female, beast);
            AddRigFamily(kind, skeletonPath, null);
        }


        void BuildCreatureRigFamilies(GameplayContentData gameplayContent, Dictionary<string, BsaEntry> bsaByName)
        {
            var actors = gameplayContent?.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Kind != ActorDefKind.Creature)
                    continue;

                string model = ResolveCreatureActorModelPath(actors[i], bsaByName);
                if (string.IsNullOrEmpty(model))
                    throw new InvalidOperationException($"Creature actor '{actors[i].Id}' has no model.");

                AddRigFamily(ActorRigFamilyKind.Creature, model, actors[i].Id);
            }
        }


        int AddRigFamily(ActorRigFamilyKind kind, string skeletonModelPath, string context)
        {
            string normalized = NormalizeModelPath(skeletonModelPath);
            if (string.IsNullOrEmpty(normalized))
                throw new InvalidOperationException($"Actor rig family '{kind}' has no skeleton model path for '{context ?? kind.ToString()}'.");

            string key = $"{(byte)kind}|{normalized}";
            if (_rigFamiliesByKey.TryGetValue(key, out int existing))
                return existing;

            int bindingIndex = RequireBindingIndex(normalized, null, $"rig family {kind}");
            var binding = _bindings[bindingIndex];
            if (binding.SkeletonIndex < 0)
                throw new InvalidOperationException($"Actor rig family '{kind}' skeleton '{normalized}' has no baked skeleton.");
            if (binding.FirstClipIndex < 0 || binding.ClipCount <= 0)
                throw new InvalidOperationException($"Actor rig family '{kind}' skeleton '{normalized}' has no baked animation clips.");
            ValidateRigFamilyClipTargets(kind, normalized, binding);

            int index = _rigFamilies.Count;
            _rigFamilies.Add(new ActorRigFamilyDef
            {
                FamilyKind = kind,
                SkeletonModelPath = normalized,
                SkeletonIndex = binding.SkeletonIndex,
                FirstClipIndex = binding.FirstClipIndex,
                ClipCount = binding.ClipCount,
            });
            _rigFamiliesByKey[key] = index;
            return index;
        }


        void ValidateRigFamilyClipTargets(ActorRigFamilyKind kind, string skeletonModelPath, ModelBindingBuildState binding)
        {
            if ((uint)binding.SkeletonIndex >= (uint)_skeletons.Count)
                throw new InvalidOperationException($"Actor rig family '{kind}' skeleton '{skeletonModelPath}' has invalid skeleton index {binding.SkeletonIndex}.");

            var bones = _skeletons[binding.SkeletonIndex]?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            int clipEnd = Math.Min(_clips.Count, binding.FirstClipIndex + binding.ClipCount);
            for (int clipIndex = binding.FirstClipIndex; clipIndex < clipEnd; clipIndex++)
            {
                var clip = _clips[clipIndex];
                if (clip == null || clip.FirstTrackIndex < 0 || clip.TrackCount <= 0)
                    continue;

                int trackEnd = Math.Min(_tracks.Count, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < trackEnd; trackIndex++)
                {
                    var track = _tracks[trackIndex];
                    if (!IsPoseTrack(track))
                        continue;

                    if (FindBoneIndex(bones, track.TargetName) < 0)
                    {
                        if (!ModelGraphContainsControllerTarget(binding, track.TargetName))
                        {
                            WarnMissingControllerTargetSkipped(kind, skeletonModelPath, clip, track);
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"Actor rig family '{kind}' skeleton '{skeletonModelPath}' is not compatible with animation clip '{clip.Name}' " +
                            $"from '{clip.SourcePath}': graph contains pose track target '{track.TargetName}' but the baked skeleton table does not.");
                    }
                }
            }
        }


        bool ModelGraphContainsControllerTarget(ModelBindingBuildState binding, string targetName)
        {
            if (binding == null || string.IsNullOrWhiteSpace(targetName))
                return false;

            int first = binding.FirstGraphNodeIndex;
            int end = Math.Min(_graphNodes.Count, first + binding.GraphNodeCount);
            for (int i = first; i >= 0 && i < end; i++)
            {
                var node = _graphNodes[i];
                if (node == null || node.Kind == ModelPrefabNodeKind.SyntheticRoot)
                    continue;
                if (string.Equals(node.Name, targetName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string canonical = CanonicalBoneName(targetName);
            for (int i = first; i >= 0 && i < end; i++)
            {
                var node = _graphNodes[i];
                if (node == null || node.Kind == ModelPrefabNodeKind.SyntheticRoot)
                    continue;
                if (string.Equals(CanonicalBoneName(node.Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }


        void WarnMissingControllerTargetSkipped(
            ActorRigFamilyKind kind,
            string skeletonModelPath,
            ActorAnimationClipDef clip,
            ActorAnimationTrackDef track)
        {
            string key = $"{kind}|{skeletonModelPath}|{clip?.SourcePath}|{track?.TargetName}";
            if (!_missingControllerTargetWarnings.Add(key))
                return;

            UnityEngine.Debug.LogWarning(
                $"[VVardenfell][ActorAnimationControllerTargetSkipped] rigFamily='{kind}' skeleton='{skeletonModelPath}' " +
                $"clip='{clip?.Name ?? string.Empty}' source='{clip?.SourcePath ?? string.Empty}' target='{track?.TargetName ?? string.Empty}' " +
                "has no node in the baked actor graph; matching OpenMW, this controller target is skipped instead of treated as a required skin pose bone.");
        }


        void BuildActorVisualRecipes(GameplayContentData gameplayContent, Dictionary<string, RaceDef> races, Dictionary<string, BsaEntry> bsaByName)
        {
            var actors = gameplayContent?.Actors ?? Array.Empty<ActorDef>();
            var bodyParts = gameplayContent?.ActorBodyParts ?? Array.Empty<ActorBodyPartDef>();
            var bodyPartsById = BuildBodyPartLookup(bodyParts);
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor.Kind == ActorDefKind.Creature)
                {
                    BuildCreatureVisualRecipe(actor, bsaByName);
                    continue;
                }

                if (actor.Kind != ActorDefKind.Npc)
                    continue;

                bool female = (actor.Flags & 0x1u) != 0;
                bool beast = IsBeastRace(actor.RaceId, races);
                BuildNpcVisualRecipe(actor, false, female, beast, bodyParts, bodyPartsById);
            }
        }


        void BuildCreatureVisualRecipe(ActorDef actor, Dictionary<string, BsaEntry> bsaByName)
        {
            string actorModel = ResolveCreatureActorModelPath(actor, bsaByName);
            int rigFamilyIndex = AddRigFamily(ActorRigFamilyKind.Creature, actorModel, actor.Id);
            int skinBindingIndex = RequireSkinBinding(
                actorModel,
                rigFamilyIndex,
                null,
                $"creature '{actor.Id}'",
                allowSemanticOnlyNoSkin: true);
            var binding = _skinBindings[skinBindingIndex];
            int firstEntry = _actorVisualRecipeEntries.Count;
            bool hasRenderableMeshes = SkinBindingHasRenderableMeshes(binding);
            if (hasRenderableMeshes)
                AddAllSkinBindingMeshesToActorRecipe(skinBindingIndex, binding, ActorVisualPartReference.Cuirass);
            AddActorVisualRecipe(
                actor.ContentId,
                false,
                ActorVisualBodyVariant.Male,
                rigFamilyIndex,
                firstEntry,
                _actorVisualRecipeEntries.Count - firstEntry,
                allowEmptySemanticOnly: !hasRenderableMeshes);
        }


        void BuildNpcVisualRecipe(
            ActorDef actor,
            bool firstPerson,
            bool female,
            bool beast,
            ActorBodyPartDef[] bodyParts,
            Dictionary<string, ActorBodyPartDef> bodyPartsById)
        {
            int rigFamilyIndex = ResolveNpcRigFamilyIndex(firstPerson, female, beast);
            var variant = female ? ActorVisualBodyVariant.Female : ActorVisualBodyVariant.Male;
            int firstEntry = _actorVisualRecipeEntries.Count;
            uint usedParts = 0u;
            var racePartTable = ActorVisualContentRules.BuildNpcRaceBodyPartTable(
                bodyParts,
                actor.RaceId,
                female,
                firstPerson,
                werewolf: false);

            if (!firstPerson)
            {
                AddExplicitNpcPart(actor, actor.HeadId, ActorVisualPartReference.Head, rigFamilyIndex, variant, bodyPartsById, ref usedParts);
                AddExplicitNpcPart(
                    actor,
                    actor.HairId,
                    ActorVisualPartReference.Hair,
                    rigFamilyIndex,
                    variant,
                    bodyPartsById,
                    ref usedParts,
                    acceptDeclaredMeshes: true);
            }

            for (int part = (int)ActorVisualPartReference.Neck; part < (int)ActorVisualPartReference.Count; part++)
            {
                var reference = (ActorVisualPartReference)part;
                if (!IsBaseSkinPartReference(reference))
                    continue;
                if (firstPerson && !IsFirstPersonPartReference(reference))
                    continue;
                if (reference == ActorVisualPartReference.Tail && !beast)
                    continue;

                var bodyPart = racePartTable[(int)reference];
                if (string.IsNullOrWhiteSpace(bodyPart.Id))
                    throw new InvalidOperationException(
                        $"NPC actor '{actor.Id}' race '{actor.RaceId}' is missing required body part '{reference}' for firstPerson={firstPerson}, female={female}.");

                AddNpcBodyPart(actor.Id, bodyPart, reference, rigFamilyIndex, ref usedParts);
            }

            AddActorVisualRecipe(actor.ContentId, firstPerson, variant, rigFamilyIndex, firstEntry, _actorVisualRecipeEntries.Count - firstEntry);
        }


        void AddActorVisualRecipe(
            ContentId actorContentId,
            bool firstPerson,
            ActorVisualBodyVariant variant,
            int rigFamilyIndex,
            int firstEntry,
            int entryCount,
            bool allowEmptySemanticOnly = false)
        {
            if (entryCount <= 0)
            {
                if (allowEmptySemanticOnly)
                {
                    firstEntry = -1;
                    entryCount = 0;
                }
                else
                {
                    throw new InvalidOperationException($"Actor visual recipe '{actorContentId}' firstPerson={firstPerson} produced no skin entries.");
                }
            }

            _actorVisualRecipes.Add(new ActorVisualRecipeDef
            {
                ActorContentId = actorContentId,
                FirstPerson = (byte)(firstPerson ? 1 : 0),
                BodyVariant = variant,
                RigFamilyIndex = rigFamilyIndex,
                FirstEntryIndex = firstEntry,
                EntryCount = entryCount,
            });
        }


        void AddExplicitNpcPart(
            ActorDef actor,
            string bodyPartId,
            ActorVisualPartReference reference,
            int rigFamilyIndex,
            ActorVisualBodyVariant variant,
            Dictionary<string, ActorBodyPartDef> bodyPartsById,
            ref uint usedParts,
            bool acceptDeclaredMeshes = false)
        {
            if (string.IsNullOrWhiteSpace(bodyPartId))
                throw new InvalidOperationException($"NPC actor '{actor.Id}' has no explicit '{reference}' body part id.");
            if (bodyPartsById == null || !bodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId), out var bodyPart))
                throw new InvalidOperationException($"NPC actor '{actor.Id}' references missing body part '{bodyPartId}' for '{reference}'.");
            if (bodyPart.Type != ActorBodyPartMeshType.Skin)
                throw new InvalidOperationException($"NPC actor '{actor.Id}' explicit body part '{bodyPartId}' is '{bodyPart.Type}', expected Skin.");

            AddNpcBodyPart(actor.Id, bodyPart, reference, rigFamilyIndex, ref usedParts, acceptDeclaredMeshes);
        }


        void AddNpcBodyPart(
            string actorId,
            ActorBodyPartDef bodyPart,
            ActorVisualPartReference reference,
            int rigFamilyIndex,
            ref uint usedParts,
            bool acceptDeclaredMeshes = false)
        {
            var expectedPart = ActorVisualMappingPolicy.GetMeshPart(reference);
            if (bodyPart.Part != expectedPart)
                throw new InvalidOperationException(
                    $"actor '{actorId}' body part '{bodyPart.Id}' declares mesh part '{bodyPart.Part}', expected '{expectedPart}' for '{reference}'.");

            ReserveUniquePart(reference, ref usedParts, $"actor '{actorId}' body recipe");
            int skinBindingIndex = RequireSkinBinding(bodyPart.Model, rigFamilyIndex, reference, $"actor '{actorId}' body part '{bodyPart.Id}'");
            AddFilteredSkinBindingMeshesToActorRecipe(skinBindingIndex, reference, $"actor '{actorId}' body part '{bodyPart.Id}'", acceptDeclaredMeshes);
        }


        void BuildEquipmentVisuals(GameplayContentData gameplayContent)
        {
            var items = gameplayContent?.Items ?? Array.Empty<BaseDef>();
            var equipmentDefs = gameplayContent?.ItemEquipment ?? Array.Empty<ItemEquipmentDef>();
            CurrentGameplayItemBodyParts = gameplayContent?.ItemEquipmentBodyParts ?? Array.Empty<ItemEquipmentBodyPartDef>();
            CurrentActorBodyPartsById = BuildBodyPartLookup(gameplayContent?.ActorBodyParts);
            for (int itemIndex = 0; itemIndex < equipmentDefs.Length; itemIndex++)
            {
                var equipment = equipmentDefs[itemIndex];
                if (!equipment.Item.IsValid || (uint)equipment.Item.Index >= (uint)items.Length)
                    throw new InvalidOperationException($"Equipment record at index {itemIndex} has invalid item handle {equipment.Item.Value}.");
                if (equipment.Kind != ItemEquipmentKind.Armor && equipment.Kind != ItemEquipmentKind.Clothing)
                    continue;

                var item = items[equipment.Item.Index];
                for (int rigFamilyIndex = 0; rigFamilyIndex < _rigFamilies.Count; rigFamilyIndex++)
                {
                    var rig = _rigFamilies[rigFamilyIndex];
                    if (!IsNpcRigFamily(rig.FamilyKind))
                        continue;

                    bool firstPerson = IsFirstPersonRigFamily(rig.FamilyKind);
                    BuildEquipmentVisual(item, equipment, rigFamilyIndex, firstPerson, ActorVisualBodyVariant.Male);
                    BuildEquipmentVisual(item, equipment, rigFamilyIndex, firstPerson, ActorVisualBodyVariant.Female);
                }
            }
        }


        void BuildEquipmentVisual(BaseDef item, ItemEquipmentDef equipment, int rigFamilyIndex, bool firstPerson, ActorVisualBodyVariant variant)
        {
            bool beastRig = IsBeastRigFamily(_rigFamilies[rigFamilyIndex].FamilyKind);
            bool invalidForBeast = beastRig && HasBeastForbiddenEquipmentPart(equipment);
            int firstEntry = _equipmentVisualEntries.Count;
            uint coverageMask = BuildEquipmentCoverageMask(equipment);

            if (!invalidForBeast)
            {
                uint usedParts = 0u;
                int firstBodyPart = equipment.FirstBodyPartIndex;
                int bodyPartCount = equipment.BodyPartCount;
                for (int i = 0; i < bodyPartCount; i++)
                {
                    int bodyPartIndex = firstBodyPart + i;
                    if (firstBodyPart < 0 || bodyPartIndex < 0)
                        continue;

                    var bodyParts = CurrentGameplayItemBodyParts;
                    if ((uint)bodyPartIndex >= (uint)bodyParts.Length)
                        throw new InvalidOperationException($"Equipment item '{item.Id}' body part index {bodyPartIndex} is out of range.");

                    var bodyPartRef = bodyParts[bodyPartIndex];
                    if (firstPerson && !IsFirstPersonPartReference((ActorVisualPartReference)(byte)bodyPartRef.Part))
                        continue;
                    if (bodyPartRef.Part == ItemEquipmentPartReference.Tail && !beastRig)
                        continue;

                    AddEquipmentPartVisual(item.Id, bodyPartRef, rigFamilyIndex, firstPerson, variant, ref usedParts);
                }
            }

            _equipmentVisuals.Add(new ActorEquipmentVisualDef
            {
                ItemContentId = item.ContentId,
                RigFamilyIndex = rigFamilyIndex,
                FirstPerson = (byte)(firstPerson ? 1 : 0),
                BodyVariant = variant,
                IsValid = (byte)(invalidForBeast ? 0 : 1),
                CoverageMask = coverageMask,
                FirstEntryIndex = _equipmentVisualEntries.Count > firstEntry ? firstEntry : -1,
                EntryCount = _equipmentVisualEntries.Count - firstEntry,
            });
        }


        ItemEquipmentBodyPartDef[] CurrentGameplayItemBodyParts { get; set; } = Array.Empty<ItemEquipmentBodyPartDef>();
        Dictionary<string, ActorBodyPartDef> CurrentActorBodyPartsById { get; set; } = new(StringComparer.OrdinalIgnoreCase);


        void AddEquipmentPartVisual(
            string itemId,
            ItemEquipmentBodyPartDef bodyPartRef,
            int rigFamilyIndex,
            bool firstPerson,
            ActorVisualBodyVariant variant,
            ref uint usedParts)
        {
            if (!TryMapEquipmentPartReference(bodyPartRef.Part, out var reference))
                return;

            if (!TryReservePart(reference, ref usedParts))
                return;

            string bodyPartId;
            if (variant == ActorVisualBodyVariant.Female)
            {
                bodyPartId = !string.IsNullOrWhiteSpace(bodyPartRef.FemaleBodyPartId)
                    ? bodyPartRef.FemaleBodyPartId
                    : bodyPartRef.MaleBodyPartId;
            }
            else
            {
                // OpenMW only falls back female -> male. Male actors do not render female-only equipment parts.
                bodyPartId = bodyPartRef.MaleBodyPartId;
            }

            if (string.IsNullOrWhiteSpace(bodyPartId))
                return;

            if (!TryResolveEquipmentBodyPart(
                    bodyPartId,
                    firstPerson,
                    out var bodyPart,
                    $"equipment item '{itemId}' part '{bodyPartRef.Part}'"))
            {
                return;
            }

            if (bodyPart.Type != ActorBodyPartMeshType.Armor && bodyPart.Type != ActorBodyPartMeshType.Clothing)
                throw new InvalidOperationException($"Equipment item '{itemId}' body part '{bodyPartId}' is '{bodyPart.Type}', expected Armor or Clothing.");

            int skinBindingIndex = RequireSkinBinding(bodyPart.Model, rigFamilyIndex, reference, $"equipment item '{itemId}' body part '{bodyPartId}'");
            AddFilteredSkinBindingMeshesToEquipmentVisual(
                skinBindingIndex,
                bodyPartRef.Part,
                reference,
                bodyPart.Part,
                $"equipment item '{itemId}' body part '{bodyPartId}'");
        }


        bool TryResolveEquipmentBodyPart(
            string bodyPartId,
            bool firstPerson,
            out ActorBodyPartDef bodyPart,
            string context)
        {
            bodyPart = default;
            if (string.IsNullOrWhiteSpace(bodyPartId) || CurrentActorBodyPartsById == null)
                return false;

            if (firstPerson
                && CurrentActorBodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId + ".1st"), out var firstPersonPart))
            {
                if (string.IsNullOrWhiteSpace(firstPersonPart.Model))
                    return false;

                bodyPart = firstPersonPart;
                return true;
            }

            if (!CurrentActorBodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId), out bodyPart))
                throw new InvalidOperationException($"{context} references missing body part '{bodyPartId}'.");

            if (string.IsNullOrWhiteSpace(bodyPart.Model))
                return false;

            if (firstPerson && !IsFirstPersonMeshPart(bodyPart.Part))
                return false;

            return true;
        }


        ActorBodyPartDef RequireBodyPart(string bodyPartId, bool firstPerson, string context)
        {
            if (firstPerson
                && CurrentActorBodyPartsById != null
                && CurrentActorBodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId + ".1st"), out var firstPersonPart))
            {
                if (string.IsNullOrWhiteSpace(firstPersonPart.Model))
                    throw new InvalidOperationException($"{context} first-person body part '{bodyPartId}.1st' has no model.");
                return firstPersonPart;
            }

            if (CurrentActorBodyPartsById == null
                || !CurrentActorBodyPartsById.TryGetValue(ContentId.NormalizeId(bodyPartId), out var bodyPart))
            {
                throw new InvalidOperationException($"{context} references missing body part '{bodyPartId}'.");
            }

            if (string.IsNullOrWhiteSpace(bodyPart.Model))
                throw new InvalidOperationException($"{context} body part '{bodyPartId}' has no model.");
            return bodyPart;
        }


        int RequireSkinBinding(
            string modelPath,
            int rigFamilyIndex,
            ActorVisualPartReference? reference,
            string context,
            bool allowSemanticOnlyNoSkin = false)
        {
            if ((uint)rigFamilyIndex >= (uint)_rigFamilies.Count)
                throw new InvalidOperationException($"{context} references invalid rig family {rigFamilyIndex}.");

            string model = NormalizeModelPath(modelPath);
            if (string.IsNullOrEmpty(model))
                throw new InvalidOperationException($"{context} has no skin model path.");

            string key = $"{model}|{rigFamilyIndex}";
            if (_skinBindingsByKey.TryGetValue(key, out int existing))
                return existing;

            string referenceSkeleton = ResolveSkinBindingReferenceSkeleton(model, rigFamilyIndex);
            int bindingIndex = RequireBindingIndex(model, referenceSkeleton, context);
            var binding = _bindings[bindingIndex];
            if (binding.FirstSkinMeshIndex < 0 || binding.SkinMeshCount <= 0)
            {
                string reason = binding.UnsupportedSkinBindingReason;
                if (string.IsNullOrEmpty(reason))
                {
                    if (!allowSemanticOnlyNoSkin)
                    {
                        throw new InvalidOperationException($"{context} skin model '{model}' has no baked skin meshes for rig '{_rigFamilies[rigFamilyIndex].SkeletonModelPath}'.");
                    }

                    reason = "model has no baked skin meshes; preserving a skeleton-only actor visual recipe";
                }

                WarnUnsupportedActorVisualSkipped(model, _rigFamilies[rigFamilyIndex].SkeletonModelPath, context, reason);
            }

            int index = _skinBindings.Count;
            _skinBindings.Add(new ActorSkinBindingDef
            {
                SkinModelPath = model,
                RigFamilyIndex = rigFamilyIndex,
                FirstSkinMeshIndex = binding.FirstSkinMeshIndex,
                SkinMeshCount = binding.SkinMeshCount,
                FirstGraphNodeIndex = binding.FirstGraphNodeIndex,
                GraphNodeCount = binding.GraphNodeCount,
            });
            _skinBindingsByKey[key] = index;
            if (!string.IsNullOrEmpty(binding.UnsupportedSkinBindingReason))
                _unsupportedSkinBindingReasonsBySkinBindingIndex[index] = binding.UnsupportedSkinBindingReason;
            return index;
        }


        string ResolveSkinBindingReferenceSkeleton(string modelPath, int rigFamilyIndex)
        {
            var rig = _rigFamilies[rigFamilyIndex];
            if (rig.FamilyKind == ActorRigFamilyKind.Creature
                && string.Equals(NormalizeModelPath(rig.SkeletonModelPath), NormalizeModelPath(modelPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return rig.SkeletonModelPath;
        }


        int RequireBindingIndex(string modelPath, string referenceSkeletonPath, string context)
        {
            string key = BuildBindingKey(modelPath, referenceSkeletonPath);
            if (!_bindingIndicesByBindingKey.TryGetValue(key, out int bindingIndex))
                throw new InvalidOperationException($"{context} requires missing actor animation binding '{key}'.");
            if ((uint)bindingIndex >= (uint)_bindings.Count)
                throw new InvalidOperationException($"{context} resolved invalid actor animation binding '{key}' index {bindingIndex}.");
            return bindingIndex;
        }


        void AddAllSkinBindingMeshesToActorRecipe(int skinBindingIndex, ActorSkinBindingDef binding, ActorVisualPartReference reference)
        {
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            for (int i = binding.FirstSkinMeshIndex; i < end; i++)
            {
                if (!IsRenderableSkinMesh(_skinMeshes[i]))
                    continue;

                _actorVisualRecipeEntries.Add(new ActorVisualRecipeEntryDef
                {
                    PartReference = reference,
                    SkinBindingIndex = skinBindingIndex,
                    SkinMeshIndex = i,
                    AttachBoneIndex = ResolveActorRecipeAttachBoneIndex(_skinMeshes[i]),
                    RigidMirrorX = 0,
                });
                added++;
            }

            if (added == 0)
                throw new InvalidOperationException($"Skin binding '{binding.SkinModelPath}' produced no renderable meshes.");
        }


        void AddFilteredSkinBindingMeshesToActorRecipe(
            int skinBindingIndex,
            ActorVisualPartReference reference,
            string context,
            bool acceptDeclaredMeshes = false)
        {
            var binding = _skinBindings[skinBindingIndex];
            var rig = _rigFamilies[binding.RigFamilyIndex];
            int attachBoneIndex = ResolvePartAttachBoneIndex(rig, reference, context);
            byte rigidMirrorX = ResolveRigidMirrorX(rig, attachBoneIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            if (acceptDeclaredMeshes || !SkinBindingHasSkinnedRenderableMeshes(binding))
            {
                added += AddDeclaredSkinBindingMeshesToActorRecipe(skinBindingIndex, binding, reference, context, attachBoneIndex, rigidMirrorX);
            }
            else
            {
                string[] meshFilters = BuildMeshFilters(reference);
                for (int filterIndex = 0; filterIndex < meshFilters.Length && added == 0; filterIndex++)
                {
                    string meshFilter = meshFilters[filterIndex];
                    for (int i = binding.FirstSkinMeshIndex; i < end; i++)
                    {
                        var mesh = _skinMeshes[i];
                        if (!IsRenderableSkinMesh(mesh) || mesh.IsRigid != 0 || !MatchesMeshFilter(mesh, meshFilter))
                            continue;

                        AddSkinBindingMeshToActorRecipe(
                            skinBindingIndex,
                            reference,
                            i,
                            mesh,
                            attachBoneIndex,
                            rigidMirrorX,
                            context);
                        added++;
                    }
                }

                if (added == 0)
                {
                    if (TryGetUnsupportedMatchingSkinMeshReason(binding, meshFilters, out string reason))
                    {
                        AddActorSemanticEntry(skinBindingIndex, reference, attachBoneIndex, rigidMirrorX);
                        WarnUnsupportedActorVisualSkipped(binding.SkinModelPath, rig.SkeletonModelPath, context, reason);
                        return;
                    }

                    if (TryGetMissingDeclaredPartPeerReason(binding, reference, out reason))
                    {
                        AddActorSemanticEntry(skinBindingIndex, reference, attachBoneIndex, rigidMirrorX);
                        WarnUnsupportedActorVisualSkipped(binding.SkinModelPath, rig.SkeletonModelPath, context, reason);
                        return;
                    }

                    throw new InvalidOperationException($"{context} produced no renderable meshes matching '{string.Join("' or '", meshFilters)}'.");
                }
            }

            if (added == 0)
            {
                if (TryGetUnsupportedSkinBindingReason(skinBindingIndex, out string reason))
                {
                    AddActorSemanticEntry(skinBindingIndex, reference, attachBoneIndex, rigidMirrorX);
                    WarnUnsupportedActorVisualSkipped(binding.SkinModelPath, rig.SkeletonModelPath, context, reason);
                    return;
                }

                throw new InvalidOperationException($"{context} declared '{reference}' but produced no renderable meshes.");
            }
        }


        int AddDeclaredSkinBindingMeshesToActorRecipe(
            int skinBindingIndex,
            ActorSkinBindingDef binding,
            ActorVisualPartReference reference,
            string context,
            int attachBoneIndex,
            byte rigidMirrorX)
        {
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            for (int i = binding.FirstSkinMeshIndex; i < end; i++)
            {
                var mesh = _skinMeshes[i];
                if (!IsRenderableSkinMesh(mesh))
                    continue;

                if (mesh.IsRigid != 0 && attachBoneIndex < 0)
                    ThrowMissingRigidAttachBone(context, reference, binding, mesh);

                AddSkinBindingMeshToActorRecipe(
                    skinBindingIndex,
                    reference,
                    i,
                    mesh,
                    attachBoneIndex,
                    rigidMirrorX,
                    context);
                added++;
            }

            return added;
        }


        void AddSkinBindingMeshToActorRecipe(
            int skinBindingIndex,
            ActorVisualPartReference reference,
            int skinMeshIndex,
            ActorSkinMeshDef mesh,
            int attachBoneIndex,
            byte rigidMirrorX,
            string context)
        {
            _actorVisualRecipeEntries.Add(new ActorVisualRecipeEntryDef
            {
                PartReference = reference,
                SkinBindingIndex = skinBindingIndex,
                SkinMeshIndex = skinMeshIndex,
                AttachBoneIndex = mesh.IsRigid != 0 ? attachBoneIndex : -1,
                RigidMirrorX = mesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
        }


        void AddActorSemanticEntry(
            int skinBindingIndex,
            ActorVisualPartReference reference,
            int attachBoneIndex,
            byte rigidMirrorX)
        {
            _actorVisualRecipeEntries.Add(new ActorVisualRecipeEntryDef
            {
                PartReference = reference,
                SkinBindingIndex = skinBindingIndex,
                SkinMeshIndex = -1,
                AttachBoneIndex = attachBoneIndex,
                RigidMirrorX = rigidMirrorX,
            });
        }


        bool TryGetUnsupportedSkinBindingReason(int skinBindingIndex, out string reason)
            => _unsupportedSkinBindingReasonsBySkinBindingIndex.TryGetValue(skinBindingIndex, out reason)
               && !string.IsNullOrEmpty(reason);


        bool TryGetUnsupportedMatchingSkinMeshReason(
            ActorSkinBindingDef binding,
            string[] meshFilters,
            out string reason)
        {
            reason = null;
            int start = Math.Max(0, binding.FirstSkinMeshIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = start; i < end; i++)
            {
                var mesh = _skinMeshes[i];
                if (mesh == null || IsRenderableSkinMesh(mesh))
                    continue;

                bool matches = false;
                for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
                {
                    if (MatchesMeshFilter(mesh, meshFilters[filterIndex]))
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                    continue;

                if (_unsupportedSkinMeshReasonsByKey.TryGetValue(
                        BuildSkinMeshUnsupportedKey(mesh.ModelPath, mesh.NodeName),
                        out reason)
                    && !string.IsNullOrWhiteSpace(reason))
                {
                    reason = $"matching mesh '{mesh.NodeName}' was marked non-renderable: {reason}";
                    return true;
                }
            }

            return false;
        }


        bool TryGetMissingDeclaredPartPeerReason(
            ActorSkinBindingDef binding,
            ActorVisualPartReference reference,
            out string reason)
        {
            reason = null;
            string meshPartFilter = ActorVisualMappingPolicy.GetMeshPartFilter(reference);
            if (string.IsNullOrWhiteSpace(meshPartFilter))
                return false;

            int start = Math.Max(0, binding.FirstSkinMeshIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = start; i < end; i++)
            {
                var mesh = _skinMeshes[i];
                if (mesh == null)
                    continue;

                if (MeshOrAncestorMatchesPartName(mesh, meshPartFilter))
                {
                    reason = $"declared body-part model has no mesh for '{ActorVisualMappingPolicy.GetMeshFilter(reference)}' but contains peer mesh '{mesh.NodeName}' for body-part slot '{meshPartFilter}'";
                    return true;
                }
            }

            return false;
        }


        bool MeshOrAncestorMatchesPartName(ActorSkinMeshDef mesh, string meshPartFilter)
        {
            if (PartNameContains(mesh.NodeName, meshPartFilter))
                return true;

            int graphNodeIndex = mesh.SourceGraphNodeIndex;
            int guard = 0;
            while ((uint)graphNodeIndex < (uint)_graphNodes.Count && guard++ < _graphNodes.Count)
            {
                var graphNode = _graphNodes[graphNodeIndex];
                if (PartNameContains(graphNode?.Name, meshPartFilter))
                    return true;

                graphNodeIndex = graphNode?.ParentIndex ?? -1;
            }

            return false;
        }


        static bool PartNameContains(string nodeName, string meshPartFilter)
        {
            string normalizedNode = NormalizePartName(nodeName);
            string normalizedFilter = NormalizePartName(meshPartFilter);
            return !string.IsNullOrWhiteSpace(normalizedNode)
                   && !string.IsNullOrWhiteSpace(normalizedFilter)
                   && normalizedNode.Contains(normalizedFilter, StringComparison.Ordinal);
        }


        void WarnUnsupportedActorVisualSkipped(
            string modelPath,
            string skeletonModelPath,
            string context,
            string reason)
        {
            string key = $"{modelPath}|{skeletonModelPath}|{context}|{reason}";
            if (!_unsupportedActorVisualWarnings.Add(key))
                return;

            Debug.LogWarning(
                $"[VVardenfell][ActorVisualMeshSkipped] {context} skin='{modelPath}' " +
                $"rig='{skeletonModelPath ?? "<unknown>"}' produced a semantic-only recipe entry: {reason}. " +
                "The current actor renderer cannot yet preserve copied local creature-style rigs inside NPC body-part visuals.");
        }


        bool SkinBindingHasOnlyRigidRenderableMeshes(ActorSkinBindingDef binding)
        {
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int renderableCount = 0;
            for (int i = binding.FirstSkinMeshIndex; i < end; i++)
            {
                var mesh = _skinMeshes[i];
                if (!IsRenderableSkinMesh(mesh))
                    continue;

                if (mesh.IsRigid == 0)
                    return false;

                renderableCount++;
            }

            return renderableCount > 0;
        }


        static int ResolveActorRecipeAttachBoneIndex(ActorSkinMeshDef mesh)
            => mesh != null
               && mesh.IsRigid != 0
               && mesh.RigAssemblyKind == ActorRigAssemblyKind.RigidAttachment
               && mesh.TargetBoneIndex >= 0
                ? mesh.TargetBoneIndex
                : -1;

    }
}
