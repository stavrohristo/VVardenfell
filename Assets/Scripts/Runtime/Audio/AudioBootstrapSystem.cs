using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class AudioBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (SystemAPI.HasSingleton<AudioContextState>())
                return;

            var tuningSettings = AudioTuningSettings.LoadRuntimeOrDefault(out var tuningLoadSource);
            AudioTuningState tuning = tuningSettings.BuildRuntimeState();

            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "VVardenfell.AudioState");
            EntityManager.AddComponentData(entity, new AudioContextState
            {
                Mode = AudioPlaybackMode.Bootstrap,
                BootstrapPhase = (byte)BootstrapAudioPhase.None,
            });
            EntityManager.AddComponentData(entity, new MusicState
            {
                Category = MusicTrackCategory.Special,
                Looping = 1,
            });
            EntityManager.AddComponentData(entity, new MusicPlaylistState());
            EntityManager.AddComponentData(entity, new MusicPlaybackStatus());
            EntityManager.AddBuffer<MusicPlaylistEntry>(entity);
            EntityManager.AddComponentData(entity, new InteriorAmbientState
            {
                Looping = 1,
            });
            EntityManager.AddComponentData(entity, new RegionAmbientState());
            EntityManager.AddComponentData(entity, new AmbientSchedulerState());
            EntityManager.AddComponentData(entity, new AmbientSettingsState
            {
                MinSecondsBetweenEnvironmentalSounds = tuning.ExteriorAmbientFallbackMinSeconds,
                MaxSecondsBetweenEnvironmentalSounds = tuning.ExteriorAmbientFallbackMaxSeconds,
            });
            EntityManager.AddComponentData(entity, tuning);
            EntityManager.AddComponentData(entity, new InteractionAudioRequestState());
            EntityManager.AddBuffer<InteractionAudioRequest>(entity);

        }

        protected override void OnUpdate()
        {
        }
    }
}
