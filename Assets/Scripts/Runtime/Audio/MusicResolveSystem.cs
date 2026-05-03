using System;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioMenuSystemGroup))]
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
            RequireForUpdate<MorrowindMusicRequest>();
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
            var scriptMusicRequests = SystemAPI.GetSingletonBuffer<MorrowindMusicRequest>();

            if (!ReferenceEquals(_lastPlaylistContentDb, contentDb))
            {
                _lastPlaylistContentDb = contentDb;
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
                    music.ResolvedTrack = ResolveMenuTrack(contentDb);
                    music.Category = music.ResolvedTrack.IsValid ? contentDb.Get(music.ResolvedTrack).Category : MusicTrackCategory.Special;
                    playlist.CurrentTrackValue = 0;
                    break;
                case AudioPlaybackMode.World:
                    if (ResolveScriptTrack(contentDb, ref music, ref playlist, playback, scriptMusicRequests))
                        break;

                    ResolveWorldTrack(contentDb, ref music, ref playlist, playback, trackPool);
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

            var tracks = contentDb.Data?.MusicTracks ?? Array.Empty<MusicTrackDef>();
            var builder = new StringBuilder();
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

        static bool ResolveScriptTrack(
            RuntimeContentDatabase contentDb,
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
                if (hasTrack && (contentDb == null || request.Track.Index < 0 || request.Track.Index >= contentDb.MusicTrackCount))
                    throw new InvalidOperationException("[VVardenfell][Audio] StreamMusic request references invalid music content.");
                if (!hasTrack && request.DirectPath.IsEmpty)
                    throw new InvalidOperationException("[VVardenfell][Audio] StreamMusic request has no music content.");

                music.Looping = 0;
                music.Scripted = 1;
                music.ResolvedTrack = request.Track;
                music.DirectPath = request.DirectPath;
                music.Category = hasTrack ? contentDb.Get(request.Track).Category : MusicTrackCategory.Special;
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
            RuntimeContentDatabase contentDb,
            ref MusicState music,
            ref MusicPlaylistState playlist,
            in MusicPlaybackStatus playback,
            DynamicBuffer<MusicPlaylistEntry> trackPool)
        {
            music.Looping = 0;
            music.DirectPath = default;
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

            var tracks = contentDb.Data?.MusicTracks ?? Array.Empty<MusicTrackDef>();
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
        }
    }
}
