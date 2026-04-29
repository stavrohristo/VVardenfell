using Unity.Entities;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
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
            ref var browserState = ref SystemAPI.GetSingletonRW<SaveLoadBrowserState>().ValueRW;
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
                    RuntimeShellStateUtility.CloseModal(ref state);
            }
            else if (state.SaveLoadBrowserOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    RuntimeShellStateUtility.CloseSaveLoadBrowser(ref state, ref browserState);
            }
            else if (state.ContainerOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    ContainerWindowRuntimeUtility.CloseContainer(ref state, ref containerState);
            }
            else if (state.InventoryOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    RuntimeShellStateUtility.CloseInventory(ref state);
            }
            else if (state.PauseMenuOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    RuntimeShellStateUtility.ClosePause(ref state);
            }
            else if (toggleInventoryPressed)
            {
                RuntimeShellStateUtility.OpenInventory(ref state);
            }
            else if (togglePausePressed)
            {
                RuntimeShellStateUtility.OpenPause(ref state);
            }

            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref state);
        }
    }
}

