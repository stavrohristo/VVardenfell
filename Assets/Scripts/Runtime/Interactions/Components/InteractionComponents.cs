using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public enum InteractableKind : byte
    {
        None = 0,
        Door = 1,
        LooseItem = 2,
        Container = 3,
        Activator = 4,
        Npc = 5,
    }

    public struct InteractionActivationProxyState : IComponentData
    {
        public Entity ProxyEntity;
    }

    public struct InteractionActivationProxyBuildPending : IComponentData
    {
    }

    public struct InteractionActivationProxyTag : IComponentData
    {
    }

    public struct DialogueReadinessState : IComponentData
    {
        public Entity PendingTargetEntity;
        public uint PendingTargetPlacedRefId;
        public ActorDefHandle PendingActor;
        public uint LastActivationSequence;
    }

    public struct InteractionRuntimeState : IComponentData
    {
        public uint NextActivationSequence;
        public byte PendingPickedItemPrune;
    }

    public struct ScriptActivationEvent : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public uint Sequence;
        public byte Kind;
    }

    public struct ScriptDefaultActivationRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public uint Sequence;
        public byte Kind;
    }

    public struct ActorForceGreetingRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public struct DoorInteractable : IComponentData
    {
        public byte IsTeleport;
        public FixedString128Bytes DestinationCellId;
        public ulong DestinationCellHash;
        public float3 DestinationPosition;
        public quaternion DestinationRotation;
    }

    public struct DoorActivated : IComponentData, IEnableableComponent
    {
    }

    public struct DoorMotionState : IComponentData
    {
        public float Progress;
        public float TargetProgress;
        public float RangeRadians;
        public float SpeedRadiansPerSecond;
        public byte Axis;
    }

    public struct PlayerInteractionFocus : IComponentData
    {
        public Entity TargetEntity;
        public uint PlacedRefId;
        public byte InteractKind;
        public float HitDistance;
        public byte HasTarget;
    }

    public struct PlayerInteractionRaycastHit : IComponentData
    {
        public uint Sequence;
        public Entity HitEntity;
        public float3 HitPosition;
        public float3 HitNormal;
        public float HitDistance;
        public float HitFraction;
        public byte HasHit;
        public Entity ProxyHitEntity;
        public float3 ProxyHitPosition;
        public float3 ProxyHitNormal;
        public float ProxyHitDistance;
        public float ProxyHitFraction;
        public byte HasProxyHit;
        public Entity SolidHitEntity;
        public float3 SolidHitPosition;
        public float3 SolidHitNormal;
        public float SolidHitDistance;
        public float SolidHitFraction;
        public byte HasSolidHit;
    }

    public struct InteractionActivationRequest : IComponentData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public uint Sequence;
        public byte Kind;
        public byte Pending;
    }

    public struct InteractionActivationResult : IComponentData
    {
        public uint Sequence;
        public byte Kind;
        public byte Success;
        public byte PendingNotification;
        public FixedString128Bytes NotificationText;
    }

    public struct InteractionPresentationState : IComponentData
    {
        public FixedString128Bytes FocusText;
        public FixedString128Bytes NotificationText;
        public float NotificationSecondsRemaining;
        public byte ShowCrosshair;
        public byte ShowFocus;
        public byte ShowNotification;
    }

}
