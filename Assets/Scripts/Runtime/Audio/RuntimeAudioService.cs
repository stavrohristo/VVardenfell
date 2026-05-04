using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;

namespace VVardenfell.Runtime.Audio
{
    public sealed class RuntimeAudioService : IDisposable
    {
        /// <summary>
        /// Most recently constructed service instance. Exposed so shell-level code
        /// (e.g. the Options window) can push volume scalars without threading a
        /// reference through the ECS system hierarchy. <c>null</c> until the
        /// <see cref="AudioPresentationSystem"/> first ticks, and again after
        /// <see cref="Dispose"/>.
        /// </summary>
        public static RuntimeAudioService Active { get; private set; }

        const float DefaultFadeSpeed = 2.4f;
        const int RegionEventSourceCount = 4;
        const int InteractionEventSourceCount = 256;
        const string RegionEventChannelName = "region-event";
        const string InteractionEventChannelName = "interaction-event";
        const string ScriptEventChannelName = "script-event";

        static readonly ProfilerMarker k_ClipLoad = new("VV.Audio.ClipLoad");
        static readonly ProfilerMarker k_ClipCacheHit = new("VV.Audio.ClipCacheHit");
        static readonly ProfilerMarker k_QueueRegionEvent = new("VV.Audio.QueueRegionEvent");
        static readonly ProfilerMarker k_PlayRegionEvent = new("VV.Audio.PlayRegionEvent");
        static readonly ProfilerMarker k_QueueInteractionEvent = new("VV.Audio.QueueInteractionEvent");
        static readonly ProfilerMarker k_PlayInteractionEvent = new("VV.Audio.PlayInteractionEvent");
        static readonly ProfilerMarker k_QueueScriptEvent = new("VV.Audio.QueueScriptEvent");

        enum ChannelBus
        {
            Music,
            Effects,
        }

        sealed class ChannelState
        {
            public readonly string Name;
            public readonly AudioSource Source;
            public readonly ChannelBus Bus;

            public string ActivePath;
            public string PendingPath;
            public bool PendingLoop;

            /// <summary>
            /// Desired volume driven by tuning state + per-sound volume lookup, before
            /// the player-facing Master/Music/Effects scalars. Stored separately from
            /// <see cref="TargetVolume"/> so we can reapply scalars when the Options
            /// sliders move without losing the tuning intent.
            /// </summary>
            public float BaselineVolume;

            /// <summary>
            /// Baseline × master × bus scalar — what the fader chases each Tick.
            /// </summary>
            public float TargetVolume;

            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;

            public ChannelState(string name, AudioSource source, ChannelBus bus)
            {
                Name = name;
                Source = source;
                Bus = bus;
            }
        }

        sealed class OneShotRequest
        {
            public string Path;
            public string ChannelName;
            public float Volume;
            public float Pitch;
            public uint EventSequence;
            public Entity SourceEntity;
            public uint SourcePlacedRefId;
            public Vector3 Position;
            public float MinDistance;
            public float MaxDistance;
            public bool Spatial;
            public bool TrackSay;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
        }

        sealed class ActiveSayPlayback
        {
            public Entity SourceEntity;
            public uint SourcePlacedRefId;
            public AudioSource Source;
            public uint EventSequence;
            public bool Loading;
        }

        readonly Dictionary<string, AudioClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _missingPathWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loadFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly List<OneShotRequest> _pendingEventRequests = new();
        readonly Dictionary<ulong, ChannelState> _scriptLoopChannels = new();
        readonly HashSet<ulong> _scriptLoopTouched = new();
        readonly List<ulong> _scriptLoopRemovalKeys = new();
        readonly List<ActiveSayPlayback> _activeSayPlaybacks = new();
        readonly RuntimeSoundResourceResolver _soundResourceResolver = new();

        readonly GameObject _root;
        readonly ChannelState _music;
        readonly ChannelState _interiorAmbient;
        readonly ChannelState _weatherAmbient;
        readonly ChannelState _weatherAmbientNext;
        readonly ChannelState _weatherRain;
        readonly ChannelState _weatherRainNext;
        readonly ChannelState _nearWater;
        readonly AudioSource[] _regionEventSources;
        readonly AudioSource[] _interactionEventSources;
        readonly string[] _interactionEventActivePaths;
        readonly Entity[] _interactionEventActiveSourceEntities;
        readonly uint[] _interactionEventActiveSourcePlacedRefIds;

        int _nextRegionEventSource;
        int _nextInteractionEventSource;
        uint _lastQueuedRegionEventSequence;
        bool _regionAmbientEnabled = true;
        bool _interactionEventPoolExhaustedWarningLogged;
        string _installPath;

        // Player-facing scalars driven by the Options window. Master multiplies
        // everything; Music applies only to the music channel; Effects applies to
        // ambient + interaction (the "sound effects" bus in vanilla MW). All
        // clamped to [0, 1] on write.
        float _masterScalar = 1f;
        float _musicScalar = 1f;
        float _effectsScalar = 1f;

        public bool IsMusicPlaying => _music?.Source != null && _music.Source.isPlaying;
        public bool HasPendingMusicTrack =>
            _music != null
            && (_music.Operation != null || (_music.Source != null && _music.Source.clip == null && !string.IsNullOrWhiteSpace(_music.PendingPath)));

        public float MasterVolume => _masterScalar;
        public float MusicVolume => _musicScalar;
        public float EffectsVolume => _effectsScalar;

