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
        public byte SaveLoadBrowserOpen;
        public byte OptionsOpen;
        public byte SelectedAction;
        public FixedString128Bytes ModalTitle;
        public FixedString512Bytes ModalBody;
    }

    public struct RuntimeShellActionRequest : IComponentData
    {
        public byte Pending;
        public byte DismissModal;
        public byte CloseOptions;
        public byte Action;
        public byte PendingPinToggle;
        public byte PinWindow;
    }

    /// <summary>
    /// Selects which pinnable window a <c>TogglePin</c> request targets. Mirrors
    /// vanilla MW's MW_Window_Pinnable subwindows of the inventory group.
    /// </summary>
    public enum RuntimeShellPinnableWindow : byte
    {
        None = 0,
        Inventory = 1,
        Stats = 2,
        Spell = 3,
        Map = 4,
    }

    public enum SaveLoadBrowserMode : byte
    {
        None = 0,
        Save = 1,
        Load = 2,
        MainMenuConfirm = 3,
    }

    public enum SaveLoadBrowserPendingAction : byte
    {
        None = 0,
        SelectSlot = 1,
        NewSave = 2,
        Overwrite = 3,
        Load = 4,
        Delete = 5,
        Cancel = 6,
        Confirm = 7,
        CancelConfirm = 8,
        SetName = 9,
    }

    public struct SaveLoadBrowserState : IComponentData
    {
        public byte Visible;
        public byte Mode;
        public byte ConfirmAction;
        public byte Busy;
        public FixedString128Bytes SelectedSlotId;
        public FixedString64Bytes DraftSaveName;
        public FixedString128Bytes StatusText;
        public FixedString512Bytes ConfirmationText;
    }

    public struct SaveLoadBrowserRequest : IComponentData
    {
        public byte Pending;
        public byte Action;
        public FixedString128Bytes SlotId;
        public FixedString64Bytes SaveName;
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
        // MW_Window_Pinnable toggle — when 1, the window stays visible on the
        // HUD layer after the player closes the inventory group (vanilla MW
        // "pin" behavior). Controlled via a pin button at top-right of the
        // caption.
        public byte Pinned;
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

    public struct StatsWindowState : IComponentData
    {
        public byte Visible;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte Pinned;
    }

    public struct StatsWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
    }

    public struct SpellWindowState : IComponentData
    {
        public byte Visible;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public int SelectedSpellIndex;
        public byte Pinned;
    }

    public struct SpellWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte PendingSelectionChange;
        public int SelectedSpellIndex;
    }

    public struct MapWindowState : IComponentData
    {
        public byte Visible;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte Pinned;
    }

    public struct MapWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
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
