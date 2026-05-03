using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ShellMessageBoxApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ShellMessageBoxRequest>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            for (int i = 0; i < requests.Length; i++)
                RuntimeShellStateUtility.ShowMessageBox(ref shell, requests[i].Body);

            requests.Clear();
        }
    }
}
