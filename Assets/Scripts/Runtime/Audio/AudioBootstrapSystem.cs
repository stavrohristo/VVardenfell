using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct AudioBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<AudioBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<AudioContextState>())
            {
                Entity audioEntity = SystemAPI.GetSingletonEntity<AudioContextState>();
                if (!systemState.EntityManager.HasBuffer<MorrowindMusicRequest>(audioEntity))
                    systemState.EntityManager.AddBuffer<MorrowindMusicRequest>(audioEntity);
                RuntimeBootstrapRequestUtility.Consume<AudioBootstrapRequest>(systemState.EntityManager);
                return;
            }

            var tuningSettings = AudioTuningSettings.LoadRuntimeOrDefault(out var tuningLoadSource);
            AudioTuningState tuning = tuningSettings.BuildRuntimeState();

            var entity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.AddComponentData(entity, new AudioContextState
            {
                Mode = AudioPlaybackMode.Bootstrap,
                BootstrapPhase = (byte)BootstrapAudioPhase.None,
            });
            systemState.EntityManager.AddComponentData(entity, new MusicState
            {
                Category = MusicTrackCategory.Special,
                Looping = 1,
            });
            systemState.EntityManager.AddComponentData(entity, new MusicPlaylistState());
            systemState.EntityManager.AddComponentData(entity, new MusicPlaybackStatus());
            systemState.EntityManager.AddBuffer<MusicPlaylistEntry>(entity);
            systemState.EntityManager.AddBuffer<MorrowindMusicRequest>(entity);
            systemState.EntityManager.AddComponentData(entity, new InteriorAmbientState
            {
                Looping = 1,
            });
            systemState.EntityManager.AddComponentData(entity, new RegionAmbientState());
            systemState.EntityManager.AddComponentData(entity, new WeatherAudioState());
            systemState.EntityManager.AddComponentData(entity, new WeatherRainAudioState());
            systemState.EntityManager.AddComponentData(entity, new NearWaterAudioState());
            systemState.EntityManager.AddComponentData(entity, new AmbientSchedulerState());
            systemState.EntityManager.AddComponentData(entity, new AmbientSettingsState
            {
                MinSecondsBetweenEnvironmentalSounds = tuning.ExteriorAmbientFallbackMinSeconds,
                MaxSecondsBetweenEnvironmentalSounds = tuning.ExteriorAmbientFallbackMaxSeconds,
            });
            systemState.EntityManager.AddComponentData(entity, tuning);
            systemState.EntityManager.AddComponentData(entity, new InteractionAudioRequestState());
            systemState.EntityManager.AddBuffer<InteractionAudioRequest>(entity);

            RuntimeBootstrapRequestUtility.Consume<AudioBootstrapRequest>(systemState.EntityManager);
        }
    }
}
