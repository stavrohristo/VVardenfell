using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
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
        static readonly ProfilerMarker k_QueueScriptEvent = new("VV.Audio.QueueScriptEvent");

        RuntimeAudioService _service;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<MusicState>();
            RequireForUpdate<MusicPlaybackStatus>();
            RequireForUpdate<InteriorAmbientState>();
            RequireForUpdate<RegionAmbientState>();
            RequireForUpdate<WeatherAudioState>();
            RequireForUpdate<WeatherRainAudioState>();
            RequireForUpdate<NearWaterAudioState>();
            RequireForUpdate<AudioTuningState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
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

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var context = SystemAPI.GetSingleton<AudioContextState>();
            var music = SystemAPI.GetSingleton<MusicState>();
            var interiorAmbient = SystemAPI.GetSingleton<InteriorAmbientState>();
            var regionAmbient = SystemAPI.GetSingleton<RegionAmbientState>();
            var weatherAmbient = SystemAPI.GetSingleton<WeatherAudioState>();
            var weatherRain = SystemAPI.GetSingleton<WeatherRainAudioState>();
            var nearWater = SystemAPI.GetSingleton<NearWaterAudioState>();
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            var interactionRequests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            ref var playbackStatus = ref SystemAPI.GetSingletonRW<MusicPlaybackStatus>().ValueRW;
            ref var interactionState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            bool runtimeActive = SystemAPI.HasSingleton<MorrowindRuntimeActive>();
            bool runtimePaused = SystemAPI.HasSingleton<MorrowindRuntimePaused>();

            using (k_SyncMusic.Auto())
                _service.SyncMusic(ref contentBlob, music, tuning);
            if (runtimeActive && !runtimePaused)
            {
                using (k_SyncInteriorAmbient.Auto())
                    _service.SyncInteriorAmbient(ref contentBlob, interiorAmbient, tuning);
                _service.SyncWeatherAmbient(ref contentBlob, weatherAmbient, tuning);
                _service.SyncWeatherRain(ref contentBlob, weatherRain, tuning);
                _service.SyncNearWater(ref contentBlob, nearWater, tuning);
                using (k_SyncRegionContext.Auto())
                    _service.SyncRegionAmbientContext(context.Mode == AudioPlaybackMode.World && regionAmbient.Region.IsValid);
                using (k_QueueRegionEvent.Auto())
                    _service.QueueRegionAmbientEvent(ref contentBlob, regionAmbient, tuning);
                using (k_QueueInteractionEvent.Auto())
                    _service.QueueInteractionAudioEvents(ref contentBlob, interactionRequests, ref interactionState, tuning);
                using (k_QueueScriptEvent.Auto())
                    ConsumeScriptAudioRequests(ref contentBlob, tuning, consume: true, keepActiveLoops: true);
            }
            else
            {
                using (k_SyncInteriorAmbient.Auto())
                    _service.SyncInteriorAmbient(ref contentBlob, default, tuning);
                _service.SyncWeatherAmbient(ref contentBlob, default, tuning);
                _service.SyncWeatherRain(ref contentBlob, default, tuning);
                _service.SyncNearWater(ref contentBlob, default, tuning);
                using (k_SyncRegionContext.Auto())
                    _service.SyncRegionAmbientContext(false);
                interactionRequests.Clear();
                interactionState = default;
                using (k_QueueScriptEvent.Auto())
                    ConsumeScriptAudioRequests(ref contentBlob, tuning, consume: false, keepActiveLoops: false);
            }

            _service.Tick(SystemAPI.Time.DeltaTime);
            playbackStatus.IsPlaying = (byte)(_service.IsMusicPlaying ? 1 : 0);
            playbackStatus.HasPendingTrack = (byte)(_service.HasPendingMusicTrack ? 1 : 0);
        }

        void ConsumeScriptAudioRequests(ref RuntimeContentBlob contentBlob, in AudioTuningState tuning, bool consume, bool keepActiveLoops)
        {
            _service.BeginScriptAudioFrame();
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (request, entity) in SystemAPI.Query<RefRO<MorrowindScriptAudioRequest>>().WithEntityAccess())
            {
                if (consume)
                    _service.QueueScriptAudioEvent(ref contentBlob, request.ValueRO, tuning);
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            BlobAssetReference<ActorAnimationCatalogBlob> actorAnimationCatalog = default;
            if (SystemAPI.TryGetSingleton<ActorAnimationBlobCatalog>(out var animationCatalog))
                actorAnimationCatalog = animationCatalog.Blob;
            _service.SyncActiveSaySpatialPositions(EntityManager, actorAnimationCatalog);
            if (SystemAPI.TryGetSingletonEntity<MorrowindScriptRuntimeState>(out var runtimeEntity)
                && EntityManager.HasBuffer<MorrowindScriptActiveSource>(runtimeEntity)
                && EntityManager.HasBuffer<MorrowindScriptPlayingSound>(runtimeEntity)
                && EntityManager.HasBuffer<MorrowindScriptActiveSay>(runtimeEntity))
            {
                var activeSources = EntityManager.GetBuffer<MorrowindScriptActiveSource>(runtimeEntity);
                var playingSounds = EntityManager.GetBuffer<MorrowindScriptPlayingSound>(runtimeEntity);
                var activeSays = EntityManager.GetBuffer<MorrowindScriptActiveSay>(runtimeEntity);
                _service.EndScriptAudioFrame(activeSources, playingSounds, activeSays, keepActiveLoops);
            }
        }
    }
}
