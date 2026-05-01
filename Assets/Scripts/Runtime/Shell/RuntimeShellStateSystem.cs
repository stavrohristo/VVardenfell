using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
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

            if (state.PauseMenuOpen == 0 && state.SaveLoadBrowserOpen == 0 && state.ModalOpen != 0)
                RuntimeShellStateUtility.ClearModal(ref state);

            if (state.ContainerOpen != 0)
            {
                state.InventoryOpen = 1;
                state.PauseMenuOpen = 0;
                state.JournalOpen = 0;
            }
            else if (state.InventoryOpen != 0 && state.PauseMenuOpen != 0)
                state.InventoryOpen = 0;

            if (state.SaveLoadBrowserOpen != 0)
            {
                state.InventoryOpen = 0;
                state.ContainerOpen = 0;
                state.PauseMenuOpen = 1;
                state.JournalOpen = 0;
            }

            if (state.JournalOpen != 0)
            {
                state.InventoryOpen = 0;
                state.ContainerOpen = 0;
                state.PauseMenuOpen = 0;
                state.SaveLoadBrowserOpen = 0;
                state.OptionsOpen = 0;
            }

            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref state);
        }
    }
}
