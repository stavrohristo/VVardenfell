using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public struct LightInstanceFlags : IComponentData
    {
        public byte Carry;
        public byte Negative;
        public byte Flicker;
        public byte FlickerSlow;
        public byte Pulse;
        public byte PulseSlow;
        public byte OffDefault;
    }

    public struct LightInstanceState : IComponentData
    {
        public byte Enabled;
        public float3 BaseColorRgb;
        public float BaseIntensity;
        public float BaseRange;
        public float CurrentIntensity;
        public float CurrentRange;
        public float AnimationTime;
    }

    public struct LightPresentationLink : IComponentData
    {
        public int Slot;
    }

    public struct ActiveEnvironmentState : IComponentData
    {
        public float3 AmbientColorRgb;
        public float3 DirectionalColorRgb;
        public float3 FogColorRgb;
        public float FogDensity;
        public float FogNearMeters;
        public float FogFarMeters;
        public int RegionHandleValue;
        public byte IsInterior;
    }

    public struct AudioEmitterAuthoring : IComponentData
    {
        public SoundDefHandle PrimarySound;
        public SoundDefHandle SecondarySound;
    }

    public struct InteriorAmbientSourceAuthoring : IComponentData
    {
        public SoundDefHandle AmbientSound;
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
        public MusicTrackCategory Category;
        public byte Looping;
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

    public struct InteriorAmbientState : IComponentData
    {
        public SoundDefHandle ResolvedSound;
        public FixedString128Bytes InteriorCellId;
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
