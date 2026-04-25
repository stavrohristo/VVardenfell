using System;
using System.Collections.Generic;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Nif;
using UnityEngine;

namespace VVardenfell.Importer.Bake
{
    public sealed class ActorAnimationBakery
    {
        const float SkinBindInvariantEpsilon = 0.005f;
        const string CandidateGeometrySkinDataRootParentInv = "geometryPath_skinData_rootParentInv";
        const string CandidateGeometrySkinRootInvSkinDataRootParentInv = "geometryPath_skinRootInv_skinData_rootParentInv";
        const string CandidateGeometrySkinDataSkinRootInvRootParentInv = "geometryPath_skinData_skinRootInv_rootParentInv";
        const string CandidateGeometrySkinData = "geometryPath_skinData";
        const string CandidateGeometrySkinRootInvSkinData = "geometryPath_skinRootInv_skinData";
        const string CandidateGeometrySkinDataSkinRootInv = "geometryPath_skinData_skinRootInv";
        const string CandidateSkinData = "skinData";
        const string CandidateSkinRootInvSkinData = "skinRootInv_skinData";
        const string CandidateSkinDataSkinRootInv = "skinData_skinRootInv";
        const string CandidateSkinRootSkinData = "skinRoot_skinData";
        const string CandidateSkinDataSkinRoot = "skinData_skinRoot";
        const string CandidateGeometryPath = "geometryPath";

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
        readonly List<ActorAnimationModelBindingDef> _bindings = new();
        readonly List<ActorSkeletonDef> _skeletons = new();
        readonly List<ActorSkinMeshDef> _skinMeshes = new();
        readonly List<ActorSkinWeightDef> _skinWeights = new();
        readonly List<ActorAnimationClipDef> _clips = new();
        readonly List<ActorAnimationTrackDef> _tracks = new();
        readonly List<ActorAnimationKeyDef> _keys = new();
        readonly List<ActorAnimationTextKeyDef> _textKeys = new();

        public bool Modified { get; private set; }
        public int ModelBindingCount => _bindings.Count;
        public int SkeletonCount => _skeletons.Count;
        public int SkinMeshCount => _skinMeshes.Count;
        public int ClipCount => _clips.Count;

        public bool TryGetAssignment(string modelPath, out Assignment assignment)
            => _assignmentsByBindingKey.TryGetValue(BuildBindingKey(modelPath, null), out assignment);

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
            _skeletons.Add(skeleton);

            int firstSkinMesh = _skinMeshes.Count;
            var skinMeshes = NifActorAnimationExtractor.ExtractSkinMeshes(modelNif, skeleton, skeletonIndex, _skinWeights);
            AttachRenderData(
                modelPath,
                skinMeshes,
                prefabSource,
                skeleton,
                skinBindReferenceSkeleton ?? skeleton,
                string.IsNullOrWhiteSpace(normalizedReferenceSkeletonPath) ? null : normalizedReferenceSkeletonPath,
                meshes,
                materials,
                textures);
            for (int i = 0; i < skinMeshes.Length; i++)
                _skinMeshes.Add(skinMeshes[i]);

            int firstClip = _clips.Count;
            AddClips(modelNif);
            AddCompanionKfClips(modelPath, bsa, bsaByName);
            AddSharedNpcKfClips(modelPath, bsa, bsaByName);

            var assignment = new Assignment(
                skeletonIndex,
                _skinMeshes.Count > firstSkinMesh ? firstSkinMesh : -1,
                _skinMeshes.Count - firstSkinMesh,
                _clips.Count > firstClip ? firstClip : -1,
                _clips.Count - firstClip);

            _assignmentsByBindingKey[bindingKey] = assignment;
            _bindings.Add(new ActorAnimationModelBindingDef
            {
                ModelPath = modelPath,
                BindReferenceSkeletonPath = normalizedReferenceSkeletonPath,
                SkeletonIndex = assignment.SkeletonIndex,
                FirstSkinMeshIndex = assignment.FirstSkinMeshIndex,
                SkinMeshCount = assignment.SkinMeshCount,
                FirstClipIndex = assignment.FirstClipIndex,
                ClipCount = assignment.ClipCount,
            });
            Modified = true;
            return assignment;
        }

