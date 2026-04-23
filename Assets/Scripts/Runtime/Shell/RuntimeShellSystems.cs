using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial class RuntimeShellBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<RuntimeShellState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.RuntimeShell");
            }

            EnsureComponent(runtimeEntity, new RuntimeShellState
            {
                HudVisible = 1,
                SelectedAction = (byte)RuntimeShellMenuActionId.Resume,
            });
            EnsureComponent(runtimeEntity, new RuntimeShellActionRequest());
            EnsureComponent(runtimeEntity, new InventoryWindowState
            {
                NormalizedX = 0.015f,
                NormalizedY = 0.54f,
                NormalizedWidth = 0.45f,
                NormalizedHeight = 0.38f,
                SelectedInventoryIndex = -1,
                ActiveCategory = (byte)InventoryWindowCategory.All,
            });
            EnsureComponent(runtimeEntity, new InventoryWindowRequest());
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    public partial class RuntimeShellStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            state.HudVisible = (byte)(BootstrapPresentationGate.BlocksGameplayInput ? 0 : 1);

            if (state.SelectedAction == 0)
                state.SelectedAction = (byte)RuntimeShellMenuActionId.Resume;

            if (state.PauseMenuOpen == 0 && state.ModalOpen != 0)
                ClearModal(ref state);

            if (state.ContainerOpen != 0)
            {
                state.InventoryOpen = 1;
                state.PauseMenuOpen = 0;
            }
            else if (state.InventoryOpen != 0 && state.PauseMenuOpen != 0)
                state.InventoryOpen = 0;
        }

        static void ClearModal(ref RuntimeShellState state)
        {
            state.ModalOpen = 0;
            state.ModalTitle = default;
            state.ModalBody = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(PlayerInputReceivingSystem))]
    public partial class RuntimeShellInputSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
        }

        protected override void OnStartRunning()
        {
            RuntimeShellPresentationGate.BlocksGameplayInput = false;
        }

        protected override void OnStopRunning()
        {
            RuntimeShellPresentationGate.BlocksGameplayInput = false;
        }

        protected override void OnUpdate()
        {
            if (BootstrapPresentationGate.BlocksGameplayInput)
            {
                RuntimeShellPresentationGate.BlocksGameplayInput = false;
                return;
            }

            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var containerState = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            bool togglePausePressed = (keyboard?.escapeKey.wasPressedThisFrame ?? false)
                || (gamepad?.startButton.wasPressedThisFrame ?? false);
            bool backPressed = gamepad?.buttonEast.wasPressedThisFrame ?? false;
            bool toggleInventoryPressed = (keyboard?.tabKey.wasPressedThisFrame ?? false)
                || (keyboard?.iKey.wasPressedThisFrame ?? false)
                || (gamepad?.buttonWest.wasPressedThisFrame ?? false);

            if (state.ModalOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    CloseModal(ref state);
            }
            else if (state.ContainerOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    ContainerWindowRuntimeUtility.CloseContainer(ref state, ref containerState);
            }
            else if (state.InventoryOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    CloseInventory(ref state);
            }
            else if (state.PauseMenuOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    ClosePause(ref state);
            }
            else if (toggleInventoryPressed)
            {
                OpenInventory(ref state);
            }
            else if (togglePausePressed)
            {
                OpenPause(ref state);
            }

            RuntimeShellPresentationGate.BlocksGameplayInput = state.InventoryOpen != 0 || state.ContainerOpen != 0 || state.PauseMenuOpen != 0 || state.ModalOpen != 0;
        }

        static void OpenPause(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            state.PauseMenuOpen = 1;
            state.SelectedAction = (byte)RuntimeShellMenuActionId.Resume;
            CloseModal(ref state);
        }

        static void OpenInventory(ref RuntimeShellState state)
        {
            state.InventoryOpen = 1;
            state.PauseMenuOpen = 0;
            state.SelectedAction = (byte)RuntimeShellMenuActionId.Inventory;
            CloseModal(ref state);
        }

        static void CloseInventory(ref RuntimeShellState state)
        {
            state.InventoryOpen = 0;
            CloseModal(ref state);
        }

        static void ClosePause(ref RuntimeShellState state)
        {
            state.PauseMenuOpen = 0;
            CloseModal(ref state);
        }

        static void CloseModal(ref RuntimeShellState state)
        {
            state.ModalOpen = 0;
            state.ModalTitle = default;
            state.ModalBody = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellInputSystem))]
    public partial class RuntimeShellActionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeShellActionRequest>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<RuntimeShellActionRequest>().ValueRW;

            if (request.DismissModal != 0)
            {
                request.DismissModal = 0;
                CloseModal(ref state);
            }

            if (request.Pending == 0)
                return;

            var action = (RuntimeShellMenuActionId)request.Action;
            request.Pending = 0;
            request.Action = 0;

            if (action == RuntimeShellMenuActionId.None)
                return;

            state.SelectedAction = (byte)action;

            switch (action)
            {
                case RuntimeShellMenuActionId.Resume:
                    ClosePause(ref state);
                    break;

                case RuntimeShellMenuActionId.Inventory:
                    state.PauseMenuOpen = 0;
                    state.InventoryOpen = 1;
                    CloseModal(ref state);
                    break;

                case RuntimeShellMenuActionId.SaveGame:
                    if (WorldSaveStorage.TryWriteContinueSave(EntityManager, out string saveError))
                    {
                        ShowDialog(
                            ref state,
                            "Game Saved",
                            "Continue save updated successfully.");
                    }
                    else
                    {
                        ShowDialog(
                            ref state,
                            "Save Failed",
                            string.IsNullOrWhiteSpace(saveError)
                                ? "The continue save could not be written."
                                : saveError);
                    }
                    break;

                case RuntimeShellMenuActionId.LoadGame:
                    ShowDialog(
                        ref state,
                        "Load Game Unavailable",
                        "Load Game belongs to the future Save/Load milestone and is not wired into the runtime shell yet.");
                    break;

                case RuntimeShellMenuActionId.Options:
                    ShowDialog(
                        ref state,
                        "Options Deferred",
                        "Options belongs to the broader Core UI Shell work and is not implemented in this first in-world shell slice.");
                    break;

                case RuntimeShellMenuActionId.MainMenu:
                    ShowDialog(
                        ref state,
                        "Main Menu Deferred",
                        "Returning from the world back to bootstrap is deferred for a later runtime shell pass.");
                    break;

                case RuntimeShellMenuActionId.ExitGame:
                    Application.Quit();
                    break;
            }
        }

        static void ShowDialog(ref RuntimeShellState state, string title, string body)
        {
            state.InventoryOpen = 0;
            state.PauseMenuOpen = 1;
            state.ModalOpen = 1;
            state.ModalTitle = ToFixedTitle(title);
            state.ModalBody = ToFixedBody(body);
        }

        static void ClosePause(ref RuntimeShellState state)
        {
            state.PauseMenuOpen = 0;
            CloseModal(ref state);
        }

        static void CloseModal(ref RuntimeShellState state)
        {
            state.ModalOpen = 0;
            state.ModalTitle = default;
            state.ModalBody = default;
        }

        static FixedString128Bytes ToFixedTitle(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            if (value.Length > 127)
                value = value.Substring(0, 127);

            return new FixedString128Bytes(value);
        }

        static FixedString512Bytes ToFixedBody(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            if (value.Length > 511)
                value = value.Substring(0, 511);

            return new FixedString512Bytes(value);
        }
    }

    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class RuntimeHudShellPresentationSystem : SystemBase
    {
        RuntimeHudShellView _view;
        bool _creationFailed;

        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InteractionPresentationState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<ContainerSessionItem>();
        }

        protected override void OnDestroy()
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);
            _view = null;
            RuntimeShellPresentationGate.BlocksGameplayInput = false;
        }

        protected override void OnUpdate()
        {
            if (_view == null && !_creationFailed)
            {
                try
                {
                    _view = RuntimeHudShellView.Create();
                }
                catch (Exception ex)
                {
                    _creationFailed = true;
                    Debug.LogError($"[VVardenfell][UI] failed creating runtime HUD shell: {ex.Message}");
                    return;
                }
            }

            if (_view == null)
                return;

            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            var interaction = SystemAPI.GetSingleton<InteractionPresentationState>();
            var inventoryState = SystemAPI.GetSingleton<InventoryWindowState>();
            var containerState = SystemAPI.GetSingleton<ContainerWindowState>();
            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            var containerItems = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            InventoryWindowViewModel inventoryModel = shell.InventoryOpen != 0
                ? BuildInventoryModel(RuntimeContentDatabase.Active, inventoryState, inventory)
                : null;
            ContainerWindowViewModel containerModel = shell.ContainerOpen != 0
                ? BuildContainerModel(RuntimeContentDatabase.Active, containerState, containerItems)
                : null;
            bool visible = !BootstrapPresentationGate.BlocksGameplayInput;
            bool showHud = shell.HudVisible != 0 && shell.InventoryOpen == 0 && shell.ContainerOpen == 0 && shell.PauseMenuOpen == 0 && shell.ModalOpen == 0;

            _view.Sync(
                visible,
                showHud,
                interaction.ShowCrosshair != 0,
                interaction.ShowFocus != 0 ? interaction.FocusText.ToString() : null,
                interaction.ShowNotification != 0 ? interaction.NotificationText.ToString() : null,
                inventoryModel,
                containerModel,
                (RuntimeShellMenuActionId)shell.SelectedAction,
                shell.PauseMenuOpen != 0,
                shell.ModalOpen != 0,
                shell.ModalTitle.ToString(),
                shell.ModalBody.ToString());
        }

        static InventoryWindowViewModel BuildInventoryModel(
            RuntimeContentDatabase contentDb,
            in InventoryWindowState state,
            DynamicBuffer<PlayerInventoryItem> inventory)
        {
            var viewModel = new InventoryWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Inventory",
                Category = (InventoryWindowCategory)state.ActiveCategory,
                FilterText = state.FilterText.ToString(),
                ArmorSummary = "Armor Rating  --\nEquipment preview deferred",
                DetailText = string.IsNullOrWhiteSpace(state.SelectedItemDetailsText.ToString())
                    ? "Select an item to inspect."
                    : state.SelectedItemDetailsText.ToString(),
                WeightBarFillNormalized = inventory.Length > 0 ? 0.46f : 0.16f,
            };

            var entries = new List<InventoryWindowEntryViewModel>(inventory.Length);
            int visibleCount = 0;
            float totalWeight = 0f;
            bool hasWeightData = false;

            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                int count = Math.Max(1, entry.Count);
                if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                {
                    if (metadata.Weight >= 0f)
                    {
                        totalWeight += metadata.Weight * count;
                        hasWeightData = true;
                    }
                }

                if (!InventoryWindowStateSystem.MatchesFilters(contentDb, entry, state))
                    continue;

                visibleCount++;
                entries.Add(BuildEntryViewModel(contentDb, entry, i, i == state.SelectedInventoryIndex));
            }

            viewModel.WeightLabel = hasWeightData
                ? $"Weight {totalWeight:0.0} / --"
                : "Weight -- / --";
            viewModel.DetailText = visibleCount == 0
                ? "No items match the current filter."
                : viewModel.DetailText;
            viewModel.Entries = entries.ToArray();
            return viewModel;
        }

        static InventoryWindowEntryViewModel BuildEntryViewModel(RuntimeContentDatabase contentDb, PlayerInventoryItem entry, int inventoryIndex, bool selected)
        {
            string name = "Unknown item";
            string iconPath = string.Empty;
            string weightText = "--";
            string valueText = "--";

            if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
            {
                name = metadata.DisplayName;
                iconPath = metadata.IconPath ?? string.Empty;
                if (metadata.Weight >= 0f)
                    weightText = metadata.Weight.ToString("0.0");
                if (metadata.Value >= 0)
                    valueText = metadata.Value.ToString();
            }

            return new InventoryWindowEntryViewModel
            {
                InventoryIndex = inventoryIndex,
                Name = name,
                IconPath = iconPath,
                CountText = Math.Max(1, entry.Count).ToString(),
                WeightText = weightText,
                ValueText = valueText,
                Selected = selected,
            };
        }

        static ContainerWindowViewModel BuildContainerModel(
            RuntimeContentDatabase contentDb,
            in ContainerWindowState state,
            DynamicBuffer<ContainerSessionItem> items)
        {
            var viewModel = new ContainerWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = string.IsNullOrWhiteSpace(state.Title.ToString()) ? "Container" : state.Title.ToString(),
                DetailText = string.IsNullOrWhiteSpace(state.SelectedItemDetailsText.ToString())
                    ? "Container is empty."
                    : state.SelectedItemDetailsText.ToString(),
            };

            var entries = new List<InventoryWindowEntryViewModel>();
            for (int i = 0; i < items.Length; i++)
            {
                var entry = items[i];
                if (entry.PlacedRefId != state.OpenPlacedRefId || entry.Count <= 0)
                    continue;

                entries.Add(BuildContainerEntryViewModel(contentDb, entry, i, i == state.SelectedItemIndex));
            }

            viewModel.CanTakeSelected = state.SelectedItemIndex >= 0 && state.SelectedItemIndex < items.Length
                && items[state.SelectedItemIndex].PlacedRefId == state.OpenPlacedRefId
                && items[state.SelectedItemIndex].Count > 0;
            viewModel.CanTakeAll = entries.Count > 0;
            viewModel.EmptyStateText = entries.Count == 0 ? "Empty" : string.Empty;
            viewModel.Entries = entries.ToArray();
            return viewModel;
        }

        static InventoryWindowEntryViewModel BuildContainerEntryViewModel(RuntimeContentDatabase contentDb, ContainerSessionItem entry, int itemIndex, bool selected)
        {
            string name = "Unknown item";
            string iconPath = string.Empty;
            string weightText = "--";
            string valueText = "--";

            if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
            {
                name = metadata.DisplayName;
                iconPath = metadata.IconPath ?? string.Empty;
                if (metadata.Weight >= 0f)
                    weightText = metadata.Weight.ToString("0.0");
                if (metadata.Value >= 0)
                    valueText = metadata.Value.ToString();
            }

            return new InventoryWindowEntryViewModel
            {
                InventoryIndex = itemIndex,
                Name = name,
                IconPath = iconPath,
                CountText = Math.Max(1, entry.Count).ToString(),
                WeightText = weightText,
                ValueText = valueText,
                Selected = selected,
            };
        }
    }
}
