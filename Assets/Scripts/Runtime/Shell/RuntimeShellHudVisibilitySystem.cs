using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial struct RuntimeShellHudVisibilitySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimePresentationEnabled>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            state.HudVisible = (byte)(BootstrapPresentationGate.BlocksGameplayInput ? 0 : 1);
        }
    }
}