        public ActorAnimationCatalogData BuildCatalog()
            => new()
            {
                ModelBindings = _bindings.ToArray(),
                Skeletons = _skeletons.ToArray(),
                SkinMeshes = _skinMeshes.ToArray(),
                SkinWeights = _skinWeights.ToArray(),
                Clips = _clips.ToArray(),
                Tracks = _tracks.ToArray(),
                Keys = _keys.ToArray(),
                TextKeys = _textKeys.ToArray(),
            };

        void AddClips(NifFile nif)
        {
            var clips = NifActorAnimationExtractor.ExtractClips(nif, _tracks, _keys, _textKeys);
            for (int i = 0; i < clips.Length; i++)
                _clips.Add(clips[i]);
        }

        void AddCompanionKfClips(string modelPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            AddKfClips(BuildCompanionKfPath(modelPath), bsa, bsaByName);
        }

        void AddSharedNpcKfClips(string modelPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            string normalized = NormalizeModelPath(modelPath);
            switch (normalized)
            {
                case "meshes\\base_anim.nif":
                case "meshes\\base_animkna.nif":
                    AddKfClips("meshes\\xbase_anim.kf", bsa, bsaByName);
                    break;
                case "meshes\\base_anim_female.nif":
                    AddKfClips("meshes\\xbase_anim.kf", bsa, bsaByName);
                    AddKfClips("meshes\\xbase_anim_female.kf", bsa, bsaByName);
                    break;
                case "meshes\\base_anim_female.1st.nif":
                case "meshes\\base_animkna.1st.nif":
                case "meshes\\xbase_anim.1st.nif":
                    AddKfClips("meshes\\xbase_anim.1st.kf", bsa, bsaByName);
                    break;
            }
        }

        void AddKfClips(string kfPath, BsaArchive bsa, Dictionary<string, BsaEntry> bsaByName)
        {
            if (string.IsNullOrEmpty(kfPath) || !bsaByName.TryGetValue(NormalizeModelPath(kfPath), out var entry))
                return;

            try
            {
                var kf = NifFile.Parse(kfPath, bsa.Read(entry));
                AddClips(kf);
            }
            catch
            {
                // Unsupported KF variants are reported by validation once the cache has a path-level entry.
            }
        }

        static bool IsActorAnimationCandidate(
            NifFile nif,
            string modelPath,
            Dictionary<string, BsaEntry> bsaByName,
            bool forceActorModel)
        {
            if (forceActorModel)
                return true;

            if (bsaByName.ContainsKey(BuildCompanionKfPath(modelPath)))
                return true;

            for (int i = 0; i < nif.Records.Length; i++)
            {
                switch (nif.Records[i])
                {
                    case NiSequence:
                    case NiKeyframeController:
                    case NiVisController:
                    case NiTextKeyExtraData:
                        return true;
                    case NiTriShape tri when tri.Skin >= 0:
                        return true;
                }
            }

            return false;
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

        static void AttachRenderData(
            string modelPath,
            ActorSkinMeshDef[] skinMeshes,
            ModelPrefabSource prefabSource,
            ActorSkeletonDef skeleton,
            ActorSkeletonDef bindReferenceSkeleton,
            string bindReferenceSkeletonPath,
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
            Vector3 rigidOffset = FindBoneOffsetPosition(prefabSource);
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
                    skinMeshes[i].GeometryToSkeletonMatrix = PackMatrix(ComputeLocalToRoot(prefabSource, nodeIndex));
                ReconcileWeightedBindPoseMatrices(
                    modelPath,
                    skinMeshes[i],
                    bindReferenceSkeleton ?? skeleton,
                    bindReferenceSkeletonPath,
                    prefabSource,
                    nodeIndex);
                if (skinMeshes[i].IsRigid != 0)
                {
                    skinMeshes[i].RigidOffsetX = rigidOffset.x;
                    skinMeshes[i].RigidOffsetY = rigidOffset.y;
                    skinMeshes[i].RigidOffsetZ = rigidOffset.z;
                }
            }
        }

