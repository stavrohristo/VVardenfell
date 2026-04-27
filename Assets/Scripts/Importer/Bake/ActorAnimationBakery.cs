using System;
using System.Collections.Generic;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Nif;
using UnityEngine;

namespace VVardenfell.Importer.Bake
{
    public sealed class ActorAnimationBakery
    {
        const uint CreatureFlagBipedal = 0x01u;

        sealed class ModelBindingBuildState
        {
            public string ModelPath;
            public string BindReferenceSkeletonPath;
            public int SkeletonIndex = -1;
            public int FirstSkinMeshIndex = -1;
            public int SkinMeshCount;
            public int FirstClipIndex = -1;
            public int ClipCount;
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
        readonly List<ModelBindingBuildState> _bindings = new();
        readonly List<ActorRigFamilyDef> _rigFamilies = new();
        readonly List<ActorSkinBindingDef> _skinBindings = new();
        readonly List<ActorVisualRecipeDef> _actorVisualRecipes = new();
        readonly List<ActorVisualRecipeEntryDef> _actorVisualRecipeEntries = new();
        readonly List<ActorEquipmentVisualDef> _equipmentVisuals = new();
        readonly List<ActorEquipmentVisualEntryDef> _equipmentVisualEntries = new();
        readonly List<ActorSkeletonDef> _skeletons = new();
        readonly List<ActorSkinMeshDef> _skinMeshes = new();
        readonly List<ActorSkinWeightDef> _skinWeights = new();
        readonly List<ActorAnimationClipDef> _clips = new();
        readonly List<ActorAnimationTrackDef> _tracks = new();
        readonly List<ActorAnimationKeyDef> _keys = new();
        readonly List<ActorAnimationTextKeyDef> _textKeys = new();
        readonly List<ActorAnimationTextMarkerDef> _textMarkers = new();
        readonly HashSet<string> _creatureActorModels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _bipedalCreatureActorModels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _requiredCompanionKfActorModels = new(StringComparer.OrdinalIgnoreCase);
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

            int skeletonIndex = _skeletons.Count;
            var skeleton = NifActorAnimationExtractor.ExtractSkeleton(modelNif);
            if (IsNpcSkeletonModel(modelPath))
                ApplyNpcLeftHelperMirroring(skeleton);
            _skeletons.Add(skeleton);

            int firstSkinMesh = _skinMeshes.Count;
            var skinMeshes = NifActorAnimationExtractor.ExtractSkinMeshes(modelNif, skeleton, skeletonIndex, _skinWeights);
            bool remapSkinBonesToReferenceSkeleton = skinBindReferenceSkeleton != null
                && !ReferenceEquals(skinBindReferenceSkeleton, skeleton);
            AttachRenderData(
                modelPath,
                skinMeshes,
                prefabSource,
                skeleton,
                skinBindReferenceSkeleton ?? skeleton,
                remapSkinBonesToReferenceSkeleton,
                meshes,
                materials,
                textures);
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
                FirstClipIndex = assignment.FirstClipIndex,
                ClipCount = assignment.ClipCount,
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

            gameplayContent ??= new GameplayContentData();
            var races = BuildRaceLookup(gameplayContent);
            BuildNpcRigFamilies();
            BuildCreatureRigFamilies(gameplayContent, bsaByName);
            BuildActorVisualRecipes(gameplayContent, races, bsaByName);
            BuildEquipmentVisuals(gameplayContent);

