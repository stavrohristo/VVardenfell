using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptShellApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptShellRequest>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptShellRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref shell, requests[i]);

            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);
            requests.Clear();
        }

        static void ApplyRequest(ref RuntimeShellState shell, in MorrowindScriptShellRequest request)
        {
            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.WakeUpPlayer)
            {
                shell.PlayerSleeping = 0;
                return;
            }

            throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported shell request operation {request.Operation}.");
        }
    }
}
