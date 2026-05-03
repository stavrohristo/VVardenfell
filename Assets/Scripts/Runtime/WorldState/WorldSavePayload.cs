using Unity.Mathematics;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldState
{
    public struct WorldSavePayload
    {
        public float3 PlayerPosition;
        public quaternion PlayerRotation;
        public float PlayerPitchDegrees;
        public ActorRuntimeStatSeed ActorStats;
        public ActorIdentitySet PlayerIdentity;
        public PlayerCrimeState PlayerCrime;
        public PlayerFactionMembership[] PlayerFactions;
        public bool InteriorActive;
        public string ActiveInteriorCellId;
        public uint NextJournalSequence;
        public uint NextRuntimeRefId;
        public PlayerInventoryItem[] Inventory;
        public ActorEquipmentSlot[] PlayerEquipment;
        public ActorKnownSpell[] KnownSpells;
        public ActorActiveMagicEffect[] ActiveMagicEffects;
        public LocalMapDiscoveryTilePayload[] ExteriorMapDiscovery;
        public GlobalMapOverlayPayload GlobalMapOverlay;
        public WorldJournalEntry[] JournalEntries;
        public MorrowindQuestJournalSavePayload QuestJournal;
        public MorrowindDialogueSavePayload Dialogue;
        public int[] ActorDeathCounts;
        public MorrowindTimeSavePayload Time;
        public MorrowindWeatherSavePayload Weather;
    }

    public struct MorrowindQuestJournalSavePayload
    {
        public uint NextEntrySequence;
        public MorrowindQuestJournalStateSavePayload[] States;
        public MorrowindQuestJournalEntrySavePayload[] Entries;
    }

    public struct MorrowindQuestJournalStateSavePayload
    {
        public int DialogueIndex;
        public int Index;
        public byte Started;
        public byte Finished;
    }

    public struct MorrowindQuestJournalEntrySavePayload
    {
        public uint Sequence;
        public int DialogueIndex;
        public int InfoIndex;
        public int JournalIndex;
        public int Day;
        public int Month;
        public int DayOfMonth;
        public byte QuestStatus;
    }

    public struct MorrowindDialogueSavePayload
    {
        public uint NextTopicEntrySequence;
        public int[] KnownTopicDialogueIndices;
        public MorrowindTopicJournalEntrySavePayload[] TopicEntries;
        public MorrowindFactionReactionSavePayload[] FactionReactions;
    }

    public struct MorrowindTopicJournalEntrySavePayload
    {
        public uint Sequence;
        public int DialogueIndex;
        public int InfoIndex;
        public uint ActorPlacedRefId;
        public string ActorId;
        public int Day;
        public int Month;
        public int DayOfMonth;
    }

    public struct MorrowindFactionReactionSavePayload
    {
        public int SourceFactionIndex;
        public int TargetFactionIndex;
        public int Reaction;
    }

    public struct MorrowindTimeSavePayload
    {
        public float GameHour;
        public int DaysPassed;
        public int Day;
        public int Month;
        public int Year;
        public float TimeScale;
        public float SimulationTimeScale;
    }

    public struct MorrowindWeatherSavePayload
    {
        public int CurrentWeather;
        public int NextWeather;
        public int QueuedWeather;
        public float Transition;
        public float TransitionFactor;
        public float TransitionDelta;
        public float HoursUntilNextChange;
        public float WeatherUpdateHoursRemaining;
        public int RegionHandleValue;
        public uint RandomState;
        public int ForcedWeather;
        public float SecondsUntilThunder;
        public float LightningBrightness;
        public uint ThunderSequence;
        public int LastThunderSoundIndex;
        public byte Initialized;
        public byte Transitioning;
        public MorrowindRegionWeatherCacheSavePayload[] RegionWeather;
    }

    public struct MorrowindRegionWeatherCacheSavePayload
    {
        public int RegionHandleValue;
        public int Weather;
    }

    public struct LocalMapDiscoveryTilePayload
    {
        public int2 Cell;
        public int Resolution;
        public byte[] Alpha;
    }

    public struct GlobalMapOverlayPayload
    {
        public int2 MinCell;
        public int2 MaxCell;
        public int CellPixelSize;
        public int Width;
        public int Height;
        public int2[] VisitedCells;
        public byte[] PngBytes;
    }
}
