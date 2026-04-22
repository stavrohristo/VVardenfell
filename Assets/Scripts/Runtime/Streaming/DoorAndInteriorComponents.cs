using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Streaming
{
    public enum InteractableKind : byte
    {
        None = 0,
        Door = 1,
    }

    public struct PlacedRefIdentity : IComponentData
    {
        public uint Value;
    }

    public struct LogicalRefTag : IComponentData
    {
    }

    public struct LogicalRefContentRef : IComponentData
    {
        public ContentReference Value;
    }

    public struct LogicalRefLocation : IComponentData
    {
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public byte IsInterior;
    }

    public struct LogicalRefParent : IComponentData
    {
        public Entity Value;
    }

    public struct LogicalRefChild : IBufferElementData
    {
        public Entity Value;
    }

    public struct LogicalRefLookup : IComponentData
    {
        public NativeParallelHashMap<uint, Entity> Map;
    }

    public struct DoorAuthoring : IComponentData
    {
        public DoorDefHandle Definition;
    }

    public struct LightSourceAuthoring : IComponentData
    {
        public LightDefHandle Definition;
    }

    public struct LightInstanceFlags : IComponentData
    {
        public byte Carry;
        public byte Negative;
        public byte Flicker;
        public byte FlickerSlow;
        public byte Pulse;
        public byte PulseSlow;
        public byte OffDefault;
    }

    public struct LightInstanceState : IComponentData
    {
        public byte Enabled;
        public float3 BaseColorRgb;
        public float BaseIntensity;
        public float BaseRange;
        public float CurrentIntensity;
        public float CurrentRange;
        public float AnimationTime;
    }

    public struct LightPresentationLink : IComponentData
    {
        public int Slot;
    }

    public struct ActiveEnvironmentState : IComponentData
    {
        public float3 AmbientColorRgb;
        public float3 DirectionalColorRgb;
        public float3 FogColorRgb;
        public float FogDensity;
        public float FogNearMeters;
        public float FogFarMeters;
        public int RegionHandleValue;
        public byte IsInterior;
    }

    public struct ActorSpawnSource : IComponentData
    {
        public ActorDefHandle Definition;
    }

    public struct ItemPickupAuthoring : IComponentData
    {
        public ItemDefHandle Definition;
    }

    public struct ContainerAuthoring : IComponentData
    {
        public ContainerDefHandle Definition;
    }

    public struct ActivatorAuthoring : IComponentData
    {
        public ActivatorDefHandle Definition;
    }

    public struct DialogueSpeakerAuthoring : IComponentData
    {
        public ActorDefHandle Definition;
    }

    public struct AudioEmitterAuthoring : IComponentData
    {
        public SoundDefHandle PrimarySound;
        public SoundDefHandle SecondarySound;
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

    public struct DoorActivationRequest : IComponentData
    {
        public Entity TargetEntity;
        public byte Pending;
    }

    public struct InteriorTransitionState : IComponentData
    {
        public byte InteriorActive;
        public byte TransitionInProgress;
        public FixedString128Bytes ActiveInteriorCellId;
    }

    public struct InteriorCellMember : IComponentData
    {
    }

    public struct InteriorSpawnedEntity : IBufferElementData
    {
        public Entity Value;
    }
}
