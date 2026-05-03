using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
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

            AdvanceScreenFade(ref state, SystemAPI.Time.DeltaTime);

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

        static void AdvanceScreenFade(ref RuntimeShellState state, float deltaTime)
        {
            if (state.ScreenFadeElapsed >= state.ScreenFadeDuration)
                return;

            if (state.ScreenFadeDuration <= 0f)
            {
                state.ScreenFadeAlpha = state.ScreenFadeTargetAlpha;
                state.ScreenFadeElapsed = state.ScreenFadeDuration;
                return;
            }

            state.ScreenFadeElapsed = math.min(state.ScreenFadeDuration, state.ScreenFadeElapsed + math.max(0f, deltaTime));
            float t = math.saturate(state.ScreenFadeElapsed / state.ScreenFadeDuration);
            state.ScreenFadeAlpha = math.lerp(state.ScreenFadeStartAlpha, state.ScreenFadeTargetAlpha, t);
        }
    }
}
