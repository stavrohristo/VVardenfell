using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
