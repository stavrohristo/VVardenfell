using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActivationCleanupSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ScriptActivationEvent>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var events = SystemAPI.GetSingletonBuffer<ScriptActivationEvent>();
            events.Clear();
        }
    }
}
