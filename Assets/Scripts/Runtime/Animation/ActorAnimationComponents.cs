using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    [System.Flags]
    public enum ActorAnimationBlendMask : ushort
    {
        LowerBody = 1 << 0,
        Torso = 1 << 1,
        LeftArm = 1 << 2,
        RightArm = 1 << 3,
        Head = 1 << 4,
        All = LowerBody | Torso | LeftArm | RightArm | Head,
    }

    public struct ActorPresentation : IComponentData
    {
        public ActorDefHandle Actor;
        public byte IsNpc;
        public byte IsFemale;
        public byte IsFirstPerson;
        public byte IsCreature;
        public int ModelBindingIndex;
        public int SkeletonIndex;
        public int FirstSkinMeshIndex;
        public int SkinMeshCount;
        public int FirstClipIndex;
        public int ClipCount;
    }

    public struct CPUAnimation : IComponentData, IEnableableComponent
    {
    }

    public struct GPUAnimation : IComponentData, IEnableableComponent
    {
    }

    public struct ActorSkeleton : IComponentData
    {
        public int SkeletonIndex;
        public int BoneCount;
        public int AccumulationBoneIndex;
        public int AccumulationSubtreeEndIndex;
        public int FirstClipIndex;
        public int ClipCount;
    }

    public struct ActorBone : IBufferElementData
    {
        public FixedString64Bytes Name;
        public int ParentIndex;
        public float3 BindPosition;
        public quaternion BindRotation;
        public float BindScale;
        public float3 LocalPosition;
        public quaternion LocalRotation;
        public float LocalScale;
        public float4x4 LocalToRoot;
        public float4x4 SkinMatrix;
    }

    public struct ActorSkinMesh : IBufferElementData
    {
        public int SkinMeshIndex;
        public int MeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public int FirstBoneIndex;
        public int BoneCount;
        public int AttachBoneIndex;
        public byte RigidMirrorX;
    }

    public struct ActorAnimationController : IComponentData
    {
        public FixedString64Bytes RequestedGroup;
        public FixedString64Bytes CurrentGroup;
        public ulong CurrentClipHash;
        public float Time;
        public float Speed;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public uint LoopCount;
        public byte Playing;
        public byte AutoDisable;
        public ActorAnimationBlendMask ActiveMask;
    }

    public struct ActorAnimationLayer : IBufferElementData
    {
        public FixedString64Bytes Group;
        public int ClipIndex;
        public ulong ClipHash;
        public float Time;
        public float Weight;
        public int Priority;
        public ActorAnimationBlendMask Mask;
    }

    public struct ActorSampledBonePose : IBufferElementData
    {
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public float3 AxisRotation;
        public byte AxisFlags;
        public int AxisOrder;
    }

    public struct ActorGpuAnimationRequest : IBufferElementData
    {
        public int ClipIndex;
        public ulong ClipHash;
        public float Time;
        public float Weight;
        public ActorAnimationBlendMask Mask;
    }

    public struct ActorGpuAnimationState : IComponentData
    {
        public int SkeletonIndex;
        public int LayerOffset;
        public int LayerCount;
        public int SkinMeshOffset;
        public int SkinMeshCount;
        public int BoneMatrixOffset;
        public int BoneMatrixCount;
    }

    public struct ActorGpuAnimationCpuFallback : IComponentData
    {
        public byte RequiresFullPoseSampling;
        public byte RequiresRootMotion;
        public byte RequiresAttachments;
    }

    public struct ActorLocalBounds : IComponentData
    {
        public float3 Center;
        public float3 Extents;
    }

    public struct ActorAttachmentBoneAnimation : IComponentData, IEnableableComponent
    {
    }

    public struct ActorAttachmentBone : IBufferElementData
    {
        public int BoneIndex;
    }

    public enum ActorAnimationLodOverrideMode : byte
    {
        None = 0,
        ForceCpu = 1,
        ForceGpu = 2,
    }

    public struct ActorAnimationLodSettings : IComponentData
    {
        public float CpuNearDistance;
        public ActorAnimationLodOverrideMode OverrideMode;
        public byte ValidationEnabled;
        public int ValidationActorIndex;
    }

    public struct ActorProceduralRenderState : IComponentData
    {
        public int BoneMatrixOffset;
        public int BoneMatrixCount;
        public int DrawStart;
        public int DrawCount;
        public uint Version;
    }

    public struct ActorRenderVisible : IComponentData, IEnableableComponent
    {
    }

    public struct ActorProceduralDraw : IBufferElementData
    {
        public int SkinMeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public int BoneMatrixOffset;
        public int BoneMatrixCount;
        public int DrawIndexCount;
        public int DrawVertexCount;
        public int AttachBoneIndex;
        public byte RigidMirrorX;
    }

    public struct ActorRigidEquipmentAttachment : IComponentData
    {
        public Entity Actor;
        public int BoneIndex;
        public float3 LocalPosition;
        public quaternion LocalRotation;
        public float LocalScale;
    }

    public struct ActorAnimationState : IComponentData
    {
        public float2 LocalMove;
        public byte IsMoving;
        public byte IsRunning;
        public byte IsSneaking;
        public byte IsSwimming;
        public byte IsJumping;
        public byte IsDead;
    }

    public struct ActorAnimationEvent : IBufferElementData
    {
        public FixedString64Bytes Group;
        public FixedString64Bytes Value;
        public FixedString128Bytes Text;
        public float Time;
        public ActorAnimationTextMarkerKind Kind;
    }

    public struct ActorRootMotion : IComponentData
    {
        public float3 Delta;
        public quaternion DeltaRotation;
        public float3 PreviousAccumulationPosition;
        public quaternion PreviousAccumulationRotation;
        public FixedString64Bytes LastGroup;
        public float LastTime;
        public byte HasDelta;
        public byte Initialized;
    }

    public struct ActorAnimationEventCursor : IComponentData
    {
        public FixedString64Bytes LastGroup;
        public float LastTime;
    }

    public struct ActorAnimationPoseDirty : IComponentData, IEnableableComponent
    {
    }
}
