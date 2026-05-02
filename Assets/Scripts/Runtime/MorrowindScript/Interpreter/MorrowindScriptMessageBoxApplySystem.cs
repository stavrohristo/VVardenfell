using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptMessageBoxApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptMessageBoxRequest>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!EntityManager.HasBuffer<MorrowindScriptMessageBoxRequest>(runtimeEntity))
                return;

            var requests = EntityManager.GetBuffer<MorrowindScriptMessageBoxRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            for (int i = 0; i < requests.Length; i++)
                RuntimeShellStateUtility.ShowMessageBox(ref shell, requests[i].Body);

            requests.Clear();
        }
    }
}
