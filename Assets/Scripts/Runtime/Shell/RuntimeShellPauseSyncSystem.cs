using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(MorrowindMenuMutationSystemGroup))]
    public partial struct RuntimeShellPauseSyncSystem : ISystem
    {
        EntityQuery _pausedQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _pausedQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            systemState.RequireForUpdate<RuntimeShellState>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            bool paused = shell.InventoryOpen != 0
                || shell.ContainerOpen != 0
                || shell.PauseMenuOpen != 0
                || shell.ModalOpen != 0
                || shell.SaveLoadBrowserOpen != 0
                || shell.OptionsOpen != 0
                || shell.JournalOpen != 0
                || shell.DialogueOpen != 0
                || shell.RestMenuOpen != 0
                || shell.RestMenuAdvancing != 0;

            if (paused)
                MorrowindRuntimeLifecycleUtility.EnsurePaused(systemState.EntityManager, _pausedQuery);
            else
                MorrowindRuntimeLifecycleUtility.RemovePaused(systemState.EntityManager, _pausedQuery);
        }
    }
}
