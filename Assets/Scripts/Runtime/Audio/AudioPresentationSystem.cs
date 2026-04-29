using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioPresentationSystemGroup))]
    public partial class AudioPresentationSystem : SystemBase
    {
        static readonly ProfilerMarker k_SyncAudio = new("VV.Audio.SyncPlayback");
        static readonly ProfilerMarker k_SyncMusic = new("VV.Audio.SyncMusic");
        static readonly ProfilerMarker k_SyncInteriorAmbient = new("VV.Audio.SyncInteriorAmbient");
        static readonly ProfilerMarker k_SyncRegionContext = new("VV.Audio.SyncRegionContext");
        static readonly ProfilerMarker k_QueueRegionEvent = new("VV.Audio.QueueRegionEvent");
        static readonly ProfilerMarker k_QueueInteractionEvent = new("VV.Audio.QueueInteractionEvent");

        RuntimeAudioService _service;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<MusicState>();
            RequireForUpdate<MusicPlaybackStatus>();
            RequireForUpdate<InteriorAmbientState>();
            RequireForUpdate<RegionAmbientState>();
            RequireForUpdate<WeatherAudioState>();
            RequireForUpdate<AudioTuningState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
        }

        protected override void OnDestroy()
        {
            _service?.Dispose();
            _service = null;
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncAudio.Auto();

            CompleteDependency();
            _service ??= new RuntimeAudioService();

            var contentDb = RuntimeContentDatabase.Active;
            var context = SystemAPI.GetSingleton<AudioContextState>();
            var music = SystemAPI.GetSingleton<MusicState>();
            var interiorAmbient = SystemAPI.GetSingleton<InteriorAmbientState>();
            var regionAmbient = SystemAPI.GetSingleton<RegionAmbientState>();
            var weatherAmbient = SystemAPI.GetSingleton<WeatherAudioState>();
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            var interactionRequests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            ref var playbackStatus = ref SystemAPI.GetSingletonRW<MusicPlaybackStatus>().ValueRW;
            ref var interactionState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            bool runtimeActive = SystemAPI.HasSingleton<MorrowindRuntimeActive>();
            bool runtimePaused = SystemAPI.HasSingleton<MorrowindRuntimePaused>();

            using (k_SyncMusic.Auto())
                _service.SyncMusic(contentDb, music, tuning);
            if (runtimeActive && !runtimePaused)
            {
                using (k_SyncInteriorAmbient.Auto())
                    _service.SyncInteriorAmbient(contentDb, interiorAmbient, tuning);
                _service.SyncWeatherAmbient(contentDb, weatherAmbient, tuning);
                using (k_SyncRegionContext.Auto())
                    _service.SyncRegionAmbientContext(context.Mode == AudioPlaybackMode.World && regionAmbient.Region.IsValid);
                using (k_QueueRegionEvent.Auto())
                    _service.QueueRegionAmbientEvent(contentDb, regionAmbient, tuning);
                using (k_QueueInteractionEvent.Auto())
                    _service.QueueInteractionAudioEvents(contentDb, interactionRequests, ref interactionState, tuning);
            }
            else
            {
                using (k_SyncInteriorAmbient.Auto())
                    _service.SyncInteriorAmbient(contentDb, default, tuning);
                _service.SyncWeatherAmbient(contentDb, default, tuning);
                using (k_SyncRegionContext.Auto())
                    _service.SyncRegionAmbientContext(false);
                interactionRequests.Clear();
                interactionState = default;
            }

            _service.Tick(SystemAPI.Time.DeltaTime);
            playbackStatus.IsPlaying = (byte)(_service.IsMusicPlaying ? 1 : 0);
            playbackStatus.HasPendingTrack = (byte)(_service.HasPendingMusicTrack ? 1 : 0);
        }
    }
}
