using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(SaveLoadBrowserActionSystem))]
    public partial class RuntimeShellActionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeShellActionRequest>();
            RequireForUpdate<SaveLoadBrowserState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<StatsWindowState>();
            RequireForUpdate<SpellWindowState>();
            RequireForUpdate<MapWindowState>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<RuntimeShellActionRequest>().ValueRW;
            ref var browser = ref SystemAPI.GetSingletonRW<SaveLoadBrowserState>().ValueRW;

            if (request.DismissModal != 0)
            {
                request.DismissModal = 0;
                RuntimeShellStateUtility.ClearModal(ref state);
            }

            // MW_Window_Pinnable toggle — flips the Pinned byte on the selected
            // inventory-group subwindow's singleton state. The presentation
            // system reads this combined with InventoryOpen to decide which
            // subwindows stay visible on the HUD layer after the group closes.
            if (request.PendingPinToggle != 0)
            {
                request.PendingPinToggle = 0;
                var target = (RuntimeShellPinnableWindow)request.PinWindow;
                request.PinWindow = 0;
                switch (target)
                {
                    case RuntimeShellPinnableWindow.Inventory:
                        ref var inv = ref SystemAPI.GetSingletonRW<InventoryWindowState>().ValueRW;
                        inv.Pinned = (byte)(inv.Pinned == 0 ? 1 : 0);
                        break;
                    case RuntimeShellPinnableWindow.Stats:
                        ref var stats = ref SystemAPI.GetSingletonRW<StatsWindowState>().ValueRW;
                        stats.Pinned = (byte)(stats.Pinned == 0 ? 1 : 0);
                        break;
                    case RuntimeShellPinnableWindow.Spell:
                        ref var spell = ref SystemAPI.GetSingletonRW<SpellWindowState>().ValueRW;
                        spell.Pinned = (byte)(spell.Pinned == 0 ? 1 : 0);
                        break;
                    case RuntimeShellPinnableWindow.Map:
                        ref var map = ref SystemAPI.GetSingletonRW<MapWindowState>().ValueRW;
                        map.Pinned = (byte)(map.Pinned == 0 ? 1 : 0);
                        break;
                }
            }

            // Options window has its own close path (footer "Close" button + Escape)
            // rather than going through the pause-menu action pipeline — the Options
            // window is visually a modal over the pause backdrop, so we drain the
            // dedicated CloseOptions flag here and restore pause-menu focus on the
            // same frame.
            if (request.CloseOptions != 0)
            {
                request.CloseOptions = 0;
                if (state.OptionsOpen != 0)
                {
                    state.OptionsOpen = 0;
                    state.PauseMenuOpen = 1;
                }
            }

            if (request.Pending == 0)
            {
                RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref state);
                return;
            }

            var action = (RuntimeShellMenuActionId)request.Action;
            request.Pending = 0;
            request.Action = 0;

            if (action == RuntimeShellMenuActionId.None)
            {
                RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref state);
                return;
            }

            state.SelectedAction = (byte)action;

            switch (action)
            {
                case RuntimeShellMenuActionId.Resume:
                    RuntimeShellStateUtility.ClosePause(ref state);
                    break;

                case RuntimeShellMenuActionId.Inventory:
                    state.PauseMenuOpen = 0;
                    state.InventoryOpen = 1;
                    RuntimeShellStateUtility.ClearModal(ref state);
                    break;

                case RuntimeShellMenuActionId.SaveGame:
                    RuntimeShellStateUtility.OpenSaveLoadBrowser(ref state, ref browser, SaveLoadBrowserMode.Save, "New Save");
                    break;

                case RuntimeShellMenuActionId.LoadGame:
                    RuntimeShellStateUtility.OpenSaveLoadBrowser(ref state, ref browser, SaveLoadBrowserMode.Load, string.Empty);
                    break;

                case RuntimeShellMenuActionId.Options:
                    // Options mirrors Inventory/SaveLoad: pause menu hides while the
                    // modal-ish Options window is up, restored on close by the
                    // CloseOptions flag above.
                    state.PauseMenuOpen = 0;
                    state.OptionsOpen = 1;
                    RuntimeShellStateUtility.ClearModal(ref state);
                    break;

                case RuntimeShellMenuActionId.MainMenu:
                    RuntimeShellStateUtility.OpenSaveLoadBrowser(ref state, ref browser, SaveLoadBrowserMode.MainMenuConfirm, string.Empty);
                    browser.ConfirmAction = (byte)SaveLoadBrowserPendingAction.Confirm;
                    browser.ConfirmationText = RuntimeShellStateUtility.ToFixedBody("Return to the main menu? Unsaved progress will be lost.");
                    break;

                case RuntimeShellMenuActionId.ExitGame:
                    Application.Quit();
                    break;
            }

            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref state);
        }
    }
}
