using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
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
        public FixedString64Bytes FilterText;
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
        public byte PendingFilterTextChange;
        public FixedString64Bytes FilterText;
    }

    public enum MapWindowMode : byte
    {
        Local = 0,
        Global = 1,
    }

    public struct MapWindowState : IComponentData
    {
        public byte Visible;
        public byte Mode;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte Pinned;
        public float LocalPanX;
        public float LocalPanY;
        public float LocalZoom;
        public float GlobalPanX;
        public float GlobalPanY;
        public float GlobalZoom;
    }

    public struct MapWindowRequest : IComponentData
    {
        public byte PendingRectUpdate;
        public float NormalizedX;
        public float NormalizedY;
        public float NormalizedWidth;
        public float NormalizedHeight;
        public byte PendingModeChange;
        public byte Mode;
        public byte PendingViewportChange;
        public byte ViewportMode;
        public float PanX;
        public float PanY;
        public float Zoom;
        public byte PendingCenterOnPlayer;
    }
}