        static void ReconcileWeightedBindPoseMatrices(
            string modelPath,
            ActorSkinMeshDef skinMesh,
            ActorSkeletonDef skeleton,
            string referenceSkeletonPath,
            ModelPrefabSource prefabSource,
            int nodeIndex)
        {
            if (skinMesh == null
                || skinMesh.IsRigid != 0
                || skinMesh.BoneIndices == null
                || skinMesh.BoneIndices.Length == 0
                || skinMesh.BindPoseMatrices == null
                || skinMesh.BindPoseMatrices.Length < 16)
            {
                return;
            }

            var bindRoots = BuildBindLocalToRootMatrices(skeleton?.Bones);
            if (bindRoots.Length == 0)
                throw new InvalidOperationException(
                    $"Actor weighted skin '{modelPath}#{skinMesh.NodeName}' has no skeleton to resolve bind poses against.");

            var boneLookup = BuildBoneLookup(skeleton);
            int skinBoneCount = skinMesh.BoneIndices.Length;
            int bindPoseCount = skinMesh.BindPoseMatrices.Length / 16;
            if (bindPoseCount < skinBoneCount)
            {
                throw new InvalidOperationException(
                    $"Actor weighted skin '{modelPath}#{skinMesh.NodeName}' has {skinBoneCount} skin bones but only {bindPoseCount} bind matrices.");
            }

            Matrix4x4 skinDataTransform = UnpackMatrix(skinMesh.GeometryToSkeletonMatrix, 0);
            if (!IsFiniteMatrix(skinDataTransform))
                throw new InvalidOperationException(
                    $"Actor weighted skin '{modelPath}#{skinMesh.NodeName}' has a non-finite skin-data transform.");

            Matrix4x4 skinRootToSkeleton = ComputeSkinRootToSkeleton(prefabSource, skinMesh.SkinRootName);
            Matrix4x4 geometryToSkeletonPath = ComputeLocalToRoot(prefabSource, nodeIndex);
            Matrix4x4 skinRootParentBindToSkeleton = ComputeSkinRootParentBindToSkeleton(skeleton, boneLookup, bindRoots, skinMesh.SkinRootName);
            var candidates = BuildBindCandidates(
                skinMesh,
                bindRoots,
                boneLookup,
                skinDataTransform,
                skinRootToSkeleton,
                geometryToSkeletonPath,
                skinRootParentBindToSkeleton,
                out int checkedCount,
                out int remappedCount);
            BindCandidate selected = SelectValidCandidate(candidates);
            if (selected.BindPoses == null)
            {
                selected = SelectInexactBodyPartCandidate(skinMesh, candidates);
                if (selected.BindPoses == null)
                {
                    string referenceText = $" referenceSkeleton='{referenceSkeletonPath}'";
                    throw new InvalidOperationException(
                        $"Actor skin bind products disagree for '{modelPath}#{skinMesh.NodeName}'. " +
                        $"{FormatCandidateFailures(candidates)} checked={checkedCount} remapped={remappedCount} " +
                        $"skinRoot='{skinMesh.SkinRootName}'{referenceText}. " +
                        "Expected one NiSkinData/root-parent transform to satisfy actorBindToRoot[bone] * meshToBoneBind ~= geometryToSkeleton.");
                }
            }

            for (int i = 0; i < skinBoneCount; i++)
            {
                int actorBoneIndex = ResolveBindReferenceBoneIndex(skinMesh, i, bindRoots.Length, boneLookup);
                if ((uint)actorBoneIndex >= (uint)bindRoots.Length)
                    throw new InvalidOperationException(
                        $"Actor weighted skin '{modelPath}#{skinMesh.NodeName}' could not resolve skin bone '{ReadBoneName(skinMesh, i)}' " +
                        $"against skeleton '{skeleton?.ModelPath ?? referenceSkeletonPath ?? "<unknown>"}'.");

                if (skinMesh.BoneIndices[i] != actorBoneIndex)
                    skinMesh.BoneIndices[i] = actorBoneIndex;

                WriteMatrix(skinMesh.BindPoseMatrices, i * 16, selected.BindPoses[i]);
            }

            WriteMatrix(skinMesh.GeometryToSkeletonMatrix, 0, Matrix4x4.identity);
        }

        readonly struct BindCandidate
        {
            public readonly string Name;
            public readonly Matrix4x4 GeometryToSkeleton;
            public readonly Matrix4x4[] BindPoses;
            public readonly float WorstDelta;
            public readonly string WorstBone;

            public BindCandidate(string name, Matrix4x4 geometryToSkeleton, Matrix4x4[] bindPoses, float worstDelta, string worstBone)
            {
                Name = name;
                GeometryToSkeleton = geometryToSkeleton;
                BindPoses = bindPoses;
                WorstDelta = worstDelta;
                WorstBone = worstBone;
            }

            public BindCandidate WithName(string name)
                => new(name, GeometryToSkeleton, BindPoses, WorstDelta, WorstBone);
        }

