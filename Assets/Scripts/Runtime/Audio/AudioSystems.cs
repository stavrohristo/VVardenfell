using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
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

            UnityEngine.Debug.Log(AudioTuningSettings.Describe(tuning, tuningLoadSource));
        }

        protected override void OnUpdate()
        {
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class AudioContextResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_ContextResolve = new("VV.Audio.ResolveContext");

        AudioPlaybackMode _lastMode;
        BootstrapAudioPhase _lastPhase;
        bool _hasLoggedContext;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_ContextResolve.Auto();

            var phase = BootstrapPresentationAudioState.CurrentPhase;
            AudioPlaybackMode mode = ResolveMode(phase);

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            context.Mode = mode;
            context.BootstrapPhase = (byte)phase;

            if (!_hasLoggedContext || _lastMode != mode || _lastPhase != phase)
            {
                _hasLoggedContext = true;
                _lastMode = mode;
                _lastPhase = phase;
                UnityEngine.Debug.Log($"[VVardenfell][Audio] context resolved: mode={mode}, phase={phase}.");
            }
        }

        static AudioPlaybackMode ResolveMode(BootstrapAudioPhase phase)
        {
            return phase switch
            {
                BootstrapAudioPhase.IntroLogo => AudioPlaybackMode.Menu,
                BootstrapAudioPhase.Menu => AudioPlaybackMode.Menu,
                BootstrapAudioPhase.Dismissed => AudioPlaybackMode.World,
                BootstrapAudioPhase.None => AudioPlaybackMode.Bootstrap,
                _ => AudioPlaybackMode.Bootstrap,
            };
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial class AmbientSettingsResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_SettingsResolve = new("VV.Audio.ResolveAmbientSettings");

        protected override void OnCreate()
        {
            RequireForUpdate<AmbientSettingsState>();
            RequireForUpdate<AudioTuningState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_SettingsResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            ref var state = ref SystemAPI.GetSingletonRW<AmbientSettingsState>().ValueRW;
            if (contentDb == null)
            {
                state.MinSecondsBetweenEnvironmentalSounds = math.max(0.05f, tuning.ExteriorAmbientFallbackMinSeconds);
                state.MaxSecondsBetweenEnvironmentalSounds = math.max(state.MinSecondsBetweenEnvironmentalSounds, tuning.ExteriorAmbientFallbackMaxSeconds);
                return;
            }

            var settings = contentDb.GetAmbientSettings();
            state.MinSecondsBetweenEnvironmentalSounds = math.max(
                0.05f,
                settings.MinSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMinIntervalMultiplier));
            state.MaxSecondsBetweenEnvironmentalSounds = math.max(
                state.MinSecondsBetweenEnvironmentalSounds,
                settings.MaxSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMaxIntervalMultiplier));
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial class MusicResolveSystem : SystemBase
    {
        const string MenuMusicTrackRelativePath = "Special/morrowind title.mp3";

        static readonly ProfilerMarker k_MusicResolve = new("VV.Audio.ResolveMusic");

        RuntimeContentDatabase _lastLoggedContentDb;
        RuntimeContentDatabase _lastPlaylistContentDb;
        bool _loggedMusicPool;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<MusicState>();
            RequireForUpdate<MusicPlaylistState>();
            RequireForUpdate<MusicPlaybackStatus>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_MusicResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            LogMusicPoolOnce(contentDb);

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var music = ref SystemAPI.GetSingletonRW<MusicState>().ValueRW;
            ref var playlist = ref SystemAPI.GetSingletonRW<MusicPlaylistState>().ValueRW;
            var playback = SystemAPI.GetSingleton<MusicPlaybackStatus>();
            var trackPool = SystemAPI.GetSingletonBuffer<MusicPlaylistEntry>();

            if (!ReferenceEquals(_lastPlaylistContentDb, contentDb))
            {
                _lastPlaylistContentDb = contentDb;
                trackPool.Clear();
                playlist = default;
            }

            switch (context.Mode)
            {
                case AudioPlaybackMode.Menu:
                    music.Looping = 1;
                    music.ResolvedTrack = ResolveMenuTrack(contentDb);
                    music.Category = music.ResolvedTrack.IsValid ? contentDb.Get(music.ResolvedTrack).Category : MusicTrackCategory.Special;
                    playlist.CurrentTrackValue = 0;
                    break;
                case AudioPlaybackMode.World:
                    ResolveWorldTrack(contentDb, ref music, ref playlist, playback, trackPool);
                    break;
                default:
                    music.Looping = 0;
                    music.ResolvedTrack = default;
                    music.Category = MusicTrackCategory.Special;
                    playlist.CurrentTrackValue = 0;
                    break;
            }
        }

        void LogMusicPoolOnce(RuntimeContentDatabase contentDb)
        {
            if (contentDb == null)
                return;

            if (!ReferenceEquals(_lastLoggedContentDb, contentDb))
            {
                _lastLoggedContentDb = contentDb;
                _loggedMusicPool = false;
            }

            if (_loggedMusicPool)
                return;

            _loggedMusicPool = true;

            var tracks = contentDb.Data?.MusicTracks ?? System.Array.Empty<MusicTrackDef>();
            var builder = new System.Text.StringBuilder();
            builder.Append("[VVardenfell][Audio] music pool loaded: total=").Append(tracks.Length);

            int exploreCount = 0;
            int battleCount = 0;
            int specialCount = 0;
            for (int i = 0; i < tracks.Length; i++)
            {
                switch (tracks[i].Category)
                {
                    case MusicTrackCategory.Explore:
                        exploreCount++;
                        break;
                    case MusicTrackCategory.Battle:
                        battleCount++;
                        break;
                    case MusicTrackCategory.Special:
                        specialCount++;
                        break;
                }
            }

            builder
                .Append(", explore=").Append(exploreCount)
                .Append(", battle=").Append(battleCount)
                .Append(", special=").Append(specialCount);

            for (int i = 0; i < tracks.Length; i++)
            {
                builder
                    .Append("\n  [")
                    .Append(i)
                    .Append("] ")
                    .Append(tracks[i].Category)
                    .Append(": ")
                    .Append(tracks[i].RelativePath ?? "<null>");
            }

            UnityEngine.Debug.Log(builder.ToString());
        }

        static MusicTrackDefHandle ResolveMenuTrack(RuntimeContentDatabase contentDb)
        {
            if (contentDb == null)
                return default;

            if (contentDb.TryGetMusicTrackHandle(MenuMusicTrackRelativePath, out var handle))
                return handle;
            if (contentDb.TryGetFirstMusicTrackByCategory(MusicTrackCategory.Special, out handle))
                return handle;
            if (contentDb.TryGetFirstMusicTrackByCategory(MusicTrackCategory.Explore, out handle))
                return handle;
            return default;
        }

        static void ResolveWorldTrack(
            RuntimeContentDatabase contentDb,
            ref MusicState music,
            ref MusicPlaylistState playlist,
            in MusicPlaybackStatus playback,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            music.Looping = 0;
            music.Category = MusicTrackCategory.Explore;

            if (contentDb == null)
            {
                music.ResolvedTrack = default;
                playlist.CurrentTrackValue = 0;
                return;
            }

            EnsurePlaylistReady(contentDb, MusicTrackCategory.Explore, ref playlist, trackPool);

            bool currentTrackMatchesCategory = music.ResolvedTrack.IsValid
                && contentDb.Get(music.ResolvedTrack).Category == MusicTrackCategory.Explore;
            bool currentTrackPendingOrPlaying = playback.HasPendingTrack != 0 || playback.IsPlaying != 0;

            if (!currentTrackMatchesCategory || playlist.CurrentTrackValue == 0)
            {
                music.ResolvedTrack = SelectNextTrack(contentDb, MusicTrackCategory.Explore, ref playlist, trackPool);
                LogSelectedTrack(contentDb, music.ResolvedTrack, "initial world track");
                return;
            }

            if (!currentTrackPendingOrPlaying)
            {
                music.ResolvedTrack = SelectNextTrack(contentDb, MusicTrackCategory.Explore, ref playlist, trackPool);
                LogSelectedTrack(contentDb, music.ResolvedTrack, "advanced world track");
            }
        }

        static void EnsurePlaylistReady(
            RuntimeContentDatabase contentDb,
            MusicTrackCategory category,
            ref MusicPlaylistState playlist,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            if (contentDb == null)
            {
                trackPool.Clear();
                playlist.Initialized = 0;
                return;
            }

            if (playlist.RandomState == 0u)
                playlist.RandomState = CreateMusicSeed(category, contentDb.MusicTrackCount);

            bool categoryChanged = playlist.ActiveCategory != (byte)category;
            bool contentChanged = playlist.ContentTrackCount != contentDb.MusicTrackCount;
            if (playlist.Initialized == 0 || categoryChanged || contentChanged)
            {
                RefillTrackPool(contentDb, category, trackPool);
                playlist.ActiveCategory = (byte)category;
                playlist.ContentTrackCount = contentDb.MusicTrackCount;
                playlist.CurrentTrackValue = 0;
                playlist.Initialized = 1;
            }
        }

        static MusicTrackDefHandle SelectNextTrack(
            RuntimeContentDatabase contentDb,
            MusicTrackCategory category,
            ref MusicPlaylistState playlist,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            if (contentDb == null)
                return default;

            if (trackPool.Length == 0)
                RefillTrackPool(contentDb, category, trackPool);

            if (trackPool.Length == 0)
            {
                playlist.CurrentTrackValue = 0;
                if (contentDb.TryGetFirstMusicTrackByCategory(MusicTrackCategory.Special, out var specialFallback))
                    return specialFallback;
                return default;
            }

            var random = new Unity.Mathematics.Random(EnsureMusicSeed(playlist.RandomState));
            int index = random.NextInt(trackPool.Length);
            if (trackPool.Length > 1 && trackPool[index].Track.Value == playlist.LastPlayedTrackValue)
                index = (index + 1) % trackPool.Length;

            MusicTrackDefHandle selected = trackPool[index].Track;
            trackPool[index] = trackPool[trackPool.Length - 1];
            trackPool.RemoveAt(trackPool.Length - 1);

            playlist.RandomState = random.state;
            playlist.LastPlayedTrackValue = selected.Value;
            playlist.CurrentTrackValue = selected.Value;
            return selected;
        }

        static void RefillTrackPool(
            RuntimeContentDatabase contentDb,
            MusicTrackCategory category,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            trackPool.Clear();
            if (contentDb == null)
                return;

            var tracks = contentDb.Data?.MusicTracks ?? System.Array.Empty<MusicTrackDef>();
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Category != category)
                    continue;

                trackPool.Add(new MusicPlaylistEntry
                {
                    Track = MusicTrackDefHandle.FromIndex(i),
                });
            }
        }

        static uint CreateMusicSeed(MusicTrackCategory category, int trackCount)
        {
            uint seed = (uint)category * 747796405u + (uint)trackCount * 2891336453u;
            return EnsureMusicSeed(seed);
        }

        static uint EnsureMusicSeed(uint seed)
        {
            return seed == 0u ? 1u : seed;
        }

        static void LogSelectedTrack(RuntimeContentDatabase contentDb, MusicTrackDefHandle handle, string reason)
        {
            if (contentDb == null || !handle.IsValid)
                return;

            ref readonly var track = ref contentDb.Get(handle);
            UnityEngine.Debug.Log($"[VVardenfell][Audio] {reason}: {track.Category} '{track.RelativePath}'.");
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class RegionAmbientResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_RegionResolve = new("VV.Audio.ResolveRegionAmbient");

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<RegionAmbientState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_RegionResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var regionState = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            regionState.PendingEventSound = default;

            if (context.Mode != AudioPlaybackMode.World || contentDb == null)
            {
                regionState.Region = default;
                return;
            }

            if (SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0)
            {
                regionState.Region = default;
                return;
            }

            var streaming = SystemAPI.HasSingleton<StreamingConfig>()
                ? SystemAPI.GetSingleton<StreamingConfig>()
                : default;
            var environment = SystemAPI.HasSingleton<ActiveEnvironmentState>()
                ? SystemAPI.GetSingleton<ActiveEnvironmentState>()
                : default;

            regionState.Region = ResolveExteriorRegion(contentDb, streaming.CameraCell, environment);
        }

        static RegionDefHandle ResolveExteriorRegion(RuntimeContentDatabase contentDb, int2 cameraCell, ActiveEnvironmentState environment)
        {
            if (contentDb == null)
                return default;

            if (WorldResources.Cells.TryGetValue(cameraCell, out var cell)
                && cell != null
                && !string.IsNullOrWhiteSpace(cell.Environment.RegionId)
                && contentDb.TryGetRegionHandle(cell.Environment.RegionId, out var regionHandle))
                return regionHandle;

            if (environment.RegionHandleValue > 0)
                return new RegionDefHandle { Value = environment.RegionHandleValue };

            return default;
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial class InteriorAmbientResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_InteriorResolve = new("VV.Audio.ResolveInteriorAmbient");

        EntityQuery _playerQuery;
        FixedString128Bytes _lastMissingInteriorId;
        bool _loggedMissingInterior;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<InteriorAmbientState>();
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            using var _ = k_InteriorResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var ambient = ref SystemAPI.GetSingletonRW<InteriorAmbientState>().ValueRW;

            ambient.Looping = 1;

            if (context.Mode != AudioPlaybackMode.World || contentDb == null || _playerQuery.IsEmptyIgnoreFilter)
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            if (!SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
            if (transition.InteriorActive == 0)
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            float3 playerPosition = _playerQuery.GetSingleton<LocalTransform>().Position;
            ambient.InteriorCellId = transition.ActiveInteriorCellId;
            ambient.ResolvedSound = ResolveNearestInteriorAmbient(
                transition.ActiveInteriorCellId,
                playerPosition,
                out uint sourcePlacedRefId,
                out float3 sourcePosition);
            ambient.SourcePlacedRefId = sourcePlacedRefId;
            ambient.SourcePosition = sourcePosition;

            if (ambient.ResolvedSound.IsValid)
            {
                ref readonly var sound = ref contentDb.Get(ambient.ResolvedSound);
                ambient.MinDistance = sound.MinRange;
                ambient.MaxDistance = math.max((float)sound.MinRange, (float)sound.MaxRange);
            }
            else
            {
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
            }

            if (!ambient.ResolvedSound.IsValid)
            {
                if (!_loggedMissingInterior || !_lastMissingInteriorId.Equals(transition.ActiveInteriorCellId))
                {
                    _loggedMissingInterior = true;
                    _lastMissingInteriorId = transition.ActiveInteriorCellId;
                    UnityEngine.Debug.Log($"[VVardenfell][Audio] interior '{transition.ActiveInteriorCellId}' resolved no ambient light source; interior ambience will idle.");
                }
            }
            else
            {
                _loggedMissingInterior = false;
            }
        }

        SoundDefHandle ResolveNearestInteriorAmbient(
            FixedString128Bytes interiorCellId,
            float3 playerPosition,
            out uint sourcePlacedRefId,
            out float3 sourcePosition)
        {
            SoundDefHandle resolved = default;
            sourcePlacedRefId = 0u;
            sourcePosition = default;
            float bestDistanceSq = float.MaxValue;

            foreach (var (ambientSource, location, placedRefId, transform) in SystemAPI
                         .Query<RefRO<InteriorAmbientSourceAuthoring>, RefRO<LogicalRefLocation>, RefRO<PlacedRefIdentity>, RefRO<LocalTransform>>()
                         .WithAll<LogicalRefTag, LightSourceAuthoring>())
            {
                if (location.ValueRO.IsInterior == 0 || !location.ValueRO.InteriorCellId.Equals(interiorCellId))
                    continue;

                float distanceSq = math.distancesq(transform.ValueRO.Position, playerPosition);
                if (distanceSq > bestDistanceSq)
                    continue;

                if (math.abs(distanceSq - bestDistanceSq) <= 0.0001f && sourcePlacedRefId != 0u && placedRefId.ValueRO.Value >= sourcePlacedRefId)
                    continue;

                bestDistanceSq = distanceSq;
                sourcePlacedRefId = placedRefId.ValueRO.Value;
                sourcePosition = transform.ValueRO.Position;
                resolved = ambientSource.ValueRO.AmbientSound;
            }

            return resolved;
        }
    }

    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(RegionAmbientResolveSystem))]
    [UpdateAfter(typeof(AmbientSettingsResolveSystem))]
    public partial class AmbientEventSchedulerSystem : SystemBase
    {
        static readonly ProfilerMarker k_ScheduleAmbient = new("VV.Audio.ScheduleRegionAmbient");

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<RegionAmbientState>();
            RequireForUpdate<AmbientSchedulerState>();
            RequireForUpdate<AmbientSettingsState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_ScheduleAmbient.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var regionState = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;
            ref var scheduler = ref SystemAPI.GetSingletonRW<AmbientSchedulerState>().ValueRW;
            var settings = SystemAPI.GetSingleton<AmbientSettingsState>();

            if (context.Mode != AudioPlaybackMode.World || !regionState.Region.IsValid)
            {
                scheduler.ActiveRegionHandleValue = 0;
                scheduler.Initialized = 0;
                return;
            }

            if (scheduler.ActiveRegionHandleValue != regionState.Region.Value || scheduler.Initialized == 0)
            {
                scheduler.ActiveRegionHandleValue = regionState.Region.Value;
                scheduler.RandomState = CreateSeed(regionState.Region.Value);
                scheduler.SecondsUntilNextAttempt = math.min(0.5f, settings.MinSecondsBetweenEnvironmentalSounds);
                scheduler.Initialized = 1;
            }

            float dt = SystemAPI.Time.DeltaTime;
            scheduler.SecondsUntilNextAttempt -= dt;
            if (scheduler.SecondsUntilNextAttempt > 0f)
                return;

            var random = new Unity.Mathematics.Random(EnsureSeed(scheduler.RandomState));
            scheduler.SecondsUntilNextAttempt = SampleNextInterval(ref random, settings);

            var refs = contentDb.GetRegionSoundRefs(regionState.Region);
            if (refs.Length > 0)
            {
                int refIndex = random.NextInt(refs.Length);
                var candidate = refs[refIndex];
                int roll = random.NextInt(100);
                if (roll < candidate.Chance && contentDb.TryGetSoundHandle(candidate.SoundId, out var handle) && handle.IsValid)
                {
                    regionState.PendingEventSound = handle;
                    regionState.EventSequence += 1u;
                }
            }

            scheduler.RandomState = random.state;
        }

        static float SampleNextInterval(ref Unity.Mathematics.Random random, in AmbientSettingsState settings)
        {
            if (settings.MaxSecondsBetweenEnvironmentalSounds <= settings.MinSecondsBetweenEnvironmentalSounds)
                return settings.MinSecondsBetweenEnvironmentalSounds;

            return random.NextFloat(settings.MinSecondsBetweenEnvironmentalSounds, settings.MaxSecondsBetweenEnvironmentalSounds);
        }

        static uint CreateSeed(int regionHandleValue)
        {
            uint seed = (uint)regionHandleValue * 747796405u + 2891336453u;
            return EnsureSeed(seed);
        }

        static uint EnsureSeed(uint seed) => seed == 0u ? 1u : seed;
    }

    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
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
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            var interactionRequests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            ref var playbackStatus = ref SystemAPI.GetSingletonRW<MusicPlaybackStatus>().ValueRW;
            ref var interactionState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;

            using (k_SyncMusic.Auto())
                _service.SyncMusic(contentDb, music, tuning);
            using (k_SyncInteriorAmbient.Auto())
                _service.SyncInteriorAmbient(contentDb, interiorAmbient, tuning);
            using (k_SyncRegionContext.Auto())
                _service.SyncRegionAmbientContext(context.Mode == AudioPlaybackMode.World && regionAmbient.Region.IsValid);
            using (k_QueueRegionEvent.Auto())
                _service.QueueRegionAmbientEvent(contentDb, regionAmbient, tuning);
            using (k_QueueInteractionEvent.Auto())
                _service.QueueInteractionAudioEvents(contentDb, interactionRequests, ref interactionState, tuning);

            _service.Tick(SystemAPI.Time.DeltaTime);
            playbackStatus.IsPlaying = (byte)(_service.IsMusicPlaying ? 1 : 0);
            playbackStatus.HasPendingTrack = (byte)(_service.HasPendingMusicTrack ? 1 : 0);
        }
    }
}
