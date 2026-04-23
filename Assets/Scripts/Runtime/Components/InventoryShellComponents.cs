using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public struct PlayerInventoryItem : IBufferElementData
    {
        public ContentReference Content;
        public int Count;
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
        public int Count;
    }

    public struct PickedItemRecord : IBufferElementData
    {
        public uint PlacedRefId;
        public ItemDefHandle Definition;
    }

    public enum RuntimeShellMenuActionId : byte
    {
        None = 0,
        Resume = 1,
        Inventory = 2,
        SaveGame = 3,
        LoadGame = 4,
        Options = 5,
        MainMenu = 6,
        ExitGame = 7,
    }

    public struct RuntimeShellState : IComponentData
    {
        public byte HudVisible;
        public byte InventoryOpen;
        public byte ContainerOpen;
        public byte PauseMenuOpen;
        public byte ModalOpen;
        public byte SelectedAction;
        public FixedString128Bytes ModalTitle;
        public FixedString512Bytes ModalBody;
    }

    public struct RuntimeShellActionRequest : IComponentData
    {
        public byte Pending;
        public byte DismissModal;
        public byte Action;
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
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public int SelectedInventoryIndex;
        public byte ActiveCategory;
        public FixedString64Bytes FilterText;
        public FixedString512Bytes SelectedItemDetailsText;
    }

    public struct InventoryWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
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
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public int SelectedItemIndex;
        public byte PreserveInventoryOnClose;
        public FixedString128Bytes Title;
        public FixedString512Bytes SelectedItemDetailsText;
    }

    public struct ContainerWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte PendingSelectionChange;
        public int SelectedItemIndex;
        public byte PendingTakeSelected;
        public byte PendingTakeAll;
        public byte PendingClose;
    }
}