            Debug.Log(
                $"[VVardenfell] Actor animation recipes: rigFamilies={_rigFamilies.Count}, skinBindings={_skinBindings.Count}, " +
                $"actorRecipes={_actorVisualRecipes.Count}, actorRecipeEntries={_actorVisualRecipeEntries.Count}, " +
                $"equipmentVisuals={_equipmentVisuals.Count}, equipmentEntries={_equipmentVisualEntries.Count}.");
            Modified = true;
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
                Skeletons = _skeletons.ToArray(),
                SkinMeshes = _skinMeshes.ToArray(),
                SkinWeights = _skinWeights.ToArray(),
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
                        throw new InvalidOperationException(
                            $"Actor rig family '{kind}' skeleton '{skeletonModelPath}' is not compatible with animation clip '{clip.Name}' " +
                            $"from '{clip.SourcePath}': missing pose track target '{track.TargetName}'.");
                    }
                }
            }
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
                if (IsPlayerActor(actor))
                    BuildNpcVisualRecipe(actor, true, female, beast, bodyParts, bodyPartsById);
            }
        }

        void BuildCreatureVisualRecipe(ActorDef actor, Dictionary<string, BsaEntry> bsaByName)
        {
            string actorModel = ResolveCreatureActorModelPath(actor, bsaByName);
            int rigFamilyIndex = AddRigFamily(ActorRigFamilyKind.Creature, actorModel, actor.Id);
            int skinBindingIndex = RequireSkinBinding(actorModel, rigFamilyIndex, null, $"creature '{actor.Id}'");
            var binding = _skinBindings[skinBindingIndex];
            int firstEntry = _actorVisualRecipeEntries.Count;
            AddAllSkinBindingMeshesToActorRecipe(skinBindingIndex, binding, ActorVisualPartReference.Cuirass);
            AddActorVisualRecipe(actor.ContentId, false, ActorVisualBodyVariant.Male, rigFamilyIndex, firstEntry, _actorVisualRecipeEntries.Count - firstEntry);
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

                if (!TryResolveNpcRaceBodyPart(bodyParts, actor.RaceId, reference, female, firstPerson, out var bodyPart))
                    throw new InvalidOperationException(
                        $"NPC actor '{actor.Id}' race '{actor.RaceId}' is missing required body part '{reference}' for firstPerson={firstPerson}, female={female}.");

                AddNpcBodyPart(actor.Id, bodyPart, reference, rigFamilyIndex, ref usedParts);
            }

            AddActorVisualRecipe(actor.ContentId, firstPerson, variant, rigFamilyIndex, firstEntry, _actorVisualRecipeEntries.Count - firstEntry);
        }

        void AddActorVisualRecipe(ContentId actorContentId, bool firstPerson, ActorVisualBodyVariant variant, int rigFamilyIndex, int firstEntry, int entryCount)
        {
            if (entryCount <= 0)
                throw new InvalidOperationException($"Actor visual recipe '{actorContentId}' firstPerson={firstPerson} produced no skin entries.");

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
            var expectedPart = GetMeshPart(reference);
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

            var bodyPart = RequireBodyPart(bodyPartId, firstPerson, $"equipment item '{itemId}' part '{bodyPartRef.Part}'");
            if (bodyPart.Type != ActorBodyPartMeshType.Armor && bodyPart.Type != ActorBodyPartMeshType.Clothing)
                throw new InvalidOperationException($"Equipment item '{itemId}' body part '{bodyPartId}' is '{bodyPart.Type}', expected Armor or Clothing.");

            int skinBindingIndex = RequireSkinBinding(bodyPart.Model, rigFamilyIndex, reference, $"equipment item '{itemId}' body part '{bodyPartId}'");
            AddFilteredSkinBindingMeshesToEquipmentVisual(skinBindingIndex, bodyPartRef.Part, reference, $"equipment item '{itemId}' body part '{bodyPartId}'");
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

        int RequireSkinBinding(string modelPath, int rigFamilyIndex, ActorVisualPartReference? reference, string context)
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
                throw new InvalidOperationException($"{context} skin model '{model}' has no baked skin meshes for rig '{_rigFamilies[rigFamilyIndex].SkeletonModelPath}'.");

            int index = _skinBindings.Count;
            _skinBindings.Add(new ActorSkinBindingDef
            {
                SkinModelPath = model,
                RigFamilyIndex = rigFamilyIndex,
                FirstSkinMeshIndex = binding.FirstSkinMeshIndex,
                SkinMeshCount = binding.SkinMeshCount,
            });
            _skinBindingsByKey[key] = index;
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
                    AttachBoneIndex = -1,
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
            int attachBoneIndex = ResolvePartAttachBoneIndex(_rigFamilies[binding.RigFamilyIndex], reference, context);
            byte rigidMirrorX = 0;
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            if (acceptDeclaredMeshes)
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
                        if (!IsRenderableSkinMesh(mesh) || !MatchesMeshFilter(mesh.NodeName, meshFilter))
                            continue;

                        if (mesh.IsRigid != 0 && attachBoneIndex < 0)
                            throw new InvalidOperationException($"{context} rigid mesh '{mesh.NodeName}' has no attach bone for '{reference}'.");

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

                if (added == 0 && SkinBindingHasOnlyRigidRenderableMeshes(binding))
                    added += AddDeclaredSkinBindingMeshesToActorRecipe(skinBindingIndex, binding, reference, context, attachBoneIndex, rigidMirrorX);

                if (added == 0)
                    throw new InvalidOperationException($"{context} produced no renderable meshes matching '{string.Join("' or '", meshFilters)}'.");
            }

            if (added == 0)
                throw new InvalidOperationException($"{context} declared '{reference}' but produced no renderable meshes.");
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
                    throw new InvalidOperationException($"{context} rigid mesh '{mesh.NodeName}' has no attach bone for '{reference}'.");

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

        void AddFilteredSkinBindingMeshesToEquipmentVisual(
            int skinBindingIndex,
            ItemEquipmentPartReference equipmentPart,
            ActorVisualPartReference reference,
            string context)
        {
            var binding = _skinBindings[skinBindingIndex];
            int attachBoneIndex = ResolvePartAttachBoneIndex(_rigFamilies[binding.RigFamilyIndex], reference, context);
            byte rigidMirrorX = 0;
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            string[] meshFilters = BuildMeshFilters(reference);
            for (int filterIndex = 0; filterIndex < meshFilters.Length && added == 0; filterIndex++)
            {
                string meshFilter = meshFilters[filterIndex];
                for (int i = binding.FirstSkinMeshIndex; i < end; i++)
                {
                    var mesh = _skinMeshes[i];
                    if (!IsRenderableSkinMesh(mesh) || !MatchesMeshFilter(mesh.NodeName, meshFilter))
                        continue;
                    if (mesh.IsRigid != 0 && attachBoneIndex < 0)
                        throw new InvalidOperationException($"{context} rigid mesh '{mesh.NodeName}' has no attach bone for '{reference}'.");

                    AddSkinBindingMeshToEquipmentVisual(
                        equipmentPart,
                        skinBindingIndex,
                        i,
                        mesh,
                        attachBoneIndex,
                        rigidMirrorX,
                        reference,
                        context);
                    added++;
                }
            }

            if (added == 0 && SkinBindingHasOnlyRigidRenderableMeshes(binding))
                added += AddDeclaredSkinBindingMeshesToEquipmentVisual(
                    skinBindingIndex,
                    binding,
                    equipmentPart,
                    reference,
                    context,
                    attachBoneIndex,
                    rigidMirrorX);

            if (added == 0)
                throw new InvalidOperationException($"{context} produced no renderable meshes matching '{string.Join("' or '", meshFilters)}'.");
        }

        int AddDeclaredSkinBindingMeshesToEquipmentVisual(
            int skinBindingIndex,
            ActorSkinBindingDef binding,
            ItemEquipmentPartReference equipmentPart,
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
                    throw new InvalidOperationException($"{context} rigid mesh '{mesh.NodeName}' has no attach bone for '{reference}'.");

                AddSkinBindingMeshToEquipmentVisual(
                    equipmentPart,
                    skinBindingIndex,
                    i,
                    mesh,
                    attachBoneIndex,
                    rigidMirrorX,
                    reference,
                    context);
                added++;
            }

            return added;
        }

        void AddSkinBindingMeshToEquipmentVisual(
            ItemEquipmentPartReference equipmentPart,
            int skinBindingIndex,
            int skinMeshIndex,
            ActorSkinMeshDef mesh,
            int attachBoneIndex,
            byte rigidMirrorX,
            ActorVisualPartReference reference,
            string context)
        {
            _equipmentVisualEntries.Add(new ActorEquipmentVisualEntryDef
            {
                PartReference = equipmentPart,
                SkinBindingIndex = skinBindingIndex,
                SkinMeshIndex = skinMeshIndex,
                AttachBoneIndex = mesh.IsRigid != 0 ? attachBoneIndex : -1,
                RigidMirrorX = mesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
        }

        static bool IsRenderableSkinMesh(ActorSkinMeshDef mesh)
            => mesh != null
               && mesh.MeshIndex >= 0
               && mesh.VertexPositions != null
               && mesh.VertexPositions.Length >= 3
               && mesh.Indices != null
               && mesh.Indices.Length > 0;

        int ResolvePartAttachBoneIndex(ActorRigFamilyDef rig, ActorVisualPartReference reference, string context)
        {
            if ((uint)rig.SkeletonIndex >= (uint)_skeletons.Count)
                throw new InvalidOperationException($"{context} references invalid rig skeleton index {rig.SkeletonIndex}.");

            var bones = _skeletons[rig.SkeletonIndex]?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            switch (reference)
            {
                case ActorVisualPartReference.Head:
                    return ResolveBoneIndex(bones, "Head", "Bip01 Head");
                case ActorVisualPartReference.Hair:
                    return ResolveBoneIndex(bones, "Hair", "Head", "Bip01 Head");
                case ActorVisualPartReference.Neck:
                    return ResolveBoneIndex(bones, "Neck", "Bip01 Neck");
                case ActorVisualPartReference.Cuirass:
                    return ResolveBoneIndex(bones, "Bip01 Spine1", "Bip01 Spine");
                case ActorVisualPartReference.Groin:
                case ActorVisualPartReference.Skirt:
                    return ResolveBoneIndex(bones, "Groin", "Bip01 Pelvis", "Pelvis");
                case ActorVisualPartReference.RightHand:
                    return ResolveBoneIndex(bones, "Right Hand", "Bip01 R Hand");
                case ActorVisualPartReference.LeftHand:
                    return ResolveBoneIndex(bones, "Left Hand", "Bip01 L Hand");
                case ActorVisualPartReference.RightWrist:
                    return ResolveBoneIndex(bones, "Right Wrist", "Bip01 R Forearm");
                case ActorVisualPartReference.LeftWrist:
                    return ResolveBoneIndex(bones, "Left Wrist", "Bip01 L Forearm");
                case ActorVisualPartReference.RightForearm:
                    return ResolveBoneIndex(bones, "Right Forearm", "Bip01 R Forearm");
                case ActorVisualPartReference.LeftForearm:
                    return ResolveBoneIndex(bones, "Left Forearm", "Bip01 L Forearm");
                case ActorVisualPartReference.RightUpperarm:
                    return ResolveBoneIndex(bones, "Right Upper Arm", "Bip01 R UpperArm");
                case ActorVisualPartReference.LeftUpperarm:
                    return ResolveBoneIndex(bones, "Left Upper Arm", "Bip01 L UpperArm");
                case ActorVisualPartReference.RightPauldron:
                    return ResolveBoneIndex(bones, "Right Clavicle", "Bip01 R Clavicle", "Bip01 R UpperArm");
                case ActorVisualPartReference.LeftPauldron:
                    return ResolveBoneIndex(bones, "Left Clavicle", "Bip01 L Clavicle", "Bip01 L UpperArm");
                case ActorVisualPartReference.RightFoot:
                    return ResolveBoneIndex(bones, "Right Foot", "Bip01 R Foot");
                case ActorVisualPartReference.LeftFoot:
                    return ResolveBoneIndex(bones, "Left Foot", "Bip01 L Foot");
                case ActorVisualPartReference.RightAnkle:
                    return ResolveBoneIndex(bones, "Right Ankle", "Bip01 R Calf");
                case ActorVisualPartReference.LeftAnkle:
                    return ResolveBoneIndex(bones, "Left Ankle", "Bip01 L Calf");
                case ActorVisualPartReference.RightKnee:
                    return ResolveBoneIndex(bones, "Right Knee", "Bip01 R Calf");
                case ActorVisualPartReference.LeftKnee:
                    return ResolveBoneIndex(bones, "Left Knee", "Bip01 L Calf");
                case ActorVisualPartReference.RightLeg:
                    return ResolveBoneIndex(bones, "Right Upper Leg", "Bip01 R Thigh");
                case ActorVisualPartReference.LeftLeg:
                    return ResolveBoneIndex(bones, "Left Upper Leg", "Bip01 L Thigh");
                case ActorVisualPartReference.Shield:
                    return ResolveBoneIndex(bones, "Shield Bone", "Bip01 L Forearm");
                case ActorVisualPartReference.Weapon:
                    return ResolveBoneIndex(bones, "Weapon Bone", "Bip01 R Hand");
                case ActorVisualPartReference.Tail:
                    return ResolveBoneIndex(bones, "Tail", "Bip01 Tail");
                default:
                    return -1;
            }
        }

        static int ResolveBoneIndex(ActorSkeletonBoneDef[] bones, params string[] names)
        {
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < bones.Length; i++)
                    if (string.Equals(bones[i].Name, names[n], StringComparison.OrdinalIgnoreCase))
                        return i;
            }

            return -1;
        }

        static void FixedStringFilter(ActorVisualPartReference reference, out string meshFilter)
        {
            meshFilter = reference switch
            {
                ActorVisualPartReference.Head => "head",
                ActorVisualPartReference.Hair => "hair",
                ActorVisualPartReference.Neck => "neck",
                ActorVisualPartReference.Cuirass => "chest",
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => "groin",
                ActorVisualPartReference.RightHand => "right hand",
                ActorVisualPartReference.LeftHand => "left hand",
                ActorVisualPartReference.RightWrist => "right wrist",
                ActorVisualPartReference.LeftWrist => "left wrist",
                ActorVisualPartReference.RightForearm => "right forearm",
                ActorVisualPartReference.LeftForearm => "left forearm",
                ActorVisualPartReference.RightUpperarm => "right upper arm",
                ActorVisualPartReference.LeftUpperarm => "left upper arm",
                ActorVisualPartReference.RightFoot => "right foot",
                ActorVisualPartReference.LeftFoot => "left foot",
                ActorVisualPartReference.RightAnkle => "right ankle",
                ActorVisualPartReference.LeftAnkle => "left ankle",
                ActorVisualPartReference.RightKnee => "right knee",
                ActorVisualPartReference.LeftKnee => "left knee",
                ActorVisualPartReference.RightLeg => "right upper leg",
                ActorVisualPartReference.LeftLeg => "left upper leg",
                ActorVisualPartReference.RightPauldron => "right clavicle",
                ActorVisualPartReference.LeftPauldron => "left clavicle",
                ActorVisualPartReference.Tail => "tail",
                _ => string.Empty,
            };
        }

        static string[] BuildMeshFilters(ActorVisualPartReference reference)
        {
            FixedStringFilter(reference, out string primary);
            string meshPart = MeshPartFilter(reference);
            if (string.IsNullOrWhiteSpace(meshPart)
                || string.Equals(primary, meshPart, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { primary };
            }

            return new[] { primary, meshPart };
        }

        static string MeshPartFilter(ActorVisualPartReference reference)
        {
            return GetMeshPart(reference) switch
            {
                ActorBodyPartMeshPart.Head => "head",
                ActorBodyPartMeshPart.Hair => "hair",
                ActorBodyPartMeshPart.Neck => "neck",
                ActorBodyPartMeshPart.Chest => "chest",
                ActorBodyPartMeshPart.Groin => "groin",
                ActorBodyPartMeshPart.Hand => "hand",
                ActorBodyPartMeshPart.Wrist => "wrist",
                ActorBodyPartMeshPart.Forearm => "forearm",
                ActorBodyPartMeshPart.Upperarm => "upper arm",
                ActorBodyPartMeshPart.Foot => "foot",
                ActorBodyPartMeshPart.Ankle => "ankle",
                ActorBodyPartMeshPart.Knee => "knee",
                ActorBodyPartMeshPart.Upperleg => "upper leg",
                ActorBodyPartMeshPart.Clavicle => "clavicle",
                ActorBodyPartMeshPart.Tail => "tail",
                _ => string.Empty,
            };
        }

        static bool MatchesMeshFilter(string nodeName, string meshFilter)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(meshFilter))
                return true;

            if (string.Equals(nodeName, meshFilter, StringComparison.OrdinalIgnoreCase))
                return true;
            if (nodeName.StartsWith("tri ", StringComparison.OrdinalIgnoreCase)
                && nodeName.Substring(4).StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedNode = NormalizePartName(nodeName);
            string normalizedFilter = NormalizePartName(meshFilter);
            return normalizedNode == normalizedFilter
                   || normalizedNode.StartsWith(normalizedFilter, StringComparison.Ordinal)
                   || normalizedNode.Contains(normalizedFilter, StringComparison.Ordinal);
        }

        static string NormalizePartName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.StartsWith("tri ", StringComparison.Ordinal))
                normalized = normalized.Substring(4);
            if (normalized.StartsWith("bip01 ", StringComparison.Ordinal))
                normalized = normalized.Substring(6);
            normalized = normalized.Replace("_", " ");
            normalized = normalized.Replace("-", " ");
            normalized = normalized.Replace(" l ", " left ");
            normalized = normalized.Replace(" r ", " right ");
            normalized = normalized.Replace("upperarm", "upper arm");
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ");
            return " " + normalized.Trim() + " ";
        }

        Dictionary<string, RaceDef> BuildRaceLookup(GameplayContentData gameplayContent)
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
            => !string.IsNullOrWhiteSpace(raceId)
               && races != null
               && races.TryGetValue(raceId, out var race)
               && (race.Flags & 0x02) != 0;

        static bool IsPlayerActor(ActorDef actor)
            => actor.Kind == ActorDefKind.Npc
               && string.Equals(actor.Id, "player", StringComparison.OrdinalIgnoreCase);

        int ResolveNpcRigFamilyIndex(bool firstPerson, bool female, bool beast)
        {
            ActorRigFamilyKind kind = firstPerson
                ? beast ? ActorRigFamilyKind.NpcBeastFirstPerson : female ? ActorRigFamilyKind.NpcFemaleFirstPerson : ActorRigFamilyKind.NpcMaleFirstPerson
                : beast ? ActorRigFamilyKind.NpcBeast : female ? ActorRigFamilyKind.NpcFemale : ActorRigFamilyKind.NpcMale;

            for (int i = 0; i < _rigFamilies.Count; i++)
                if (_rigFamilies[i].FamilyKind == kind)
                    return i;

            throw new InvalidOperationException($"NPC rig family '{kind}' was not built.");
        }

        static bool IsNpcRigFamily(ActorRigFamilyKind kind)
            => kind is ActorRigFamilyKind.NpcMale
                or ActorRigFamilyKind.NpcFemale
                or ActorRigFamilyKind.NpcBeast
                or ActorRigFamilyKind.NpcMaleFirstPerson
                or ActorRigFamilyKind.NpcFemaleFirstPerson
                or ActorRigFamilyKind.NpcBeastFirstPerson;

        static bool IsFirstPersonRigFamily(ActorRigFamilyKind kind)
            => kind is ActorRigFamilyKind.NpcMaleFirstPerson
                or ActorRigFamilyKind.NpcFemaleFirstPerson
                or ActorRigFamilyKind.NpcBeastFirstPerson;

        static bool IsBeastRigFamily(ActorRigFamilyKind kind)
            => kind is ActorRigFamilyKind.NpcBeast or ActorRigFamilyKind.NpcBeastFirstPerson;

        static bool IsNpcSkeletonModel(string modelPath)
        {
            string normalized = NormalizeModelPath(modelPath);
            return normalized.Equals("meshes\\base_anim.nif", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("meshes\\base_anim_female.nif", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("meshes\\base_animkna.nif", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("meshes\\xbase_anim.1st.nif", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("meshes\\base_anim_female.1st.nif", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("meshes\\base_animkna.1st.nif", StringComparison.OrdinalIgnoreCase);
        }

        static void ApplyNpcLeftHelperMirroring(ActorSkeletonDef skeleton)
        {
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if (bones.Length == 0)
                return;

            var rootMatrices = new Matrix4x4[bones.Length];
            Matrix4x4 mirrorX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                Matrix4x4 local = UnpackMatrix(bone.BindLocalMatrix, 0);
                if (IsMirroredNpcLeftHelper(bone.Name))
                    local *= mirrorX;

                Matrix4x4 root = bone.ParentIndex >= 0 && bone.ParentIndex < i
                    ? rootMatrices[bone.ParentIndex] * local
                    : local;

                bone.BindLocalMatrix = PackMatrix(local);
                bone.BindLocalToRootMatrix = PackMatrix(root);
                bones[i] = bone;
                rootMatrices[i] = root;
            }
        }

        static bool IsMirroredNpcLeftHelper(string name)
            => name.Equals("Left Clavicle", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Upper Arm", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Forearm", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Wrist", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Upper Leg", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Knee", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Ankle", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Left Foot", StringComparison.OrdinalIgnoreCase);

        static string ResolveNpcSkeletonModel(bool firstPerson, bool female, bool beast)
            => NormalizeModelPath(firstPerson
                ? beast ? "meshes\\base_animkna.1st.nif"
                    : female ? "meshes\\base_anim_female.1st.nif"
                    : "meshes\\xbase_anim.1st.nif"
                : beast ? "meshes\\base_animkna.nif"
                    : female ? "meshes\\base_anim_female.nif"
                    : "meshes\\base_anim.nif");

        static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorVisualPartReference partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
        {
            result = default;
            bodyParts ??= Array.Empty<ActorBodyPartDef>();

            ActorBodyPartMeshPart meshPart = GetMeshPart(partReference);
            bool isFirstPersonArmPart = IsFirstPersonMeshPart(meshPart);
            int bestScore = int.MaxValue;
            for (int i = 0; i < bodyParts.Length; i++)
            {
                var part = bodyParts[i];
                if (part.Type != ActorBodyPartMeshType.Skin
                    || part.Vampire != 0
                    || part.NotPlayable != 0
                    || part.Part != meshPart
                    || !string.Equals(part.RaceId, raceId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool partFirstPerson = part.FirstPerson != 0;
                bool partFemale = part.Female != 0;
                int score = ResolveNpcRaceBodyPartScore(firstPerson, female, isFirstPersonArmPart, partFirstPerson, partFemale);
                if (score >= bestScore)
                    continue;

                result = part;
                bestScore = score;
                if (score == 0)
                    return true;
            }

            return bestScore < int.MaxValue;
        }

        static int ResolveNpcRaceBodyPartScore(
            bool firstPerson,
            bool female,
            bool isFirstPersonArmPart,
            bool partFirstPerson,
            bool partFemale)
        {
            if (partFirstPerson == firstPerson && partFemale == female)
                return 0;

            if (firstPerson && isFirstPersonArmPart && !partFirstPerson && partFemale == female)
                return 10;

            if (female && partFirstPerson == firstPerson && !partFemale)
                return 20;

            if (firstPerson && isFirstPersonArmPart && female && !partFirstPerson && !partFemale)
                return 30;

            return int.MaxValue;
        }

        static bool IsBaseSkinPartReference(ActorVisualPartReference type)
            => type is ActorVisualPartReference.Neck
                or ActorVisualPartReference.Cuirass
                or ActorVisualPartReference.Groin
                or ActorVisualPartReference.RightHand
                or ActorVisualPartReference.LeftHand
                or ActorVisualPartReference.RightWrist
                or ActorVisualPartReference.LeftWrist
                or ActorVisualPartReference.RightForearm
                or ActorVisualPartReference.LeftForearm
                or ActorVisualPartReference.RightUpperarm
                or ActorVisualPartReference.LeftUpperarm
                or ActorVisualPartReference.RightFoot
                or ActorVisualPartReference.LeftFoot
                or ActorVisualPartReference.RightAnkle
                or ActorVisualPartReference.LeftAnkle
                or ActorVisualPartReference.RightKnee
                or ActorVisualPartReference.LeftKnee
                or ActorVisualPartReference.RightLeg
                or ActorVisualPartReference.LeftLeg
                or ActorVisualPartReference.Tail;

        static bool IsFirstPersonPartReference(ActorVisualPartReference type)
            => type is ActorVisualPartReference.RightHand
                or ActorVisualPartReference.LeftHand
                or ActorVisualPartReference.RightWrist
                or ActorVisualPartReference.LeftWrist
                or ActorVisualPartReference.RightForearm
                or ActorVisualPartReference.LeftForearm
                or ActorVisualPartReference.RightUpperarm
                or ActorVisualPartReference.LeftUpperarm;

        static bool IsFirstPersonMeshPart(ActorBodyPartMeshPart part)
            => part is ActorBodyPartMeshPart.Hand
                or ActorBodyPartMeshPart.Wrist
                or ActorBodyPartMeshPart.Forearm
                or ActorBodyPartMeshPart.Upperarm;

        static bool TryMapEquipmentPartReference(ItemEquipmentPartReference source, out ActorVisualPartReference target)
        {
            if ((byte)source >= (byte)ActorVisualPartReference.Count)
            {
                target = default;
                return false;
            }

            target = (ActorVisualPartReference)(byte)source;
            return target != ActorVisualPartReference.Weapon;
        }

        static ActorBodyPartMeshPart GetMeshPart(ActorVisualPartReference type)
        {
            return type switch
            {
                ActorVisualPartReference.Head => ActorBodyPartMeshPart.Head,
                ActorVisualPartReference.Hair => ActorBodyPartMeshPart.Hair,
                ActorVisualPartReference.Neck => ActorBodyPartMeshPart.Neck,
                ActorVisualPartReference.Cuirass => ActorBodyPartMeshPart.Chest,
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => ActorBodyPartMeshPart.Groin,
                ActorVisualPartReference.RightHand or ActorVisualPartReference.LeftHand => ActorBodyPartMeshPart.Hand,
                ActorVisualPartReference.RightWrist or ActorVisualPartReference.LeftWrist => ActorBodyPartMeshPart.Wrist,
                ActorVisualPartReference.RightForearm or ActorVisualPartReference.LeftForearm => ActorBodyPartMeshPart.Forearm,
                ActorVisualPartReference.RightUpperarm or ActorVisualPartReference.LeftUpperarm => ActorBodyPartMeshPart.Upperarm,
                ActorVisualPartReference.RightFoot or ActorVisualPartReference.LeftFoot => ActorBodyPartMeshPart.Foot,
                ActorVisualPartReference.RightAnkle or ActorVisualPartReference.LeftAnkle => ActorBodyPartMeshPart.Ankle,
                ActorVisualPartReference.RightKnee or ActorVisualPartReference.LeftKnee => ActorBodyPartMeshPart.Knee,
                ActorVisualPartReference.RightLeg or ActorVisualPartReference.LeftLeg => ActorBodyPartMeshPart.Upperleg,
                ActorVisualPartReference.RightPauldron or ActorVisualPartReference.LeftPauldron => ActorBodyPartMeshPart.Clavicle,
                ActorVisualPartReference.Tail => ActorBodyPartMeshPart.Tail,
                _ => ActorBodyPartMeshPart.Chest,
            };
        }

        bool HasBeastForbiddenEquipmentPart(ItemEquipmentDef equipment)
        {
            if (equipment.FirstBodyPartIndex < 0 || equipment.BodyPartCount <= 0)
                return false;

            var parts = CurrentGameplayItemBodyParts ?? Array.Empty<ItemEquipmentBodyPartDef>();
            int end = Math.Min(parts.Length, equipment.FirstBodyPartIndex + equipment.BodyPartCount);
            for (int i = equipment.FirstBodyPartIndex; i < end; i++)
            {
                var part = parts[i].Part;
                if (part == ItemEquipmentPartReference.Head
                    || part == ItemEquipmentPartReference.RightFoot
                    || part == ItemEquipmentPartReference.LeftFoot)
                {
                    return true;
                }
            }

            return false;
        }

        uint BuildEquipmentCoverageMask(ItemEquipmentDef equipment)
        {
            uint mask = 0u;
            var parts = CurrentGameplayItemBodyParts ?? Array.Empty<ItemEquipmentBodyPartDef>();
            if (equipment.FirstBodyPartIndex >= 0 && equipment.BodyPartCount > 0)
            {
                int end = Math.Min(parts.Length, equipment.FirstBodyPartIndex + equipment.BodyPartCount);
                for (int i = equipment.FirstBodyPartIndex; i < end; i++)
                    if (TryMapEquipmentPartReference(parts[i].Part, out var reference))
                        ReservePart(reference, ref mask);
            }

            if (equipment.Slot == ItemEquipmentSlot.Helmet)
                ReservePart(ActorVisualPartReference.Hair, ref mask);
            else if (equipment.Slot == ItemEquipmentSlot.Robe)
            {
                ReservePart(ActorVisualPartReference.Groin, ref mask);
                ReservePart(ActorVisualPartReference.Skirt, ref mask);
                ReservePart(ActorVisualPartReference.RightLeg, ref mask);
                ReservePart(ActorVisualPartReference.LeftLeg, ref mask);
                ReservePart(ActorVisualPartReference.RightUpperarm, ref mask);
                ReservePart(ActorVisualPartReference.LeftUpperarm, ref mask);
                ReservePart(ActorVisualPartReference.RightKnee, ref mask);
                ReservePart(ActorVisualPartReference.LeftKnee, ref mask);
                ReservePart(ActorVisualPartReference.RightForearm, ref mask);
                ReservePart(ActorVisualPartReference.LeftForearm, ref mask);
                ReservePart(ActorVisualPartReference.Cuirass, ref mask);
            }
            else if (equipment.Slot == ItemEquipmentSlot.Skirt)
            {
                ReservePart(ActorVisualPartReference.Groin, ref mask);
                ReservePart(ActorVisualPartReference.RightLeg, ref mask);
                ReservePart(ActorVisualPartReference.LeftLeg, ref mask);
            }

            return mask;
        }

        static void ReserveUniquePart(ActorVisualPartReference reference, ref uint usedParts, string context)
        {
            int bit = (int)reference;
            if ((uint)bit >= 32u)
                return;

            uint mask = 1u << bit;
            if ((usedParts & mask) != 0)
                throw new InvalidOperationException($"{context} has duplicate visual part '{reference}'.");
            usedParts |= mask;
        }

        static void ReservePart(ActorVisualPartReference reference, ref uint usedParts)
        {
            TryReservePart(reference, ref usedParts);
        }

        static bool TryReservePart(ActorVisualPartReference reference, ref uint usedParts)
        {
            int bit = (int)reference;
            if ((uint)bit < 32u)
            {
                uint mask = 1u << bit;
                if ((usedParts & mask) != 0)
                    return false;

                usedParts |= mask;
            }

            return true;
        }



        void RebuildTextMarkers()
        {
            _textMarkers.Clear();
            for (int clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
            {
                var clip = _clips[clipIndex];
                if (clip == null || clip.FirstTextKeyIndex < 0 || clip.TextKeyCount <= 0)
                {
                    if (clip != null)
                    {
                        clip.FirstTextMarkerIndex = -1;
                        clip.TextMarkerCount = 0;
                    }
                    continue;
                }

                int firstMarker = _textMarkers.Count;
                int keyEnd = Math.Min(_textKeys.Count, clip.FirstTextKeyIndex + clip.TextKeyCount);
                for (int keyIndex = clip.FirstTextKeyIndex; keyIndex < keyEnd; keyIndex++)
                    AddTextMarkers(_textKeys[keyIndex]);

                clip.FirstTextMarkerIndex = _textMarkers.Count > firstMarker ? firstMarker : -1;
                clip.TextMarkerCount = _textMarkers.Count - firstMarker;
            }
        }

        void AddTextMarkers(ActorAnimationTextKeyDef key)
        {
            if (string.IsNullOrWhiteSpace(key.Text))
                return;

            string[] markers = key.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < markers.Length; i++)
            {
                string text = markers[i]?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                SplitMarker(text, out string group, out string value);
                _textMarkers.Add(new ActorAnimationTextMarkerDef
                {
                    Time = key.Time,
                    Group = group,
                    Value = value,
                    Text = text,
                    Kind = ResolveMarkerKind(value),
                });
            }
        }

        static void SplitMarker(string text, out string group, out string value)
        {
            int colon = text.IndexOf(':');
            if (colon < 0)
            {
                group = string.Empty;
                value = text.Trim();
                return;
            }

            group = text.Substring(0, colon).Trim();
            value = text.Substring(colon + 1).Trim();
        }

        static ActorAnimationTextMarkerKind ResolveMarkerKind(string value)
        {
            if (string.Equals(value, "start", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.Start;
            if (string.Equals(value, "loop start", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.LoopStart;
            if (string.Equals(value, "loop stop", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.LoopStop;
            if (string.Equals(value, "stop", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.Stop;
            return ActorAnimationTextMarkerKind.Marker;
        }

        void AddClips(NifFile nif)
        {
            var clips = NifActorAnimationExtractor.ExtractClips(nif, _tracks, _keys, _textKeys);
            for (int i = 0; i < clips.Length; i++)
                _clips.Add(clips[i]);
        }

        void AddCompanionKfClips(string modelPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            string normalized = NormalizeModelPath(modelPath);
            AddKfClips(
                BuildCompanionKfPath(normalized),
                bsa,
                bsaByName,
                required: _requiredCompanionKfActorModels.Contains(normalized));
        }

        void AddSharedCreatureKfClips(string modelPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            if (_bipedalCreatureActorModels.Contains(NormalizeModelPath(modelPath)))
                AddKfClips("meshes\\xbase_anim.kf", bsa, bsaByName, required: true);
        }

        void AddSharedNpcKfClips(string modelPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            string normalized = NormalizeModelPath(modelPath);
            switch (normalized)
            {
                case "meshes\\base_anim.nif":
                case "meshes\\base_animkna.nif":
                    AddKfClips("meshes\\xbase_anim.kf", bsa, bsaByName, required: true);
                    break;
                case "meshes\\base_anim_female.nif":
                    AddKfClips("meshes\\xbase_anim.kf", bsa, bsaByName, required: true);
                    AddKfClips("meshes\\xbase_anim_female.kf", bsa, bsaByName, required: true);
                    break;
                case "meshes\\base_anim_female.1st.nif":
                case "meshes\\base_animkna.1st.nif":
                case "meshes\\xbase_anim.1st.nif":
                    AddKfClips("meshes\\xbase_anim.1st.kf", bsa, bsaByName, required: true);
                    break;
            }
        }

        void AddKfClips(string kfPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName, bool required)
        {
            string normalized = NormalizeModelPath(kfPath);
            if (string.IsNullOrEmpty(normalized) || bsa == null || bsaByName == null || !bsaByName.TryGetValue(normalized, out var entry))
            {
                if (required)
                    throw new InvalidOperationException($"Required actor animation source '{normalized}' is missing from the archive.");
                return;
            }

            try
            {
                var kf = NifFile.Parse(normalized, bsa.Read(entry));
                AddClips(kf);
            }
            catch (Exception ex)
            {
                if (required)
                    throw new InvalidOperationException($"Required actor animation source '{normalized}' could not be parsed.", ex);
            }
        }

        static bool IsActorAnimationCandidate(
            NifFile nif,
            string modelPath,
            Dictionary<string, BsaEntry> bsaByName,
            bool forceActorModel)
        {
            return forceActorModel;
        }

        static bool IsPoseTrack(ActorAnimationTrackDef track)
        {
            return track != null
                   && track.Kind is ActorAnimationTrackKind.Translation
                       or ActorAnimationTrackKind.Rotation
                       or ActorAnimationTrackKind.Scale
                       or ActorAnimationTrackKind.XRotation
                       or ActorAnimationTrackKind.YRotation
                       or ActorAnimationTrackKind.ZRotation;
        }

        static int FindBoneIndex(ActorSkeletonBoneDef[] bones, string name)
        {
            if (bones == null || string.IsNullOrEmpty(name))
                return -1;

            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;

            string canonical = CanonicalBoneName(name);
            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(CanonicalBoneName(bones[i].Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return i;

            return -1;
        }

        static string BuildCompanionKfPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return string.Empty;

            int dot = modelPath.LastIndexOf('.');
            if (dot < 0)
                return modelPath + ".kf";
            return modelPath.Substring(0, dot) + ".kf";
        }

        static string ResolveCreatureActorModelPath(ActorDef actor, Dictionary<string, BsaEntry> bsaByName)
        {
            string model = NormalizeModelPath(actor.Model);
            if (string.IsNullOrEmpty(model))
                return model;

            string corrected = BuildPrefixedActorModelPath(model);
            string correctedKf = BuildCompanionKfPath(corrected);
            if (bsaByName == null || !bsaByName.ContainsKey(correctedKf))
                return model;

            if (!bsaByName.ContainsKey(corrected))
                throw new InvalidOperationException(
                    $"Creature actor '{actor.Id}' resolves animation source '{correctedKf}' but required actor model '{corrected}' is missing.");

            return corrected;
        }

        static string BuildPrefixedActorModelPath(string modelPath)
        {
            string normalized = NormalizeModelPath(modelPath);
            int slash = normalized.LastIndexOf('\\');
            return slash >= 0
                ? normalized.Substring(0, slash + 1) + "x" + normalized.Substring(slash + 1)
                : "x" + normalized;
        }

        static string NormalizeModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\"))
                trimmed = trimmed.Replace("\\\\", "\\");
            if (!trimmed.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase))
                trimmed = "meshes\\" + trimmed;
            return trimmed.ToLowerInvariant();
        }

        static string BuildBindingKey(string modelPath, string bindReferenceSkeletonPath)
        {
            string model = NormalizeModelPath(modelPath);
            string reference = NormalizeModelPath(bindReferenceSkeletonPath);
            return string.IsNullOrEmpty(reference) ? model : $"{model}|{reference}";
        }

        void AttachRenderData(
            string modelPath,
            ActorSkinMeshDef[] skinMeshes,
            ModelPrefabSource prefabSource,
            ActorSkeletonDef skeleton,
            ActorSkeletonDef bindReferenceSkeleton,
            bool remapSkinBonesToReferenceSkeleton,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures)
        {
            if (skinMeshes == null || skinMeshes.Length == 0 || prefabSource?.Nodes == null)
                return;

            var renderLeaves = new List<int>();
            for (int i = 0; i < prefabSource.Nodes.Length; i++)
            {
                if (prefabSource.Nodes[i].Kind == ModelPrefabNodeKind.RenderLeaf
                    && prefabSource.Nodes[i].RenderLeaf.VertexCount > 0)
                {
                    renderLeaves.Add(i);
                }
            }

            var usedLeaves = new bool[renderLeaves.Count];
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int leafSlot = FindRenderLeafSlot(prefabSource, renderLeaves, usedLeaves, skinMeshes[i].NodeName);
                if ((uint)leafSlot >= (uint)renderLeaves.Count)
                    continue;

                usedLeaves[leafSlot] = true;
                int nodeIndex = renderLeaves[leafSlot];
                var node = prefabSource.Nodes[nodeIndex];
                var renderLeaf = node.RenderLeaf;
                int meshIndex = meshes?.AddOrGet($"{modelPath}#{nodeIndex}", renderLeaf) ?? -1;
                int materialIndex = materials?.AddOrGet(node.MaterialFlags) ?? -1;
                int textureIndex = textures?.AddOrGet(node.TexturePath) ?? -1;
                Bounds bounds = renderLeaf.LocalBounds;

                skinMeshes[i].MeshIndex = meshIndex;
                skinMeshes[i].MaterialIndex = materialIndex;
                skinMeshes[i].TextureIndex = textureIndex;
                skinMeshes[i].BoundsCenterX = bounds.center.x;
                skinMeshes[i].BoundsCenterY = bounds.center.y;
                skinMeshes[i].BoundsCenterZ = bounds.center.z;
                skinMeshes[i].BoundsExtentsX = bounds.extents.x;
                skinMeshes[i].BoundsExtentsY = bounds.extents.y;
                skinMeshes[i].BoundsExtentsZ = bounds.extents.z;
                skinMeshes[i].VertexPositions = PackVector3Array(renderLeaf.Vertices);
                skinMeshes[i].VertexNormals = PackVector3Array(renderLeaf.Normals);
                skinMeshes[i].VertexUvs = PackVector2Array(renderLeaf.Uvs);
                skinMeshes[i].Indices = renderLeaf.Indices != null
                    ? (int[])renderLeaf.Indices.Clone()
                    : Array.Empty<int>();
                if (skinMeshes[i].IsRigid != 0)
                {
                    skinMeshes[i].GeometryToSkeletonMatrix = PackMatrix(ComputeReferenceStyleSourceLocalToRoot(prefabSource, nodeIndex));
                    skinMeshes[i].RigidOffsetX = 0f;
                    skinMeshes[i].RigidOffsetY = 0f;
                    skinMeshes[i].RigidOffsetZ = 0f;
                }
                else
                {
                    if (remapSkinBonesToReferenceSkeleton)
                        RemapWeightedSkinBoneIndices(skinMeshes[i], bindReferenceSkeleton ?? skeleton);
                    ValidateWeightedSkinMeshBake(
                        skinMeshes[i],
                        bindReferenceSkeleton ?? skeleton,
                        _skinWeights,
                        renderLeaf.Vertices?.Length ?? 0);
                }

            }
        }

        static Dictionary<string, int> BuildBoneLookup(ActorSkeletonDef skeleton)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            for (int i = 0; i < bones.Length; i++)
            {
                string name = bones[i].Name;
                AddBoneLookup(lookup, name, i);
                AddBoneLookup(lookup, CanonicalBoneName(name), i);
            }

            return lookup;
        }

        static void AddBoneLookup(Dictionary<string, int> lookup, string name, int index)
        {
            if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                lookup[name] = index;
        }

        static int ResolveBindReferenceBoneIndex(
            ActorSkinMeshDef skinMesh,
            int skinBoneIndex,
            int bindBoneCount,
            Dictionary<string, int> boneLookup)
        {
            string boneName = ReadBoneName(skinMesh, skinBoneIndex);
            if (!string.IsNullOrWhiteSpace(boneName)
                && boneLookup != null
                && TryResolveBoneName(boneLookup, boneName, out int byName)
                && (uint)byName < (uint)bindBoneCount)
            {
                return byName;
            }

            return -1;
        }

        static bool TryResolveBoneName(Dictionary<string, int> boneLookup, string boneName, out int boneIndex)
        {
            if (boneLookup.TryGetValue(boneName, out boneIndex))
                return true;

            string canonical = CanonicalBoneName(boneName);
            if (boneLookup.TryGetValue(canonical, out boneIndex))
                return true;

            boneIndex = -1;
            return false;
        }

        static string CanonicalBoneName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string value = name.Trim().ToLowerInvariant();
            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");

            return value;
        }

        static Matrix4x4[] BuildBindLocalToRootMatrices(ActorSkeletonBoneDef[] bones)
        {
            var matrices = new Matrix4x4[bones?.Length ?? 0];
            for (int i = 0; i < matrices.Length; i++)
            {
                if (TryUnpackMatrix(bones[i].BindLocalToRootMatrix, 0, out Matrix4x4 root)
                    && IsFiniteMatrix(root))
                {
                    matrices[i] = root;
                    continue;
                }

                Matrix4x4 local = TryUnpackMatrix(bones[i].BindLocalMatrix, 0, out Matrix4x4 exactLocal)
                    && IsFiniteMatrix(exactLocal)
                        ? exactLocal
                        : BuildDecomposedBindLocalMatrix(bones[i]);
                matrices[i] = bones[i].ParentIndex >= 0 && bones[i].ParentIndex < i
                    ? matrices[bones[i].ParentIndex] * local
                    : local;
            }

            return matrices;
        }

        static Matrix4x4 BuildDecomposedBindLocalMatrix(ActorSkeletonBoneDef bone)
        {
            var rotation = new Quaternion(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
            float rotationLengthSq = rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            if (rotationLengthSq <= 0.000001f)
                rotation = Quaternion.identity;
            else
                rotation.Normalize();

            float scale = bone.Scale <= 0f ? 1f : bone.Scale;
            return Matrix4x4.TRS(
                new Vector3(bone.PosX, bone.PosY, bone.PosZ),
                rotation,
                new Vector3(scale, scale, scale));
        }

        static string ReadBoneName(ActorSkinMeshDef skinMesh, int index)
        {
            return skinMesh.BoneNames != null && (uint)index < (uint)skinMesh.BoneNames.Length
                ? skinMesh.BoneNames[index] ?? string.Empty
                : string.Empty;
        }

        static Matrix4x4 UnpackMatrix(float[] values, int start)
        {
            if (values == null || values.Length < start + 16)
                return Matrix4x4.identity;

            var m = Matrix4x4.identity;
            m.m00 = values[start + 0]; m.m01 = values[start + 1]; m.m02 = values[start + 2]; m.m03 = values[start + 3];
            m.m10 = values[start + 4]; m.m11 = values[start + 5]; m.m12 = values[start + 6]; m.m13 = values[start + 7];
            m.m20 = values[start + 8]; m.m21 = values[start + 9]; m.m22 = values[start + 10]; m.m23 = values[start + 11];
            m.m30 = values[start + 12]; m.m31 = values[start + 13]; m.m32 = values[start + 14]; m.m33 = values[start + 15];
            return m;
        }

        static bool TryUnpackMatrix(float[] values, int start, out Matrix4x4 matrix)
        {
            if (values == null || values.Length < start + 16)
            {
                matrix = Matrix4x4.identity;
                return false;
            }

            matrix = UnpackMatrix(values, start);
            return true;
        }

        static bool IsFiniteMatrix(Matrix4x4 m)
            => IsFinite(m.m00) && IsFinite(m.m01) && IsFinite(m.m02) && IsFinite(m.m03)
               && IsFinite(m.m10) && IsFinite(m.m11) && IsFinite(m.m12) && IsFinite(m.m13)
               && IsFinite(m.m20) && IsFinite(m.m21) && IsFinite(m.m22) && IsFinite(m.m23)
               && IsFinite(m.m30) && IsFinite(m.m31) && IsFinite(m.m32) && IsFinite(m.m33);

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        static bool IsIdentityMatrix(Matrix4x4 m)
            => Math.Abs(m.m00 - 1f) <= 0.000001f
               && Math.Abs(m.m11 - 1f) <= 0.000001f
               && Math.Abs(m.m22 - 1f) <= 0.000001f
               && Math.Abs(m.m33 - 1f) <= 0.000001f
               && Math.Abs(m.m01) <= 0.000001f
               && Math.Abs(m.m02) <= 0.000001f
               && Math.Abs(m.m03) <= 0.000001f
               && Math.Abs(m.m10) <= 0.000001f
               && Math.Abs(m.m12) <= 0.000001f
               && Math.Abs(m.m13) <= 0.000001f
               && Math.Abs(m.m20) <= 0.000001f
               && Math.Abs(m.m21) <= 0.000001f
               && Math.Abs(m.m23) <= 0.000001f
               && Math.Abs(m.m30) <= 0.000001f
               && Math.Abs(m.m31) <= 0.000001f
               && Math.Abs(m.m32) <= 0.000001f;

        static NifMeshBuilder.RawBuiltMesh TransformRenderLeaf(
            in NifMeshBuilder.RawBuiltMesh source,
            Matrix4x4 transform)
        {
            Vector3[] vertices = source.Vertices != null
                ? new Vector3[source.Vertices.Length]
                : Array.Empty<Vector3>();
            Vector3[] normals = source.Normals != null
                ? new Vector3[source.Normals.Length]
                : null;

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = transform.MultiplyPoint3x4(source.Vertices[i]);
                vertices[i] = p;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            if (normals != null)
            {
                int count = Math.Min(normals.Length, source.Normals.Length);
                for (int i = 0; i < count; i++)
                    normals[i] = transform.MultiplyVector(source.Normals[i]).normalized;
            }

            Bounds bounds = vertices.Length > 0
                ? new Bounds((min + max) * 0.5f, max - min)
                : default;

            return new NifMeshBuilder.RawBuiltMesh(
                vertices,
                normals,
                source.Uvs != null ? (Vector2[])source.Uvs.Clone() : null,
                source.Indices != null ? (int[])source.Indices.Clone() : Array.Empty<int>(),
                source.TexturePath,
                source.Name,
                bounds,
                source.AlphaFlags,
                source.AlphaThreshold);
        }

        static Matrix4x4 ComputeLocalToRoot(ModelPrefabSource prefabSource, int nodeIndex)
        {
            if (prefabSource?.Nodes == null || (uint)nodeIndex >= (uint)prefabSource.Nodes.Length)
                return Matrix4x4.identity;

            Matrix4x4 matrix = Matrix4x4.identity;
            int current = nodeIndex;
            int guard = 0;
            while ((uint)current < (uint)prefabSource.Nodes.Length && guard++ < prefabSource.Nodes.Length)
            {
                var node = prefabSource.Nodes[current];
                Matrix4x4 local = node.SourceLocalMatrix;
                matrix = local * matrix;
                current = node.ParentIndex;
            }

            return matrix;
        }

        static Matrix4x4 ComputeReferenceStyleSourceLocalToRoot(ModelPrefabSource prefabSource, int nodeIndex)
        {
            if (prefabSource?.Nodes == null || (uint)nodeIndex >= (uint)prefabSource.Nodes.Length)
                return Matrix4x4.identity;

            Matrix4x4 unityMatrix = Matrix4x4.identity;
            int current = nodeIndex;
            int guard = 0;
            while ((uint)current < (uint)prefabSource.Nodes.Length && guard++ < prefabSource.Nodes.Length)
            {
                var node = prefabSource.Nodes[current];
                Matrix4x4 local = BuildReferenceStyleUnityMatrix(node.SourceLocalMatrix);
                unityMatrix = local * unityMatrix;
                current = node.ParentIndex;
            }

            return UnityAffineToSource(unityMatrix);
        }

        static Matrix4x4 BuildReferenceStyleUnityMatrix(Matrix4x4 sourceLocalMatrix)
        {
            DecomposeReferenceStyle(sourceLocalMatrix, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            return Matrix4x4.TRS(position, rotation, scale);
        }

        static void DecomposeReferenceStyle(Matrix4x4 source, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = new Vector3(source.m03, source.m23, source.m13) * WorldScale.MwUnitsToMeters;

            float sx = new Vector3(source.m00, source.m10, source.m20).magnitude;
            float sy = new Vector3(source.m01, source.m11, source.m21).magnitude;
            float sz = new Vector3(source.m02, source.m12, source.m22).magnitude;
            float uniformScale = (sx + sy + sz) / 3f;
            if (uniformScale <= 0.000001f)
                uniformScale = 1f;
            scale = new Vector3(uniformScale, uniformScale, uniformScale);

            float invScale = 1f / uniformScale;
            float m00 = source.m00 * invScale;
            float m02 = source.m01 * invScale;
            float m01 = source.m02 * invScale;
            float m20 = source.m10 * invScale;
            float m22 = source.m11 * invScale;
            float m21 = source.m12 * invScale;
            float m10 = source.m20 * invScale;
            float m12 = source.m21 * invScale;
            float m11 = source.m22 * invScale;

            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = 0.5f * s,
                    x = (m21 - m12) * recip,
                    y = (m02 - m20) * recip,
                    z = (m10 - m01) * recip,
                };
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m21 - m12) * recip,
                    x = 0.5f * s,
                    y = (m01 + m10) * recip,
                    z = (m02 + m20) * recip,
                };
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m02 - m20) * recip,
                    x = (m01 + m10) * recip,
                    y = 0.5f * s,
                    z = (m12 + m21) * recip,
                };
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m10 - m01) * recip,
                    x = (m02 + m20) * recip,
                    y = (m12 + m21) * recip,
                    z = 0.5f * s,
                };
            }

            float rotationLengthSq = rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            rotation = rotationLengthSq > 0.000001f
                ? rotation.normalized
                : Quaternion.identity;
        }

        static Matrix4x4 UnityAffineToSource(Matrix4x4 unity)
        {
            var source = Matrix4x4.identity;
            source.m00 = unity.m00;
            source.m01 = unity.m02;
            source.m02 = unity.m01;
            source.m10 = unity.m20;
            source.m11 = unity.m22;
            source.m12 = unity.m21;
            source.m20 = unity.m10;
            source.m21 = unity.m12;
            source.m22 = unity.m11;
            source.m03 = unity.m03 / WorldScale.MwUnitsToMeters;
            source.m13 = unity.m23 / WorldScale.MwUnitsToMeters;
            source.m23 = unity.m13 / WorldScale.MwUnitsToMeters;
            return source;
        }

        static float[] PackMatrix(Matrix4x4 m)
        {
            return new[]
            {
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33,
            };
        }

        static float[] PackVector3Array(Vector3[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<float>();

            var packed = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++)
            {
                int o = i * 3;
                packed[o + 0] = values[i].x;
                packed[o + 1] = values[i].y;
                packed[o + 2] = values[i].z;
            }
            return packed;
        }

        static float[] PackVector3ArrayTransformed(Vector3[] values, Matrix4x4 transform)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<float>();

            var packed = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++)
            {
                Vector3 v = transform.MultiplyPoint3x4(values[i]);
                int o = i * 3;
                packed[o + 0] = v.x;
                packed[o + 1] = v.y;
                packed[o + 2] = v.z;
            }
            return packed;
        }

        static float[] PackVector3ArrayRotated(Vector3[] values, Matrix4x4 transform)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<float>();

            var packed = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++)
            {
                Vector3 v = transform.MultiplyVector(values[i]).normalized;
                int o = i * 3;
                packed[o + 0] = v.x;
                packed[o + 1] = v.y;
                packed[o + 2] = v.z;
            }
            return packed;
        }

        static void RemapWeightedSkinBoneIndices(ActorSkinMeshDef skinMesh, ActorSkeletonDef referenceSkeleton)
        {
            if (skinMesh == null || skinMesh.IsRigid != 0)
                return;

            int skinBoneCount = skinMesh.BoneIndices?.Length ?? 0;
            if (skinBoneCount == 0)
                return;

            var bones = referenceSkeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if (bones.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Actor weighted skin '{skinMesh.ModelPath}#{skinMesh.NodeName}' has no reference skeleton to remap skin bones against.");
            }

            var boneLookup = BuildBoneLookup(referenceSkeleton);
            for (int i = 0; i < skinBoneCount; i++)
            {
                int referenceBoneIndex = ResolveBindReferenceBoneIndex(skinMesh, i, bones.Length, boneLookup);
                if ((uint)referenceBoneIndex >= (uint)bones.Length)
                {
                    throw new InvalidOperationException(
                        $"Actor weighted skin '{skinMesh.ModelPath}#{skinMesh.NodeName}' could not resolve skin bone '{ReadBoneName(skinMesh, i)}' " +
                        $"against reference skeleton '{referenceSkeleton?.ModelPath ?? "<unknown>"}'.");
                }

                if (skinMesh.BoneIndices[i] != referenceBoneIndex)
                    skinMesh.BoneIndices[i] = referenceBoneIndex;
            }
        }

        static void ValidateWeightedSkinMeshBake(
            ActorSkinMeshDef skinMesh,
            ActorSkeletonDef referenceSkeleton,
            List<ActorSkinWeightDef> skinWeights,
            int vertexCount)
        {
            if (skinMesh == null || skinMesh.IsRigid != 0)
                return;

            string label = $"{skinMesh.ModelPath}#{skinMesh.NodeName}";
            int skinBoneCount = skinMesh.BoneIndices?.Length ?? 0;
            if (skinBoneCount <= 0)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has no skin-bone table.");

            if ((skinMesh.BoneNames?.Length ?? 0) != skinBoneCount)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has mismatched bone-name and bone-index counts.");

            if ((skinMesh.BindPoseMatrices?.Length ?? 0) != skinBoneCount * 16)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has mismatched bind-pose matrix count.");

            Matrix4x4 geometryToSkeleton = UnpackMatrix(skinMesh.GeometryToSkeletonMatrix, 0);
            if (!IsFiniteMatrix(geometryToSkeleton))
                throw new InvalidOperationException($"Actor weighted skin '{label}' has a non-finite geometry-to-skeleton matrix.");
            if (!IsIdentityMatrix(geometryToSkeleton))
                throw new InvalidOperationException($"Actor weighted skin '{label}' must bake identity GeometryToSkeletonMatrix.");

            var bones = referenceSkeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if (bones.Length == 0)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has no reference skeleton.");

            for (int i = 0; i < skinBoneCount; i++)
            {
                int targetBoneIndex = skinMesh.BoneIndices[i];
                if ((uint)targetBoneIndex >= (uint)bones.Length)
                {
                    throw new InvalidOperationException(
                        $"Actor weighted skin '{label}' skin-bone slot {i} ('{ReadBoneName(skinMesh, i)}') resolved invalid target bone index {targetBoneIndex}.");
                }

                Matrix4x4 bindPose = UnpackMatrix(skinMesh.BindPoseMatrices, i * 16);
                if (!IsFiniteMatrix(bindPose))
                    throw new InvalidOperationException($"Actor weighted skin '{label}' skin-bone slot {i} has a non-finite bind-pose matrix.");
            }

            int firstWeight = skinMesh.FirstWeightIndex;
            int weightCount = skinMesh.WeightCount;
            if (weightCount < 0 || firstWeight < 0 || firstWeight + weightCount > (skinWeights?.Count ?? 0))
                throw new InvalidOperationException($"Actor weighted skin '{label}' has an invalid skin-weight range.");

            for (int i = firstWeight; i < firstWeight + weightCount; i++)
            {
                ActorSkinWeightDef weight = skinWeights[i];
                if (weight.BoneIndex >= skinBoneCount)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' weight {i} references local skin-bone slot {weight.BoneIndex} outside skinBoneCount={skinBoneCount}.");

                if (!IsFinite(weight.Weight))
                    throw new InvalidOperationException($"Actor weighted skin '{label}' weight {i} has non-finite weight value.");
            }
        }

        static float[] PackVector2Array(Vector2[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<float>();

            var packed = new float[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                int o = i * 2;
                packed[o + 0] = values[i].x;
                packed[o + 1] = values[i].y;
            }
            return packed;
        }

        static int FindRenderLeafSlot(
            ModelPrefabSource prefabSource,
            List<int> renderLeaves,
            bool[] usedLeaves,
            string nodeName)
        {
            if (!string.IsNullOrEmpty(nodeName))
            {
                for (int i = 0; i < renderLeaves.Count; i++)
                {
                    if (usedLeaves[i])
                        continue;
                    string candidate = prefabSource.Nodes[renderLeaves[i]].Name ?? string.Empty;
                    if (string.Equals(candidate, nodeName, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            for (int i = 0; i < renderLeaves.Count; i++)
            {
                if (!usedLeaves[i])
                    return i;
            }
            return -1;
        }
    }
}
