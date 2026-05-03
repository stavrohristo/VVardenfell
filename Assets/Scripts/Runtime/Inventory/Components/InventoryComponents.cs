using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct PlayerInventoryItem : IBufferElementData
    {
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
    }

    public struct ActorInventoryItem : IBufferElementData
    {
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
        public int AuthoredOrder;
    }

    public struct ActorEquipmentSlot : IBufferElementData
    {
        public ItemEquipmentSlot Slot;
        public ContentReference Content;
        public int InventoryIndex;
        public byte VisualMode;
    }

    public struct ActorInventoryDropRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public ContentReference Content;
        public int Count;
    }

    public struct ActorRigidEquipment : IBufferElementData
    {
        public ItemEquipmentSlot Slot;
        public ContentReference Content;
        public int ModelPrefabIndex;
        public int AttachBoneIndex;
    }

    public struct ContainerSessionHeader : IBufferElementData
    {
        public uint PlacedRefId;
        public ContainerDefHandle Definition;
    }

    public struct ContainerSessionItem : IBufferElementData
    {
        public uint PlacedRefId;
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
    }

    public struct PickedItemRecord : IBufferElementData
    {
        public uint PlacedRefId;
        public ItemDefHandle Definition;
    }

    public enum InventoryWindowCategory : byte
    {
        All = 0,
        Weapons = 1,
        Apparel = 2,
        Magic = 3,
        Misc = 4,
    }

    public struct InventoryWindowState : IComponentData
    {
        public byte Visible;
        public RuntimeWindowRect Rect;
        public int SelectedInventoryIndex;
        public byte ActiveCategory;
        // MW_Window_Pinnable toggle - when 1, the window stays visible on the
        // HUD layer after the player closes the inventory group (vanilla MW
        // "pin" behavior). Controlled via a pin button at top-right of the
        // caption.
        public byte Pinned;
        public FixedString64Bytes FilterText;
        public FixedString512Bytes SelectedItemDetailsText;
    }

    public struct InventoryWindowRequest : IComponentData
    {
        public RuntimeWindowRectRequest RectRequest;
        public byte PendingSelectionChange;
        public int SelectedInventoryIndex;
        public byte PendingCategoryChange;
        public byte ActiveCategory;
        public byte PendingFilterTextChange;
        public FixedString64Bytes FilterText;
    }

    public struct ContainerWindowState : IComponentData
    {
        public byte Visible;
        public uint OpenPlacedRefId;
        public Entity OpenTargetEntity;
        public ContainerDefHandle Definition;
        public RuntimeWindowRect Rect;
        public int SelectedItemIndex;
        public byte PreserveInventoryOnClose;
        public FixedString128Bytes Title;
        public FixedString512Bytes SelectedItemDetailsText;
    }

    public struct ContainerWindowRequest : IComponentData
    {
        public RuntimeWindowRectRequest RectRequest;
        public byte PendingSelectionChange;
        public int SelectedItemIndex;
        public byte PendingTakeSelected;
        public byte PendingTakeAll;
        public byte PendingClose;
    }
}
