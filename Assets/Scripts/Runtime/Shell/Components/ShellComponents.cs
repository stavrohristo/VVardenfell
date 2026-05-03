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

    public enum RuntimeShellRestMenuActionId : byte
    {
        None = 0,
        SetHours = 1,
        Start = 2,
        UntilHealed = 3,
        Cancel = 4,
    }

    public struct RuntimeShellState : IComponentData
    {
        public byte HudVisible;
        public byte InventoryOpen;
        public byte ContainerOpen;
        public byte PauseMenuOpen;
        public byte ModalOpen;
        public byte ModalButtonCount;
        public byte ModalButtonPressedValid;
        public byte SaveLoadBrowserOpen;
        public byte OptionsOpen;
        public byte JournalOpen;
        public byte DialogueOpen;
        public byte PlayerSleeping;
        public byte SelectedAction;
        public byte PlayerControlsDisabled;
        public byte PlayerFightingDisabled;
        public byte PlayerJumpingDisabled;
        public byte PlayerMagicDisabled;
        public byte PlayerViewSwitchDisabled;
        public byte VanityModeDisabled;
        public byte RestDisabled;
        public byte TeleportingDisabled;
        public byte InventoryMenuDisabled;
        public byte StatsMenuDisabled;
        public byte MagicMenuDisabled;
        public byte MapMenuDisabled;
        public byte NameMenuDisabled;
        public byte RaceMenuDisabled;
        public byte ClassMenuDisabled;
        public byte BirthMenuDisabled;
        public byte StatReviewMenuDisabled;
        public byte RestMenuOpen;
        public byte RestMenuCanSleep;
        public byte RestMenuAdvancing;
        public byte RestMenuSleeping;
        public byte MovieOpen;
        public byte MovieAllowSkipping;
        public float ScreenFadeAlpha;
        public float ScreenFadeStartAlpha;
        public float ScreenFadeTargetAlpha;
        public float ScreenFadeDuration;
        public float ScreenFadeElapsed;
        public int ModalButtonPressed;
        public int RestMenuSelectedHours;
        public int RestMenuProgressHours;
        public int RestMenuTargetHours;
        public Entity RestMenuBedEntity;
        public uint RestMenuBedPlacedRefId;
        public FixedString128Bytes MovieName;
        public FixedString128Bytes ModalTitle;
        public FixedString512Bytes ModalBody;
        public FixedString128Bytes ModalButton0;
        public FixedString128Bytes ModalButton1;
        public FixedString128Bytes ModalButton2;
        public FixedString128Bytes ModalButton3;
        public FixedString128Bytes ModalButton4;
        public FixedString128Bytes ModalButton5;
        public FixedString128Bytes ModalButton6;
        public FixedString128Bytes ModalButton7;
        public FixedString128Bytes ModalButton8;
        public FixedString128Bytes ModalButton9;
    }

    public struct RuntimeShellActionRequest : IComponentData
    {
        public byte Pending;
        public byte DismissModal;
        public int DismissModalButton;
        public byte CloseOptions;
        public byte CloseJournal;
        public byte CloseDialogue;
        public byte CloseMovie;
        public byte Action;
        public byte PendingPinToggle;
        public byte PinWindow;
        public byte RestMenuAction;
        public int RestMenuHours;
    }

    public struct ShellMessageBoxRequest : IBufferElementData
    {
        public FixedString512Bytes Body;
        public byte ButtonCount;
        public byte ArgCount;
        public byte Arg0Kind;
        public byte Arg1Kind;
        public byte Arg2Kind;
        public byte Arg3Kind;
        public byte Arg4Kind;
        public byte Arg5Kind;
        public byte Arg6Kind;
        public byte Arg7Kind;
        public int Arg0Int;
        public int Arg1Int;
        public int Arg2Int;
        public int Arg3Int;
        public int Arg4Int;
        public int Arg5Int;
        public int Arg6Int;
        public int Arg7Int;
        public float Arg0Float;
        public float Arg1Float;
        public float Arg2Float;
        public float Arg3Float;
        public float Arg4Float;
        public float Arg5Float;
        public float Arg6Float;
        public float Arg7Float;
        public FixedString128Bytes Button0;
        public FixedString128Bytes Button1;
        public FixedString128Bytes Button2;
        public FixedString128Bytes Button3;
        public FixedString128Bytes Button4;
        public FixedString128Bytes Button5;
        public FixedString128Bytes Button6;
        public FixedString128Bytes Button7;
        public FixedString128Bytes Button8;
        public FixedString128Bytes Button9;
    }

    public struct GlobalMapRevealRequest : IBufferElementData
    {
        public FixedString512Bytes CellNamePrefix;
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

    public struct RuntimeWindowRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    public struct RuntimeWindowRectRequest
    {
        public byte Pending;
        public RuntimeWindowRect Rect;
    }

    public struct StatsWindowState : IComponentData
    {
        public byte Visible;
        public RuntimeWindowRect Rect;
        public byte Pinned;
    }

    public struct StatsWindowRequest : IComponentData
    {
        public RuntimeWindowRectRequest RectRequest;
    }

    public struct SpellWindowState : IComponentData
    {
        public byte Visible;
        public RuntimeWindowRect Rect;
        public int SelectedSpellIndex;
        public byte Pinned;
        public FixedString64Bytes FilterText;
    }

    public struct SpellWindowRequest : IComponentData
    {
        public RuntimeWindowRectRequest RectRequest;
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
        public RuntimeWindowRect Rect;
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
        public RuntimeWindowRectRequest RectRequest;
        public byte PendingModeChange;
        public byte Mode;
        public byte PendingViewportChange;
        public byte ViewportMode;
        public float PanX;
        public float PanY;
        public float Zoom;
        public byte PendingCenterOnPlayer;
    }

    public struct JournalWindowState : IComponentData
    {
        public byte Visible;
        public byte ShowAll;
        public byte Mode;
        public byte OverlayOpen;
        public RuntimeWindowRect Rect;
        public int SelectedDialogueIndex;
        public int Page;
        public float QuestScrollY;
        public float EntryScrollY;
    }

    public struct JournalWindowRequest : IComponentData
    {
        public RuntimeWindowRectRequest RectRequest;
        public byte PendingShowAllChange;
        public byte ShowAll;
        public byte PendingSelectionChange;
        public int SelectedDialogueIndex;
        public byte PendingModeChange;
        public byte Mode;
        public byte PendingOverlayChange;
        public byte OverlayOpen;
        public byte PendingPageChange;
        public int Page;
        public byte PendingScrollChange;
        public float QuestScrollY;
        public float EntryScrollY;
    }
}
