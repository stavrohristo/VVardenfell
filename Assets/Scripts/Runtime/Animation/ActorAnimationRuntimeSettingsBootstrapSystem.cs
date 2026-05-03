using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup), OrderFirst = true)]
    public partial struct ActorAnimationRuntimeSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<ActorAnimationRuntimeSettings>())
                return;

            Entity settingsEntity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(settingsEntity, "VVardenfell.ActorAnimationRuntimeSettings");
            state.EntityManager.AddComponentData(settingsEntity, new ActorAnimationRuntimeSettings
            {
                Mode = ActorAnimationRuntimeMode.Gpu,
                ValidationEnabled = 0,
                ValidationActorIndex = 0,
            });
        }
    }
}
