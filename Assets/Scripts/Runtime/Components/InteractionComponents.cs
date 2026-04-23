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

    public struct DoorInteractable : IComponentData
    {
        public byte IsTeleport;
        public FixedString128Bytes DestinationCellId;
        public float3 DestinationPosition;
        public quaternion DestinationRotation;
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

    public struct InteractionDiagnosticsState : IComponentData
    {
        public uint SnapshotSequence;
        public uint LastFixedTick;
        public uint LastRaycastSequence;
        public uint LastActivationRequestSequence;
        public uint LastActivationResultSequence;
        public byte LastHadRaycastHit;
        public byte LastHadFocus;
        public byte LastRequestPending;
        public byte LastResultSuccess;
    }

    public struct InteractionDiagnosticsSnapshot : IComponentData
    {
        public uint SnapshotSequence;
        public uint FixedTick;
        public uint RaycastSequence;
        public byte HasPrimaryHit;
        public Entity PrimaryHitEntity;
        public float PrimaryHitDistance;
        public byte HasProxyHit;
        public Entity ProxyHitEntity;
        public float ProxyHitDistance;
        public byte HasSolidHit;
        public Entity SolidHitEntity;
        public float SolidHitDistance;

        public byte HasFocus;
        public Entity FocusEntity;
        public uint FocusPlacedRefId;
        public byte FocusKind;
        public float FocusDistance;
        public FixedString128Bytes FocusLabel;

        public byte RequestPending;
        public uint RequestSequence;
        public Entity RequestTargetEntity;
        public uint RequestPlacedRefId;
        public byte RequestKind;

        public uint ResultSequence;
        public byte ResultKind;
        public byte ResultSuccess;
        public byte ResultPendingNotification;
        public FixedString128Bytes ResultText;

        public FixedString128Bytes PresentationFocusText;
        public FixedString128Bytes PresentationNotificationText;
        public byte PresentationShowFocus;
        public byte PresentationShowNotification;
    }
}
