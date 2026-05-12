using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Components
{
    public struct PlacedRefOverlayState : IBufferElementData
    {
        public uint PlacedRefId;
        public byte HasRemoved;
        public byte Removed;
        public ContentReference RemovedContent;
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
        public byte HasContainer;
    }

    public struct PlacedRefOverlayScriptInstance : IBufferElementData
    {
        public uint PlacedRefId;
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public FixedString128Bytes DisabledReason;
    }

    public struct PlacedRefOverlayScriptLocalValue : IBufferElementData
    {
        public uint PlacedRefId;
        public int ProgramIndex;
        public int LocalIndex;
        public MorrowindScriptLocalValue Value;
    }

    public struct PlacedRefOverlayActorInventoryItem : IBufferElementData
    {
        public uint PlacedRefId;
        public ActorInventoryItem Item;
    }

    public struct PlacedRefOverlayContainerItem : IBufferElementData
    {
        public uint PlacedRefId;
        public ContainerSessionItem Item;
    }

    public struct PlacedRefOverlayRuntimeIndex : IComponentData
    {
        public NativeParallelHashMap<uint, int> StateByPlacedRefId;
        public NativeParallelMultiHashMap<uint, int> ScriptInstancesByPlacedRefId;
        public NativeParallelMultiHashMap<uint, int> ScriptLocalsByPlacedRefId;
        public NativeParallelMultiHashMap<uint, int> ActorInventoryByPlacedRefId;
        public NativeParallelMultiHashMap<uint, int> ContainerItemsByPlacedRefId;
        public uint Revision;

        public bool IsCreated => StateByPlacedRefId.IsCreated
                                 && ScriptInstancesByPlacedRefId.IsCreated
                                 && ScriptLocalsByPlacedRefId.IsCreated
                                 && ActorInventoryByPlacedRefId.IsCreated
                                 && ContainerItemsByPlacedRefId.IsCreated;

        public static PlacedRefOverlayRuntimeIndex Create(int capacity)
        {
            capacity = math.max(capacity, 16);
            return new PlacedRefOverlayRuntimeIndex
            {
                StateByPlacedRefId = new NativeParallelHashMap<uint, int>(capacity, Allocator.Persistent),
                ScriptInstancesByPlacedRefId = new NativeParallelMultiHashMap<uint, int>(capacity, Allocator.Persistent),
                ScriptLocalsByPlacedRefId = new NativeParallelMultiHashMap<uint, int>(capacity, Allocator.Persistent),
                ActorInventoryByPlacedRefId = new NativeParallelMultiHashMap<uint, int>(capacity, Allocator.Persistent),
                ContainerItemsByPlacedRefId = new NativeParallelMultiHashMap<uint, int>(capacity, Allocator.Persistent),
            };
        }

        public void Dispose()
        {
            if (StateByPlacedRefId.IsCreated)
                StateByPlacedRefId.Dispose();
            if (ScriptInstancesByPlacedRefId.IsCreated)
                ScriptInstancesByPlacedRefId.Dispose();
            if (ScriptLocalsByPlacedRefId.IsCreated)
                ScriptLocalsByPlacedRefId.Dispose();
            if (ActorInventoryByPlacedRefId.IsCreated)
                ActorInventoryByPlacedRefId.Dispose();
            if (ContainerItemsByPlacedRefId.IsCreated)
                ContainerItemsByPlacedRefId.Dispose();
            this = default;
        }
    }

    public struct PlacedRefOverlayIndexDirty : IComponentData, IEnableableComponent
    {
    }

    public struct PlacedRefOverlayProjectionApplied : IComponentData, IEnableableComponent
    {
    }
}
