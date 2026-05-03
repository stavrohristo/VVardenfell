using System;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellInputSystem))]
    [UpdateBefore(typeof(RuntimeShellActionSystem))]
    public partial class SaveLoadBrowserActionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<SaveLoadBrowserState>();
            RequireForUpdate<SaveLoadBrowserRequest>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var browser = ref SystemAPI.GetSingletonRW<SaveLoadBrowserState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<SaveLoadBrowserRequest>().ValueRW;

            if (request.Pending == 0)
                return;

            HandleSaveLoadBrowserRequest(ref state, ref browser, ref request);
            request.Pending = 0;
            request.Action = 0;
            request.SlotId = default;
            request.SaveName = default;
        }

        void HandleSaveLoadBrowserRequest(
            ref RuntimeShellState state,
            ref SaveLoadBrowserState browser,
            ref SaveLoadBrowserRequest request)
        {
            var action = (SaveLoadBrowserPendingAction)request.Action;
            switch (action)
            {
                case SaveLoadBrowserPendingAction.SelectSlot:
                    browser.SelectedSlotId = request.SlotId;
                    browser.StatusText = default;
                    break;

                case SaveLoadBrowserPendingAction.SetName:
                    browser.DraftSaveName = request.SaveName;
                    break;

                case SaveLoadBrowserPendingAction.NewSave:
                    if (WorldSaveStorage.TryWriteNewSlot(EntityManager, request.SaveName.ToString(), out var newSummary, out string newError))
                    {
                        browser.SelectedSlotId = RuntimeShellStateUtility.ToFixedSlotId(newSummary.SlotId);
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Game saved.");
                    }
                    else
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(newError);
                    }
                    break;

                case SaveLoadBrowserPendingAction.Overwrite:
                    if (string.IsNullOrWhiteSpace(request.SlotId.ToString()))
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Select a save slot to overwrite.");
                    }
                    else
                    {
                        browser.ConfirmAction = (byte)SaveLoadBrowserPendingAction.Overwrite;
                        browser.SelectedSlotId = request.SlotId;
                        browser.ConfirmationText = RuntimeShellStateUtility.ToFixedBody("Overwrite the selected save?");
                    }
                    break;

                case SaveLoadBrowserPendingAction.Load:
                    if (string.IsNullOrWhiteSpace(request.SlotId.ToString()))
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Select a save slot to load.");
                    }
                    else if (!IsSlotLoadable(request.SlotId.ToString(), out string loadabilityError))
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(loadabilityError);
                    }
                    else
                    {
                        browser.ConfirmAction = (byte)SaveLoadBrowserPendingAction.Load;
                        browser.SelectedSlotId = request.SlotId;
                        browser.ConfirmationText = RuntimeShellStateUtility.ToFixedBody("Load the selected save? Unsaved progress will be lost.");
                    }
                    break;

                case SaveLoadBrowserPendingAction.Delete:
                    if (string.IsNullOrWhiteSpace(request.SlotId.ToString()))
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Select a save slot to delete.");
                    }
                    else
                    {
                        browser.ConfirmAction = (byte)SaveLoadBrowserPendingAction.Delete;
                        browser.SelectedSlotId = request.SlotId;
                        browser.ConfirmationText = RuntimeShellStateUtility.ToFixedBody("Delete the selected save?");
                    }
                    break;

                case SaveLoadBrowserPendingAction.Confirm:
                    ConfirmSaveLoadBrowserAction(ref state, ref browser);
                    break;

                case SaveLoadBrowserPendingAction.CancelConfirm:
                    browser.ConfirmAction = 0;
                    browser.ConfirmationText = default;
                    break;

                case SaveLoadBrowserPendingAction.Cancel:
                    RuntimeShellStateUtility.CloseSaveLoadBrowser(ref state, ref browser);
                    break;
            }
        }

        void ConfirmSaveLoadBrowserAction(ref RuntimeShellState state, ref SaveLoadBrowserState browser)
        {
            var action = (SaveLoadBrowserPendingAction)browser.ConfirmAction;
            string slotId = browser.SelectedSlotId.ToString();
            browser.ConfirmAction = 0;
            browser.ConfirmationText = default;

            switch (action)
            {
                case SaveLoadBrowserPendingAction.Overwrite:
                    if (WorldSaveStorage.TryOverwriteSlot(EntityManager, slotId, browser.DraftSaveName.ToString(), out _, out string overwriteError))
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Save overwritten.");
                    else
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(overwriteError);
                    break;

                case SaveLoadBrowserPendingAction.Load:
                    if (!IsSlotLoadable(slotId, out string loadabilityError))
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(loadabilityError);
                    }
                    else if (WorldSaveReplayUtility.TryRestoreSlotInPlace(World, EntityManager, slotId, out string loadError))
                    {
                        state.InventoryOpen = 0;
                        state.ContainerOpen = 0;
                        RuntimeShellStateUtility.CloseSaveLoadBrowser(ref state, ref browser);
                        RuntimeShellStateUtility.ClosePause(ref state);
                    }
                    else
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(loadError);
                    }
                    break;

                case SaveLoadBrowserPendingAction.Delete:
                    if (WorldSaveStorage.TryDeleteSlot(slotId, out string deleteError))
                    {
                        browser.SelectedSlotId = default;
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus("Save deleted.");
                    }
                    else
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(deleteError);
                    }
                    break;

                case SaveLoadBrowserPendingAction.Confirm:
                    if (BootstrapController.TryShowRuntimeMainMenu(out string menuError))
                    {
                        RuntimeShellStateUtility.CloseSaveLoadBrowser(ref state, ref browser);
                        state.PauseMenuOpen = 0;
                    }
                    else
                    {
                        browser.StatusText = RuntimeShellStateUtility.ToFixedStatus(menuError);
                    }
                    break;
            }
        }

        static bool IsSlotLoadable(string slotId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                error = "Select a save slot to load.";
                return false;
            }

            var slots = WorldSaveStorage.EnumerateSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (!string.Equals(slots[i].SlotId, slotId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!slots[i].IsValid)
                {
                    error = string.IsNullOrWhiteSpace(slots[i].Error)
                        ? "The selected save slot is invalid."
                        : $"The selected save slot is invalid: {slots[i].Error}";
                    return false;
                }

                return true;
            }

            error = "Save slot no longer exists.";
            return false;
        }
    }
}

