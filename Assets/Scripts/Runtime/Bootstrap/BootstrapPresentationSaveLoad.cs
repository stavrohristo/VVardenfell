using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView
    {
        void HandleMenuSaveLoadInput()
        {
            if (!_menuSaveLoadVisible || _phase != PresentationPhase.Menu)
                return;

            bool cancelPressed = (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
            if (!cancelPressed)
                return;

            if (_menuSaveLoadConfirmAction != SaveLoadBrowserPendingAction.None)
                CancelMenuSaveLoadConfirm();
            else
                CloseMenuSaveLoadBrowser();
        }


        void BuildMenuSaveLoadBrowserView()
        {
            _menuSaveLoadBrowser = new SaveLoadBrowserView(
                _menuRoot,
                _theme,
                OnMenuSaveLoadSelectSlot,
                _ => { },
                () => { },
                () => { },
                OnMenuSaveLoadLoad,
                OnMenuSaveLoadDelete,
                CloseMenuSaveLoadBrowser,
                ConfirmMenuSaveLoadAction,
                CancelMenuSaveLoadConfirm);
        }


        void OpenMenuSaveLoadBrowser()
        {
            CloseMenuDialog();
            _menuSaveLoadVisible = true;
            _menuSaveLoadSelectedSlotId = string.Empty;
            _menuSaveLoadStatus = string.Empty;
            _menuSaveLoadConfirmation = string.Empty;
            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.None;
            SyncMenuSaveLoadBrowser();

            if (_eventSystem != null)
                _eventSystem.SetSelectedGameObject(null);
        }

        void CloseMenuSaveLoadBrowser()
        {
            _menuSaveLoadVisible = false;
            _menuSaveLoadConfirmation = string.Empty;
            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.None;
            _menuSaveLoadBrowser?.Sync(null);
            RestoreMenuSelection();
        }

        void OnMenuSaveLoadSelectSlot(string slotId)
        {
            _menuSaveLoadSelectedSlotId = slotId ?? string.Empty;
            _menuSaveLoadStatus = string.Empty;
            _menuSaveLoadConfirmation = string.Empty;
            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.None;
            SyncMenuSaveLoadBrowser();
        }

        void OnMenuSaveLoadLoad()
        {
            if (!IsMenuSaveLoadSlotLoadable(_menuSaveLoadSelectedSlotId, out string error))
            {
                _menuSaveLoadStatus = error;
                SyncMenuSaveLoadBrowser();
                return;
            }

            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.Load;
            _menuSaveLoadConfirmation = "Load the selected save? Unsaved progress will be lost.";
            SyncMenuSaveLoadBrowser();
        }

        void OnMenuSaveLoadDelete()
        {
            if (!TryGetMenuSaveLoadSlot(_menuSaveLoadSelectedSlotId, out var slot))
            {
                _menuSaveLoadStatus = "Select a save slot to delete.";
                SyncMenuSaveLoadBrowser();
                return;
            }

            if (slot.IsLegacy)
            {
                _menuSaveLoadStatus = "The legacy continue payload cannot be deleted from the save browser.";
                SyncMenuSaveLoadBrowser();
                return;
            }

            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.Delete;
            _menuSaveLoadConfirmation = "Delete the selected save?";
            SyncMenuSaveLoadBrowser();
        }

        void ConfirmMenuSaveLoadAction()
        {
            var action = _menuSaveLoadConfirmAction;
            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.None;
            _menuSaveLoadConfirmation = string.Empty;

            switch (action)
            {
                case SaveLoadBrowserPendingAction.Load:
                    if (!IsMenuSaveLoadSlotLoadable(_menuSaveLoadSelectedSlotId, out string loadableError))
                    {
                        _menuSaveLoadStatus = loadableError;
                        break;
                    }

                    if (GameInitializationRequestBridge.TryRequestLoadGame(_menuSaveLoadSelectedSlotId, out string loadError))
                    {
                        Dismiss();
                        return;
                    }

                    _menuSaveLoadStatus = loadError;
                    break;

                case SaveLoadBrowserPendingAction.Delete:
                    if (WorldSaveStorage.TryDeleteSlot(_menuSaveLoadSelectedSlotId, out string deleteError))
                    {
                        _menuSaveLoadSelectedSlotId = string.Empty;
                        _menuSaveLoadStatus = "Save deleted.";
                        RefreshMenuButtons();
                    }
                    else
                    {
                        _menuSaveLoadStatus = deleteError;
                    }
                    break;
            }

            SyncMenuSaveLoadBrowser();
        }

        void CancelMenuSaveLoadConfirm()
        {
            _menuSaveLoadConfirmAction = SaveLoadBrowserPendingAction.None;
            _menuSaveLoadConfirmation = string.Empty;
            SyncMenuSaveLoadBrowser();
        }

        void SyncMenuSaveLoadBrowser()
        {
            if (_menuSaveLoadBrowser == null)
                return;

            if (!_menuSaveLoadVisible || _phase != PresentationPhase.Menu)
            {
                _menuSaveLoadBrowser.Sync(null);
                return;
            }

            _menuSaveLoadBrowser.Sync(BuildMenuSaveLoadBrowserModel());
        }

        SaveLoadBrowserViewModel BuildMenuSaveLoadBrowserModel()
        {
            var summaries = WorldSaveStorage.EnumerateSlots();
            var rows = new SaveSlotRowViewModel[summaries.Length];
            bool selectedValid = false;
            bool selectedLegacy = false;
            for (int i = 0; i < summaries.Length; i++)
            {
                var slot = summaries[i];
                bool selected = string.Equals(_menuSaveLoadSelectedSlotId, slot.SlotId, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    selectedValid = slot.IsValid;
                    selectedLegacy = slot.IsLegacy;
                }

                rows[i] = new SaveSlotRowViewModel
                {
                    SlotId = slot.SlotId ?? string.Empty,
                    Name = string.IsNullOrWhiteSpace(slot.DisplayName) ? "Save" : slot.DisplayName,
                    TimestampText = slot.LastModifiedUtcTicks > 0
                        ? new DateTime(slot.LastModifiedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "--",
                    CharacterText = $"{(string.IsNullOrWhiteSpace(slot.CharacterName) ? "Player" : slot.CharacterName)}  Lv {Math.Max(1, slot.PlayerLevel)}",
                    LocationText = string.IsNullOrWhiteSpace(slot.LocationName) ? "--" : slot.LocationName,
                    VersionText = slot.IsValid ? $"v{slot.PayloadVersion}" : "Invalid",
                    Valid = slot.IsValid,
                    Legacy = slot.IsLegacy,
                    Selected = selected,
                    ErrorText = slot.Error ?? string.Empty,
                };
            }

            bool hasSelection = !string.IsNullOrWhiteSpace(_menuSaveLoadSelectedSlotId);
            return new SaveLoadBrowserViewModel
            {
                Mode = SaveLoadBrowserMode.Load,
                Title = "Load Game",
                DraftSaveName = string.Empty,
                StatusText = _menuSaveLoadStatus,
                ConfirmationText = _menuSaveLoadConfirmation,
                Confirming = _menuSaveLoadConfirmAction != SaveLoadBrowserPendingAction.None,
                PrimaryButtonText = "Load",
                CanPrimary = hasSelection && selectedValid,
                CanOverwrite = false,
                CanDelete = hasSelection && !selectedLegacy,
                Slots = rows,
            };
        }

        bool IsMenuSaveLoadSlotLoadable(string slotId, out string error)
        {
            error = null;
            if (!TryGetMenuSaveLoadSlot(slotId, out var slot))
            {
                error = "Select a save slot to load.";
                return false;
            }

            if (!slot.IsValid)
            {
                error = string.IsNullOrWhiteSpace(slot.Error)
                    ? "The selected save slot is invalid."
                    : $"The selected save slot is invalid: {slot.Error}";
                return false;
            }

            return true;
        }

        bool TryGetMenuSaveLoadSlot(string slotId, out SaveGameSlotSummary slot)
        {
            slot = default;
            if (string.IsNullOrWhiteSpace(slotId))
                return false;

            var slots = WorldSaveStorage.EnumerateSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (!string.Equals(slots[i].SlotId, slotId, StringComparison.OrdinalIgnoreCase))
                    continue;

                slot = slots[i];
                return true;
            }

            return false;
        }


    }
}
