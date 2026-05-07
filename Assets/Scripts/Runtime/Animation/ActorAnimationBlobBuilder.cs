using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public static partial class ActorAnimationBlobBuilder
    {
        const int MaxSkinInfluences = 8;

        public static BlobAssetReference<ActorAnimationCatalogBlob> Build(ActorAnimationCatalogData source)
        {
            source ??= new ActorAnimationCatalogData();

            int[] clipRigFamilyIndices = BuildClipRigFamilyIndices(source);
            int[] trackTargetBoneIndices = BuildTrackTargetBoneIndices(source, clipRigFamilyIndices);
            ushort[] trackBlendMasks = BuildTrackBlendMasks(source, clipRigFamilyIndices, trackTargetBoneIndices);
            RuntimeTextMarker[] runtimeTextMarkers = BuildRuntimeTextMarkers(source, out int[] clipTextMarkerStarts, out int[] clipTextMarkerCounts);
            RuntimeAnimationGroup[] runtimeGroups = BuildRuntimeGroups(source, runtimeTextMarkers, clipTextMarkerStarts, clipTextMarkerCounts);
            BuildRuntimeGroupIndexes(
                source,
                runtimeGroups,
                out RuntimeRigFamilyAnimationIndex[] runtimeRigFamilyAnimationIndexes,
                out RuntimeGroupLookup[] runtimeGroupLookups);
            CountSkinPayload(source, out int vertexCount, out int indexCount, out int skinBoneCount);

            var builder = new BlobBuilder(Allocator.Temp);
            ref ActorAnimationCatalogBlob root = ref builder.ConstructRoot<ActorAnimationCatalogBlob>();

            BuildRigFamilies(source, ref builder, ref root);
            BuildSkinBindings(source, ref builder, ref root);
            BuildActorVisualRecipes(source, ref builder, ref root);
            BuildActorVisualRecipeEntries(source, ref builder, ref root);
            BuildEquipmentVisuals(source, ref builder, ref root);
            BuildEquipmentVisualEntries(source, ref builder, ref root);
            BuildGraphNodes(source, ref builder, ref root);
            BuildSkeletons(source, ref builder, ref root);
            BuildSkinMeshes(source, ref builder, ref root, vertexCount, indexCount, skinBoneCount);
            BuildHeadMorphTargets(source, ref builder, ref root);
            BuildHeadMorphVertices(source, ref builder, ref root);
            BuildClips(source, clipRigFamilyIndices, clipTextMarkerStarts, clipTextMarkerCounts, ref builder, ref root);
            BuildTextMarkers(runtimeTextMarkers, ref builder, ref root);
            BuildTracks(source, trackTargetBoneIndices, trackBlendMasks, ref builder, ref root);
            BuildKeys(source, ref builder, ref root);
            BuildGroups(runtimeGroups, ref builder, ref root);
            BuildRigFamilyAnimationIndexes(runtimeRigFamilyAnimationIndexes, ref builder, ref root);
            BuildGroupLookups(runtimeGroupLookups, ref builder, ref root);

            var blob = builder.CreateBlobAssetReference<ActorAnimationCatalogBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        static void BuildRigFamilies(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            BlobBuilderArray<ActorRigFamilyBlob> dst = builder.Allocate(ref root.RigFamilies, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                dst[i] = new ActorRigFamilyBlob
                {
                    FamilyKind = value?.FamilyKind ?? default,
                    SkeletonModelPath = Fixed128(ActorAnimationHash.NormalizePath(value?.SkeletonModelPath)),
                    SkeletonIndex = value?.SkeletonIndex ?? -1,
                    FirstClipIndex = value?.FirstClipIndex ?? -1,
                    ClipCount = value?.ClipCount ?? 0,
                };
            }
        }

        static void BuildSkinBindings(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.SkinBindings ?? Array.Empty<ActorSkinBindingDef>();
            BlobBuilderArray<ActorSkinBindingBlob> dst = builder.Allocate(ref root.SkinBindings, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                dst[i] = new ActorSkinBindingBlob
                {
                    SkinModelPath = Fixed128(ActorAnimationHash.NormalizePath(value?.SkinModelPath)),
                    RigFamilyIndex = value?.RigFamilyIndex ?? -1,
                    FirstSkinMeshIndex = value?.FirstSkinMeshIndex ?? -1,
                    SkinMeshCount = value?.SkinMeshCount ?? 0,
                    FirstGraphNodeIndex = value?.FirstGraphNodeIndex ?? -1,
                    GraphNodeCount = value?.GraphNodeCount ?? 0,
                };
            }
        }

        static void BuildGraphNodes(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.GraphNodes ?? Array.Empty<ActorModelGraphNodeDef>();
            BlobBuilderArray<ActorModelGraphNodeBlob> dst = builder.Allocate(ref root.GraphNodes, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                dst[i] = new ActorModelGraphNodeBlob
                {
                    Name = Fixed64(NormalizeNodeName(value?.Name)),
                    ParentIndex = value?.ParentIndex ?? -1,
                    SourceRecordIndex = value?.SourceRecordIndex ?? -1,
                    Kind = value?.Kind ?? ModelPrefabNodeKind.None,
                    Flags = value?.Flags ?? 0,
                    LocalMatrix = ReadMatrix(value?.LocalMatrix, 0),
                    LocalToRootMatrix = ReadMatrix(value?.LocalToRootMatrix, 0),
                    SourceLocalToRootMatrix = ReadMatrix(value?.SourceLocalToRootMatrix, 0),
                };
            }
        }

        static void BuildActorVisualRecipes(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.ActorVisualRecipes ?? Array.Empty<ActorVisualRecipeDef>();
            BlobBuilderArray<ActorVisualRecipeBlob> dst = builder.Allocate(ref root.ActorVisualRecipes, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                dst[i] = new ActorVisualRecipeBlob
                {
                    ActorContentId = value?.ActorContentId.Value ?? 0UL,
                    FirstPerson = value?.FirstPerson ?? 0,
                    BodyVariant = value?.BodyVariant ?? default,
                    RigFamilyIndex = value?.RigFamilyIndex ?? -1,
                    FirstEntryIndex = value?.FirstEntryIndex ?? -1,
                    EntryCount = value?.EntryCount ?? 0,
                };
            }
        }

        static void BuildActorVisualRecipeEntries(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.ActorVisualRecipeEntries ?? Array.Empty<ActorVisualRecipeEntryDef>();
            BlobBuilderArray<ActorVisualRecipeEntryBlob> dst = builder.Allocate(ref root.ActorVisualRecipeEntries, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                dst[i] = new ActorVisualRecipeEntryBlob
                {
                    PartReference = values[i].PartReference,
                    SkinBindingIndex = values[i].SkinBindingIndex,
                    SkinMeshIndex = values[i].SkinMeshIndex,
                    AttachBoneIndex = values[i].AttachBoneIndex,
                    RigidMirrorX = values[i].RigidMirrorX,
                };
            }
        }

        static void BuildEquipmentVisuals(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.EquipmentVisuals ?? Array.Empty<ActorEquipmentVisualDef>();
            BlobBuilderArray<ActorEquipmentVisualBlob> dst = builder.Allocate(ref root.EquipmentVisuals, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                dst[i] = new ActorEquipmentVisualBlob
                {
                    ItemContentId = value?.ItemContentId.Value ?? 0UL,
                    RigFamilyIndex = value?.RigFamilyIndex ?? -1,
                    FirstPerson = value?.FirstPerson ?? 0,
                    BodyVariant = value?.BodyVariant ?? default,
                    IsValid = value?.IsValid ?? 0,
                    CoverageMask = value?.CoverageMask ?? 0u,
                    FirstEntryIndex = value?.FirstEntryIndex ?? -1,
                    EntryCount = value?.EntryCount ?? 0,
                };
            }
        }

        static void BuildEquipmentVisualEntries(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.EquipmentVisualEntries ?? Array.Empty<ActorEquipmentVisualEntryDef>();
            BlobBuilderArray<ActorEquipmentVisualEntryBlob> dst = builder.Allocate(ref root.EquipmentVisualEntries, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                dst[i] = new ActorEquipmentVisualEntryBlob
                {
                    PartReference = values[i].PartReference,
                    SkinBindingIndex = values[i].SkinBindingIndex,
                    SkinMeshIndex = values[i].SkinMeshIndex,
                    AttachBoneIndex = values[i].AttachBoneIndex,
                    RigidMirrorX = values[i].RigidMirrorX,
                };
            }
        }

        static void BuildSkeletons(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var skeletons = source.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            int boneCount = 0;
            for (int i = 0; i < skeletons.Length; i++)
                boneCount += skeletons[i]?.Bones?.Length ?? 0;

            BlobBuilderArray<ActorSkeletonBlob> dstSkeletons = builder.Allocate(ref root.Skeletons, skeletons.Length);
            BlobBuilderArray<ActorSkeletonBoneBlob> dstBones = builder.Allocate(ref root.Bones, boneCount);
            int cursor = 0;
            for (int i = 0; i < skeletons.Length; i++)
            {
                var skeleton = skeletons[i];
                var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
                int accumulationBoneIndex = ResolveAccumulationBoneIndex(bones, skeleton?.AccumulationBoneIndex ?? -1);
                dstSkeletons[i] = new ActorSkeletonBlob
                {
                    ModelPath = Fixed128(ActorAnimationHash.NormalizePath(skeleton?.ModelPath)),
                    AccumulationBoneIndex = accumulationBoneIndex,
                    AccumulationSubtreeEndIndex = ResolveSubtreeEnd(bones, accumulationBoneIndex),
                    FirstBoneIndex = cursor,
                    BoneCount = bones.Length,
                };

                float4x4[] bindLocalToRoot = BuildBindLocalToRootMatrices(bones);
                for (int j = 0; j < bones.Length; j++)
                {
                    var bone = bones[j];
                    FixedString64Bytes boneName = Fixed64(NormalizeNodeName(bone.Name));
                    quaternion rotation = new(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
                    if (math.lengthsq(rotation.value) <= 0.000001f)
                        rotation = quaternion.identity;

                    dstBones[cursor++] = new ActorSkeletonBoneBlob
                    {
                        Name = boneName,
                        NameHash = ActorSkeletonNameHash.Hash(boneName),
                        ParentIndex = bone.ParentIndex,
                        SourceRecordIndex = bone.SourceRecordIndex,
                        BindPosition = new float3(bone.PosX, bone.PosY, bone.PosZ),
                        BindRotation = math.normalize(rotation),
                        BindScale = bone.Scale <= 0f ? 1f : bone.Scale,
                        BindLocalMatrix = ReadMatrix(bone.BindLocalMatrix, 0, BuildDecomposedBindLocalMatrix(bone)),
                        BindLocalToRootMatrix = bindLocalToRoot[j],
                    };
                }
            }
        }

        static void BuildSkinMeshes(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root,
            int vertexCount,
            int indexCount,
            int skinBoneCount)
        {
            var meshes = source.SkinMeshes ?? Array.Empty<ActorSkinMeshDef>();
            BlobBuilderArray<ActorSkinMeshBlob> dstMeshes = builder.Allocate(ref root.SkinMeshes, meshes.Length);
            BlobBuilderArray<ActorSkinVertexBlob> dstVertices = builder.Allocate(ref root.SkinVertices, vertexCount);
            BlobBuilderArray<int> dstIndices = builder.Allocate(ref root.SkinIndices, indexCount);
            BlobBuilderArray<ActorSkinBoneBlob> dstSkinBones = builder.Allocate(ref root.SkinBones, skinBoneCount);

            int vertexCursor = 0;
            int indexCursor = 0;
            int skinBoneCursor = 0;
            var skinWeights = source.SkinWeights ?? Array.Empty<ActorSkinWeightDef>();
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                var mesh = meshes[meshIndex];
                int meshVertexCount = mesh?.VertexPositions?.Length / 3 ?? 0;
                int meshIndexCount = mesh?.Indices?.Length ?? 0;
                int meshSkinBoneCount = mesh?.BoneIndices?.Length ?? 0;

                dstMeshes[meshIndex] = new ActorSkinMeshBlob
                {
                    ModelPath = Fixed128(ActorAnimationHash.NormalizePath(mesh?.ModelPath)),
                    NodeName = Fixed64(NormalizeNodeName(mesh?.NodeName)),
                    SourceRecordIndex = mesh?.SourceRecordIndex ?? -1,
                    MeshIndex = mesh?.MeshIndex ?? -1,
                    MaterialIndex = mesh?.MaterialIndex ?? -1,
                    TextureIndex = mesh?.TextureIndex ?? -1,
                    IsRigid = mesh?.IsRigid ?? 0,
                    SkeletonIndex = mesh?.SkeletonIndex ?? -1,
                    FirstSkinBoneIndex = skinBoneCursor,
                    SkinBoneCount = meshSkinBoneCount,
                    FirstVertexIndex = vertexCursor,
                    VertexCount = meshVertexCount,
                    FirstIndexIndex = indexCursor,
                    IndexCount = meshIndexCount,
                    RigAssemblyKind = mesh?.RigAssemblyKind ?? ActorRigAssemblyKind.Unknown,
                    SourceGraphNodeIndex = mesh?.SourceGraphNodeIndex ?? -1,
                    SkinRootSourceRecordIndex = mesh?.SkinRootSourceRecordIndex ?? -1,
                    SkinRootGraphNodeIndex = mesh?.SkinRootGraphNodeIndex ?? -1,
                    CopiedRigRootGraphNodeIndex = mesh?.CopiedRigRootGraphNodeIndex ?? -1,
                    InsertedParentGraphNodeIndex = mesh?.InsertedParentGraphNodeIndex ?? -1,
                    CancelledTransformGraphNodeIndex = mesh?.CancelledTransformGraphNodeIndex ?? -1,
                    SourceSkeletonRootGraphNodeIndex = mesh?.SourceSkeletonRootGraphNodeIndex ?? -1,
                    TargetBoneIndex = mesh?.TargetBoneIndex ?? -1,
                    AssemblyMirrorX = mesh?.AssemblyMirrorX ?? 0,
                    BoundsCenter = new float3(mesh?.BoundsCenterX ?? 0f, mesh?.BoundsCenterY ?? 0f, mesh?.BoundsCenterZ ?? 0f),
                    BoundsExtents = new float3(mesh?.BoundsExtentsX ?? 0f, mesh?.BoundsExtentsY ?? 0f, mesh?.BoundsExtentsZ ?? 0f),
                    GeometryToSkeleton = ReadMatrix(mesh?.GeometryToSkeletonMatrix, 0),
                    RigidOffset = new float3(mesh?.RigidOffsetX ?? 0f, mesh?.RigidOffsetY ?? 0f, mesh?.RigidOffsetZ ?? 0f),
                    TalkStart = mesh?.TalkStart ?? 0f,
                    TalkStop = mesh?.TalkStop ?? 0f,
                    BlinkStart = mesh?.BlinkStart ?? 0f,
                    BlinkStop = mesh?.BlinkStop ?? 0f,
                    FirstHeadMorphTargetIndex = mesh?.FirstHeadMorphTargetIndex ?? -1,
                    HeadMorphTargetCount = mesh?.HeadMorphTargetCount ?? 0,
                };

                for (int i = 0; i < meshSkinBoneCount; i++)
                {
                    dstSkinBones[skinBoneCursor++] = new ActorSkinBoneBlob
                    {
                        Name = Fixed64(NormalizeNodeName(ReadBoneName(mesh, i))),
                        BoneIndex = mesh.BoneIndices[i],
                        SourceRecordIndex = ReadBoneSourceRecordIndex(mesh, i),
                        BindPose = ReadMatrix(mesh.BindPoseMatrices, i * 16),
                    };
                }

                WriteSkinVertices(mesh, skinWeights, dstVertices, vertexCursor);
                vertexCursor += meshVertexCount;

                for (int i = 0; i < meshIndexCount; i++)
                    dstIndices[indexCursor++] = mesh.Indices[i];
            }
        }

        static void BuildHeadMorphTargets(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.HeadMorphTargets ?? Array.Empty<ActorHeadMorphTargetDef>();
            BlobBuilderArray<ActorHeadMorphTargetBlob> dst = builder.Allocate(ref root.HeadMorphTargets, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                dst[i] = new ActorHeadMorphTargetBlob
                {
                    FirstKeyIndex = values[i].FirstKeyIndex,
                    KeyCount = values[i].KeyCount,
                    FirstVertexIndex = values[i].FirstVertexIndex,
                    VertexCount = values[i].VertexCount,
                    Interpolation = values[i].Interpolation,
                };
            }
        }

        static void BuildHeadMorphVertices(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var values = source.HeadMorphVertices ?? Array.Empty<ActorHeadMorphVertexDef>();
            BlobBuilderArray<ActorHeadMorphVertexBlob> dst = builder.Allocate(ref root.HeadMorphVertices, values.Length);
            for (int i = 0; i < values.Length; i++)
                dst[i] = new ActorHeadMorphVertexBlob { Value = new float3(values[i].X, values[i].Y, values[i].Z) };
        }

        static void CountSkinPayload(
            ActorAnimationCatalogData source,
            out int vertexCount,
            out int indexCount,
            out int skinBoneCount)
        {
            vertexCount = 0;
            indexCount = 0;
            skinBoneCount = 0;
            var meshes = source.SkinMeshes ?? Array.Empty<ActorSkinMeshDef>();
            for (int i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i];
                vertexCount += mesh?.VertexPositions?.Length / 3 ?? 0;
                indexCount += mesh?.Indices?.Length ?? 0;
                skinBoneCount += mesh?.BoneIndices?.Length ?? 0;
            }
        }

        static void WriteSkinVertices(
            ActorSkinMeshDef mesh,
            ActorSkinWeightDef[] skinWeights,
            BlobBuilderArray<ActorSkinVertexBlob> dst,
            int dstStart)
        {
            int vertexCount = mesh?.VertexPositions?.Length / 3 ?? 0;
            if (vertexCount <= 0)
                return;

            var boneIndices = new int[vertexCount, MaxSkinInfluences];
            var weights = new float[vertexCount, MaxSkinInfluences];
            for (int i = 0; i < vertexCount; i++)
            {
                for (int j = 0; j < MaxSkinInfluences; j++)
                    boneIndices[i, j] = -1;
            }

            int firstWeight = mesh.FirstWeightIndex;
            int weightCount = mesh.WeightCount;
            if (firstWeight >= 0 && weightCount > 0 && skinWeights != null)
            {
                int end = math.min(skinWeights.Length, firstWeight + weightCount);
                for (int i = firstWeight; i < end; i++)
                {
                    var weight = skinWeights[i];
                    if (weight.VertexIndex >= vertexCount || weight.Weight <= 0f)
                        continue;

                    InsertInfluence(
                        boneIndices,
                        weights,
                        weight.VertexIndex,
                        MapWeightBoneIndex(mesh, weight.BoneIndex),
                        weight.Weight);
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                NormalizeInfluences(boneIndices, weights, i);
                dst[dstStart + i] = new ActorSkinVertexBlob
                {
                    Position = ReadFloat3(mesh.VertexPositions, i * 3),
                    Normal = ReadFloat3(mesh.VertexNormals, i * 3, new float3(0f, 1f, 0f)),
                    Uv = ReadFloat2(mesh.VertexUvs, i * 2),
                    BoneIndices0 = new int4(boneIndices[i, 0], boneIndices[i, 1], boneIndices[i, 2], boneIndices[i, 3]),
                    BoneIndices1 = new int4(boneIndices[i, 4], boneIndices[i, 5], boneIndices[i, 6], boneIndices[i, 7]),
                    Weights0 = new float4(weights[i, 0], weights[i, 1], weights[i, 2], weights[i, 3]),
                    Weights1 = new float4(weights[i, 4], weights[i, 5], weights[i, 6], weights[i, 7]),
                };
            }
        }

        static int MapWeightBoneIndex(ActorSkinMeshDef mesh, int weightBoneIndex)
        {
            return mesh?.BoneIndices != null && (uint)weightBoneIndex < (uint)mesh.BoneIndices.Length
                ? weightBoneIndex
                : -1;
        }

        static string ReadBoneName(ActorSkinMeshDef mesh, int boneIndex)
        {
            return mesh?.BoneNames != null && (uint)boneIndex < (uint)mesh.BoneNames.Length
                ? mesh.BoneNames[boneIndex]
                : string.Empty;
        }

        static int ReadBoneSourceRecordIndex(ActorSkinMeshDef mesh, int boneIndex)
        {
            return mesh?.BoneSourceRecordIndices != null && (uint)boneIndex < (uint)mesh.BoneSourceRecordIndices.Length
                ? mesh.BoneSourceRecordIndices[boneIndex]
                : -1;
        }

        static void InsertInfluence(int[,] boneIndices, float[,] weights, int vertex, int boneIndex, float weight)
        {
            int replace = -1;
            float minWeight = weight;
            for (int i = 0; i < MaxSkinInfluences; i++)
            {
                if (weights[vertex, i] <= 0f)
                {
                    replace = i;
                    break;
                }

                if (weights[vertex, i] < minWeight)
                {
                    minWeight = weights[vertex, i];
                    replace = i;
                }
            }

            if (replace < 0)
                return;

            boneIndices[vertex, replace] = boneIndex;
            weights[vertex, replace] = weight;
        }

        static void NormalizeInfluences(int[,] boneIndices, float[,] weights, int vertex)
        {
            float sum = 0f;
            for (int i = 0; i < MaxSkinInfluences; i++)
                sum += math.max(0f, weights[vertex, i]);

            if (sum <= 0.000001f)
            {
                boneIndices[vertex, 0] = 0;
                weights[vertex, 0] = 1f;
                return;
            }

            float inv = 1f / sum;
            for (int i = 0; i < MaxSkinInfluences; i++)
            {
                weights[vertex, i] = math.max(0f, weights[vertex, i]) * inv;
                if (weights[vertex, i] <= 0f)
                    boneIndices[vertex, i] = -1;
            }
        }

        static float3 ReadFloat3(float[] values, int start, float3 fallback = default)
        {
            if (values == null || start < 0 || start + 2 >= values.Length)
                return fallback;
            return new float3(values[start], values[start + 1], values[start + 2]);
        }

        static float2 ReadFloat2(float[] values, int start)
        {
            if (values == null || start < 0 || start + 1 >= values.Length)
                return float2.zero;
            return new float2(values[start], values[start + 1]);
        }

        static float4x4 ReadMatrix(float[] values, int start)
            => ReadMatrix(values, start, float4x4.identity);

        static float4x4 ReadMatrix(float[] values, int start, float4x4 fallback)
        {
            if (values == null || start < 0 || start + 15 >= values.Length)
                return fallback;

            return new float4x4(
                new float4(values[start], values[start + 4], values[start + 8], values[start + 12]),
                new float4(values[start + 1], values[start + 5], values[start + 9], values[start + 13]),
                new float4(values[start + 2], values[start + 6], values[start + 10], values[start + 14]),
                new float4(values[start + 3], values[start + 7], values[start + 11], values[start + 15]));
        }

        static float4x4 BuildDecomposedBindLocalMatrix(ActorSkeletonBoneDef bone)
        {
            quaternion rotation = new(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
            if (math.lengthsq(rotation.value) <= 0.000001f)
                rotation = quaternion.identity;
            else
                rotation = math.normalize(rotation);

            float scale = bone.Scale <= 0f ? 1f : bone.Scale;
            return float4x4.TRS(
                new float3(bone.PosX, bone.PosY, bone.PosZ),
                rotation,
                new float3(scale));
        }

        static float4x4[] BuildBindLocalToRootMatrices(ActorSkeletonBoneDef[] bones)
        {
            var matrices = new float4x4[bones?.Length ?? 0];
            for (int i = 0; i < matrices.Length; i++)
            {
                var bone = bones[i];
                float4x4 local = ReadMatrix(bone.BindLocalMatrix, 0, BuildDecomposedBindLocalMatrix(bone));
                float4x4 root = ReadMatrix(bone.BindLocalToRootMatrix, 0, default);
                bool hasRoot = !root.Equals(default(float4x4));
                matrices[i] = hasRoot
                    ? root
                    : bone.ParentIndex >= 0 && bone.ParentIndex < i
                        ? math.mul(matrices[bone.ParentIndex], local)
                        : local;
            }

            return matrices;
        }

        static int ResolveAccumulationBoneIndex(ActorSkeletonBoneDef[] bones, int cachedIndex)
        {
            if (bones == null || bones.Length == 0)
                return -1;

            int bip01 = FindBoneIndex(bones, "Bip01");
            if (bip01 >= 0)
                return bip01;

            int rootBone = FindBoneIndex(bones, "Root Bone");
            if (rootBone >= 0)
                return rootBone;

            return (uint)cachedIndex < (uint)bones.Length ? cachedIndex : -1;
        }

        static bool IsKnownLoopingGroup(FixedString64Bytes group)
        {
            return EqualsAscii(group, Fixed64("idle"))
                   || EqualsAscii(group, Fixed64("idle2"))
                   || EqualsAscii(group, Fixed64("idle3"))
                   || EqualsAscii(group, Fixed64("idle4"))
                   || EqualsAscii(group, Fixed64("idle5"))
                   || EqualsAscii(group, Fixed64("idle6"))
                   || EqualsAscii(group, Fixed64("idle7"))
                   || EqualsAscii(group, Fixed64("idle8"))
                   || EqualsAscii(group, Fixed64("idle9"))
                   || EqualsAscii(group, Fixed64("idlesneak"))
                   || EqualsAscii(group, Fixed64("jump"))
                   || StartsWithAscii(group, Fixed64("walk"))
                   || StartsWithAscii(group, Fixed64("run"))
                   || StartsWithAscii(group, Fixed64("sneak"))
                   || StartsWithAscii(group, Fixed64("swimwalk"))
                   || StartsWithAscii(group, Fixed64("swimrun"))
                   || StartsWithAscii(group, Fixed64("turn"))
                   || StartsWithAscii(group, Fixed64("swimturn"));
        }

        static bool StartsWithAscii(FixedString64Bytes value, FixedString64Bytes prefix)
        {
            if (value.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
                if (ToLowerAscii(value[i]) != ToLowerAscii(prefix[i]))
                    return false;
            return true;
        }

        static bool EqualsAscii(FixedString64Bytes left, FixedString64Bytes right)
        {
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (ToLowerAscii(left[i]) != ToLowerAscii(right[i]))
                    return false;
            return true;
        }

        static byte ToLowerAscii(byte value)
            => value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;

        static FixedString64Bytes Fixed64(string value)
        {
            value ??= string.Empty;
            if (value.Length > 60)
                value = value.Substring(0, 60);
            return new FixedString64Bytes(value);
        }

        static string NormalizeNodeName(string value)
            => string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();

        static FixedString128Bytes Fixed128(string value)
        {
            value ??= string.Empty;
            if (value.Length > 124)
                value = value.Substring(0, 124);
            return new FixedString128Bytes(value);
        }
    }
}
