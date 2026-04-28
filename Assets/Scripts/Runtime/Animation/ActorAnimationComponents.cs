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

    public struct ActorAnimationPlaybackState
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
    }

    public struct ActorAnimationState : IComponentData
    {
        public ActorAnimationPlaybackState Playback;
        public byte Initialized;
    }

    public struct ActorAnimationOverlayState : IBufferElementData
    {
        public ActorAnimationPlaybackState Playback;
        public float Weight;
        public int Priority;
        public ActorAnimationBlendMask Mask;
    }

    public static class ActorAnimationPlaybackUtility
    {
        public static bool IsActive(in ActorAnimationPlaybackState playback)
            => playback.Playing != 0 && playback.ClipIndex >= 0;

        public static bool Matches(in ActorAnimationPlaybackState playback, ulong groupHash, ulong clipHash)
            => playback.Playing != 0 && playback.GroupHash == groupHash && playback.ClipHash == clipHash;

        public static void Start(
            ref ActorAnimationPlaybackState playback,
            ActorAnimationGroupBlob group,
            uint requestedLoopCount)
        {
            playback.GroupHash = group.GroupHash;
            playback.ClipHash = group.ClipHash;
            playback.ClipIndex = group.ClipIndex;
            playback.PreviousTime = group.StartTime;
            playback.Time = group.StartTime;
            playback.Speed = playback.Speed < 0f ? 0f : playback.Speed;
            playback.StartTime = group.StartTime;
            playback.LoopStartTime = group.LoopStartTime;
            playback.LoopStopTime = group.LoopStopTime;
            playback.StopTime = group.StopTime;
            playback.LoopCount = group.Looping != 0 ? requestedLoopCount : 0u;
            playback.Playing = 1;
        }

        public static void Clear(ref ActorAnimationPlaybackState playback)
        {
            playback.Playing = 0;
            playback.ClipIndex = -1;
            playback.GroupHash = 0UL;
            playback.ClipHash = 0UL;
            playback.PreviousTime = 0f;
            playback.Time = 0f;
            playback.StartTime = 0f;
            playback.LoopStartTime = 0f;
            playback.LoopStopTime = 0f;
            playback.StopTime = 0f;
            playback.LoopCount = 0u;
        }

        public static void Advance(ref ActorAnimationPlaybackState playback, float deltaTime, uint infiniteLoops)
        {
            if (!IsActive(playback))
                return;

            float speed = playback.Speed <= 0f ? 1f : playback.Speed;
            playback.PreviousTime = playback.Time;
            float nextTime = playback.Time + deltaTime * speed;
            bool canLoop = playback.LoopCount > 0 && playback.LoopStopTime > playback.LoopStartTime;
            if (canLoop && nextTime >= playback.LoopStopTime)
            {
                if (playback.LoopCount != infiniteLoops)
                    playback.LoopCount--;
                float duration = playback.LoopStopTime - playback.LoopStartTime;
                playback.Time = playback.LoopStartTime + math.fmod(nextTime - playback.LoopStartTime, duration);
                return;
            }

            playback.Time = nextTime >= playback.StopTime ? playback.StopTime : nextTime;
            if (playback.Time >= playback.StopTime)
                playback.Playing = 0;
        }
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

    public struct ActorShadowCasterVisible : IComponentData, IEnableableComponent
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
