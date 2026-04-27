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
            => ActorVisualContentRules.BuildPrefixedActorModelPath(modelPath, lowerInvariant: true);


        static string NormalizeModelPath(string modelPath)
            => ActorVisualContentRules.NormalizeModelPath(modelPath, lowerInvariant: true);


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
            int firstGraphNodeIndex,
            int graphNodeCount,
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
                int leafSlot = FindRenderLeafSlot(
                    prefabSource,
                    renderLeaves,
                    usedLeaves,
                    skinMeshes[i].SourceRecordIndex,
                    skinMeshes[i].NodeName);
                if ((uint)leafSlot >= (uint)renderLeaves.Count)
                {
                    // Skin extraction scans raw NIF records; prefab source has already applied render selection/filtering.
                    continue;
                }

                usedLeaves[leafSlot] = true;
                int nodeIndex = renderLeaves[leafSlot];
                if ((uint)nodeIndex >= (uint)graphNodeCount)
                    throw new InvalidOperationException($"Actor skin mesh '{skinMeshes[i].ModelPath}#{skinMeshes[i].NodeName}' has no matching baked graph node.");

                var node = prefabSource.Nodes[nodeIndex];
                var renderLeaf = node.RenderLeaf;
                int meshIndex = meshes?.AddOrGet($"{modelPath}#{nodeIndex}", renderLeaf) ?? -1;
                int materialIndex = materials?.AddOrGet(node.MaterialFlags) ?? -1;
                int textureIndex = textures?.AddOrGet(node.TexturePath) ?? -1;
                Bounds bounds = renderLeaf.LocalBounds;

                skinMeshes[i].SourceGraphNodeIndex = firstGraphNodeIndex + nodeIndex;
                skinMeshes[i].SkinRootGraphNodeIndex = ResolveSkinRootGraphNodeIndex(
                    firstGraphNodeIndex,
                    graphNodeCount,
                    skinMeshes[i].SkinRootSourceRecordIndex,
                    skinMeshes[i].SkinRootName);
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
                    ApplyRigAssemblyOperation(
                        skinMeshes[i],
                        BuildRigidAttachmentOperation(
                            firstGraphNodeIndex + nodeIndex,
                            bindReferenceSkeleton ?? skeleton,
                            fullModelBinding: !remapSkinBonesToReferenceSkeleton));
                    skinMeshes[i].RigidOffsetX = 0f;
                    skinMeshes[i].RigidOffsetY = 0f;
                    skinMeshes[i].RigidOffsetZ = 0f;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(skinMeshes[i].SkinRootName)
                        && skinMeshes[i].SkinRootGraphNodeIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Actor weighted skin '{skinMeshes[i].ModelPath}#{skinMeshes[i].NodeName}' skin root '{skinMeshes[i].SkinRootName}' was not found in the baked graph.");
                    }

                    RigAssemblyOperation operation = ComputeWeightedRigAssemblyOperation(
                        firstGraphNodeIndex,
                        graphNodeCount,
                        nodeIndex,
                        skinMeshes[i],
                        skeleton,
                        bindReferenceSkeleton ?? skeleton,
                        remapSkinBonesToReferenceSkeleton);
                    ApplyRigAssemblyOperation(skinMeshes[i], operation);
                    if (remapSkinBonesToReferenceSkeleton && operation.Kind == ActorRigAssemblyKind.Unknown)
                    {
                        // OpenMW's CopyRigVisitor simply copies no rig geometry when a template
                        // drawable does not match the active body-part filter.
                        MarkSkinMeshNonRenderable(skinMeshes[i]);
                        continue;
                    }

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


        static void MarkSkinMeshNonRenderable(ActorSkinMeshDef skinMesh)
        {
            skinMesh.MeshIndex = -1;
            skinMesh.MaterialIndex = -1;
            skinMesh.TextureIndex = -1;
            skinMesh.FirstWeightIndex = -1;
            skinMesh.WeightCount = 0;
            skinMesh.VertexPositions = Array.Empty<float>();
            skinMesh.VertexNormals = Array.Empty<float>();
            skinMesh.VertexUvs = Array.Empty<float>();
            skinMesh.Indices = Array.Empty<int>();
        }


        void AppendModelGraphNodes(ModelPrefabSource prefabSource, int firstGraphNodeIndex)
        {
            if (prefabSource?.Nodes == null)
                return;

            for (int i = 0; i < prefabSource.Nodes.Length; i++)
            {
                var node = prefabSource.Nodes[i];
                int parentIndex = node.ParentIndex >= 0
                    ? firstGraphNodeIndex + node.ParentIndex
                    : -1;
                Matrix4x4 localToRoot = ComputeReferenceStyleSourceLocalToRoot(prefabSource, i);
                Matrix4x4 sourceLocalToRoot = ComputeLocalToRoot(prefabSource, i);
                if (!IsFiniteMatrix(node.SourceLocalMatrix)
                    || !IsFiniteMatrix(localToRoot)
                    || !IsFiniteMatrix(sourceLocalToRoot))
                    throw new InvalidOperationException(
                        $"Actor model graph '{prefabSource.ModelPath}' node '{node.Name}' has a non-finite transform.");

                _graphNodes.Add(new ActorModelGraphNodeDef
                {
                    Name = node.Name ?? string.Empty,
                    ParentIndex = parentIndex,
                    SourceRecordIndex = node.SourceRecordIndex,
                    Kind = node.Kind,
                    Flags = node.Flags,
                    LocalMatrix = PackMatrix(node.SourceLocalMatrix),
                    LocalToRootMatrix = PackMatrix(localToRoot),
                    SourceLocalToRootMatrix = PackMatrix(sourceLocalToRoot),
                });
            }
        }


        int ResolveSkinRootGraphNodeIndex(
            int firstGraphNodeIndex,
            int graphNodeCount,
            int skinRootSourceRecordIndex,
            string skinRootName)
        {
            if (graphNodeCount <= 0)
                return -1;

            int end = firstGraphNodeIndex + graphNodeCount;
            if (skinRootSourceRecordIndex >= 0)
            {
                for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
                {
                    if (_graphNodes[i].SourceRecordIndex == skinRootSourceRecordIndex)
                        return i;
                }
            }

            if (string.IsNullOrWhiteSpace(skinRootName))
                return -1;

            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (string.Equals(_graphNodes[i].Name, skinRootName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            string canonical = CanonicalBoneName(skinRootName);
            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (string.Equals(CanonicalBoneName(_graphNodes[i].Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }


        sealed class RigAssemblyOperation
        {
            public ActorRigAssemblyKind Kind;
            public int SourceGraphNodeIndex = -1;
            public int SkinRootGraphNodeIndex = -1;
            public int CopiedRigRootGraphNodeIndex = -1;
            public int InsertedParentGraphNodeIndex = -1;
            public int CancelledTransformGraphNodeIndex = -1;
            public int SourceSkeletonRootGraphNodeIndex = -1;
            public int TargetBoneIndex = -1;
            public byte AssemblyMirrorX;
            public Matrix4x4 GeometryToSkeleton = Matrix4x4.identity;
        }


        void ApplyRigAssemblyOperation(ActorSkinMeshDef skinMesh, RigAssemblyOperation operation)
        {
            skinMesh.RigAssemblyKind = operation?.Kind ?? ActorRigAssemblyKind.Unknown;
            skinMesh.SourceGraphNodeIndex = operation?.SourceGraphNodeIndex ?? skinMesh.SourceGraphNodeIndex;
            skinMesh.SkinRootGraphNodeIndex = operation?.SkinRootGraphNodeIndex ?? skinMesh.SkinRootGraphNodeIndex;
            skinMesh.CopiedRigRootGraphNodeIndex = operation?.CopiedRigRootGraphNodeIndex ?? -1;
            skinMesh.InsertedParentGraphNodeIndex = operation?.InsertedParentGraphNodeIndex ?? -1;
            skinMesh.CancelledTransformGraphNodeIndex = operation?.CancelledTransformGraphNodeIndex ?? -1;
            skinMesh.SourceSkeletonRootGraphNodeIndex = operation?.SourceSkeletonRootGraphNodeIndex ?? -1;
            skinMesh.TargetBoneIndex = operation?.TargetBoneIndex ?? -1;
            skinMesh.AssemblyMirrorX = operation?.AssemblyMirrorX ?? 0;
            skinMesh.GeometryToSkeletonMatrix = PackMatrix(operation?.GeometryToSkeleton ?? Matrix4x4.identity);
        }


        RigAssemblyOperation BuildRigidAttachmentOperation(
            int graphNodeIndex,
            ActorSkeletonDef skeleton,
            bool fullModelBinding)
        {
            if (fullModelBinding)
            {
                int targetBoneIndex = ResolveFullModelGraphBoneIndex(graphNodeIndex, skeleton);
                if (targetBoneIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Actor rigid skin graph node '{ReadGraphNodeName(graphNodeIndex)}' has no matching flattened skeleton bone.");
                }

                return new RigAssemblyOperation
                {
                    Kind = ActorRigAssemblyKind.RigidAttachment,
                    SourceGraphNodeIndex = graphNodeIndex,
                    TargetBoneIndex = targetBoneIndex,
                    GeometryToSkeleton = Matrix4x4.identity,
                };
            }

            return new RigAssemblyOperation
            {
                Kind = ActorRigAssemblyKind.RigidAttachment,
                SourceGraphNodeIndex = graphNodeIndex,
                GeometryToSkeleton = ReadGraphLocalToRoot(graphNodeIndex),
            };
        }


        int ResolveFullModelGraphBoneIndex(int graphNodeIndex, ActorSkeletonDef skeleton)
        {
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if ((uint)graphNodeIndex >= (uint)_graphNodes.Count || bones.Length == 0)
                return -1;

            var graphNode = _graphNodes[graphNodeIndex];
            int sourceRecordIndex = graphNode?.SourceRecordIndex ?? -1;
            if (sourceRecordIndex >= 0)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].SourceRecordIndex == sourceRecordIndex)
                        return i;
                }
            }

            string name = graphNode?.Name;
            if (string.IsNullOrWhiteSpace(name))
                return -1;

            for (int i = 0; i < bones.Length; i++)
            {
                if (string.Equals(bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            string canonical = CanonicalBoneName(name);
            for (int i = 0; i < bones.Length; i++)
            {
                if (string.Equals(CanonicalBoneName(bones[i].Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }


        string ReadGraphNodeName(int graphNodeIndex)
            => (uint)graphNodeIndex < (uint)_graphNodes.Count
                ? _graphNodes[graphNodeIndex]?.Name ?? $"#{graphNodeIndex}"
                : $"#{graphNodeIndex}";


        RigAssemblyOperation ComputeWeightedRigAssemblyOperation(
            int firstGraphNodeIndex,
            int graphNodeCount,
            int renderLeafNodeIndex,
            ActorSkinMeshDef skinMesh,
            ActorSkeletonDef skeleton,
            ActorSkeletonDef targetSkeleton,
            bool copiedRigBinding)
        {
            return copiedRigBinding
                ? ComputeCopiedRigAssemblyOperation(firstGraphNodeIndex, renderLeafNodeIndex, skinMesh, targetSkeleton)
                : ComputeFullGraphAssemblyOperation(firstGraphNodeIndex, graphNodeCount, skinMesh, skeleton);
        }


        RigAssemblyOperation ComputeFullGraphAssemblyOperation(
            int firstGraphNodeIndex,
            int graphNodeCount,
            ActorSkinMeshDef skinMesh,
            ActorSkeletonDef skeleton)
        {
            var operation = new RigAssemblyOperation
            {
                Kind = ActorRigAssemblyKind.FullModel,
                SourceGraphNodeIndex = skinMesh?.SourceGraphNodeIndex ?? -1,
                SkinRootGraphNodeIndex = skinMesh?.SkinRootGraphNodeIndex ?? -1,
            };

            int skeletonRootGraphNodeIndex = ResolveFullGraphSourceRootGraphNodeIndex(
                firstGraphNodeIndex,
                graphNodeCount,
                skinMesh?.SourceGraphNodeIndex ?? -1,
                skeleton);
            operation.SourceSkeletonRootGraphNodeIndex = skeletonRootGraphNodeIndex;
            if (skeletonRootGraphNodeIndex < 0)
                return operation;

            int cancellationNode = ResolveFullGraphCancelledTransformNode(
                skeletonRootGraphNodeIndex,
                skinMesh?.SkinRootGraphNodeIndex ?? -1,
                skinMesh?.SourceGraphNodeIndex ?? -1,
                skinMesh?.NodeName);
            operation.CancelledTransformGraphNodeIndex = cancellationNode;
            if ((uint)cancellationNode >= (uint)_graphNodes.Count)
                return operation;

            // Full-model actor skeletons are baked from the same flattened source graph as
            // the geometry, so bind-pose bone local-to-root matrices already carry the
            // source graph placement that OpenMW cancels at scene-graph runtime.
            operation.GeometryToSkeleton = Matrix4x4.identity;
            return operation;
        }


        int ResolveFullGraphCancelledTransformNode(
            int skeletonRootGraphNodeIndex,
            int skinRootGraphNodeIndex,
            int sourceGraphNodeIndex,
            string geometryName)
        {
            if ((uint)skinRootGraphNodeIndex < (uint)_graphNodes.Count
                && IsGraphAncestorOrSelf(skeletonRootGraphNodeIndex, skinRootGraphNodeIndex))
            {
                return skinRootGraphNodeIndex;
            }

            int parent = (uint)sourceGraphNodeIndex < (uint)_graphNodes.Count
                ? _graphNodes[sourceGraphNodeIndex]?.ParentIndex ?? -1
                : -1;
            if ((uint)parent >= (uint)_graphNodes.Count
                || !IsGraphAncestorOrSelf(skeletonRootGraphNodeIndex, parent))
            {
                return skeletonRootGraphNodeIndex;
            }

            string parentName = _graphNodes[parent]?.Name ?? string.Empty;
            if (string.Equals(parentName, geometryName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                int beforeParent = _graphNodes[parent]?.ParentIndex ?? -1;
                return (uint)beforeParent < (uint)_graphNodes.Count
                    && IsGraphAncestorOrSelf(skeletonRootGraphNodeIndex, beforeParent)
                        ? beforeParent
                        : skeletonRootGraphNodeIndex;
            }

            return parent;
        }


        int ResolveFullGraphSourceRootGraphNodeIndex(
            int firstGraphNodeIndex,
            int graphNodeCount,
            int sourceGraphNodeIndex,
            ActorSkeletonDef skeleton)
        {
            int sourceRoot = ResolveSourceRootAncestor(firstGraphNodeIndex, graphNodeCount, sourceGraphNodeIndex);
            if (sourceRoot >= 0)
                return sourceRoot;

            return ResolveSkeletonRootGraphNodeIndex(firstGraphNodeIndex, graphNodeCount, skeleton);
        }


        int ResolveSourceRootAncestor(int firstGraphNodeIndex, int graphNodeCount, int graphNodeIndex)
        {
            if ((uint)graphNodeIndex >= (uint)_graphNodes.Count || graphNodeCount <= 0)
                return -1;

            int start = firstGraphNodeIndex;
            int end = firstGraphNodeIndex + graphNodeCount;
            int current = graphNodeIndex;
            int candidate = -1;
            int guard = 0;
            while ((uint)current < (uint)_graphNodes.Count && current >= start && current < end && guard++ < graphNodeCount)
            {
                candidate = current;
                int parent = _graphNodes[current]?.ParentIndex ?? -1;
                if (parent < start || parent >= end || _graphNodes[parent]?.Kind == ModelPrefabNodeKind.SyntheticRoot)
                    return candidate;

                current = parent;
            }

            return candidate;
        }


        RigAssemblyOperation ComputeCopiedRigAssemblyOperation(
            int firstGraphNodeIndex,
            int renderLeafNodeIndex,
            ActorSkinMeshDef skinMesh,
            ActorSkeletonDef targetSkeleton)
        {
            int graphNodeIndex = firstGraphNodeIndex + renderLeafNodeIndex;
            var operation = new RigAssemblyOperation
            {
                Kind = ActorRigAssemblyKind.Unknown,
                SourceGraphNodeIndex = graphNodeIndex,
                SkinRootGraphNodeIndex = skinMesh?.SkinRootGraphNodeIndex ?? -1,
            };

            if ((uint)graphNodeIndex >= (uint)_graphNodes.Count)
                return operation;

            if (!TryResolveCopiedRigReference(graphNodeIndex, out var reference, out string meshFilter))
                return operation;

            operation.Kind = ActorRigAssemblyKind.CopiedRig;
            operation.TargetBoneIndex = ResolveCopiedRigTargetBoneIndex(targetSkeleton, reference);
            if (operation.TargetBoneIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Actor weighted skin '{skinMesh?.ModelPath}#{skinMesh?.NodeName}' copied-rig filter '{meshFilter}' " +
                    $"could not resolve target bone '{ActorVisualMappingPolicy.GetBoneName(reference)}' against skeleton '{targetSkeleton?.ModelPath ?? "<unknown>"}'.");
            }

            int copiedRoot = ResolveCopiedRigRootGraphNodeIndex(graphNodeIndex, meshFilter);
            operation.CopiedRigRootGraphNodeIndex = copiedRoot;
            if ((uint)copiedRoot >= (uint)_graphNodes.Count)
                return operation;

            int copiedRootParent = _graphNodes[copiedRoot]?.ParentIndex ?? -1;
            operation.InsertedParentGraphNodeIndex = copiedRootParent;
            operation.SourceSkeletonRootGraphNodeIndex = copiedRootParent;
            int lastCancelledNode = ResolveCopiedRigLastCancelledNode(
                copiedRoot,
                graphNodeIndex,
                skinMesh?.SkinRootName,
                skinMesh?.NodeName);
            if ((uint)lastCancelledNode >= (uint)_graphNodes.Count
                && (uint)copiedRootParent < (uint)_graphNodes.Count)
            {
                // OpenMW can end up with no transform to cancel when the trishape
                // parent was optimized out. Represent that as the inserted parent
                // boundary so the derived matrix is identity.
                lastCancelledNode = copiedRootParent;
            }

            operation.CancelledTransformGraphNodeIndex = lastCancelledNode;
            if ((uint)lastCancelledNode >= (uint)_graphNodes.Count)
                return operation;

            Matrix4x4 copiedParentToRoot = (uint)copiedRootParent < (uint)_graphNodes.Count
                ? ReadGraphLocalToRoot(copiedRootParent)
                : Matrix4x4.identity;
            Matrix4x4 cancelledToRoot = ReadGraphLocalToRoot(lastCancelledNode);
            Matrix4x4 skeletonToCancelled = copiedParentToRoot.inverse * cancelledToRoot;
            Matrix4x4 matrix = skeletonToCancelled.inverse;
            if (!IsFiniteMatrix(matrix))
            {
                throw new InvalidOperationException(
                    $"Actor weighted skin '{skinMesh?.ModelPath}#{skinMesh?.NodeName}' computed a non-finite copied-rig geometry-to-skeleton matrix.");
            }

            operation.GeometryToSkeleton = matrix;
            return operation;
        }

        bool TryResolveCopiedRigReference(
            int graphNodeIndex,
            out ActorVisualPartReference reference,
            out string meshFilter)
        {
            meshFilter = string.Empty;
            reference = default;
            string nodeName = (uint)graphNodeIndex < (uint)_graphNodes.Count
                ? _graphNodes[graphNodeIndex]?.Name
                : string.Empty;

            for (int i = 0; i < (int)ActorVisualPartReference.Count; i++)
            {
                var candidateReference = (ActorVisualPartReference)i;
                string candidate = ActorVisualMappingPolicy.GetMeshFilter(candidateReference);
                if (MatchesMeshFilter(nodeName, candidate))
                {
                    reference = candidateReference;
                    meshFilter = candidate;
                    return true;
                }
            }

            return false;
        }


        static int ResolveCopiedRigTargetBoneIndex(ActorSkeletonDef targetSkeleton, ActorVisualPartReference reference)
        {
            var bones = targetSkeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            return ResolveBoneIndex(bones, BuildAttachBoneCandidates(reference));
        }


        int ResolveCopiedRigRootGraphNodeIndex(int graphNodeIndex, string meshFilter)
        {
            int current = graphNodeIndex;
            int guard = 0;
            while ((uint)current < (uint)_graphNodes.Count && guard++ < _graphNodes.Count)
            {
                int parent = _graphNodes[current]?.ParentIndex ?? -1;
                if ((uint)parent >= (uint)_graphNodes.Count
                    || !MatchesMeshFilter(_graphNodes[parent]?.Name, meshFilter))
                {
                    return current;
                }

                current = parent;
            }

            return graphNodeIndex;
        }


        int ResolveCopiedRigLastCancelledNode(
            int copiedRoot,
            int graphNodeIndex,
            string skinRootName,
            string geometryName)
        {
            if (!string.IsNullOrWhiteSpace(skinRootName))
            {
                int root = FindCopiedPathNodeByName(copiedRoot, graphNodeIndex, skinRootName);
                if (root >= 0)
                    return root;
            }

            int parent = _graphNodes[graphNodeIndex]?.ParentIndex ?? -1;
            if ((uint)parent >= (uint)_graphNodes.Count || !IsGraphAncestorOrSelf(copiedRoot, parent))
                return _graphNodes[copiedRoot]?.ParentIndex ?? -1;

            string parentName = _graphNodes[parent]?.Name ?? string.Empty;
            if (string.Equals(parentName, geometryName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                int beforeParent = _graphNodes[parent]?.ParentIndex ?? -1;
                return (uint)beforeParent < (uint)_graphNodes.Count && IsGraphAncestorOrSelf(copiedRoot, beforeParent)
                    ? beforeParent
                    : _graphNodes[copiedRoot]?.ParentIndex ?? -1;
            }

            return parent;
        }


        int FindCopiedPathNodeByName(int copiedRoot, int graphNodeIndex, string name)
        {
            int current = graphNodeIndex;
            string canonical = CanonicalBoneName(name);
            int guard = 0;
            while ((uint)current < (uint)_graphNodes.Count && guard++ < _graphNodes.Count)
            {
                string nodeName = _graphNodes[current]?.Name;
                if (string.Equals(nodeName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(CanonicalBoneName(nodeName), canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }

                if (current == copiedRoot)
                    break;

                current = _graphNodes[current]?.ParentIndex ?? -1;
            }

            return -1;
        }


        bool IsGraphAncestorOrSelf(int ancestor, int node)
        {
            int current = node;
            int guard = 0;
            while ((uint)current < (uint)_graphNodes.Count && guard++ < _graphNodes.Count)
            {
                if (current == ancestor)
                    return true;

                current = _graphNodes[current]?.ParentIndex ?? -1;
            }

            return false;
        }


        int ResolveSkeletonRootGraphNodeIndex(int firstGraphNodeIndex, int graphNodeCount, ActorSkeletonDef skeleton)
        {
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if (bones.Length == 0 || graphNodeCount <= 0)
                return -1;

            int sourceRecordIndex = bones[0].SourceRecordIndex;
            if (sourceRecordIndex >= 0)
            {
                int byRecord = FindGraphNodeBySourceRecord(firstGraphNodeIndex, graphNodeCount, sourceRecordIndex);
                if (byRecord >= 0)
                    return byRecord;
            }

            string name = bones[0].Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                int byName = FindGraphNodeByName(firstGraphNodeIndex, graphNodeCount, name);
                if (byName >= 0)
                    return byName;
            }

            int end = firstGraphNodeIndex + graphNodeCount;
            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (_graphNodes[i].Kind != ModelPrefabNodeKind.SyntheticRoot)
                    return i;
            }

            return -1;
        }


        int FindGraphNodeBySourceRecord(int firstGraphNodeIndex, int graphNodeCount, int sourceRecordIndex)
        {
            int end = firstGraphNodeIndex + graphNodeCount;
            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (_graphNodes[i].SourceRecordIndex == sourceRecordIndex)
                    return i;
            }

            return -1;
        }


        int FindGraphNodeByName(int firstGraphNodeIndex, int graphNodeCount, string name)
        {
            int end = firstGraphNodeIndex + graphNodeCount;
            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (string.Equals(_graphNodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            string canonical = CanonicalBoneName(name);
            for (int i = firstGraphNodeIndex; i < end && i < _graphNodes.Count; i++)
            {
                if (string.Equals(CanonicalBoneName(_graphNodes[i].Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }


        Matrix4x4 ReadGraphLocalToRoot(int graphNodeIndex)
            => (uint)graphNodeIndex < (uint)_graphNodes.Count
                ? UnpackMatrix(_graphNodes[graphNodeIndex].LocalToRootMatrix, 0)
                : Matrix4x4.identity;


        Matrix4x4 ReadGraphSourceLocalToRoot(int graphNodeIndex)
            => (uint)graphNodeIndex < (uint)_graphNodes.Count
                ? UnpackMatrix(_graphNodes[graphNodeIndex].SourceLocalToRootMatrix, 0)
                : Matrix4x4.identity;


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


        static bool IsFiniteMatrix(Matrix4x4 m)
            => IsFinite(m.m00) && IsFinite(m.m01) && IsFinite(m.m02) && IsFinite(m.m03)
               && IsFinite(m.m10) && IsFinite(m.m11) && IsFinite(m.m12) && IsFinite(m.m13)
               && IsFinite(m.m20) && IsFinite(m.m21) && IsFinite(m.m22) && IsFinite(m.m23)
               && IsFinite(m.m30) && IsFinite(m.m31) && IsFinite(m.m32) && IsFinite(m.m33);


        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);




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


        }
    }
