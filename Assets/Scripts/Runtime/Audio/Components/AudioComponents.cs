using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct AudioEmitterAuthoring : IComponentData
    {
        public SoundDefHandle PrimarySound;
        public SoundDefHandle SecondarySound;
    }

    public enum AudioPlaybackMode : byte
    {
        Bootstrap = 0,
        Menu = 1,
        World = 2,
    }

    public struct AudioContextState : IComponentData
    {
        public AudioPlaybackMode Mode;
        public byte BootstrapPhase;
    }

    public struct MusicState : IComponentData
    {
        public MusicTrackDefHandle ResolvedTrack;
        public FixedString512Bytes DirectPath;
        public MusicTrackCategory Category;
        public byte Looping;
        public byte Scripted;
    }

    public struct MusicPlaylistState : IComponentData
    {
        public uint RandomState;
        public int LastPlayedTrackValue;
        public int CurrentTrackValue;
        public int ContentTrackCount;
        public byte ActiveCategory;
        public byte Initialized;
    }

    public struct MusicPlaybackStatus : IComponentData
    {
        public byte IsPlaying;
        public byte HasPendingTrack;
    }

    public struct MusicPlaylistEntry : IBufferElementData
    {
        public MusicTrackDefHandle Track;
    }

    public struct MorrowindMusicRequest : IBufferElementData
    {
        public MusicTrackDefHandle Track;
        public FixedString512Bytes DirectPath;
    }

    public struct InteriorAmbientState : IComponentData
    {
        public SoundDefHandle ResolvedSound;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public uint SourcePlacedRefId;
        public float3 SourcePosition;
        public float MinDistance;
        public float MaxDistance;
        public byte Looping;
    }

    public struct RegionAmbientState : IComponentData
    {
        public RegionDefHandle Region;
        public SoundDefHandle PendingEventSound;
        public uint EventSequence;
    }

    public struct WeatherAudioState : IComponentData
    {
        public SoundDefHandle ResolvedLoopSound;
        public SoundDefHandle ResolvedNextLoopSound;
        public float CurrentLoopVolume;
        public float NextLoopVolume;
        public uint LastThunderSequence;
    }

    public struct WeatherRainAudioState : IComponentData
    {
        public SoundDefHandle ResolvedLoopSound;
        public SoundDefHandle ResolvedNextLoopSound;
        public float CurrentLoopVolume;
        public float NextLoopVolume;
    }

    public struct NearWaterAudioState : IComponentData
    {
        public SoundDefHandle ResolvedLoopSound;
        public float Volume;
        public byte Looping;
        public byte IsInterior;
    }

    public struct AmbientSchedulerState : IComponentData
    {
        public float SecondsUntilNextAttempt;
        public uint RandomState;
        public int ActiveRegionHandleValue;
        public byte Initialized;
    }

    public struct AmbientSettingsState : IComponentData
    {
        public float MinSecondsBetweenEnvironmentalSounds;
        public float MaxSecondsBetweenEnvironmentalSounds;
    }

    public struct AudioTuningState : IComponentData
    {
        public float MusicGlobalVolume;
        public float MusicMenuSpecialScalar;
        public float MusicExploreScalar;
        public float MusicBattleScalar;
        public float InteriorAmbientVolumeMultiplier;
        public float InteriorAmbientFallbackBaseVolume;
        public float InteriorAmbientMinDistanceMultiplier;
        public float InteriorAmbientMaxDistanceMultiplier;
        public float ExteriorAmbientVolumeMultiplier;
        public float ExteriorAmbientFallbackBaseVolume;
        public float ExteriorAmbientMinIntervalMultiplier;
        public float ExteriorAmbientMaxIntervalMultiplier;
        public float ExteriorAmbientFallbackMinSeconds;
        public float ExteriorAmbientFallbackMaxSeconds;
        public float InteractionVolumeMultiplier;
        public float InteractionFallbackBaseVolume;
        public float InteractionMinDistanceMultiplier;
        public float InteractionMaxDistanceMultiplier;
    }

    public enum InteractionAudioKind : byte
    {
        None = 0,
        Door = 1,
        LooseItem = 2,
        Container = 3,
    }

    public struct InteractionAudioRequestState : IComponentData
    {
        public uint NextSequence;
        public uint LastConsumedSequence;
    }

    public struct InteractionAudioRequest : IBufferElementData
    {
        public uint Sequence;
        public SoundDefHandle Sound;
        public float3 Position;
        public uint SourcePlacedRefId;
        public byte Kind;
    }
}