        static BindCandidate EvaluateWeightedBindCandidate(
            ActorSkinMeshDef skinMesh,
            Matrix4x4[] bindRoots,
            Dictionary<string, int> boneLookup,
            Matrix4x4 skinBaseToSkeleton,
            Matrix4x4 bindTargetToSkeleton,
            out int checkedCount,
            out int remappedCount)
        {
            checkedCount = 0;
            remappedCount = 0;
            int skinBoneCount = skinMesh.BoneIndices.Length;
            var bindPoses = new Matrix4x4[skinBoneCount];
            float worstDelta = 0f;
            string worstBone = string.Empty;

            if (!IsFiniteMatrix(skinBaseToSkeleton) || !IsFiniteMatrix(bindTargetToSkeleton))
            {
                return new BindCandidate(
                    "invalid",
                    skinBaseToSkeleton,
                    bindPoses,
                    float.PositiveInfinity,
                    string.Empty);
            }

            for (int i = 0; i < skinBoneCount; i++)
            {
                int actorBoneIndex = ResolveBindReferenceBoneIndex(skinMesh, i, bindRoots.Length, boneLookup);
                if ((uint)actorBoneIndex >= (uint)bindRoots.Length)
                {
                    worstDelta = float.PositiveInfinity;
                    worstBone = ReadBoneName(skinMesh, i);
                    continue;
                }

                if (skinMesh.BoneIndices[i] != actorBoneIndex)
                    remappedCount++;

                Matrix4x4 inverseBind = UnpackMatrix(skinMesh.BindPoseMatrices, i * 16);

                if (!IsFiniteMatrix(inverseBind))
                {
                    worstDelta = float.PositiveInfinity;
                    worstBone = ReadBoneName(skinMesh, i);
                    continue;
                }

                Matrix4x4 meshToBoneBind = skinBaseToSkeleton * inverseBind;
                bindPoses[i] = meshToBoneBind;

                Matrix4x4 bindSkinMatrix = bindRoots[actorBoneIndex] * meshToBoneBind;
                if (!IsFiniteMatrix(bindSkinMatrix))
                {
                    worstDelta = float.PositiveInfinity;
                    worstBone = ReadBoneName(skinMesh, i);
                    continue;
                }

                float delta = MatrixDelta(bindSkinMatrix, bindTargetToSkeleton);
                if (delta > worstDelta)
                {
                    worstDelta = delta;
                    worstBone = ReadBoneName(skinMesh, i);
                }

                checkedCount++;
            }

            if (checkedCount == 0 && worstDelta <= 0f)
                worstDelta = float.PositiveInfinity;

            return new BindCandidate(
                "leftTransform",
                skinBaseToSkeleton,
                bindPoses,
                worstDelta,
                worstBone);
        }

