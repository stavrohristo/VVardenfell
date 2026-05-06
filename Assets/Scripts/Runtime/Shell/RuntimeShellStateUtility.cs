using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    static class RuntimeShellStateUtility
    {
        const float SubtitleMinSeconds = 1.5f;
        const float SubtitleMaxSeconds = 12f;
        const float SubtitleLeadInSeconds = 0.6f;
        const float SubtitleWordsPerSecond = 2.6f;

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
            if (state.InventoryMenuDisabled != 0)
                return;

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

        public static void OpenRestMenu(ref RuntimeShellState state, Entity bedEntity, uint bedPlacedRefId, bool canSleep)
        {
            if (state.RestDisabled != 0)
            {
                ShowMessageBox(ref state, "You cannot rest now.");
                return;
            }

            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 0;
            state.SaveLoadBrowserOpen = 0;
            state.OptionsOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            ClearModal(ref state);

            state.RestMenuOpen = 1;
            state.RestMenuCanSleep = canSleep ? (byte)1 : (byte)0;
            state.RestMenuAdvancing = 0;
            state.RestMenuSleeping = 0;
            state.RestMenuSelectedHours = 1;
            state.RestMenuProgressHours = 0;
            state.RestMenuTargetHours = 0;
            state.RestMenuBedEntity = bedEntity;
            state.RestMenuBedPlacedRefId = bedPlacedRefId;
            state.PlayerSleeping = 0;
        }

        public static void CloseRestMenu(ref RuntimeShellState state)
        {
            state.RestMenuOpen = 0;
            state.RestMenuCanSleep = 0;
            state.RestMenuAdvancing = 0;
            state.RestMenuSleeping = 0;
            state.RestMenuSelectedHours = 1;
            state.RestMenuProgressHours = 0;
            state.RestMenuTargetHours = 0;
            state.RestMenuBedEntity = Entity.Null;
            state.RestMenuBedPlacedRefId = 0u;
            state.PlayerSleeping = 0;
        }

        public static void OpenMovie(ref RuntimeShellState state, FixedString128Bytes movieName, bool allowSkipping)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 0;
            state.SaveLoadBrowserOpen = 0;
            state.OptionsOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            CloseRestMenu(ref state);
            ClearModal(ref state);

            state.MovieOpen = 1;
            state.MovieAllowSkipping = allowSkipping ? (byte)1 : (byte)0;
            state.MovieName = movieName;
        }

        public static void CloseMovie(ref RuntimeShellState state)
        {
            state.MovieOpen = 0;
            state.MovieAllowSkipping = 0;
            state.MovieName = default;
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
            state.ModalButtonCount = 0;
            state.ModalTitle = default;
            state.ModalBody = default;
            state.ModalButton0 = default;
            state.ModalButton1 = default;
            state.ModalButton2 = default;
            state.ModalButton3 = default;
            state.ModalButton4 = default;
            state.ModalButton5 = default;
            state.ModalButton6 = default;
            state.ModalButton7 = default;
            state.ModalButton8 = default;
            state.ModalButton9 = default;
        }

        public static void CloseModal(ref RuntimeShellState state)
        {
            ClearModal(ref state);
        }

        public static void ShowDialog(ref RuntimeShellState state, string title, string body)
            => ShowDialog(ref state, ToFixedTitle(title), ToFixedBody(body));

        public static void ShowDialog(ref RuntimeShellState state, FixedString128Bytes title, FixedString512Bytes body)
        {
            state.InventoryOpen = 0;
            state.ContainerOpen = 0;
            state.PauseMenuOpen = 1;
            state.SaveLoadBrowserOpen = 0;
            state.JournalOpen = 0;
            state.DialogueOpen = 0;
            state.ModalOpen = 1;
            state.ModalTitle = title;
            state.ModalBody = body;
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
            state.ModalButtonPressedValid = 0;
            state.ModalButtonPressed = -1;
            state.ModalTitle = default;
            state.ModalBody = body;
            state.ModalButtonCount = 0;
            state.ModalButton0 = default;
            state.ModalButton1 = default;
            state.ModalButton2 = default;
            state.ModalButton3 = default;
            state.ModalButton4 = default;
            state.ModalButton5 = default;
            state.ModalButton6 = default;
            state.ModalButton7 = default;
            state.ModalButton8 = default;
            state.ModalButton9 = default;
        }

        public static void ShowMessageBox(ref RuntimeShellState state, in ShellMessageBoxRequest request, FixedString512Bytes body)
        {
            ShowMessageBox(ref state, body);
            state.ModalButtonCount = request.ButtonCount;
            state.ModalButton0 = request.Button0;
            state.ModalButton1 = request.Button1;
            state.ModalButton2 = request.Button2;
            state.ModalButton3 = request.Button3;
            state.ModalButton4 = request.Button4;
            state.ModalButton5 = request.Button5;
            state.ModalButton6 = request.Button6;
            state.ModalButton7 = request.Button7;
            state.ModalButton8 = request.Button8;
            state.ModalButton9 = request.Button9;
        }

        public static void ShowSubtitle(ref RuntimeSubtitleState state, FixedString512Bytes text, float seconds)
        {
            if (text.IsEmpty)
                return;

            state.Text = text;
            state.SecondsRemaining = math.max(0.1f, seconds);
            state.Visible = 1;
        }

        public static void ActivateHitOverlay(ref RuntimeShellState state)
        {
            state.HitOverlayAlpha = 1f;
            state.HitOverlayDuration = 0.5f;
            state.HitOverlayElapsed = 0f;
        }

        public static float EstimateSubtitleDurationSeconds(FixedString512Bytes text)
        {
            int wordCount = CountSubtitleWords(text);
            if (wordCount == 0)
                return 0f;

            return math.clamp(
                SubtitleLeadInSeconds + wordCount / SubtitleWordsPerSecond,
                SubtitleMinSeconds,
                SubtitleMaxSeconds);
        }

        static int CountSubtitleWords(FixedString512Bytes text)
        {
            if (text.Length == 0)
                return 0;

            int count = 0;
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsAsciiWhiteSpace(text[i]))
                {
                    inWord = false;
                    continue;
                }

                if (inWord)
                    continue;

                count++;
                inWord = true;
            }

            return count;
        }

        static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v';

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
                || state.DialogueOpen != 0
                || state.RestMenuOpen != 0
                || state.RestMenuAdvancing != 0
                || state.MovieOpen != 0
                || state.PlayerControlsDisabled != 0;

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
