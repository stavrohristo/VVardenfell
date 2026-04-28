using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Movement
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct MorrowindMovementSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<MorrowindMovementSettings>())
                return;

            Entity entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "VVardenfell.MovementSettings");
            state.EntityManager.AddComponentData(entity, MorrowindMovementSettings.OpenMwDefaults());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
}
