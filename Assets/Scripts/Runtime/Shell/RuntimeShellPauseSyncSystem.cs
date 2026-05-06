using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(MorrowindMenuMutationSystemGroup))]
    public partial class RuntimeShellPauseSyncSystem : SystemBase
    {
        EntityQuery _pausedQuery;

        protected override void OnCreate()
        {
            _pausedQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
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
                MorrowindRuntimeLifecycleUtility.EnsurePaused(EntityManager, _pausedQuery);
            else
                MorrowindRuntimeLifecycleUtility.RemovePaused(EntityManager, _pausedQuery);
        }
    }
}
