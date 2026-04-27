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
        void AddFilteredSkinBindingMeshesToEquipmentVisual(
            int skinBindingIndex,
            ItemEquipmentPartReference equipmentPart,
            ActorVisualPartReference reference,
            ActorBodyPartMeshPart declaredMeshPart,
            string context)
        {
            var binding = _skinBindings[skinBindingIndex];
            var rig = _rigFamilies[binding.RigFamilyIndex];
            int attachBoneIndex = ResolvePartAttachBoneIndex(rig, reference, context);
            byte rigidMirrorX = ResolveRigidMirrorX(rig, attachBoneIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            bool filteredSkinnedTemplate = SkinBindingHasSkinnedRenderableMeshes(binding);
            if (filteredSkinnedTemplate)
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
            }
            else
            {
                added += AddDeclaredSkinBindingMeshesToEquipmentVisual(
                    skinBindingIndex,
                    binding,
                    equipmentPart,
                    reference,
                    context,
                    attachBoneIndex,
                    rigidMirrorX);
            }

            if (added == 0)
            {
                // OpenMW's CopyRigVisitor can produce an empty attachment when a valid equipment
                // body part has no geometry matching the strict part filter. Preserve the declared
                // equipment semantics for coverage without inventing broader mesh fallbacks.
                AddEquipmentSemanticEntry(equipmentPart, skinBindingIndex, attachBoneIndex, rigidMirrorX);
                return;
            }
        }

        bool CanUseDeclaredEquipmentMeshFallback(
            ActorSkinBindingDef binding,
            ActorVisualPartReference reference,
            ActorBodyPartMeshPart declaredMeshPart)
        {
            if (ActorVisualMappingPolicy.IsAttachmentOnlyPart(reference))
                return SkinBindingHasRenderableMeshes(binding);

            if (!ActorVisualMappingPolicy.TryGetMeshPart(reference, out var expectedMeshPart)
                || declaredMeshPart != expectedMeshPart)
            {
                return false;
            }

            if (SkinBindingHasOnlyRigidRenderableMeshes(binding))
                return true;

            int start = Math.Max(0, binding.FirstSkinMeshIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int renderableCount = 0;
            for (int i = start; i < end; i++)
            {
                if (IsRenderableSkinMesh(_skinMeshes[i]))
                    renderableCount++;
            }

            return renderableCount == 1;
        }


        bool SkinBindingHasRenderableMeshes(ActorSkinBindingDef binding)
        {
            int start = Math.Max(0, binding.FirstSkinMeshIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = start; i < end; i++)
            {
                if (IsRenderableSkinMesh(_skinMeshes[i]))
                    return true;
            }

            return false;
        }


        bool SkinBindingHasSkinnedRenderableMeshes(ActorSkinBindingDef binding)
        {
            int start = Math.Max(0, binding.FirstSkinMeshIndex);
            int end = Math.Min(_skinMeshes.Count, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = start; i < end; i++)
            {
                var mesh = _skinMeshes[i];
                if (IsRenderableSkinMesh(mesh) && mesh.IsRigid == 0)
                    return true;
            }

            return false;
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
                    ThrowMissingRigidAttachBone(context, reference, binding, mesh);

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


        void AddEquipmentSemanticEntry(
            ItemEquipmentPartReference equipmentPart,
            int skinBindingIndex,
            int attachBoneIndex,
            byte rigidMirrorX)
        {
            _equipmentVisualEntries.Add(new ActorEquipmentVisualEntryDef
            {
                PartReference = equipmentPart,
                SkinBindingIndex = skinBindingIndex,
                SkinMeshIndex = -1,
                AttachBoneIndex = attachBoneIndex,
                RigidMirrorX = rigidMirrorX,
            });
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


        void ThrowMissingRigidAttachBone(
            string context,
            ActorVisualPartReference reference,
            ActorSkinBindingDef binding,
            ActorSkinMeshDef mesh)
        {
            throw new InvalidOperationException($"{context} rigid mesh '{mesh?.NodeName}' has no attach bone for '{reference}'.");
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
            return ResolveBoneIndex(bones, BuildAttachBoneCandidates(reference));
        }


        byte ResolveRigidMirrorX(ActorRigFamilyDef rig, int attachBoneIndex)
        {
            if ((uint)rig.SkeletonIndex >= (uint)_skeletons.Count)
                return 0;

            var bones = _skeletons[rig.SkeletonIndex]?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if ((uint)attachBoneIndex >= (uint)bones.Length)
                return 0;

            return !string.IsNullOrEmpty(bones[attachBoneIndex].Name)
                   && bones[attachBoneIndex].Name.Contains("Left", StringComparison.Ordinal)
                ? (byte)1
                : (byte)0;
        }


        static int ResolveBoneIndex(ActorSkeletonBoneDef[] bones, params string[] names)
        {
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < bones.Length; i++)
                    if (string.Equals(bones[i].Name, names[n], StringComparison.OrdinalIgnoreCase))
                        return i;
            }

            for (int n = 0; n < names.Length; n++)
            {
                string canonicalName = ActorVisualMappingPolicy.CanonicalizeBoneName(names[n]);
                if (string.IsNullOrEmpty(canonicalName))
                    continue;

                for (int i = 0; i < bones.Length; i++)
                {
                    if (string.Equals(
                            ActorVisualMappingPolicy.CanonicalizeBoneName(bones[i].Name),
                            canonicalName,
                            StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }


        static string[] BuildAttachBoneCandidates(ActorVisualPartReference reference)
        {
            string openMwName = ActorVisualMappingPolicy.GetBoneName(reference);
            string[] aliases = ActorVisualMappingPolicy.GetBoneAliases(reference);
            if (string.IsNullOrWhiteSpace(openMwName))
                return aliases;

            var candidates = new string[aliases.Length + 1];
            candidates[0] = openMwName;
            for (int i = 0; i < aliases.Length; i++)
                candidates[i + 1] = aliases[i];
            return candidates;
        }


        static string[] BuildMeshFilters(ActorVisualPartReference reference)
            => ActorVisualMappingPolicy.GetMeshFilters(reference);


        bool MatchesMeshFilter(ActorSkinMeshDef mesh, string meshFilter)
        {
            if (mesh == null)
                return false;

            if (MatchesMeshFilter(mesh.NodeName, meshFilter))
                return true;

            int graphNodeIndex = mesh.SourceGraphNodeIndex;
            int guard = 0;
            while ((uint)graphNodeIndex < (uint)_graphNodes.Count && guard++ < _graphNodes.Count)
            {
                var graphNode = _graphNodes[graphNodeIndex];
                if (MatchesMeshFilter(graphNode?.Name, meshFilter))
                    return true;

                graphNodeIndex = graphNode?.ParentIndex ?? -1;
            }

            return false;
        }


        static bool MatchesMeshFilter(string nodeName, string meshFilter)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(meshFilter))
                return true;

            if (nodeName.StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase))
                return true;

            return nodeName.StartsWith("tri ", StringComparison.OrdinalIgnoreCase)
                   && nodeName.Substring(4).StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase);
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
            => ActorVisualContentRules.IsBeastRace(raceId, races);


        static bool IsPlayerActor(ActorDef actor)
            => ActorVisualContentRules.IsPlayerActor(actor);


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
            => ActorVisualContentRules.ResolveNpcSkeletonModel(firstPerson, female, beast, lowerInvariant: true);


        static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorVisualPartReference partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
            => ActorVisualContentRules.TryResolveNpcRaceBodyPart(bodyParts, raceId, partReference, female, firstPerson, out result);


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


        static bool IsBaseSkinPartReference(ActorVisualPartReference type)
            => ActorVisualContentRules.IsBaseSkinPartReference(type);


        static bool IsFirstPersonPartReference(ActorVisualPartReference type)
            => ActorVisualContentRules.IsFirstPersonPartReference(type);


        static bool IsFirstPersonMeshPart(ActorBodyPartMeshPart part)
            => ActorVisualContentRules.IsFirstPersonMeshPart(part);


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
            => ActorVisualContentRules.BuildCompanionKfPath(modelPath, lowerInvariant: true);


        }
    }