        static BindCandidate[] BuildBindCandidates(
            ActorSkinMeshDef skinMesh,
            Matrix4x4[] bindRoots,
            Dictionary<string, int> boneLookup,
            Matrix4x4 skinDataTransform,
            Matrix4x4 skinRootToSkeleton,
            Matrix4x4 geometryToSkeletonPath,
            Matrix4x4 skinRootParentBindToSkeleton,
            out int checkedCount,
            out int remappedCount)
        {
            Matrix4x4 skeletonToSkinRoot = skinRootToSkeleton.inverse;
            Matrix4x4 skeletonToSkinRootParentBind = skinRootParentBindToSkeleton.inverse;
            var transforms = new (string Name, Matrix4x4 Matrix)[]
            {
                (CandidateGeometrySkinDataRootParentInv, geometryToSkeletonPath * skinDataTransform * skeletonToSkinRootParentBind),
                (CandidateGeometrySkinRootInvSkinDataRootParentInv, geometryToSkeletonPath * skinRootToSkeleton * skinDataTransform * skeletonToSkinRootParentBind),
                (CandidateGeometrySkinDataSkinRootInvRootParentInv, geometryToSkeletonPath * skinDataTransform * skinRootToSkeleton * skeletonToSkinRootParentBind),
                (CandidateGeometrySkinData, geometryToSkeletonPath * skinDataTransform),
                (CandidateGeometrySkinRootInvSkinData, geometryToSkeletonPath * skinRootToSkeleton * skinDataTransform),
                (CandidateGeometrySkinDataSkinRootInv, geometryToSkeletonPath * skinDataTransform * skinRootToSkeleton),
                (CandidateSkinData, skinDataTransform),
                (CandidateSkinRootInvSkinData, skinRootToSkeleton * skinDataTransform),
                (CandidateSkinDataSkinRootInv, skinDataTransform * skinRootToSkeleton),
                (CandidateSkinRootSkinData, skeletonToSkinRoot * skinDataTransform),
                (CandidateSkinDataSkinRoot, skinDataTransform * skeletonToSkinRoot),
                (CandidateGeometryPath, geometryToSkeletonPath),
            };

            var candidates = new List<BindCandidate>(transforms.Length);
            checkedCount = 0;
            remappedCount = 0;
            bool wroteCounts = false;
            for (int i = 0; i < transforms.Length; i++)
            {
                var candidate = EvaluateWeightedBindCandidate(
                    skinMesh,
                    bindRoots,
                    boneLookup,
                    transforms[i].Matrix,
                    geometryToSkeletonPath,
                    out int candidateChecked,
                    out int candidateRemapped);
                candidates.Add(candidate.WithName(transforms[i].Name));

                if (!wroteCounts)
                {
                    checkedCount = candidateChecked;
                    remappedCount = candidateRemapped;
                    wroteCounts = true;
                }
            }

            return candidates.ToArray();
        }

        static BindCandidate SelectValidCandidate(BindCandidate[] candidates)
        {
            BindCandidate best = default;
            bool hasBest = false;
            for (int i = 0; i < (candidates?.Length ?? 0); i++)
            {
                var candidate = candidates[i];
                if (candidate.BindPoses == null || candidate.WorstDelta > SkinBindInvariantEpsilon)
                    continue;

                if (!hasBest || CandidateRank(candidate.Name) < CandidateRank(best.Name))
                {
                    best = candidate;
                    hasBest = true;
                }
            }

            return hasBest ? best : default;
        }

        static BindCandidate SelectBestFiniteCandidate(BindCandidate[] candidates)
        {
            BindCandidate best = default;
            bool hasBest = false;
            for (int i = 0; i < (candidates?.Length ?? 0); i++)
            {
                var candidate = candidates[i];
                if (candidate.BindPoses == null || float.IsNaN(candidate.WorstDelta) || float.IsInfinity(candidate.WorstDelta))
                    continue;

                if (!hasBest
                    || candidate.WorstDelta < best.WorstDelta - 0.000001f
                    || (Mathf.Abs(candidate.WorstDelta - best.WorstDelta) <= 0.000001f
                        && CandidateRank(candidate.Name) < CandidateRank(best.Name)))
                {
                    best = candidate;
                    hasBest = true;
                }
            }

            return hasBest ? best : default;
        }

        static BindCandidate SelectInexactBodyPartCandidate(ActorSkinMeshDef skinMesh, BindCandidate[] candidates)
        {
            string preferredCandidate = PreferredInexactCandidateName(skinMesh);
            if (!string.IsNullOrEmpty(preferredCandidate)
                && TrySelectFiniteCandidate(candidates, preferredCandidate, out var bodyPartCandidate))
            {
                return bodyPartCandidate;
            }

            return SelectBestFiniteCandidate(candidates);
        }

        static string PreferredInexactCandidateName(ActorSkinMeshDef skinMesh)
        {
            if (IsChestSkin(skinMesh))
                return CandidateGeometrySkinRootInvSkinDataRootParentInv;

            if (IsStandardHumanoidMaleLeftHandSkin(skinMesh) || IsStandardHumanoidFemaleLeftThumbSubmesh(skinMesh))
                return CandidateGeometrySkinRootInvSkinData;

            return string.Empty;
        }

        static bool TrySelectFiniteCandidate(BindCandidate[] candidates, string name, out BindCandidate selected)
        {
            for (int i = 0; i < (candidates?.Length ?? 0); i++)
            {
                var candidate = candidates[i];
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal)
                    || candidate.BindPoses == null
                    || float.IsNaN(candidate.WorstDelta)
                    || float.IsInfinity(candidate.WorstDelta))
                {
                    continue;
                }

