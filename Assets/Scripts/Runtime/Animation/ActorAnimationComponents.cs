using Unity.Entities;
using Unity.Collections;
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
        UpperBody = Torso | LeftArm | RightArm,
        All = LowerBody | UpperBody,
    }

    public struct ActorPresentation : IComponentData
    {
        public int RigFamilyIndex;
    }

    public struct ActorPresentationEquipmentSignature : IComponentData
    {
        public ulong Value;
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

    public enum ActorAnimationRuntimeMode : byte
    {
        Cpu = 0,
        Gpu = 1,
    }

    public struct ActorAnimationRuntimeSettings : IComponentData
    {
        public ActorAnimationRuntimeMode Mode;
        public byte ValidationEnabled;
        public int ValidationActorIndex;
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
        public int DeformedVertexOffset;
        public int DeformedVertexCount;
        public byte Valid;
    }

    public struct ActorGpuAnimationRequest : IBufferElementData
    {
        public int ClipIndex;
        public ulong ClipHash;
        public int SampleKey;
        public float PreviousTime;
        public float Time;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public float Weight;
        public int Priority;
        public ActorAnimationBlendMask Mask;
        public byte HasPreviousLayer;
    }

    public struct ActorHiddenVisualPartMask : IComponentData
    {
        public uint Mask;
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
        public byte HoldAtStop;
    }

    public struct ActorAnimationState : IComponentData
    {
        public ActorAnimationPlaybackState Playback;
        public ActorAnimationPlaybackState TransitionPlayback;
        public float TransitionTime;
        public float TransitionDuration;
        public byte TransitionActive;
        public byte Initialized;
    }

    public struct ActorJumpAnimationState : IComponentData
    {
        public float AirborneTime;
        public float LandingGroundedTime;
        public byte Phase;
    }

    public enum ActorWeaponAnimationPhase : byte
    {
        Hidden = 0,
        Equipping = 1,
        Equipped = 2,
        AttackWindUp = 3,
        AttackRelease = 4,
        AttackFollow = 5,
    }

    public enum ActorWeaponAttackType : byte
    {
        Chop = 0,
        Slash = 1,
        Thrust = 2,
    }

    public struct ActorWeaponAnimationState : IComponentData
    {
        public ContentReference WeaponContent;
        public int WeaponType;
        public byte Drawn;
        public ActorWeaponAnimationPhase Phase;
        public ActorWeaponAttackType AttackType;
        public float AttackStrength;
        public byte ReadyWeaponTogglePressed;
        public byte AttackHeld;
        public byte AttackPressed;
        public byte AttackReleased;
        public byte ReleaseQueued;
    }

    public struct ActorAnimationOverlayState : IBufferElementData
    {
        public ActorAnimationPlaybackState Playback;
        public float Weight;
        public int Priority;
        public ActorAnimationBlendMask Mask;
    }

    public struct ActorAnimationEvent : IBufferElementData
    {
        public FixedString64Bytes Group;
        public FixedString64Bytes Value;
        public FixedString128Bytes Text;
        public float Time;
        public ActorAnimationTextMarkerKind Kind;
    }

    public static class ActorAnimationPlaybackUtility
    {
        public static bool IsActive(in ActorAnimationPlaybackState playback)
            => playback.Playing != 0 && playback.ClipIndex >= 0;

        public static bool CanSample(in ActorAnimationPlaybackState playback)
            => playback.ClipIndex >= 0;

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
            playback.HoldAtStop = 0;
        }

        public static void StartAt(
            ref ActorAnimationPlaybackState playback,
            ActorAnimationGroupBlob group,
            uint requestedLoopCount,
            float startTime)
        {
            Start(ref playback, group, requestedLoopCount);
            playback.PreviousTime = startTime;
            playback.Time = startTime;
        }

        public static void StartWindow(
            ref ActorAnimationPlaybackState playback,
            ActorAnimationGroupBlob group,
            float startTime,
            float stopTime,
            bool holdAtStop)
        {
            Start(ref playback, group, requestedLoopCount: 0u);
            playback.PreviousTime = startTime;
            playback.Time = startTime;
            playback.StartTime = startTime;
            playback.LoopStartTime = startTime;
            playback.LoopStopTime = stopTime;
            playback.StopTime = stopTime;
            playback.LoopCount = 0u;
            playback.HoldAtStop = holdAtStop ? (byte)1 : (byte)0;
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
            playback.HoldAtStop = 0;
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
                playback.Playing = playback.HoldAtStop != 0 ? (byte)1 : (byte)0;
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

    public struct ActorRenderVisible : IComponentData, IEnableableComponent
    {
    }

    public struct ActorShadowCasterVisible : IComponentData, IEnableableComponent
    {
    }

    public struct ActorRigidEquipmentAttachment : IComponentData
    {
        public Entity Actor;
        public ContentReference Content;
        public ItemEquipmentSlot Slot;
        public int BoneIndex;
        public float3 LocalPosition;
        public quaternion LocalRotation;
        public float LocalScale;
    }

}
