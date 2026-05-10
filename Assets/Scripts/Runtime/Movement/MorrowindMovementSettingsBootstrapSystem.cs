using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Movement
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct MorrowindMovementSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindMovementSettingsBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<MorrowindMovementSettings>())
            {
                RuntimeBootstrapRequestUtility.Consume<MorrowindMovementSettingsBootstrapRequest>(state.EntityManager);
                return;
            }

            Entity entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, MorrowindMovementSettings.OpenMwDefaults());
            RuntimeBootstrapRequestUtility.Consume<MorrowindMovementSettingsBootstrapRequest>(state.EntityManager);
        }
    }
}
