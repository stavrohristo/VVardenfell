using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct PlayerTravelingResetSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<PlayerTravelingState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var state = ref SystemAPI.GetSingletonRW<PlayerTravelingState>().ValueRW;
            state.Active = 0;
        }
    }
}
