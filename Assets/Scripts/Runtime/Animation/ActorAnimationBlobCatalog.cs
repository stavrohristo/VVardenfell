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
        public BlobArray<ActorModelGraphNodeBlob> GraphNodes;
        public BlobArray<ActorSkeletonBlob> Skeletons;
        public BlobArray<ActorSkeletonBoneBlob> Bones;
        public BlobArray<ActorSkinMeshBlob> SkinMeshes;
        public BlobArray<ActorSkinVertexBlob> SkinVertices;
        public BlobArray<int> SkinIndices;
        public BlobArray<ActorSkinBoneBlob> SkinBones;
        public BlobArray<ActorAnimationClipBlob> Clips;
        public BlobArray<ActorAnimationTrackBlob> Tracks;
        public BlobArray<ActorAnimationKeyBlob> Keys;
        public BlobArray<ActorAnimationGroupBlob> Groups;
        public BlobArray<ActorRigFamilyAnimationIndexBlob> RigFamilyAnimationIndexes;
        public BlobArray<ActorAnimationGroupLookupBlob> GroupLookups;
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
        public int FirstGraphNodeIndex;
        public int GraphNodeCount;
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
        public ulong NameHash;
        public int ParentIndex;
        public int SourceRecordIndex;
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
        public int SourceRecordIndex;
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
        public ActorRigAssemblyKind RigAssemblyKind;
        public int SourceGraphNodeIndex;
        public int SkinRootSourceRecordIndex;
        public int SkinRootGraphNodeIndex;
        public int CopiedRigRootGraphNodeIndex;
        public int InsertedParentGraphNodeIndex;
        public int CancelledTransformGraphNodeIndex;
        public int SourceSkeletonRootGraphNodeIndex;
        public int TargetBoneIndex;
        public byte AssemblyMirrorX;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public float4x4 GeometryToSkeleton;
        public float3 RigidOffset;
    }

    public struct ActorSkinBoneBlob
    {
        public FixedString64Bytes Name;
        public int BoneIndex;
        public int SourceRecordIndex;
        public float4x4 BindPose;
    }

    public struct ActorModelGraphNodeBlob
    {
        public FixedString64Bytes Name;
        public int ParentIndex;
        public int SourceRecordIndex;
        public ModelPrefabNodeKind Kind;
        public ushort Flags;
        public float4x4 LocalMatrix;
        public float4x4 LocalToRootMatrix;
        public float4x4 SourceLocalToRootMatrix;
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
        public ushort BlendMask;
    }

    public struct ActorAnimationKeyBlob
    {
        public float Time;
        public float4 Value;
        public float4 InTangent;
        public float4 OutTangent;
    }

    public struct ActorAnimationGroupBlob
    {
        public ulong GroupHash;
        public ulong ClipHash;
        public int ClipIndex;
        public int RigFamilyIndex;
        public float Velocity;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public byte Looping;
    }

    public struct ActorRigFamilyAnimationIndexBlob
    {
        public int FirstGroupLookupIndex;
        public int GroupLookupCount;
    }

    public struct ActorAnimationGroupLookupBlob
    {
        public ulong GroupHash;
        public int GroupIndex;
    }

    public static class ActorAnimationGroupHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Hash(FixedString64Bytes value)
        {
            ulong hash = Offset;
            for (int i = 0; i < value.Length; i++)
            {
                byte c = value[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= Prime;
            }

            return hash;
        }
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
}
