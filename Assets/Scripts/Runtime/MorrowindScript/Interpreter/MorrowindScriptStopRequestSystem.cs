using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptStopRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptStopRequest>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptStopRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            foreach (var (instance, entity) in SystemAPI.Query<RefRW<MorrowindScriptInstance>>().WithEntityAccess())
            {
                if (!ShouldStop(instance.ValueRO.ProgramIndex, requests))
                    continue;

                instance.ValueRW.Status = (byte)MorrowindScriptInstanceStatus.Disabled;
                instance.ValueRW.DisabledReason = "Stopped by StopScript.";
            }

            requests.Clear();
        }

        static bool ShouldStop(int programIndex, DynamicBuffer<MorrowindScriptStopRequest> requests)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].ProgramIndex == programIndex)
                    return true;
            }

            return false;
        }
    }
}
