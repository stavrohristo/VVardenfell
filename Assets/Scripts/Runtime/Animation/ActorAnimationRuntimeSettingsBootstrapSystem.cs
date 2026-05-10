using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup), OrderFirst = true)]
    public partial struct ActorAnimationRuntimeSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationRuntimeSettingsBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<ActorAnimationRuntimeSettings>())
            {
                RuntimeBootstrapRequestUtility.Consume<ActorAnimationRuntimeSettingsBootstrapRequest>(state.EntityManager);
                return;
            }

            Entity settingsEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(settingsEntity, new ActorAnimationRuntimeSettings
            {
                Mode = ActorAnimationRuntimeMode.Gpu,
                ValidationEnabled = 0,
                ValidationActorIndex = 0,
            });
            RuntimeBootstrapRequestUtility.Consume<ActorAnimationRuntimeSettingsBootstrapRequest>(state.EntityManager);
        }
    }
}
