using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Components
{
    public struct PlacedRefSavedState : IBufferElementData
    {
        public uint PlacedRefId;
        public byte HasDisabled;
        public byte Disabled;
        public byte HasLock;
        public int LockLevel;
        public byte Locked;
        public FixedString64Bytes KeyId;
        public FixedString64Bytes TrapId;
        public byte HasTransform;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct PlacedRefSavedScriptInstance : IBufferElementData
    {
        public uint PlacedRefId;
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public FixedString128Bytes DisabledReason;
    }

    public struct PlacedRefSavedScriptLocalValue : IBufferElementData
    {
        public uint PlacedRefId;
        public int ProgramIndex;
        public int LocalIndex;
        public MorrowindScriptLocalValue Value;
    }

    public struct PlacedRefSavedActorInventoryItem : IBufferElementData
    {
        public uint PlacedRefId;
        public ActorInventoryItem Item;
    }

    public struct PlacedRefSavedStateProjectionApplied : IComponentData
    {
    }
}
