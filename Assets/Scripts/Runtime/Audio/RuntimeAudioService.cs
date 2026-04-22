using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Audio
{
    public sealed class RuntimeAudioService : IDisposable
    {
        const float DefaultFadeSpeed = 2.4f;
        const int RegionEventSourceCount = 4;
        const int InteractionEventSourceCount = 8;

        static readonly ProfilerMarker k_ClipLoad = new("VV.Audio.ClipLoad");
        static readonly ProfilerMarker k_ClipCacheHit = new("VV.Audio.ClipCacheHit");
        static readonly ProfilerMarker k_QueueRegionEvent = new("VV.Audio.QueueRegionEvent");
        static readonly ProfilerMarker k_PlayRegionEvent = new("VV.Audio.PlayRegionEvent");
        static readonly ProfilerMarker k_QueueInteractionEvent = new("VV.Audio.QueueInteractionEvent");
        static readonly ProfilerMarker k_PlayInteractionEvent = new("VV.Audio.PlayInteractionEvent");

        sealed class ChannelState
        {
            public readonly string Name;
            public readonly AudioSource Source;

            public string ActivePath;
            public string PendingPath;
            public bool PendingLoop;
            public float TargetVolume;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;

            public ChannelState(string name, AudioSource source)
            {
                Name = name;
                Source = source;
            }
        }

        sealed class OneShotRequest
        {
            public string Path;
            public string ChannelName;
            public float Volume;
            public uint EventSequence;
            public Vector3 Position;
            public float MinDistance;
            public float MaxDistance;
            public bool Spatial;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
        }

        readonly Dictionary<string, AudioClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _missingPathWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loadFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
        readonly List<OneShotRequest> _pendingEventRequests = new();

        readonly GameObject _root;
        readonly ChannelState _music;
        readonly ChannelState _interiorAmbient;
        readonly AudioSource[] _regionEventSources;
        readonly AudioSource[] _interactionEventSources;

        int _nextRegionEventSource;
        int _nextInteractionEventSource;
        uint _lastQueuedRegionEventSequence;
        bool _regionAmbientEnabled = true;
        string _installPath;

        public bool IsMusicPlaying => _music?.Source != null && _music.Source.isPlaying;
        public bool HasPendingMusicTrack =>
            _music != null
            && (_music.Operation != null || (_music.Source != null && _music.Source.clip == null && !string.IsNullOrWhiteSpace(_music.PendingPath)));

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

            _music = new ChannelState("music", musicSource);
            _interiorAmbient = new ChannelState("interior-ambient", interiorAmbientSource);

            _regionEventSources = new AudioSource[RegionEventSourceCount];
            for (int i = 0; i < _regionEventSources.Length; i++)
            {
                var eventSource = _root.AddComponent<AudioSource>();
                eventSource.playOnAwake = false;
                eventSource.loop = false;
                eventSource.spatialBlend = 0f;
                eventSource.ignoreListenerPause = true;
                _regionEventSources[i] = eventSource;
            }

            _interactionEventSources = new AudioSource[InteractionEventSourceCount];
            for (int i = 0; i < _interactionEventSources.Length; i++)
            {
                var eventSource = _root.AddComponent<AudioSource>();
                eventSource.playOnAwake = false;
                eventSource.loop = false;
                eventSource.spatialBlend = 1f;
                eventSource.ignoreListenerPause = true;
                eventSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _interactionEventSources[i] = eventSource;
            }

            RefreshInstallPath();
        }

        public void Dispose()
        {
            DisposeChannel(_music);
            DisposeChannel(_interiorAmbient);
            DisposePendingEvents();

            foreach (var clip in _clipCache.Values)
            {
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
            }
            _clipCache.Clear();

            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }

        public void SyncMusic(RuntimeContentDatabase contentDb, MusicState state, in AudioTuningState tuning)
        {
            string path = ResolveMusicPath(contentDb, state.ResolvedTrack);
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

        public void QueueRegionAmbientEvent(RuntimeContentDatabase contentDb, RegionAmbientState state, in AudioTuningState tuning)
        {
            using var _ = k_QueueRegionEvent.Auto();

            if (!_regionAmbientEnabled || state.EventSequence == 0 || state.EventSequence == _lastQueuedRegionEventSequence || !state.PendingEventSound.IsValid)
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
                PlayRegionEvent(cachedClip, volume);
                return;
            }

            var request = new OneShotRequest
            {
                Path = path,
                ChannelName = "region-event",
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

        public void Tick(float deltaTime)
        {
            RefreshInstallPath();
            TickChannel(_music, deltaTime);
            TickChannel(_interiorAmbient, deltaTime);
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

        string ResolveSoundPath(RuntimeContentDatabase contentDb, SoundDefHandle handle)
        {
            if (contentDb == null || !handle.IsValid || string.IsNullOrWhiteSpace(_installPath))
                return null;

            ref readonly var sound = ref contentDb.Get(handle);
            if (string.IsNullOrWhiteSpace(sound.SoundPath))
                return null;

            string relativePath = SoundPathResolver.Correct(sound.SoundPath);
            string wavPath = Path.Combine(_installPath, "Data Files", relativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(wavPath))
                return wavPath;

            string mp3RelativePath = SoundPathResolver.ChangeExtension(relativePath, ".mp3");
            string mp3Path = Path.Combine(_installPath, "Data Files", mp3RelativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(mp3Path))
                return mp3Path;

            WarnMissing(wavPath, "resolve");
            return null;
        }

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
            channel.TargetVolume = Mathf.Clamp01(targetVolume);

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
                    DisposeOneShotRequest(request);
                    _pendingEventRequests.RemoveAt(i);
                    continue;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request.Request);
                if (clip == null)
                {
                    WarnLoadFailure(request.Path, request.ChannelName, "request completed without an AudioClip.");
                    DisposeOneShotRequest(request);
                    _pendingEventRequests.RemoveAt(i);
                    continue;
                }

                _clipCache[request.Path] = clip;
                PlayOneShot(request, clip);
                DisposeOneShotRequest(request);
                _pendingEventRequests.RemoveAt(i);
            }
        }

        void PlayRegionEvent(AudioClip clip, float volume)
        {
            using var _ = k_PlayRegionEvent.Auto();

            if (clip == null || _regionEventSources.Length == 0)
                return;

            int sourceIndex = _nextRegionEventSource;
            _nextRegionEventSource = (_nextRegionEventSource + 1) % _regionEventSources.Length;

            var source = _regionEventSources[sourceIndex];
            source.volume = Mathf.Clamp01(volume);
            source.pitch = 1f;
            source.PlayOneShot(clip, 1f);
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

            if (_clipCache.TryGetValue(path, out var cachedClip) && cachedClip != null)
            {
                using var cacheHit = k_ClipCacheHit.Auto();
                PlayInteractionEvent(cachedClip, volume, request.Position, minDistance, maxDistance);
                return;
            }

            var pendingRequest = new OneShotRequest
            {
                Path = path,
                ChannelName = "interaction-event",
                Volume = volume,
                EventSequence = request.Sequence,
                Position = new Vector3(request.Position.x, request.Position.y, request.Position.z),
                MinDistance = minDistance,
                MaxDistance = maxDistance,
                Spatial = true,
                Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, GetAudioType(path)),
            };
            pendingRequest.Operation = pendingRequest.Request.SendWebRequest();
            _pendingEventRequests.Add(pendingRequest);
        }

        void PlayOneShot(OneShotRequest request, AudioClip clip)
        {
            if (request == null)
                return;

            if (request.Spatial)
            {
                PlayInteractionEvent(clip, request.Volume, request.Position, request.MinDistance, request.MaxDistance);
                return;
            }

            PlayRegionEvent(clip, request.Volume);
        }

        void PlayInteractionEvent(AudioClip clip, float volume, Vector3 position, float minDistance, float maxDistance)
        {
            using var _ = k_PlayInteractionEvent.Auto();

            if (clip == null || _interactionEventSources.Length == 0)
                return;

            int sourceIndex = _nextInteractionEventSource;
            _nextInteractionEventSource = (_nextInteractionEventSource + 1) % _interactionEventSources.Length;

            var source = _interactionEventSources[sourceIndex];
            source.transform.position = position;
            source.minDistance = Mathf.Max(0f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.volume = Mathf.Clamp01(volume);
            source.pitch = 1f;
            source.PlayOneShot(clip, 1f);
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
                DisposeOneShotRequest(_pendingEventRequests[i]);
            _pendingEventRequests.Clear();
        }

        void CancelPendingOneShots(bool spatial)
        {
            for (int i = _pendingEventRequests.Count - 1; i >= 0; i--)
            {
                if (_pendingEventRequests[i].Spatial != spatial)
                    continue;

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
            minDistance = Mathf.Max(0f, sound.MinRange * Mathf.Max(0f, minMultiplier));
            maxDistance = Mathf.Max(minDistance, sound.MaxRange * Mathf.Max(0f, maxMultiplier));
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
