using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct ShellMessageBoxApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ShellMessageBoxRequest>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                RuntimeShellStateUtility.ShowMessageBox(ref shell, request, ShellMessageBoxFormatUtility.Format(request));
            }

            requests.Clear();
        }
    }
}