                selected = candidate;
                return true;
            }

            selected = default;
            return false;
        }

        static bool IsChestSkin(ActorSkinMeshDef skinMesh)
        {
            string nodeName = skinMesh?.NodeName ?? string.Empty;
            return nodeName.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsStandardHumanoidMaleLeftHandSkin(ActorSkinMeshDef skinMesh)
        {
            return IsStandardHumanoidLeftHandSkin(skinMesh) && !IsFemaleSkinModel(skinMesh);
        }

        static bool IsStandardHumanoidFemaleLeftThumbSubmesh(ActorSkinMeshDef skinMesh)
        {
            if (!IsStandardHumanoidLeftHandSkin(skinMesh) || !IsFemaleSkinModel(skinMesh))
                return false;

            bool hasHand = false;
            bool hasFinger0 = false;
            bool hasNonThumbFinger = false;
            var names = skinMesh.BoneNames ?? Array.Empty<string>();
            for (int i = 0; i < names.Length; i++)
            {
                string name = CanonicalBoneName(names[i]);
                if (string.Equals(name, "bip01 l hand", StringComparison.Ordinal))
                    hasHand = true;
                else if (name.StartsWith("bip01 l finger0", StringComparison.Ordinal))
                    hasFinger0 = true;
                else if (name.StartsWith("bip01 l finger", StringComparison.Ordinal))
                    hasNonThumbFinger = true;
            }

            return hasHand && hasFinger0 && !hasNonThumbFinger;
        }

        static bool IsStandardHumanoidLeftHandSkin(ActorSkinMeshDef skinMesh)
        {
            string nodeName = skinMesh?.NodeName ?? string.Empty;
            if (nodeName.IndexOf("left hand", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string modelPath = skinMesh?.ModelPath ?? string.Empty;
            return modelPath.IndexOf("argonian", StringComparison.OrdinalIgnoreCase) < 0
                   && modelPath.IndexOf("khajiit", StringComparison.OrdinalIgnoreCase) < 0;
        }

        static bool IsFemaleSkinModel(ActorSkinMeshDef skinMesh)
        {
            string modelPath = skinMesh?.ModelPath ?? string.Empty;
            return modelPath.IndexOf("_F_", StringComparison.OrdinalIgnoreCase) >= 0
                   || modelPath.IndexOf("_Female", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int CandidateRank(string name)
        {
            if (string.Equals(name, CandidateGeometrySkinDataRootParentInv, StringComparison.Ordinal))
                return 0;
            if (string.Equals(name, CandidateGeometrySkinRootInvSkinDataRootParentInv, StringComparison.Ordinal))
                return 1;
            if (string.Equals(name, CandidateGeometrySkinDataSkinRootInvRootParentInv, StringComparison.Ordinal))
                return 2;
            if (string.Equals(name, CandidateGeometrySkinData, StringComparison.Ordinal))
                return 3;
            if (string.Equals(name, CandidateGeometrySkinRootInvSkinData, StringComparison.Ordinal))
                return 4;
            if (string.Equals(name, CandidateGeometrySkinDataSkinRootInv, StringComparison.Ordinal))
                return 5;
            if (string.Equals(name, CandidateSkinData, StringComparison.Ordinal))
                return 6;
            if (string.Equals(name, CandidateSkinRootInvSkinData, StringComparison.Ordinal))
                return 7;
            if (string.Equals(name, CandidateSkinDataSkinRootInv, StringComparison.Ordinal))
                return 8;
            if (string.Equals(name, CandidateGeometryPath, StringComparison.Ordinal))
                return 9;
            return 10;
        }

        static string FormatCandidateFailures(BindCandidate[] candidates)
        {
            string result = string.Empty;
            int count = Math.Min(candidates?.Length ?? 0, 12);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    result += "; ";
                result += $"{candidates[i].Name} worstBone='{candidates[i].WorstBone}' worstDelta={candidates[i].WorstDelta:F4}";
            }

            return result;
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
                var bone = bones[i];
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
                var local = Matrix4x4.TRS(
                    new Vector3(bone.PosX, bone.PosY, bone.PosZ),
                    rotation,
                    new Vector3(scale, scale, scale));

                matrices[i] = bone.ParentIndex >= 0 && bone.ParentIndex < i
                    ? matrices[bone.ParentIndex] * local
                    : local;
            }

            return matrices;
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

        static void WriteMatrix(float[] values, int start, Matrix4x4 m)
        {
            if (values == null || values.Length < start + 16)
                return;

            values[start + 0] = m.m00; values[start + 1] = m.m01; values[start + 2] = m.m02; values[start + 3] = m.m03;
            values[start + 4] = m.m10; values[start + 5] = m.m11; values[start + 6] = m.m12; values[start + 7] = m.m13;
            values[start + 8] = m.m20; values[start + 9] = m.m21; values[start + 10] = m.m22; values[start + 11] = m.m23;
            values[start + 12] = m.m30; values[start + 13] = m.m31; values[start + 14] = m.m32; values[start + 15] = m.m33;
        }

        static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
            => (a.GetColumn(0) - b.GetColumn(0)).magnitude
               + (a.GetColumn(1) - b.GetColumn(1)).magnitude
               + (a.GetColumn(2) - b.GetColumn(2)).magnitude
               + (a.GetColumn(3) - b.GetColumn(3)).magnitude;

        static bool IsFiniteMatrix(Matrix4x4 m)
            => IsFinite(m.m00) && IsFinite(m.m01) && IsFinite(m.m02) && IsFinite(m.m03)
               && IsFinite(m.m10) && IsFinite(m.m11) && IsFinite(m.m12) && IsFinite(m.m13)
               && IsFinite(m.m20) && IsFinite(m.m21) && IsFinite(m.m22) && IsFinite(m.m23)
               && IsFinite(m.m30) && IsFinite(m.m31) && IsFinite(m.m32) && IsFinite(m.m33);

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

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

        static Vector3 FindBoneOffsetPosition(ModelPrefabSource prefabSource)
        {
            if (prefabSource?.Nodes == null)
                return Vector3.zero;

            for (int i = 0; i < prefabSource.Nodes.Length; i++)
            {
                var node = prefabSource.Nodes[i];
                if (string.Equals(node.Name, "BoneOffset", StringComparison.OrdinalIgnoreCase))
                    return node.LocalPosition;
            }

            return Vector3.zero;
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
                Matrix4x4 local = Matrix4x4.TRS(
                    node.LocalPosition,
                    node.LocalRotation,
                    new Vector3(node.LocalScale, node.LocalScale, node.LocalScale));
                matrix = local * matrix;
                current = node.ParentIndex;
            }

            return matrix;
        }

        static Matrix4x4 ComputeSkinRootToSkeleton(ModelPrefabSource prefabSource, string skinRootName)
        {
            if (string.IsNullOrWhiteSpace(skinRootName))
                return Matrix4x4.identity;
            if (prefabSource?.Nodes == null)
                return Matrix4x4.identity;

            for (int i = 0; i < prefabSource.Nodes.Length; i++)
            {
                if (!string.Equals(prefabSource.Nodes[i].Name, skinRootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Matrix4x4 localToRoot = ComputeLocalToRoot(prefabSource, i);
                if (!IsFiniteMatrix(localToRoot))
                    throw new InvalidOperationException(
                        $"Actor skin root '{skinRootName}' produced a non-finite local-to-root matrix.");
                return localToRoot.inverse;
            }

            throw new InvalidOperationException(
                $"Actor skin root '{skinRootName}' was not found in model prefab hierarchy.");
        }

        static Matrix4x4 ComputeSkinRootParentBindToSkeleton(
            ActorSkeletonDef skeleton,
            Dictionary<string, int> boneLookup,
            Matrix4x4[] bindRoots,
            string skinRootName)
        {
            if (string.IsNullOrWhiteSpace(skinRootName)
                || skeleton?.Bones == null
                || bindRoots == null
                || bindRoots.Length == 0
                || boneLookup == null
                || !TryResolveBoneName(boneLookup, skinRootName, out int skinRootIndex)
                || (uint)skinRootIndex >= (uint)skeleton.Bones.Length)
            {
                return Matrix4x4.identity;
            }

            int parentIndex = skeleton.Bones[skinRootIndex].ParentIndex;
            if ((uint)parentIndex >= (uint)bindRoots.Length)
                return Matrix4x4.identity;

            Matrix4x4 parentBind = bindRoots[parentIndex];
            if (!IsFiniteMatrix(parentBind))
                throw new InvalidOperationException(
                    $"Actor skin root '{skinRootName}' has a non-finite bind parent matrix.");

            return parentBind;
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
