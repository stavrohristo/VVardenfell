using System;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioMenuSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial struct MusicResolveSystem : ISystem
    {
        const string MenuMusicTrackRelativePath = "Special/morrowind title.mp3";

        static readonly ProfilerMarker k_MusicResolve = new("VV.Audio.ResolveMusic");

        int _lastPlaylistTrackCount;
        bool _loggedMusicPool;

        public void OnCreate(ref SystemState systemState)
        {
            _lastPlaylistTrackCount = -1;
            systemState.RequireForUpdate<AudioContextState>();
            systemState.RequireForUpdate<MusicState>();
            systemState.RequireForUpdate<MusicPlaylistState>();
            systemState.RequireForUpdate<MusicPlaybackStatus>();
            systemState.RequireForUpdate<MorrowindMusicRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            using var _ = k_MusicResolve.Auto();

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            LogMusicPoolOnce(ref contentBlob);

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var music = ref SystemAPI.GetSingletonRW<MusicState>().ValueRW;
            ref var playlist = ref SystemAPI.GetSingletonRW<MusicPlaylistState>().ValueRW;
            var playback = SystemAPI.GetSingleton<MusicPlaybackStatus>();
            var trackPool = SystemAPI.GetSingletonBuffer<MusicPlaylistEntry>();
            var scriptMusicRequests = SystemAPI.GetSingletonBuffer<MorrowindMusicRequest>();

            if (_lastPlaylistTrackCount != contentBlob.MusicTracks.Length)
            {
                _lastPlaylistTrackCount = contentBlob.MusicTracks.Length;
                trackPool.Clear();
                playlist = default;
            }

            switch (context.Mode)
            {
                case AudioPlaybackMode.Menu:
                    scriptMusicRequests.Clear();
                    music.Scripted = 0;
                    music.DirectPath = default;
                    music.Looping = 1;
                    music.ResolvedTrack = ResolveMenuTrack(ref contentBlob);
                    music.Category = music.ResolvedTrack.IsValid ? RuntimeContentBlobUtility.Get(ref contentBlob, music.ResolvedTrack).Category : MusicTrackCategory.Special;
                    playlist.CurrentTrackValue = 0;
                    break;
                case AudioPlaybackMode.World:
                    if (ResolveScriptTrack(ref contentBlob, ref music, ref playlist, playback, scriptMusicRequests))
                        break;

                    ResolveWorldTrack(ref contentBlob, ref music, ref playlist, playback, trackPool);
                    break;
                default:
                    scriptMusicRequests.Clear();
                    music.Scripted = 0;
                    music.DirectPath = default;
                    music.Looping = 0;
                    music.ResolvedTrack = default;
                    music.Category = MusicTrackCategory.Special;
                    playlist.CurrentTrackValue = 0;
                    break;
            }
        }

        void LogMusicPoolOnce(ref RuntimeContentBlob contentBlob)
        {
            if (_loggedMusicPool)
                return;

            _loggedMusicPool = true;

            var builder = new StringBuilder();
            builder.Append("[VVardenfell][Audio] music pool loaded: total=").Append(contentBlob.MusicTracks.Length);

            int exploreCount = 0;
            int battleCount = 0;
            int specialCount = 0;
            for (int i = 0; i < contentBlob.MusicTracks.Length; i++)
            {
                ref var track = ref contentBlob.MusicTracks[i];
                switch (track.Category)
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

            for (int i = 0; i < contentBlob.MusicTracks.Length; i++)
            {
                ref var track = ref contentBlob.MusicTracks[i];
                builder
                    .Append("\n  [")
                    .Append(i)
                    .Append("] ")
                    .Append(track.Category)
                    .Append(": ")
                    .Append(track.RelativePath.ToString());
            }

        }

        static MusicTrackDefHandle ResolveMenuTrack(ref RuntimeContentBlob contentBlob)
        {
            if (TryGetMusicTrackHandle(ref contentBlob, MenuMusicTrackRelativePath, out var handle))
                return handle;
            if (TryGetFirstMusicTrackByCategory(ref contentBlob, MusicTrackCategory.Special, out handle))
                return handle;
            if (TryGetFirstMusicTrackByCategory(ref contentBlob, MusicTrackCategory.Explore, out handle))
                return handle;
            return default;
        }

        static bool ResolveScriptTrack(
            ref RuntimeContentBlob contentBlob,
            ref MusicState music,
            ref MusicPlaylistState playlist,
            in MusicPlaybackStatus playback,
            DynamicBuffer<MorrowindMusicRequest> requests)
        {
            if (requests.Length > 0)
            {
                var request = requests[requests.Length - 1];
                requests.Clear();
                bool hasTrack = request.Track.IsValid;
                if (hasTrack && (request.Track.Index < 0 || request.Track.Index >= contentBlob.MusicTracks.Length))
                    throw new InvalidOperationException("[VVardenfell][Audio] StreamMusic request references invalid music content.");
                if (!hasTrack && request.DirectPath.IsEmpty)
                    throw new InvalidOperationException("[VVardenfell][Audio] StreamMusic request has no music content.");

                music.Looping = 0;
                music.Scripted = 1;
                music.ResolvedTrack = request.Track;
                music.DirectPath = request.DirectPath;
                music.Category = hasTrack ? RuntimeContentBlobUtility.Get(ref contentBlob, request.Track).Category : MusicTrackCategory.Special;
                playlist.CurrentTrackValue = request.Track.Value;
                return true;
            }

            if (music.Scripted == 0)
                return false;

            bool currentTrackPendingOrPlaying = playback.HasPendingTrack != 0 || playback.IsPlaying != 0;
            if ((music.ResolvedTrack.IsValid || !music.DirectPath.IsEmpty) && currentTrackPendingOrPlaying)
                return true;

            music.Scripted = 0;
            music.DirectPath = default;
            playlist.CurrentTrackValue = 0;
            return false;
        }

        static void ResolveWorldTrack(
            ref RuntimeContentBlob contentBlob,
            ref MusicState music,
            ref MusicPlaylistState playlist,
            in MusicPlaybackStatus playback,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            music.Looping = 0;
            music.DirectPath = default;
            music.Category = MusicTrackCategory.Explore;

            EnsurePlaylistReady(ref contentBlob, MusicTrackCategory.Explore, ref playlist, trackPool);

            bool currentTrackMatchesCategory = music.ResolvedTrack.IsValid
                && RuntimeContentBlobUtility.Get(ref contentBlob, music.ResolvedTrack).Category == MusicTrackCategory.Explore;
            bool currentTrackPendingOrPlaying = playback.HasPendingTrack != 0 || playback.IsPlaying != 0;

            if (!currentTrackMatchesCategory || playlist.CurrentTrackValue == 0)
            {
                music.ResolvedTrack = SelectNextTrack(ref contentBlob, MusicTrackCategory.Explore, ref playlist, trackPool);
                LogSelectedTrack(ref contentBlob, music.ResolvedTrack, "initial world track");
                return;
            }

            if (!currentTrackPendingOrPlaying)
            {
                music.ResolvedTrack = SelectNextTrack(ref contentBlob, MusicTrackCategory.Explore, ref playlist, trackPool);
                LogSelectedTrack(ref contentBlob, music.ResolvedTrack, "advanced world track");
            }
        }

        static void EnsurePlaylistReady(
            ref RuntimeContentBlob contentBlob,
            MusicTrackCategory category,
            ref MusicPlaylistState playlist,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            if (playlist.RandomState == 0u)
                playlist.RandomState = CreateMusicSeed(category, contentBlob.MusicTracks.Length);

            bool categoryChanged = playlist.ActiveCategory != (byte)category;
            bool contentChanged = playlist.ContentTrackCount != contentBlob.MusicTracks.Length;
            if (playlist.Initialized == 0 || categoryChanged || contentChanged)
            {
                RefillTrackPool(ref contentBlob, category, trackPool);
                playlist.ActiveCategory = (byte)category;
                playlist.ContentTrackCount = contentBlob.MusicTracks.Length;
                playlist.CurrentTrackValue = 0;
                playlist.Initialized = 1;
            }
        }

        static MusicTrackDefHandle SelectNextTrack(
            ref RuntimeContentBlob contentBlob,
            MusicTrackCategory category,
            ref MusicPlaylistState playlist,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            if (trackPool.Length == 0)
                RefillTrackPool(ref contentBlob, category, trackPool);

            if (trackPool.Length == 0)
            {
                playlist.CurrentTrackValue = 0;
                if (TryGetFirstMusicTrackByCategory(ref contentBlob, MusicTrackCategory.Special, out var specialFallback))
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
            ref RuntimeContentBlob contentBlob,
            MusicTrackCategory category,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            trackPool.Clear();
            for (int i = 0; i < contentBlob.MusicTracks.Length; i++)
            {
                if (contentBlob.MusicTracks[i].Category != category)
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

        static void LogSelectedTrack(ref RuntimeContentBlob contentBlob, MusicTrackDefHandle handle, string reason)
        {
            if (!handle.IsValid)
                return;

            ref var track = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
        }

        static bool TryGetMusicTrackHandle(ref RuntimeContentBlob contentBlob, string relativePath, out MusicTrackDefHandle handle)
        {
            for (int i = 0; i < contentBlob.MusicTracks.Length; i++)
            {
                if (string.Equals(contentBlob.MusicTracks[i].RelativePath.ToString(), relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    handle = MusicTrackDefHandle.FromIndex(i);
                    return true;
                }
            }

            handle = default;
            return false;
        }

        static bool TryGetFirstMusicTrackByCategory(ref RuntimeContentBlob contentBlob, MusicTrackCategory category, out MusicTrackDefHandle handle)
        {
            for (int i = 0; i < contentBlob.MusicTracks.Length; i++)
            {
                if (contentBlob.MusicTracks[i].Category == category)
                {
                    handle = MusicTrackDefHandle.FromIndex(i);
                    return true;
                }
            }

            handle = default;
            return false;
        }
    }
}