        public void SetMasterVolume(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_masterScalar, clamped))
                return;
            _masterScalar = clamped;
            ApplyScalars();
        }

        public void SetMusicVolume(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_musicScalar, clamped))
                return;
            _musicScalar = clamped;
            ApplyScalars();
        }

        public void SetEffectsVolume(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_effectsScalar, clamped))
                return;
            _effectsScalar = clamped;
            ApplyScalars();
        }

        /// <summary>
        /// Recomputes each looping channel's target volume from its stored baseline
        /// × the current master/bus scalars. Call after any scalar changes; one-shot
        /// players (region + interaction) pick the scalars up at play time via
        /// <see cref="ComputeBusMultiplier"/>.
        /// </summary>
        void ApplyScalars()
        {
            _music.TargetVolume = Mathf.Clamp01(_music.BaselineVolume * ComputeBusMultiplier(_music.Bus));
            _interiorAmbient.TargetVolume = Mathf.Clamp01(_interiorAmbient.BaselineVolume * ComputeBusMultiplier(_interiorAmbient.Bus));
            _weatherAmbient.TargetVolume = Mathf.Clamp01(_weatherAmbient.BaselineVolume * ComputeBusMultiplier(_weatherAmbient.Bus));
            _weatherAmbientNext.TargetVolume = Mathf.Clamp01(_weatherAmbientNext.BaselineVolume * ComputeBusMultiplier(_weatherAmbientNext.Bus));
            _weatherRain.TargetVolume = Mathf.Clamp01(_weatherRain.BaselineVolume * ComputeBusMultiplier(_weatherRain.Bus));
            _weatherRainNext.TargetVolume = Mathf.Clamp01(_weatherRainNext.BaselineVolume * ComputeBusMultiplier(_weatherRainNext.Bus));
            _nearWater.TargetVolume = Mathf.Clamp01(_nearWater.BaselineVolume * ComputeBusMultiplier(_nearWater.Bus));
            foreach (var channel in _scriptLoopChannels.Values)
                channel.TargetVolume = Mathf.Clamp01(channel.BaselineVolume * ComputeBusMultiplier(channel.Bus));
        }

        float ComputeBusMultiplier(ChannelBus bus) => bus switch
        {
            ChannelBus.Music => _masterScalar * _musicScalar,
            ChannelBus.Effects => _masterScalar * _effectsScalar,
            _ => _masterScalar,
        };

        public RuntimeAudioService()
        {
            _root = new GameObject("VVardenfell.RuntimeAudio");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var musicSource = _root.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.ignoreListenerPause = true;

            var interiorAmbientSource = _root.AddComponent<AudioSource>();
            interiorAmbientSource.playOnAwake = false;
            interiorAmbientSource.loop = true;
            interiorAmbientSource.spatialBlend = 1f;
            interiorAmbientSource.ignoreListenerPause = true;
            interiorAmbientSource.rolloffMode = AudioRolloffMode.Logarithmic;

            var weatherAmbientSource = _root.AddComponent<AudioSource>();
            weatherAmbientSource.playOnAwake = false;
            weatherAmbientSource.loop = true;
            weatherAmbientSource.spatialBlend = 0f;
            weatherAmbientSource.ignoreListenerPause = true;

            var weatherAmbientNextSource = _root.AddComponent<AudioSource>();
            weatherAmbientNextSource.playOnAwake = false;
            weatherAmbientNextSource.loop = true;
            weatherAmbientNextSource.spatialBlend = 0f;
            weatherAmbientNextSource.ignoreListenerPause = true;

            var weatherRainSource = _root.AddComponent<AudioSource>();
            weatherRainSource.playOnAwake = false;
            weatherRainSource.loop = true;
            weatherRainSource.spatialBlend = 0f;
            weatherRainSource.ignoreListenerPause = true;

            var weatherRainNextSource = _root.AddComponent<AudioSource>();
            weatherRainNextSource.playOnAwake = false;
            weatherRainNextSource.loop = true;
            weatherRainNextSource.spatialBlend = 0f;
            weatherRainNextSource.ignoreListenerPause = true;

            var nearWaterSource = _root.AddComponent<AudioSource>();
            nearWaterSource.playOnAwake = false;
            nearWaterSource.loop = true;
            nearWaterSource.spatialBlend = 0f;
            nearWaterSource.ignoreListenerPause = true;

            _music = new ChannelState("music", musicSource, ChannelBus.Music);
            _interiorAmbient = new ChannelState("interior-ambient", interiorAmbientSource, ChannelBus.Effects);
            _weatherAmbient = new ChannelState("weather-ambient", weatherAmbientSource, ChannelBus.Effects);
            _weatherAmbientNext = new ChannelState("weather-ambient-next", weatherAmbientNextSource, ChannelBus.Effects);
            _weatherRain = new ChannelState("weather-rain", weatherRainSource, ChannelBus.Effects);
            _weatherRainNext = new ChannelState("weather-rain-next", weatherRainNextSource, ChannelBus.Effects);
            _nearWater = new ChannelState("near-water", nearWaterSource, ChannelBus.Effects);

            _regionEventSources = new AudioSource[RegionEventSourceCount];
            for (int i = 0; i < _regionEventSources.Length; i++)
            {
                var eventSource = CreateChildAudioSource($"{RegionEventChannelName}-{i:00}");
                eventSource.playOnAwake = false;
                eventSource.loop = false;
                eventSource.spatialBlend = 0f;
                eventSource.ignoreListenerPause = true;
                _regionEventSources[i] = eventSource;
            }

            _interactionEventSources = new AudioSource[InteractionEventSourceCount];
            _interactionEventActivePaths = new string[InteractionEventSourceCount];
            _interactionEventActiveSourceEntities = new Entity[InteractionEventSourceCount];
            _interactionEventActiveSourcePlacedRefIds = new uint[InteractionEventSourceCount];
            for (int i = 0; i < _interactionEventSources.Length; i++)
            {
                var eventSource = CreateChildAudioSource($"{InteractionEventChannelName}-{i:00}");
                eventSource.playOnAwake = false;
                eventSource.loop = false;
                eventSource.spatialBlend = 1f;
                eventSource.ignoreListenerPause = true;
                eventSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _interactionEventSources[i] = eventSource;
            }

            RefreshInstallPath();
            Active = this;

            // Pick up persisted audio scalars immediately so baseline music and
            // ambience honor the player's saved Options values from the very first
            // tick, not only after Options is opened mid-session.
            if (ConfigStorage.TryLoad(out var cfg) && cfg != null)
            {
                _masterScalar = Mathf.Clamp01(cfg.MasterVolume);
                _musicScalar = Mathf.Clamp01(cfg.MusicVolume);
                _effectsScalar = Mathf.Clamp01(cfg.EffectsVolume);
            }
        }

        AudioSource CreateChildAudioSource(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, worldPositionStays: false);
            return child.AddComponent<AudioSource>();
        }

        public void Dispose()
        {
            if (Active == this)
                Active = null;
            DisposeChannel(_music);
            DisposeChannel(_interiorAmbient);
            DisposeChannel(_weatherAmbient);
            DisposeChannel(_weatherAmbientNext);
            DisposeChannel(_weatherRain);
            DisposeChannel(_weatherRainNext);
            DisposeChannel(_nearWater);
            foreach (var channel in _scriptLoopChannels.Values)
                DisposeChannel(channel);
            _scriptLoopChannels.Clear();
            _scriptLoopTouched.Clear();
            _scriptLoopRemovalKeys.Clear();
            DisposePendingEvents();

            foreach (var clip in _clipCache.Values)
            {
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
            }
            _clipCache.Clear();
            _soundResourceResolver.Dispose();

            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }

        public void SyncMusic(RuntimeContentDatabase contentDb, MusicState state, in AudioTuningState tuning)
        {
            string path = state.DirectPath.IsEmpty
                ? ResolveMusicPath(contentDb, state.ResolvedTrack)
                : ResolveDirectMusicPath(state.DirectPath.ToString());
            SyncChannel(_music, path, state.Looping != 0, ResolveMusicVolume(state, tuning));
        }

        public void SyncInteriorAmbient(RuntimeContentDatabase contentDb, InteriorAmbientState state, in AudioTuningState tuning)
        {
            string path = ResolveSoundPath(contentDb, state.ResolvedSound);
            float volume = ResolveSoundVolume(
                contentDb,
                state.ResolvedSound,
                tuning.InteriorAmbientFallbackBaseVolume,
                tuning.InteriorAmbientVolumeMultiplier);
            SyncChannel(_interiorAmbient, path, state.Looping != 0, volume);
            ApplyInteriorAmbientSpatial(state, tuning);
        }

        public void SyncWeatherAmbient(RuntimeContentDatabase contentDb, WeatherAudioState state, in AudioTuningState tuning)
        {
            string path = ResolveSoundPath(contentDb, state.ResolvedLoopSound);
            float volume = ResolveSoundVolume(
                contentDb,
                state.ResolvedLoopSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier) * Mathf.Clamp01(state.CurrentLoopVolume <= 0f && !state.ResolvedNextLoopSound.IsValid ? 1f : state.CurrentLoopVolume);
            SyncChannel(_weatherAmbient, path, state.ResolvedLoopSound.IsValid && volume > 0.0001f, volume);

            string nextPath = ResolveSoundPath(contentDb, state.ResolvedNextLoopSound);
            float nextVolume = ResolveSoundVolume(
                contentDb,
                state.ResolvedNextLoopSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier) * Mathf.Clamp01(state.NextLoopVolume);
            SyncChannel(_weatherAmbientNext, nextPath, state.ResolvedNextLoopSound.IsValid && nextVolume > 0.0001f, nextVolume);
        }

        public void SyncWeatherRain(RuntimeContentDatabase contentDb, WeatherRainAudioState state, in AudioTuningState tuning)
        {
            string path = ResolveSoundPath(contentDb, state.ResolvedLoopSound);
            float volume = ResolveSoundVolume(
                contentDb,
                state.ResolvedLoopSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier) * Mathf.Clamp01(state.CurrentLoopVolume <= 0f && !state.ResolvedNextLoopSound.IsValid ? 1f : state.CurrentLoopVolume);
            SyncChannel(_weatherRain, path, state.ResolvedLoopSound.IsValid && volume > 0.0001f, volume);

            string nextPath = ResolveSoundPath(contentDb, state.ResolvedNextLoopSound);
            float nextVolume = ResolveSoundVolume(
                contentDb,
                state.ResolvedNextLoopSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier) * Mathf.Clamp01(state.NextLoopVolume);
            SyncChannel(_weatherRainNext, nextPath, state.ResolvedNextLoopSound.IsValid && nextVolume > 0.0001f, nextVolume);
        }

        public void SyncNearWater(RuntimeContentDatabase contentDb, NearWaterAudioState state, in AudioTuningState tuning)
        {
            string path = ResolveSoundPath(contentDb, state.ResolvedLoopSound);
            float volume = ResolveSoundVolume(
                contentDb,
                state.ResolvedLoopSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier) * Mathf.Clamp01(state.Volume);
            SyncChannel(_nearWater, path, state.Looping != 0 && state.ResolvedLoopSound.IsValid && volume > 0.0001f, volume);
        }

        public void QueueRegionAmbientEvent(RuntimeContentDatabase contentDb, RegionAmbientState state, in AudioTuningState tuning)
        {
            using var _ = k_QueueRegionEvent.Auto();

            if (!_regionAmbientEnabled || state.EventSequence == 0 || state.EventSequence == _lastQueuedRegionEventSequence || !state.PendingEventSound.IsValid)
                return;
            if (HasActiveRegionEvent())
                return;

            string path = ResolveSoundPath(contentDb, state.PendingEventSound);
            float volume = ResolveSoundVolume(
                contentDb,
                state.PendingEventSound,
                tuning.ExteriorAmbientFallbackBaseVolume,
                tuning.ExteriorAmbientVolumeMultiplier);
            _lastQueuedRegionEventSequence = state.EventSequence;

            if (string.IsNullOrWhiteSpace(path))
                return;

            if (_clipCache.TryGetValue(path, out var cachedClip) && cachedClip != null)
            {
                using var cacheHit = k_ClipCacheHit.Auto();
                PlayRegionEvent(cachedClip, volume, 1f);
                return;
            }

            var request = new OneShotRequest
            {
                Path = path,
                ChannelName = RegionEventChannelName,
                Volume = volume,
                EventSequence = state.EventSequence,
                Spatial = false,
                Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, GetAudioType(path)),
            };
            request.Operation = request.Request.SendWebRequest();
            _pendingEventRequests.Add(request);
        }

        public void SyncRegionAmbientContext(bool enabled)
        {
            if (_regionAmbientEnabled == enabled)
                return;

            _regionAmbientEnabled = enabled;
            if (_regionAmbientEnabled)
                return;

            CancelPendingOneShots(spatial: false);
            StopRegionEvents();
        }

        public void QueueInteractionAudioEvents(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<InteractionAudioRequest> requests,
            ref InteractionAudioRequestState state,
            in AudioTuningState tuning)
        {
            using var _ = k_QueueInteractionEvent.Auto();

            if (requests.Length == 0)
                return;

            uint lastConsumedSequence = state.LastConsumedSequence;

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.Sequence <= lastConsumedSequence)
                    continue;

                QueueInteractionAudioEvent(contentDb, request, tuning);
                if (request.Sequence > lastConsumedSequence)
                    lastConsumedSequence = request.Sequence;
            }

            state.LastConsumedSequence = lastConsumedSequence;
            requests.Clear();
        }

        public void BeginScriptAudioFrame()
        {
            PruneCompletedSayPlaybacks();
            _scriptLoopTouched.Clear();
        }

        public void QueueScriptAudioEvent(RuntimeContentDatabase contentDb, in MorrowindScriptAudioRequest request, in AudioTuningState tuning)
        {
            using var _ = k_QueueScriptEvent.Auto();

            if ((MorrowindScriptAudioKind)request.Kind == MorrowindScriptAudioKind.StopSound)
            {
                RemoveScriptLoopChannel(BuildScriptLoopKey(request));
                return;
            }

            bool directPath = !request.DirectPath.IsEmpty;
            if (contentDb == null)
            {
                if (request.Sound.IsValid || directPath)
                    throw new InvalidOperationException("[VVardenfell][Audio] Missing content database for script audio request.");
                return;
            }

            if (!request.Sound.IsValid && !directPath)
                return;

            string path = request.Sound.IsValid
                ? ResolveSoundPath(contentDb, request.Sound)
                : ResolveDirectSoundPath(request.DirectPath.ToString());
            if (string.IsNullOrWhiteSpace(path))
            {
                if (directPath)
                    throw new InvalidOperationException($"[VVardenfell][Audio] Missing direct say audio file '{request.DirectPath}'.");

                ref readonly var sound = ref contentDb.Get(request.Sound);
                throw new InvalidOperationException($"[VVardenfell][Audio] Missing audio file for sound '{sound.Id}'.");
            }

            float requestVolume = Mathf.Clamp01(request.Volume <= 0f ? 1f : request.Volume);
            float volume = request.Sound.IsValid
                ? ResolveSoundVolume(
                    contentDb,
                    request.Sound,
                    tuning.InteractionFallbackBaseVolume,
                    tuning.InteractionVolumeMultiplier) * requestVolume
                : Mathf.Clamp01(tuning.InteractionFallbackBaseVolume * tuning.InteractionVolumeMultiplier) * requestVolume;
            float minDistance;
            float maxDistance;
            if (request.Sound.IsValid)
            {
                ResolveSoundRange(
                    contentDb,
                    request.Sound,
                    tuning.InteractionMinDistanceMultiplier,
                    tuning.InteractionMaxDistanceMultiplier,
                    out minDistance,
                    out maxDistance);
            }
            else
            {
                ResolveVoiceSoundRange(contentDb, out minDistance, out maxDistance);
            }

            var position = new Vector3(request.Position.x, request.Position.y, request.Position.z);
            if (request.Spatial != 0 && IsBeyondSpatialMaxDistance(position, maxDistance))
                return;

            float pitch = Mathf.Max(0.01f, request.Pitch <= 0f ? 1f : request.Pitch);
            if (directPath)
                StopSayPlaybacksForSource(request.SourceEntity, request.SourcePlacedRefId);

            if (request.Looping != 0)
            {
                ulong key = BuildScriptLoopKey(request);
                _scriptLoopTouched.Add(key);
                var channel = GetOrCreateScriptLoopChannel(key);
                channel.Source.spatialBlend = request.Spatial != 0 ? 1f : 0f;
                channel.Source.transform.position = position;
                channel.Source.minDistance = Mathf.Max(0f, minDistance);
                channel.Source.maxDistance = Mathf.Max(channel.Source.minDistance, maxDistance);
                channel.Source.pitch = pitch;
                SyncChannel(channel, path, true, volume);
                return;
            }

            CancelPendingMatchingOneShots(path, request.SourceEntity, request.SourcePlacedRefId, request.Spatial != 0, directPath);

            if (_clipCache.TryGetValue(path, out var cachedClip) && cachedClip != null)
            {
                using var cacheHit = k_ClipCacheHit.Auto();
                AudioSource source = request.Spatial != 0
                    ? PlayInteractionEvent(cachedClip, path, request.SourceEntity, request.SourcePlacedRefId, volume, pitch, position, minDistance, maxDistance)
                    : PlayNonSpatialEffectEvent(cachedClip, path, request.SourceEntity, request.SourcePlacedRefId, volume, pitch);
                if (directPath)
                    TrackSayPlayback(request.SourceEntity, request.SourcePlacedRefId, request.Sequence, source, loading: false);
                return;
            }

            var pendingRequest = new OneShotRequest
            {
                Path = path,
                ChannelName = ScriptEventChannelName,
                Volume = volume,
                Pitch = pitch,
                EventSequence = request.Sequence,
                SourceEntity = request.SourceEntity,
                SourcePlacedRefId = request.SourcePlacedRefId,
                Position = position,
                MinDistance = minDistance,
                MaxDistance = maxDistance,
                Spatial = request.Spatial != 0,
                TrackSay = directPath,
                Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, GetAudioType(path)),
            };
            pendingRequest.Operation = pendingRequest.Request.SendWebRequest();
            _pendingEventRequests.Add(pendingRequest);
            if (directPath)
                TrackSayPlayback(request.SourceEntity, request.SourcePlacedRefId, request.Sequence, source: null, loading: true);
        }

        public void EndScriptAudioFrame(
            DynamicBuffer<MorrowindScriptActiveSource> activeSources,
            DynamicBuffer<MorrowindScriptPlayingSound> playingSounds,
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            bool keepActiveLoops)
        {
            PruneCompletedSayPlaybacks();

            _scriptLoopRemovalKeys.Clear();
            foreach (var pair in _scriptLoopChannels)
            {
                if (keepActiveLoops && ContainsActiveScriptLoopSource(activeSources, ExtractScriptLoopSourceKey(pair.Key)))
                    continue;

                _scriptLoopRemovalKeys.Add(pair.Key);
            }

            for (int i = 0; i < _scriptLoopRemovalKeys.Count; i++)
                RemoveScriptLoopChannel(_scriptLoopRemovalKeys[i]);

            _scriptLoopRemovalKeys.Clear();
            if (playingSounds.IsCreated)
            {
                playingSounds.Clear();
                foreach (var pair in _scriptLoopChannels)
                {
                    if (IsScriptLoopAudibleOrLoading(pair.Value))
                        playingSounds.Add(new MorrowindScriptPlayingSound { LoopKey = pair.Key });
                }
            }

            if (activeSays.IsCreated)
            {
                activeSays.Clear();
                for (int i = 0; i < _activeSayPlaybacks.Count; i++)
                {
                    var say = _activeSayPlaybacks[i];
                    activeSays.Add(new MorrowindScriptActiveSay
                    {
                        SourceEntity = say.SourceEntity,
                        SourcePlacedRefId = say.SourcePlacedRefId,
                    });
                }
            }
        }

        public void Tick(float deltaTime)
        {
            RefreshInstallPath();
            TickChannel(_music, deltaTime);
            TickChannel(_interiorAmbient, deltaTime);
            TickChannel(_weatherAmbient, deltaTime);
            TickChannel(_weatherAmbientNext, deltaTime);
            TickChannel(_weatherRain, deltaTime);
            TickChannel(_weatherRainNext, deltaTime);
            TickChannel(_nearWater, deltaTime);
            foreach (var channel in _scriptLoopChannels.Values)
                TickChannel(channel, deltaTime);
            RemoveStoppedScriptLoopChannels();
            TickPendingEvents();
        }

        string ResolveMusicPath(RuntimeContentDatabase contentDb, MusicTrackDefHandle handle)
        {
            if (contentDb == null || !handle.IsValid || string.IsNullOrWhiteSpace(_installPath))
                return null;

            ref readonly var track = ref contentDb.Get(handle);
            if (string.IsNullOrWhiteSpace(track.RelativePath))
                return null;

            string path = Path.Combine(_installPath, "Data Files", "Music", track.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            return NormalizeExistingPath(path);
        }

        string ResolveDirectMusicPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || string.IsNullOrWhiteSpace(_installPath))
                return null;

            string relativePath = rawPath.Trim().Trim('"').Replace('\\', '/');
            const string prefix = "Music/";
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring(prefix.Length);

            string path = Path.Combine(_installPath, "Data Files", "Music", relativePath.Replace('/', Path.DirectorySeparatorChar));
            return NormalizeExistingPath(path);
        }

        string ResolveSoundPath(RuntimeContentDatabase contentDb, SoundDefHandle handle)
        {
            if (contentDb == null || !handle.IsValid || string.IsNullOrWhiteSpace(_installPath))
                return null;

            ref readonly var sound = ref contentDb.Get(handle);
            if (string.IsNullOrWhiteSpace(sound.SoundPath))
                return null;

            string relativePath = SoundPathResolver.Correct(sound.SoundPath);
            if (_soundResourceResolver.TryResolve(_installPath, relativePath, out string resolvedPath))
                return resolvedPath;

            WarnMissing(BuildLooseSoundPath(relativePath), "resolve");
            return null;
        }

        string ResolveDirectSoundPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || string.IsNullOrWhiteSpace(_installPath))
                return null;

            string relativePath = SoundPathResolver.Correct(rawPath);
            if (_soundResourceResolver.TryResolve(_installPath, relativePath, out string resolvedPath))
                return resolvedPath;

            WarnMissing(BuildLooseSoundPath(relativePath), "resolve");
            return null;
        }

        string BuildLooseSoundPath(string relativePath)
            => string.IsNullOrWhiteSpace(_installPath) || string.IsNullOrWhiteSpace(relativePath)
                ? null
                : Path.Combine(_installPath, "Data Files", relativePath.Replace('\\', Path.DirectorySeparatorChar));

        float ResolveSoundVolume(RuntimeContentDatabase contentDb, SoundDefHandle handle, float fallbackBaseVolume, float multiplier)
        {
            float baseVolume = ResolveSoundBaseVolume(contentDb, handle, fallbackBaseVolume);
            return Mathf.Clamp01(baseVolume * Mathf.Max(0f, multiplier));
        }

        float ResolveSoundBaseVolume(RuntimeContentDatabase contentDb, SoundDefHandle handle, float fallbackBaseVolume)
        {
            if (contentDb == null || !handle.IsValid)
                return Mathf.Clamp01(fallbackBaseVolume);

            ref readonly var sound = ref contentDb.Get(handle);
            if (sound.Volume == 0)
                return Mathf.Clamp01(fallbackBaseVolume);

            return Mathf.Clamp01(sound.Volume / 255f);
        }

        void SyncChannel(ChannelState channel, string path, bool loop, float targetVolume)
        {
            channel.PendingLoop = loop;
            // Record the tuning-intended volume as the baseline so the Options
            // scalars can be reapplied later without re-doing the tuning lookup.
            channel.BaselineVolume = Mathf.Clamp01(targetVolume);
            channel.TargetVolume = Mathf.Clamp01(channel.BaselineVolume * ComputeBusMultiplier(channel.Bus));

            if (string.IsNullOrWhiteSpace(path))
            {
                channel.PendingPath = null;
                CancelLoad(channel);
                return;
            }

            if (string.Equals(channel.ActivePath, path, StringComparison.OrdinalIgnoreCase) && channel.Source.clip != null)
            {
                using var _ = k_ClipCacheHit.Auto();
                channel.PendingPath = path;
                channel.Source.loop = loop;
                if (!channel.Source.isPlaying)
                    channel.Source.Play();
                return;
            }

            if (string.Equals(channel.PendingPath, path, StringComparison.OrdinalIgnoreCase) && channel.Operation != null)
                return;

            channel.PendingPath = path;

            if (_clipCache.TryGetValue(path, out var cachedClip) && cachedClip != null)
            {
                using var _ = k_ClipCacheHit.Auto();
                if (channel.Source.clip == cachedClip)
                {
                    channel.ActivePath = path;
                    channel.Source.loop = loop;
                    if (!channel.Source.isPlaying)
                        channel.Source.Play();
                    return;
                }

                ApplyLoadedClip(channel, path, cachedClip);
                return;
            }

            BeginLoad(channel, path);
        }

        void TickChannel(ChannelState channel, float deltaTime)
        {
            if (channel.Operation != null && channel.Operation.isDone)
                CompleteLoad(channel);

            bool shouldPlay = !string.IsNullOrWhiteSpace(channel.PendingPath) && channel.Source.clip != null;
            float fadeSpeed = deltaTime <= 0f ? 1f : DefaultFadeSpeed * deltaTime;
            float targetVolume = shouldPlay ? channel.TargetVolume : 0f;
            channel.Source.volume = Mathf.MoveTowards(channel.Source.volume, targetVolume, fadeSpeed);

            if (!shouldPlay && channel.Source.volume <= 0.0001f)
            {
                if (channel.Source.isPlaying)
                    channel.Source.Stop();
                if (string.IsNullOrWhiteSpace(channel.PendingPath))
                    channel.ActivePath = null;
            }
        }

        void ApplyInteriorAmbientSpatial(InteriorAmbientState state, in AudioTuningState tuning)
        {
            if (_interiorAmbient?.Source == null)
                return;

            var source = _interiorAmbient.Source;
            source.spatialBlend = 1f;
            source.transform.position = new Vector3(state.SourcePosition.x, state.SourcePosition.y, state.SourcePosition.z);

            float minDistance = Mathf.Max(0f, state.MinDistance * Mathf.Max(0f, tuning.InteriorAmbientMinDistanceMultiplier));
            float maxDistance = Mathf.Max(minDistance, state.MaxDistance * Mathf.Max(0f, tuning.InteriorAmbientMaxDistanceMultiplier));
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
        }

        void BeginLoad(ChannelState channel, string path)
        {
            CancelLoad(channel);
            if (!File.Exists(path))
            {
                channel.PendingPath = null;
                WarnMissing(path, channel.Name);
                return;
            }

            using var _ = k_ClipLoad.Auto();
            channel.Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, GetAudioType(path));
            channel.Operation = channel.Request.SendWebRequest();
        }

        void CompleteLoad(ChannelState channel)
        {
            if (channel.Request == null)
                return;

            if (channel.Request.result != UnityWebRequest.Result.Success)
            {
                WarnLoadFailure(channel.PendingPath, channel.Name, channel.Request.error);
                CancelLoad(channel);
                return;
            }

            var clip = DownloadHandlerAudioClip.GetContent(channel.Request);
            if (clip == null)
            {
                WarnLoadFailure(channel.PendingPath, channel.Name, "request completed without an AudioClip.");
                CancelLoad(channel);
                return;
            }

            string path = channel.PendingPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                UnityEngine.Object.Destroy(clip);
                CancelLoad(channel);
                return;
            }

            _clipCache[path] = clip;
            ApplyLoadedClip(channel, path, clip);
            CancelLoad(channel, disposeClip: false);
        }

        void ApplyLoadedClip(ChannelState channel, string path, AudioClip clip)
        {
            if (channel.Source.clip == clip && string.Equals(channel.ActivePath, path, StringComparison.OrdinalIgnoreCase))
            {
                channel.Source.loop = channel.PendingLoop;
                if (!channel.Source.isPlaying)
                    channel.Source.Play();
                return;
            }

            channel.Source.clip = clip;
            channel.Source.loop = channel.PendingLoop;
            channel.ActivePath = path;
            if (!channel.Source.isPlaying)
                channel.Source.Play();
        }

        void RemoveStoppedScriptLoopChannels()
        {
            _scriptLoopRemovalKeys.Clear();
            foreach (var pair in _scriptLoopChannels)
            {
                var channel = pair.Value;
                if (channel == null || channel.Operation != null || !string.IsNullOrWhiteSpace(channel.PendingPath))
                    continue;

                if (channel.Source != null && (channel.Source.isPlaying || channel.Source.volume > 0.0001f))
                    continue;

                _scriptLoopRemovalKeys.Add(pair.Key);
            }

            for (int i = 0; i < _scriptLoopRemovalKeys.Count; i++)
            {
                ulong key = _scriptLoopRemovalKeys[i];
                if (!_scriptLoopChannels.TryGetValue(key, out var channel))
                    continue;

                RemoveScriptLoopChannel(key);
            }

            _scriptLoopRemovalKeys.Clear();
        }

        void RemoveScriptLoopChannel(ulong key)
        {
            if (!_scriptLoopChannels.TryGetValue(key, out var channel))
                return;

            DisposeChannel(channel);
            if (channel.Source != null)
                UnityEngine.Object.Destroy(channel.Source.gameObject);

            _scriptLoopChannels.Remove(key);
            _scriptLoopTouched.Remove(key);
        }

        void TickPendingEvents()
        {
            for (int i = _pendingEventRequests.Count - 1; i >= 0; i--)
            {
                var request = _pendingEventRequests[i];
                if (request.Operation == null || !request.Operation.isDone)
                    continue;

                if (request.Request.result != UnityWebRequest.Result.Success)
                {
                    WarnLoadFailure(request.Path, request.ChannelName, request.Request.error);
                    if (request.TrackSay)
                        StopTrackingSayPlayback(request.EventSequence);
                    DisposeOneShotRequest(request);
                    _pendingEventRequests.RemoveAt(i);
                    continue;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request.Request);
                if (clip == null)
                {
                    WarnLoadFailure(request.Path, request.ChannelName, "request completed without an AudioClip.");
                    if (request.TrackSay)
                        StopTrackingSayPlayback(request.EventSequence);
                    DisposeOneShotRequest(request);
                    _pendingEventRequests.RemoveAt(i);
                    continue;
                }

                _clipCache[request.Path] = clip;
                AudioSource source = PlayOneShot(request, clip);
                if (request.TrackSay)
                    TrackSayPlayback(request.SourceEntity, request.SourcePlacedRefId, request.EventSequence, source, loading: false);
                DisposeOneShotRequest(request);
                _pendingEventRequests.RemoveAt(i);
            }
        }

        void PlayRegionEvent(AudioClip clip, float volume, float pitch)
        {
            using var _ = k_PlayRegionEvent.Auto();

            if (clip == null || _regionEventSources.Length == 0)
                return;
            if (CountPlayingRegionEventSources() > 0)
                return;

            int sourceIndex = _nextRegionEventSource;
            _nextRegionEventSource = (_nextRegionEventSource + 1) % _regionEventSources.Length;

            var source = _regionEventSources[sourceIndex];
            // Region events route through the Effects bus (master × effects scalar)
            // so the Options sliders apply to one-shots the same way they apply to
            // the looping ambient channel.
            source.volume = Mathf.Clamp01(volume * ComputeBusMultiplier(ChannelBus.Effects));
            source.pitch = Mathf.Max(0.01f, pitch);
            source.PlayOneShot(clip, 1f);
        }

        bool HasActiveRegionEvent()
        {
            return CountPlayingRegionEventSources() > 0 || CountPendingRegionEventLoads() > 0;
        }

        int CountPlayingRegionEventSources()
        {
            int count = 0;
            for (int i = 0; i < _regionEventSources.Length; i++)
            {
                var source = _regionEventSources[i];
                if (source != null && source.isPlaying)
                    count++;
            }

            return count;
        }

        int CountPendingRegionEventLoads()
        {
            int count = 0;
            for (int i = 0; i < _pendingEventRequests.Count; i++)
            {
                var request = _pendingEventRequests[i];
                if (request != null && !request.Spatial && string.Equals(request.ChannelName, RegionEventChannelName, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        void QueueInteractionAudioEvent(RuntimeContentDatabase contentDb, in InteractionAudioRequest request, in AudioTuningState tuning)
        {
            if (contentDb == null || !request.Sound.IsValid)
                return;

            string path = ResolveSoundPath(contentDb, request.Sound);
            if (string.IsNullOrWhiteSpace(path))
                return;

            float volume = ResolveSoundVolume(
                contentDb,
                request.Sound,
                tuning.InteractionFallbackBaseVolume,
                tuning.InteractionVolumeMultiplier);
            ResolveSoundRange(
                contentDb,
                request.Sound,
                tuning.InteractionMinDistanceMultiplier,
                tuning.InteractionMaxDistanceMultiplier,
                out float minDistance,
                out float maxDistance);

            Vector3 position = request.Position;
            if (IsBeyondSpatialMaxDistance(position, maxDistance))
                return;

            CancelPendingMatchingOneShots(path, Entity.Null, request.SourcePlacedRefId, spatial: true, matchAnySayForSource: false);

            if (_clipCache.TryGetValue(path, out var cachedClip) && cachedClip != null)
            {
                using var cacheHit = k_ClipCacheHit.Auto();
                PlayInteractionEvent(cachedClip, path, Entity.Null, request.SourcePlacedRefId, volume, 1f, position, minDistance, maxDistance);
                return;
            }

            var pendingRequest = new OneShotRequest
            {
                Path = path,
                ChannelName = InteractionEventChannelName,
                Volume = volume,
                Pitch = 1f,
                EventSequence = request.Sequence,
                SourcePlacedRefId = request.SourcePlacedRefId,
                Position = position,
                MinDistance = minDistance,
                MaxDistance = maxDistance,
                Spatial = true,
                Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, GetAudioType(path)),
            };
            pendingRequest.Operation = pendingRequest.Request.SendWebRequest();
            _pendingEventRequests.Add(pendingRequest);
        }

        AudioSource PlayOneShot(OneShotRequest request, AudioClip clip)
        {
            if (request == null)
                return null;

            if (string.Equals(request.ChannelName, RegionEventChannelName, StringComparison.Ordinal))
            {
                PlayRegionEvent(clip, request.Volume, request.Pitch <= 0f ? 1f : request.Pitch);
                return null;
            }

            if (request.Spatial)
            {
                return PlayInteractionEvent(clip, request.Path, request.SourceEntity, request.SourcePlacedRefId, request.Volume, request.Pitch <= 0f ? 1f : request.Pitch, request.Position, request.MinDistance, request.MaxDistance);
            }

            return PlayNonSpatialEffectEvent(clip, request.Path, request.SourceEntity, request.SourcePlacedRefId, request.Volume, request.Pitch <= 0f ? 1f : request.Pitch);
        }

        AudioSource PlayInteractionEvent(AudioClip clip, string path, Entity sourceEntity, uint sourcePlacedRefId, float volume, float pitch, Vector3 position, float minDistance, float maxDistance)
        {
            using var _ = k_PlayInteractionEvent.Auto();

            if (clip == null || _interactionEventSources.Length == 0)
                return null;
            if (IsBeyondSpatialMaxDistance(position, maxDistance))
                return null;

            StopMatchingInteractionEventSource(path, sourceEntity, sourcePlacedRefId, spatial: true);

            if (!TryGetIdleInteractionEventSource(out var source, out int sourceIndex))
            {
                if (!_interactionEventPoolExhaustedWarningLogged)
                {
                    Debug.LogWarning(
                        $"[VVardenfell][Audio] interaction one-shot pool exhausted ({_interactionEventSources.Length} sources); dropping spatial one-shot '{clip.name}' to avoid cutting off active audio.");
                    _interactionEventPoolExhaustedWarningLogged = true;
                }

                return null;
            }

            _interactionEventPoolExhaustedWarningLogged = false;
            SetInteractionEventSourceTag(sourceIndex, path, sourceEntity, sourcePlacedRefId);
            source.spatialBlend = 1f;
            source.transform.position = position;
            source.minDistance = Mathf.Max(0f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            // Interaction SFX also route through the Effects bus.
            source.volume = Mathf.Clamp01(volume * ComputeBusMultiplier(ChannelBus.Effects));
            source.pitch = Mathf.Max(0.01f, pitch);
            source.clip = clip;
            source.Play();
            return source;
        }

        AudioSource PlayNonSpatialEffectEvent(AudioClip clip, string path, Entity sourceEntity, uint sourcePlacedRefId, float volume, float pitch)
        {
            using var _ = k_PlayInteractionEvent.Auto();

            if (clip == null || _interactionEventSources.Length == 0)
                return null;

            StopMatchingInteractionEventSource(path, sourceEntity, sourcePlacedRefId, spatial: false);

            if (!TryGetIdleInteractionEventSource(out var source, out int sourceIndex))
            {
                if (!_interactionEventPoolExhaustedWarningLogged)
                {
                    Debug.LogWarning(
                        $"[VVardenfell][Audio] interaction one-shot pool exhausted ({_interactionEventSources.Length} sources); dropping non-spatial one-shot '{clip.name}' to avoid cutting off active audio.");
                    _interactionEventPoolExhaustedWarningLogged = true;
                }

                return null;
            }

            _interactionEventPoolExhaustedWarningLogged = false;
            SetInteractionEventSourceTag(sourceIndex, path, sourceEntity, sourcePlacedRefId);
            source.spatialBlend = 0f;
            source.volume = Mathf.Clamp01(volume * ComputeBusMultiplier(ChannelBus.Effects));
            source.pitch = Mathf.Max(0.01f, pitch);
            source.clip = clip;
            source.Play();
            return source;
        }

        static bool IsBeyondSpatialMaxDistance(Vector3 position, float maxDistance)
        {
            if (maxDistance <= 0f || !TryGetListenerPosition(out var listenerPosition))
                return false;

            return (position - listenerPosition).sqrMagnitude > maxDistance * maxDistance;
        }

        bool TryGetIdleInteractionEventSource(out AudioSource source, out int sourceIndex)
        {
            for (int i = 0; i < _interactionEventSources.Length; i++)
            {
                int candidateIndex = (_nextInteractionEventSource + i) % _interactionEventSources.Length;
                var candidate = _interactionEventSources[candidateIndex];
                if (candidate == null || candidate.isPlaying)
                    continue;

                _nextInteractionEventSource = (candidateIndex + 1) % _interactionEventSources.Length;
                sourceIndex = candidateIndex;
                source = candidate;
                return true;
            }

            sourceIndex = -1;
            source = null;
            return false;
        }

        void StopMatchingInteractionEventSource(string path, Entity sourceEntity, uint sourcePlacedRefId, bool spatial)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            for (int i = 0; i < _interactionEventSources.Length; i++)
            {
                var source = _interactionEventSources[i];
                if (source == null || !source.isPlaying)
                    continue;

                if (!string.Equals(_interactionEventActivePaths[i], path, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (spatial && !SameOneShotSource(_interactionEventActiveSourceEntities[i], _interactionEventActiveSourcePlacedRefIds[i], sourceEntity, sourcePlacedRefId))
                    continue;

                StopInteractionEventSource(i);
            }
        }

        void StopInteractionEventSource(int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= _interactionEventSources.Length)
                return;

            var source = _interactionEventSources[sourceIndex];
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }

            ClearInteractionEventSourceTag(sourceIndex);
        }

        void SetInteractionEventSourceTag(int sourceIndex, string path, Entity sourceEntity, uint sourcePlacedRefId)
        {
            if (sourceIndex < 0 || sourceIndex >= _interactionEventSources.Length)
                return;

            _interactionEventActivePaths[sourceIndex] = path;
            _interactionEventActiveSourceEntities[sourceIndex] = sourceEntity;
            _interactionEventActiveSourcePlacedRefIds[sourceIndex] = sourcePlacedRefId;
        }

        void ClearInteractionEventSourceTag(int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= _interactionEventSources.Length)
                return;

            _interactionEventActivePaths[sourceIndex] = null;
            _interactionEventActiveSourceEntities[sourceIndex] = Entity.Null;
            _interactionEventActiveSourcePlacedRefIds[sourceIndex] = 0u;
        }

        ChannelState GetOrCreateScriptLoopChannel(ulong key)
        {
            if (_scriptLoopChannels.TryGetValue(key, out var channel))
                return channel;

            var source = CreateChildAudioSource($"script-loop-{key:x16}");
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.ignoreListenerPause = true;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            channel = new ChannelState($"script-loop-{key:x16}", source, ChannelBus.Effects);
            _scriptLoopChannels[key] = channel;
            return channel;
        }

        static ulong BuildScriptLoopKey(in MorrowindScriptAudioRequest request)
        {
            return MorrowindScriptOpcodeTable.BuildScriptLoopKey(request.SourcePlacedRefId, request.SourceEntity, request.Sound.Value);
        }

        static ulong ExtractScriptLoopSourceKey(ulong loopKey)
            => loopKey >> 32;

        static bool ContainsActiveScriptLoopSource(DynamicBuffer<MorrowindScriptActiveSource> activeSources, ulong sourceKey)
        {
            if (!activeSources.IsCreated)
                return false;

            for (int i = 0; i < activeSources.Length; i++)
            {
                if (activeSources[i].LoopSourceKey == sourceKey)
                    return true;
            }

            return false;
        }

        static bool SameOneShotSource(Entity activeEntity, uint activePlacedRefId, Entity sourceEntity, uint sourcePlacedRefId)
        {
            if (activePlacedRefId != 0u || sourcePlacedRefId != 0u)
                return activePlacedRefId == sourcePlacedRefId;

            return activeEntity != Entity.Null && activeEntity == sourceEntity;
        }

        static bool IsScriptLoopAudibleOrLoading(ChannelState channel)
        {
            if (channel == null)
                return false;

            if (channel.Operation != null || !string.IsNullOrWhiteSpace(channel.PendingPath))
                return true;

            return channel.Source != null && (channel.Source.isPlaying || channel.Source.volume > 0.0001f);
        }

        void CancelPendingMatchingOneShots(string path, Entity sourceEntity, uint sourcePlacedRefId, bool spatial, bool matchAnySayForSource)
        {
            if (string.IsNullOrWhiteSpace(path) && !matchAnySayForSource)
                return;

            for (int i = _pendingEventRequests.Count - 1; i >= 0; i--)
            {
                var pending = _pendingEventRequests[i];
                if (pending == null)
                    continue;

                bool matches;
                if (matchAnySayForSource)
                {
                    matches = pending.TrackSay
                        && SameOneShotSource(pending.SourceEntity, pending.SourcePlacedRefId, sourceEntity, sourcePlacedRefId);
                }
                else if (spatial)
                {
                    matches = pending.Spatial
                        && string.Equals(pending.Path, path, StringComparison.OrdinalIgnoreCase)
                        && SameOneShotSource(pending.SourceEntity, pending.SourcePlacedRefId, sourceEntity, sourcePlacedRefId);
                }
                else
                {
                    matches = !pending.Spatial
                        && string.Equals(pending.Path, path, StringComparison.OrdinalIgnoreCase);
                }

                if (!matches)
                    continue;

                if (pending.TrackSay)
                    StopTrackingSayPlayback(pending.EventSequence);
                DisposeOneShotRequest(pending);
                _pendingEventRequests.RemoveAt(i);
            }
        }

        void StopSayPlaybacksForSource(Entity sourceEntity, uint sourcePlacedRefId)
        {
            CancelPendingMatchingOneShots(null, sourceEntity, sourcePlacedRefId, spatial: false, matchAnySayForSource: true);

            for (int i = _activeSayPlaybacks.Count - 1; i >= 0; i--)
            {
                var active = _activeSayPlaybacks[i];
                if (!SameOneShotSource(active.SourceEntity, active.SourcePlacedRefId, sourceEntity, sourcePlacedRefId))
                    continue;

                StopInteractionEventSource(active.Source);
                _activeSayPlaybacks.RemoveAt(i);
            }
        }

        void StopInteractionEventSource(AudioSource source)
        {
            if (source == null)
                return;

            for (int i = 0; i < _interactionEventSources.Length; i++)
            {
                if (_interactionEventSources[i] != source)
                    continue;

                StopInteractionEventSource(i);
                return;
            }

            source.Stop();
            source.clip = null;
        }

        void TrackSayPlayback(Entity sourceEntity, uint sourcePlacedRefId, uint eventSequence, AudioSource source, bool loading)
        {
            if (sourceEntity == Entity.Null && sourcePlacedRefId == 0u)
                return;

            for (int i = 0; i < _activeSayPlaybacks.Count; i++)
            {
                var active = _activeSayPlaybacks[i];
                if (active.EventSequence != eventSequence)
                    continue;

                if (!loading && source == null)
                {
                    _activeSayPlaybacks.RemoveAt(i);
                    return;
                }

                active.Source = source;
                active.Loading = loading;
                return;
            }

            if (!loading && source == null)
                return;

            for (int i = _activeSayPlaybacks.Count - 1; i >= 0; i--)
            {
                var active = _activeSayPlaybacks[i];
                if (active.EventSequence == eventSequence)
                    continue;
                if (!SameOneShotSource(active.SourceEntity, active.SourcePlacedRefId, sourceEntity, sourcePlacedRefId))
                    continue;

                StopInteractionEventSource(active.Source);
                _activeSayPlaybacks.RemoveAt(i);
            }

            _activeSayPlaybacks.Add(new ActiveSayPlayback
            {
                SourceEntity = sourceEntity,
                SourcePlacedRefId = sourcePlacedRefId,
                Source = source,
                EventSequence = eventSequence,
                Loading = loading,
            });
        }

        void StopTrackingSayPlayback(uint eventSequence)
        {
            for (int i = _activeSayPlaybacks.Count - 1; i >= 0; i--)
            {
                if (_activeSayPlaybacks[i].EventSequence == eventSequence)
                    _activeSayPlaybacks.RemoveAt(i);
            }
        }

        void PruneCompletedSayPlaybacks()
        {
            for (int i = _activeSayPlaybacks.Count - 1; i >= 0; i--)
            {
                var active = _activeSayPlaybacks[i];
                if (active.Loading)
                    continue;

                if (active.Source == null || !active.Source.isPlaying)
                    _activeSayPlaybacks.RemoveAt(i);
            }
        }

        void CancelLoad(ChannelState channel, bool disposeClip = false)
        {
            if (channel.Request != null)
            {
                channel.Request.Dispose();
                channel.Request = null;
            }

            channel.Operation = null;
            if (disposeClip && channel.Source.clip != null)
            {
                UnityEngine.Object.Destroy(channel.Source.clip);
                channel.Source.clip = null;
            }
        }

        void DisposeChannel(ChannelState channel)
        {
            CancelLoad(channel);
            if (channel.Source != null)
            {
                channel.Source.Stop();
                channel.Source.clip = null;
            }
        }

        void DisposePendingEvents()
        {
            for (int i = 0; i < _pendingEventRequests.Count; i++)
            {
                if (_pendingEventRequests[i].TrackSay)
                    StopTrackingSayPlayback(_pendingEventRequests[i].EventSequence);
                DisposeOneShotRequest(_pendingEventRequests[i]);
            }
            _pendingEventRequests.Clear();
            _activeSayPlaybacks.Clear();
        }

        void CancelPendingOneShots(bool spatial)
        {
            for (int i = _pendingEventRequests.Count - 1; i >= 0; i--)
            {
                if (_pendingEventRequests[i].Spatial != spatial)
                    continue;

                if (_pendingEventRequests[i].TrackSay)
                    StopTrackingSayPlayback(_pendingEventRequests[i].EventSequence);
                DisposeOneShotRequest(_pendingEventRequests[i]);
                _pendingEventRequests.RemoveAt(i);
            }
        }

        static void DisposeOneShotRequest(OneShotRequest request)
        {
            if (request?.Request != null)
            {
                request.Request.Dispose();
                request.Request = null;
            }

            request.Operation = null;
        }

        void RefreshInstallPath()
        {
            if (!string.IsNullOrWhiteSpace(_installPath))
                return;

            if (ConfigStorage.TryLoad(out var config) && config != null)
                _installPath = config.InstallPath;
        }

        void StopRegionEvents()
        {
            for (int i = 0; i < _regionEventSources.Length; i++)
            {
                var source = _regionEventSources[i];
                if (source == null || !source.isPlaying)
                    continue;

                source.Stop();
            }
        }

        string NormalizeExistingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (File.Exists(path))
                return path;

            WarnMissing(path, "resolve");
            return null;
        }

        void WarnMissing(string path, string channelName)
        {
            if (string.IsNullOrWhiteSpace(path) || !_missingPathWarnings.Add(path))
                return;

            Debug.LogWarning($"[VVardenfell][Audio] missing audio file for {channelName}: '{path}'.");
        }

        void WarnLoadFailure(string path, string channelName, string error)
        {
            string key = $"{channelName}|{path}|{error}";
            if (!_loadFailureWarnings.Add(key))
                return;

            Debug.LogWarning($"[VVardenfell][Audio] failed loading {channelName} clip '{path}': {error}");
        }

        float ResolveMusicVolume(in MusicState state, in AudioTuningState tuning)
        {
            float categoryScalar = state.Category switch
            {
                MusicTrackCategory.Special => tuning.MusicMenuSpecialScalar,
                MusicTrackCategory.Explore => tuning.MusicExploreScalar,
                MusicTrackCategory.Battle => tuning.MusicBattleScalar,
                _ => 1f,
            };

            return Mathf.Clamp01(tuning.MusicGlobalVolume * Mathf.Max(0f, categoryScalar));
        }

        void ResolveSoundRange(
            RuntimeContentDatabase contentDb,
            SoundDefHandle handle,
            float minMultiplier,
            float maxMultiplier,
            out float minDistance,
            out float maxDistance)
        {
            minDistance = 0f;
            maxDistance = 0f;

            if (contentDb == null || !handle.IsValid)
                return;

            ref readonly var sound = ref contentDb.Get(handle);
            float minRange = sound.MinRange;
            float maxRange = sound.MaxRange;
            if (sound.MinRange == 0 && sound.MaxRange == 0)
            {
                minRange = contentDb.RequireGameSettingFloat("fAudioDefaultMinDistance");
                maxRange = contentDb.RequireGameSettingFloat("fAudioDefaultMaxDistance");
            }

            minRange *= Mathf.Max(0f, minMultiplier);
            maxRange *= Mathf.Max(0f, maxMultiplier);
            float audioMinMultiplier = contentDb.RequireGameSettingFloat("fAudioMinDistanceMult");
            float audioMaxMultiplier = contentDb.RequireGameSettingFloat("fAudioMaxDistanceMult");
            minRange *= Mathf.Max(0f, audioMinMultiplier);
            maxRange *= Mathf.Max(0f, audioMaxMultiplier);

            minRange = Mathf.Max(minRange, 1f);
            maxRange = Mathf.Max(minRange, maxRange);

            minDistance = minRange * WorldScale.MwUnitsToMeters;
            maxDistance = maxRange * WorldScale.MwUnitsToMeters;
        }

        void ResolveVoiceSoundRange(RuntimeContentDatabase contentDb, out float minDistance, out float maxDistance)
        {
            minDistance = 0f;
            maxDistance = 0f;
            if (contentDb == null)
                return;

            float minRange = 0f;
            float maxRange = 0f;
            minRange = contentDb.RequireGameSettingFloat("fAudioVoiceDefaultMinDistance");
            maxRange = contentDb.RequireGameSettingFloat("fAudioVoiceDefaultMaxDistance");
            float audioMinMultiplier = contentDb.RequireGameSettingFloat("fAudioMinDistanceMult");
            float audioMaxMultiplier = contentDb.RequireGameSettingFloat("fAudioMaxDistanceMult");
            minRange *= Mathf.Max(0f, audioMinMultiplier);
            maxRange *= Mathf.Max(0f, audioMaxMultiplier);

            minRange = Mathf.Max(minRange, 1f);
            maxRange = Mathf.Max(minRange, maxRange);

            minDistance = minRange * WorldScale.MwUnitsToMeters;
            maxDistance = maxRange * WorldScale.MwUnitsToMeters;
        }

        static bool TryGetListenerPosition(out Vector3 position)
        {
            var listener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                position = listener.transform.position;
                return true;
            }

            position = default;
            return false;
        }

        static AudioType GetAudioType(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                return AudioType.WAV;
            if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
                return AudioType.OGGVORBIS;
            return AudioType.MPEG;
        }
    }
}
