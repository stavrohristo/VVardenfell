using Unity.Collections;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    static class RuntimeShellStateUtility
    {
        public static void OpenPause(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            state.PauseMenuOpen = 1;
            state.SelectedAction = (byte)RuntimeShellMenuActionId.Resume;
            ClearModal(ref state);
        }

        public static void OpenInventory(ref RuntimeShellState state)
        {
            state.InventoryOpen = 1;
            state.PauseMenuOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            state.SelectedAction = (byte)RuntimeShellMenuActionId.Inventory;
            ClearModal(ref state);
        }

        public static void OpenJournal(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 0;
            state.SaveLoadBrowserOpen = 0;
            state.OptionsOpen = 0;
            state.DialogueOpen = 0;
            state.JournalOpen = 1;
            ClearModal(ref state);
        }

        public static void CloseInventory(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            ClearModal(ref state);
        }

        public static void ClosePause(ref RuntimeShellState state)
        {
            state.PauseMenuOpen = 0;
            ClearModal(ref state);
        }

        public static void CloseJournal(ref RuntimeShellState state)
        {
            state.JournalOpen = 0;
            ClearModal(ref state);
        }

        public static void OpenDialogue(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 0;
            state.SaveLoadBrowserOpen = 0;
            state.OptionsOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 1;
            ClearModal(ref state);
        }

        public static void CloseDialogue(ref RuntimeShellState state)
        {
            state.DialogueOpen = 0;
            ClearModal(ref state);
        }

        public static void CloseSaveLoadBrowser(ref RuntimeShellState shell, ref SaveLoadBrowserState browser)
        {
            shell.SaveLoadBrowserOpen = 0;
            browser.Visible = 0;
            browser.ConfirmAction = 0;
            browser.ConfirmationText = default;
        }

        public static void ClearModal(ref RuntimeShellState state)
        {
            state.ModalOpen = 0;
            state.ModalTitle = default;
            state.ModalBody = default;
        }

        public static void CloseModal(ref RuntimeShellState state)
        {
            ClearModal(ref state);
        }

        public static void ShowDialog(ref RuntimeShellState state, string title, string body)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 1;
            state.SaveLoadBrowserOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            state.ModalOpen = 1;
            state.ModalTitle = ToFixedTitle(title);
            state.ModalBody = ToFixedBody(body);
        }

        public static void ShowMessageBox(ref RuntimeShellState state, string body)
            => ShowMessageBox(ref state, ToFixedBody(body));

        public static void ShowMessageBox(ref RuntimeShellState state, FixedString512Bytes body)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 1;
            state.SaveLoadBrowserOpen = 0;
            state.OptionsOpen = 0;
            state.ModalOpen = 1;
            state.ModalTitle = default;
            state.ModalBody = body;
        }

        public static void OpenSaveLoadBrowser(ref RuntimeShellState state, ref SaveLoadBrowserState browser, SaveLoadBrowserMode mode, string saveName)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 1;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            state.ModalOpen = 0;
            state.SaveLoadBrowserOpen = 1;
            browser.Visible = 1;
            browser.Mode = (byte)mode;
            browser.ConfirmAction = 0;
            browser.StatusText = default;
            browser.ConfirmationText = default;
            if (!string.IsNullOrWhiteSpace(saveName))
                browser.DraftSaveName = ToFixedName(saveName);
        }

        public static void SyncGameplayGateAndCursor(ref RuntimeShellState state)
        {
            bool shellBlocksGameplay = state.InventoryOpen != 0
                || state.ContainerOpen != 0
                || state.PauseMenuOpen != 0
                || state.ModalOpen != 0
                || state.SaveLoadBrowserOpen != 0
                || state.OptionsOpen != 0
                || state.JournalOpen != 0
                || state.DialogueOpen != 0;

            RuntimeShellPresentationGate.BlocksGameplayInput = !BootstrapPresentationGate.BlocksGameplayInput && shellBlocksGameplay;
            ApplyCursorState(!GameplayInputGate.BlocksGameplayInput);
        }

        static void ApplyCursorState(bool gameplayInputAllowed)
        {
            if (gameplayInputAllowed)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.Locked;
                if (Cursor.visible)
                    Cursor.visible = false;
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.None)
                    Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible)
                    Cursor.visible = true;
            }
        }

        public static FixedString64Bytes ToFixedName(string value)
        {
            return RuntimeFixedStringUtility.ToFixed64OrDefault(value);
        }

        public static FixedString128Bytes ToFixedSlotId(string value)
        {
            return RuntimeFixedStringUtility.ToFixed128OrDefault(value);
        }

        public static FixedString128Bytes ToFixedStatus(string value)
        {
            return RuntimeFixedStringUtility.ToFixed128OrDefault(value);
        }

        public static FixedString128Bytes ToFixedTitle(string value)
        {
            return RuntimeFixedStringUtility.ToFixed128OrDefault(value);
        }

        public static FixedString512Bytes ToFixedBody(string value)
        {
            return RuntimeFixedStringUtility.ToFixed512OrDefault(value);
        }
    }
}
