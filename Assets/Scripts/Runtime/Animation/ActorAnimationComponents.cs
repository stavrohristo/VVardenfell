using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Animation
{
    [System.Flags]
    public enum ActorAnimationBlendMask : ushort
    {
        LowerBody = 1 << 0,
        Torso = 1 << 1,
        LeftArm = 1 << 2,
        RightArm = 1 << 3,
        UpperBody = Torso | LeftArm | RightArm,
        All = LowerBody | UpperBody,
    }

    public struct ActorPresentation : IComponentData
    {
        public int RigFamilyIndex;
    }

    public struct ActorSkeleton : IComponentData
    {
        public int SkeletonIndex;
        public int BoneCount;
        public int AccumulationBoneIndex;
    }

    public struct ActorBone : IBufferElementData
    {
        public float3 LocalPosition;
        public quaternion LocalRotation;
        public float LocalScale;
        public byte LocalPoseAnimated;
        public float4x4 LocalToRoot;
    }

    public struct ActorSkinMesh : IBufferElementData
    {
        public int SkinMeshIndex;
        public int AttachBoneIndex;
        public byte RigidMirrorX;
    }

    public struct ActorAnimationState : IComponentData
    {
        public ulong GroupHash;
        public ulong ClipHash;
        public int ClipIndex;
        public float PreviousTime;
        public float Time;
        public float Speed;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public uint LoopCount;
        public byte Playing;
        public byte Initialized;
    }

    public struct ActorAnimationOverlayState : IBufferElementData
    {
        public ulong GroupHash;
        public ulong ClipHash;
        public int ClipIndex;
        public float PreviousTime;
        public float Time;
        public float Speed;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public float Weight;
        public int Priority;
        public uint LoopCount;
        public ActorAnimationBlendMask Mask;
        public byte Playing;
    }

    public struct ActorSampledBonePose : IBufferElementData
    {
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public float3 AxisRotation;
        public byte AxisFlags;
        public byte HasTrack;
        public int AxisOrder;
    }

    public struct ActorLocalBounds : IComponentData
    {
        public float3 Center;
        public float3 Extents;
    }

    public struct ActorAttachmentBone : IBufferElementData
    {
        public int BoneIndex;
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

}
