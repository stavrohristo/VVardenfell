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
            bool paused = SystemAPI.HasSingleton<RuntimeShellUiInputBlocker>();

            if (paused)
                MorrowindRuntimeLifecycleUtility.EnsurePaused(systemState.EntityManager, _pausedQuery);
            else
                MorrowindRuntimeLifecycleUtility.RemovePaused(systemState.EntityManager, _pausedQuery);
        }
    }
}
