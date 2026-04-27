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

            if ((skinMesh.BoneSourceRecordIndices?.Length ?? 0) != skinBoneCount)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has mismatched bone source-record and bone-index counts.");

            if ((skinMesh.BindPoseMatrices?.Length ?? 0) != skinBoneCount * 16)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has mismatched bind-pose matrix count.");

            Matrix4x4 geometryToSkeleton = UnpackMatrix(skinMesh.GeometryToSkeletonMatrix, 0);
            if (!IsFiniteMatrix(geometryToSkeleton))
                throw new InvalidOperationException($"Actor weighted skin '{label}' has a non-finite geometry-to-skeleton matrix.");

            if (skinMesh.SourceGraphNodeIndex < 0)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has no source graph node.");

            if (skinMesh.RigAssemblyKind is ActorRigAssemblyKind.Unknown or ActorRigAssemblyKind.RigidAttachment)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has invalid rig assembly kind '{skinMesh.RigAssemblyKind}'.");

            if (!string.IsNullOrWhiteSpace(skinMesh.SkinRootName) && skinMesh.SkinRootGraphNodeIndex < 0)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has unresolved skin root '{skinMesh.SkinRootName}'.");
            if (skinMesh.SkinRootSourceRecordIndex >= 0 && skinMesh.SkinRootGraphNodeIndex < 0)
                throw new InvalidOperationException($"Actor weighted skin '{label}' has unresolved skin root source record {skinMesh.SkinRootSourceRecordIndex}.");

            if (skinMesh.RigAssemblyKind == ActorRigAssemblyKind.FullModel)
            {
                if (skinMesh.SourceSkeletonRootGraphNodeIndex < 0)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' full-model assembly has no source skeleton root graph node.");
                if (skinMesh.CancelledTransformGraphNodeIndex < 0)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' full-model assembly has no cancelled transform boundary.");
            }
            else if (skinMesh.RigAssemblyKind == ActorRigAssemblyKind.CopiedRig)
            {
                if (skinMesh.CopiedRigRootGraphNodeIndex < 0)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' copied-rig assembly has no copied root graph node.");
                if (skinMesh.CancelledTransformGraphNodeIndex < 0)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' copied-rig assembly has no cancelled transform boundary.");
                if (skinMesh.TargetBoneIndex < 0)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' copied-rig assembly has no target bone index.");
            }

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

                int skinBoneSourceRecordIndex = skinMesh.BoneSourceRecordIndices[i];
                if (skinMesh.RigAssemblyKind == ActorRigAssemblyKind.FullModel
                    && skinBoneSourceRecordIndex >= 0
                    && bones[targetBoneIndex].SourceRecordIndex != skinBoneSourceRecordIndex)
                {
                    throw new InvalidOperationException(
                        $"Actor weighted skin '{label}' full-model skin-bone slot {i} ('{ReadBoneName(skinMesh, i)}') " +
                        $"source record {skinBoneSourceRecordIndex} resolved to skeleton bone {targetBoneIndex} " +
                        $"with source record {bones[targetBoneIndex].SourceRecordIndex}.");
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

                if (weight.VertexIndex >= vertexCount)
                    throw new InvalidOperationException($"Actor weighted skin '{label}' weight {i} references vertex {weight.VertexIndex} outside vertexCount={vertexCount}.");
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
            int sourceRecordIndex,
            string nodeName)
        {
            if (sourceRecordIndex >= 0)
            {
                for (int i = 0; i < renderLeaves.Count; i++)
                {
                    if (usedLeaves[i])
                        continue;
                    if (prefabSource.Nodes[renderLeaves[i]].SourceRecordIndex == sourceRecordIndex)
                        return i;
                }

                return -1;
            }

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
