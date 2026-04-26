using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public struct ActorAnimationBlobCatalog : IComponentData
    {
        public BlobAssetReference<ActorAnimationCatalogBlob> Blob;
    }

    public struct ActorAnimationCatalogBlob
    {
        public BlobArray<ActorRigFamilyBlob> RigFamilies;
        public BlobArray<ActorSkinBindingBlob> SkinBindings;
        public BlobArray<ActorVisualRecipeBlob> ActorVisualRecipes;
        public BlobArray<ActorVisualRecipeEntryBlob> ActorVisualRecipeEntries;
        public BlobArray<ActorEquipmentVisualBlob> EquipmentVisuals;
        public BlobArray<ActorEquipmentVisualEntryBlob> EquipmentVisualEntries;
        public BlobArray<ActorSkeletonBlob> Skeletons;
        public BlobArray<ActorSkeletonBoneBlob> Bones;
        public BlobArray<ActorSkinMeshBlob> SkinMeshes;
        public BlobArray<ActorSkinVertexBlob> SkinVertices;
        public BlobArray<int> SkinIndices;
        public BlobArray<ActorSkinBoneBlob> SkinBones;
        public BlobArray<ActorAnimationClipBlob> Clips;
        public BlobArray<ActorAnimationTrackBlob> Tracks;
        public BlobArray<ActorAnimationKeyBlob> Keys;
        public BlobArray<ActorAnimationTextKeyBlob> TextKeys;
        public BlobArray<ActorAnimationTextMarkerBlob> TextMarkers;
        public BlobArray<ActorAnimationHashEntryBlob> ClipHashes;
    }

    public struct ActorRigFamilyBlob
    {
        public ActorRigFamilyKind FamilyKind;
        public FixedString128Bytes SkeletonModelPath;
        public int SkeletonIndex;
        public int FirstClipIndex;
        public int ClipCount;
    }

    public struct ActorSkinBindingBlob
    {
        public FixedString128Bytes SkinModelPath;
        public int RigFamilyIndex;
        public int FirstSkinMeshIndex;
        public int SkinMeshCount;
    }

    public struct ActorVisualRecipeBlob
    {
        public ulong ActorContentId;
        public byte FirstPerson;
        public ActorVisualBodyVariant BodyVariant;
        public int RigFamilyIndex;
        public int FirstEntryIndex;
        public int EntryCount;
    }

    public struct ActorVisualRecipeEntryBlob
    {
        public ActorVisualPartReference PartReference;
        public int SkinBindingIndex;
        public int SkinMeshIndex;
        public int AttachBoneIndex;
        public byte RigidMirrorX;
    }

    public struct ActorEquipmentVisualBlob
    {
        public ulong ItemContentId;
        public int RigFamilyIndex;
        public byte FirstPerson;
        public ActorVisualBodyVariant BodyVariant;
        public byte IsValid;
        public uint CoverageMask;
        public int FirstEntryIndex;
        public int EntryCount;
    }

    public struct ActorEquipmentVisualEntryBlob
    {
        public ItemEquipmentPartReference PartReference;
        public int SkinBindingIndex;
        public int SkinMeshIndex;
        public int AttachBoneIndex;
        public byte RigidMirrorX;
    }

    public struct ActorSkeletonBlob
    {
        public FixedString128Bytes ModelPath;
        public int AccumulationBoneIndex;
        public int AccumulationSubtreeEndIndex;
        public int FirstBoneIndex;
        public int BoneCount;
    }

    public struct ActorSkeletonBoneBlob
    {
        public FixedString64Bytes Name;
        public int ParentIndex;
        public float3 BindPosition;
        public quaternion BindRotation;
        public float BindScale;
        public float4x4 BindLocalMatrix;
        public float4x4 BindLocalToRootMatrix;
    }

    public struct ActorSkinMeshBlob
    {
        public FixedString128Bytes ModelPath;
        public FixedString64Bytes NodeName;
        public int MeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public byte IsRigid;
        public int SkeletonIndex;
        public int FirstSkinBoneIndex;
        public int SkinBoneCount;
        public int FirstVertexIndex;
        public int VertexCount;
        public int FirstIndexIndex;
        public int IndexCount;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public float4x4 GeometryToSkeleton;
        public float3 RigidOffset;
    }

    public struct ActorSkinBoneBlob
    {
        public FixedString64Bytes Name;
        public int BoneIndex;
        public float4x4 BindPose;
    }

    public struct ActorSkinVertexBlob
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;
        public int4 BoneIndices0;
        public int4 BoneIndices1;
        public float4 Weights0;
        public float4 Weights1;
    }

    public struct ActorAnimationClipBlob
    {
        public FixedString128Bytes SourcePath;
        public FixedString64Bytes Name;
        public FixedString64Bytes AccumRootName;
        public ulong AnimationHash;
        public float Duration;
        public int FirstTrackIndex;
        public int TrackCount;
        public int FirstTextKeyIndex;
        public int TextKeyCount;
        public int FirstTextMarkerIndex;
        public int TextMarkerCount;
    }

    public struct ActorAnimationTrackBlob
    {
        public FixedString64Bytes TargetName;
        public int TargetBoneIndex;
        public ActorAnimationTrackKind Kind;
        public ActorAnimationInterpolation Interpolation;
        public int AxisOrder;
        public ushort ControllerFlags;
        public float Frequency;
        public float Phase;
        public float TimeStart;
        public float TimeStop;
        public int FirstKeyIndex;
        public int KeyCount;
    }

    public struct ActorAnimationKeyBlob
    {
        public float Time;
        public float4 Value;
        public float4 InTangent;
        public float4 OutTangent;
    }

    public struct ActorAnimationTextKeyBlob
    {
        public float Time;
        public FixedString128Bytes Text;
    }

    public struct ActorAnimationTextMarkerBlob
    {
        public float Time;
        public FixedString64Bytes Group;
        public FixedString64Bytes Value;
        public FixedString128Bytes Text;
        public ActorAnimationTextMarkerKind Kind;
    }

    public struct ActorAnimationHashEntryBlob
    {
        public ulong Hash;
        public int ClipIndex;
        public int RigFamilyIndex;
    }

    public static class ActorAnimationHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Clip(string modelPath, string clipName)
        {
            string normalizedModel = NormalizePath(modelPath);
            string normalizedClip = string.IsNullOrWhiteSpace(clipName)
                ? string.Empty
                : clipName.Trim();
            return Combine(normalizedModel, normalizedClip);
        }

        public static ulong Combine(string left, string right)
        {
            ulong hash = Offset;
            AppendLowerInvariant(ref hash, left);
            Add(ref hash, (byte)'|');
            AppendLowerInvariant(ref hash, right);
            return hash;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string trimmed = path.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\"))
                trimmed = trimmed.Replace("\\\\", "\\");
            return trimmed.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"meshes\\{trimmed}";
        }

        static void AppendLowerInvariant(ref ulong hash, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                Add(ref hash, c <= 0x7f ? (byte)c : (byte)'?');
            }
        }

        static void Add(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }

    public static class ActorAnimationBlobBuilder
    {
        const int MaxSkinInfluences = 8;

        public static BlobAssetReference<ActorAnimationCatalogBlob> Build(ActorAnimationCatalogData source)
        {
            source ??= new ActorAnimationCatalogData();

            int[] clipRigFamilyIndices = BuildClipRigFamilyIndices(source);
            int[] trackTargetBoneIndices = BuildTrackTargetBoneIndices(source, clipRigFamilyIndices);
            CountSkinPayload(source, out int vertexCount, out int indexCount, out int skinBoneCount);

            var builder = new BlobBuilder(Allocator.Temp);
            ref ActorAnimationCatalogBlob root = ref builder.ConstructRoot<ActorAnimationCatalogBlob>();

            BuildRigFamilies(source, ref builder, ref root);
            BuildSkinBindings(source, ref builder, ref root);
            BuildActorVisualRecipes(source, ref builder, ref root);
            BuildActorVisualRecipeEntries(source, ref builder, ref root);
            BuildEquipmentVisuals(source, ref builder, ref root);
            BuildEquipmentVisualEntries(source, ref builder, ref root);
            BuildSkeletons(source, ref builder, ref root);
            BuildSkinMeshes(source, ref builder, ref root, vertexCount, indexCount, skinBoneCount);
            BuildClips(source, clipRigFamilyIndices, ref builder, ref root);
            BuildTracks(source, trackTargetBoneIndices, ref builder, ref root);
            BuildKeys(source, ref builder, ref root);
            BuildTextKeys(source, ref builder, ref root);
            BuildTextMarkers(source, ref builder, ref root);
            BuildClipHashes(source, clipRigFamilyIndices, ref builder, ref root);

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
                dstSkeletons[i] = new ActorSkeletonBlob
                {
                    ModelPath = Fixed128(ActorAnimationHash.NormalizePath(skeleton?.ModelPath)),
                    AccumulationBoneIndex = skeleton?.AccumulationBoneIndex ?? -1,
                    AccumulationSubtreeEndIndex = ResolveSubtreeEnd(bones, skeleton?.AccumulationBoneIndex ?? -1),
                    FirstBoneIndex = cursor,
                    BoneCount = bones.Length,
                };

                float4x4[] bindLocalToRoot = BuildBindLocalToRootMatrices(bones);
                for (int j = 0; j < bones.Length; j++)
                {
                    var bone = bones[j];
                    quaternion rotation = new(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
                    if (math.lengthsq(rotation.value) <= 0.000001f)
                        rotation = quaternion.identity;

                    dstBones[cursor++] = new ActorSkeletonBoneBlob
                    {
                        Name = Fixed64(NormalizeNodeName(bone.Name)),
                        ParentIndex = bone.ParentIndex,
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
                    BoundsCenter = new float3(mesh?.BoundsCenterX ?? 0f, mesh?.BoundsCenterY ?? 0f, mesh?.BoundsCenterZ ?? 0f),
                    BoundsExtents = new float3(mesh?.BoundsExtentsX ?? 0f, mesh?.BoundsExtentsY ?? 0f, mesh?.BoundsExtentsZ ?? 0f),
                    GeometryToSkeleton = ReadMatrix(mesh?.GeometryToSkeletonMatrix, 0),
                    RigidOffset = new float3(mesh?.RigidOffsetX ?? 0f, mesh?.RigidOffsetY ?? 0f, mesh?.RigidOffsetZ ?? 0f),
                };

                for (int i = 0; i < meshSkinBoneCount; i++)
                {
                    dstSkinBones[skinBoneCursor++] = new ActorSkinBoneBlob
                    {
                        Name = Fixed64(NormalizeNodeName(ReadBoneName(mesh, i))),
                        BoneIndex = mesh.BoneIndices[i],
                        BindPose = ReadMatrix(mesh.BindPoseMatrices, i * 16),
                    };
                }

                WriteSkinVertices(mesh, skinWeights, dstVertices, vertexCursor);
                vertexCursor += meshVertexCount;

                for (int i = 0; i < meshIndexCount; i++)
                    dstIndices[indexCursor++] = mesh.Indices[i];
            }
        }

        static void BuildClips(
            ActorAnimationCatalogData source,
            int[] clipRigFamilyIndices,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            BlobBuilderArray<ActorAnimationClipBlob> dst = builder.Allocate(ref root.Clips, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                string modelPath = string.Empty;
                int rigFamilyIndex = (uint)i < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[i] : -1;
                if ((uint)rigFamilyIndex < (uint)rigFamilies.Length)
                    modelPath = rigFamilies[rigFamilyIndex]?.SkeletonModelPath;

                dst[i] = new ActorAnimationClipBlob
                {
                    SourcePath = Fixed128(clip?.SourcePath),
                    Name = Fixed64(clip?.Name),
                    AccumRootName = Fixed64(clip?.AccumRootName),
                    AnimationHash = ActorAnimationHash.Clip(modelPath, clip?.Name),
                    Duration = clip?.Duration ?? 0f,
                    FirstTrackIndex = clip?.FirstTrackIndex ?? -1,
                    TrackCount = clip?.TrackCount ?? 0,
                    FirstTextKeyIndex = clip?.FirstTextKeyIndex ?? -1,
                    TextKeyCount = clip?.TextKeyCount ?? 0,
                    FirstTextMarkerIndex = clip?.FirstTextMarkerIndex ?? -1,
                    TextMarkerCount = clip?.TextMarkerCount ?? 0,
                };
            }
        }

        static void BuildTracks(
            ActorAnimationCatalogData source,
            int[] targetBoneIndices,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var tracks = source.Tracks ?? Array.Empty<ActorAnimationTrackDef>();
            BlobBuilderArray<ActorAnimationTrackBlob> dst = builder.Allocate(ref root.Tracks, tracks.Length);
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                dst[i] = new ActorAnimationTrackBlob
                {
                    TargetName = Fixed64(track?.TargetName),
                    TargetBoneIndex = (uint)i < (uint)targetBoneIndices.Length ? targetBoneIndices[i] : -1,
                    Kind = track?.Kind ?? default,
                    Interpolation = track?.Interpolation ?? default,
                    AxisOrder = track?.AxisOrder ?? 0,
                    ControllerFlags = track?.ControllerFlags ?? 0,
                    Frequency = track?.Frequency ?? 1f,
                    Phase = track?.Phase ?? 0f,
                    TimeStart = track?.TimeStart ?? 0f,
                    TimeStop = track?.TimeStop ?? 0f,
                    FirstKeyIndex = track?.FirstKeyIndex ?? -1,
                    KeyCount = track?.KeyCount ?? 0,
                };
            }
        }

        static void BuildKeys(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var keys = source.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            BlobBuilderArray<ActorAnimationKeyBlob> dst = builder.Allocate(ref root.Keys, keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                dst[i] = new ActorAnimationKeyBlob
                {
                    Time = key.Time,
                    Value = new float4(key.X, key.Y, key.Z, key.W),
                    InTangent = new float4(key.InX, key.InY, key.InZ, key.InW),
                    OutTangent = new float4(key.OutX, key.OutY, key.OutZ, key.OutW),
                };
            }
        }

        static void BuildTextKeys(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var keys = source.TextKeys ?? Array.Empty<ActorAnimationTextKeyDef>();
            BlobBuilderArray<ActorAnimationTextKeyBlob> dst = builder.Allocate(ref root.TextKeys, keys.Length);
            for (int i = 0; i < keys.Length; i++)
                dst[i] = new ActorAnimationTextKeyBlob { Time = keys[i].Time, Text = Fixed128(keys[i].Text) };
        }

        static void BuildTextMarkers(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var markers = source.TextMarkers ?? Array.Empty<ActorAnimationTextMarkerDef>();
            BlobBuilderArray<ActorAnimationTextMarkerBlob> dst = builder.Allocate(ref root.TextMarkers, markers.Length);
            for (int i = 0; i < markers.Length; i++)
            {
                dst[i] = new ActorAnimationTextMarkerBlob
                {
                    Time = markers[i].Time,
                    Group = Fixed64(markers[i].Group),
                    Value = Fixed64(markers[i].Value),
                    Text = Fixed128(markers[i].Text),
                    Kind = markers[i].Kind,
                };
            }
        }

        static void BuildClipHashes(
            ActorAnimationCatalogData source,
            int[] clipRigFamilyIndices,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            BlobBuilderArray<ActorAnimationHashEntryBlob> dst = builder.Allocate(ref root.ClipHashes, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                int rigFamilyIndex = (uint)i < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[i] : -1;
                string modelPath = (uint)rigFamilyIndex < (uint)rigFamilies.Length ? rigFamilies[rigFamilyIndex]?.SkeletonModelPath : string.Empty;
                dst[i] = new ActorAnimationHashEntryBlob
                {
                    Hash = ActorAnimationHash.Clip(modelPath, clips[i]?.Name),
                    ClipIndex = i,
                    RigFamilyIndex = rigFamilyIndex,
                };
            }
        }

        static int[] BuildClipRigFamilyIndices(ActorAnimationCatalogData source)
        {
            int clipCount = source.Clips?.Length ?? 0;
            var result = new int[clipCount];
            Array.Fill(result, -1);
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            for (int rigFamilyIndex = 0; rigFamilyIndex < rigFamilies.Length; rigFamilyIndex++)
            {
                var rigFamily = rigFamilies[rigFamilyIndex];
                int first = rigFamily?.FirstClipIndex ?? -1;
                int count = rigFamily?.ClipCount ?? 0;
                if (first < 0 || count <= 0)
                    continue;

                int end = math.min(clipCount, first + count);
                for (int clipIndex = first; clipIndex < end; clipIndex++)
                    if (result[clipIndex] < 0)
                        result[clipIndex] = rigFamilyIndex;
            }
            return result;
        }

        static int[] BuildTrackTargetBoneIndices(ActorAnimationCatalogData source, int[] clipRigFamilyIndices)
        {
            int trackCount = source.Tracks?.Length ?? 0;
            var result = new int[trackCount];
            Array.Fill(result, -1);

            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                int rigFamilyIndex = (uint)clipIndex < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[clipIndex] : -1;
                int skeletonIndex = (uint)rigFamilyIndex < (uint)rigFamilies.Length ? rigFamilies[rigFamilyIndex]?.SkeletonIndex ?? -1 : -1;
                var bones = ResolveBones(source, skeletonIndex);
                var clip = clips[clipIndex];
                if (clip == null || bones == null || clip.FirstTrackIndex < 0 || clip.TrackCount <= 0)
                    continue;

                int end = math.min(trackCount, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < end; trackIndex++)
                {
                    var track = source.Tracks[trackIndex];
                    int boneIndex = FindBoneIndex(bones, track?.TargetName);
                    if (boneIndex < 0 && IsPoseTrack(track))
                    {
                        throw new InvalidOperationException(
                            $"Actor animation clip '{clip.Name}' in '{clip.SourcePath}' could not resolve pose track target '{track.TargetName}' " +
                            $"against skeleton '{source.Skeletons[skeletonIndex]?.ModelPath ?? "<unknown>"}'.");
                    }

                    result[trackIndex] = boneIndex;
                }
            }
            return result;
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

        static ActorSkeletonBoneDef[] ResolveBones(ActorAnimationCatalogData source, int skeletonIndex)
        {
            var skeletons = source.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            if ((uint)skeletonIndex >= (uint)skeletons.Length)
                return null;
            return skeletons[skeletonIndex]?.Bones;
        }

        static int ResolveSubtreeEnd(ActorSkeletonBoneDef[] bones, int rootIndex)
        {
            if (bones == null || (uint)rootIndex >= (uint)bones.Length)
                return -1;

            int end = rootIndex + 1;
            for (int i = rootIndex + 1; i < bones.Length; i++)
            {
                if (IsDescendantOf(bones, i, rootIndex))
                    end = i + 1;
            }
            return end;
        }

        static bool IsDescendantOf(ActorSkeletonBoneDef[] bones, int boneIndex, int ancestorIndex)
        {
            int parent = bones[boneIndex].ParentIndex;
            while ((uint)parent < (uint)bones.Length)
            {
                if (parent == ancestorIndex)
                    return true;
                parent = bones[parent].ParentIndex;
            }
            return false;
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

        static string CanonicalBoneName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string value = name.Trim().ToLowerInvariant();
            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");

            return value;
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
