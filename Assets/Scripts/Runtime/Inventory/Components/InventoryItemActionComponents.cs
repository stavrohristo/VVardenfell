using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public enum InventoryItemOwnerKind : byte
    {
        None = 0,
        PlayerInventory = 1,
        Container = 2,
    }

    public enum InventoryItemActionKind : byte
    {
        None = 0,
        BeginDrag = 1,
        DirectTransfer = 2,
        DropHeldToInventory = 3,
        DropHeldToContainer = 4,
        UseHeld = 5,
        ClearHeld = 6,
    }

    public struct InventoryItemActionRequest : IComponentData
    {
        public byte Pending;
        public byte Action;
        public byte SourceOwner;
        public byte TargetOwner;
        public int SourceIndex;
        public uint SourcePlacedRefId;
        public uint TargetPlacedRefId;
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
        public uint Sequence;
    }

    public struct InventoryItemActionRequestElement : IBufferElementData
    {
        public byte Action;
        public byte SourceOwner;
        public byte TargetOwner;
        public int SourceIndex;
        public uint SourcePlacedRefId;
        public uint TargetPlacedRefId;
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
        public uint Sequence;
    }

    public struct InventoryHeldItemState : IComponentData
    {
        public byte Active;
        public byte Owner;
        public int InventoryIndex;
        public uint SourcePlacedRefId;
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
        public uint Sequence;
    }
}
