using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateBefore(typeof(RuntimeShellPauseSyncSystem))]
    public partial struct RuntimeShellUiInputBlockerSystem : ISystem
    {
        EntityQuery _blockerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _blockerQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeShellUiInputBlocker>());
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            bool characterGenerationBlocking = false;
            if (SystemAPI.HasSingleton<CharGenStage>()
                && SystemAPI.TryGetSingleton<CharacterGenerationState>(out var charGen))
            {
                characterGenerationBlocking = charGen.Finalized == 0
                    && (CharacterGenerationMenu)charGen.CurrentMenu != CharacterGenerationMenu.None;
            }

            bool blocked = IsShellUiBlocking(shell)
                || characterGenerationBlocking
                || SystemAPI.HasSingleton<BattleSimulatorSetupUiActive>()
                || (SystemAPI.TryGetSingleton<BookReaderState>(out var bookReader) && bookReader.Visible != 0);

            SetBlocker(ref systemState, blocked);
            ApplyCursorState(!BootstrapPresentationGate.BlocksGameplayInput && !blocked);
        }

        void SetBlocker(ref SystemState systemState, bool blocked)
        {
            bool exists = !_blockerQuery.IsEmptyIgnoreFilter;
            if (blocked == exists)
                return;

            if (blocked)
            {
                Entity entity = systemState.EntityManager.CreateEntity(typeof(RuntimeShellUiInputBlocker));
                return;
            }

            systemState.EntityManager.DestroyEntity(_blockerQuery);
        }

        static bool IsShellUiBlocking(in RuntimeShellState state)
        {
            return state.InventoryOpen != 0
                   || state.ContainerOpen != 0
                   || state.PauseMenuOpen != 0
                   || state.ModalOpen != 0
                   || state.SaveLoadBrowserOpen != 0
                   || state.OptionsOpen != 0
                   || state.JournalOpen != 0
                   || state.DialogueOpen != 0
                   || state.CharacterGenerationOpen != 0
                   || state.RestMenuOpen != 0
                   || state.RestMenuAdvancing != 0
                   || state.MovieOpen != 0;
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
    }
}
