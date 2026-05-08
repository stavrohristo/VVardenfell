using Unity.Mathematics;
using VVardenfell.Core.Cache;
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
        public PlayerRaceAppearance PlayerAppearance;
        public PlayerCustomClass PlayerCustomClass;
        public CharacterGenerationState CharacterGeneration;
        public PlayerCrimeState PlayerCrime;
        public PlayerFactionMembership[] PlayerFactions;
        public bool InteriorActive;
        public string ActiveInteriorCellId;
        public uint NextJournalSequence;
        public uint NextRuntimeRefId;
        public PlayerInventoryItem[] Inventory;
        public ActorEquipmentSlot[] PlayerEquipment;
        public ActorKnownSpell[] KnownSpells;
        public ActorActiveSpell[] ActiveSpells;
        public ActorActiveMagicEffect[] ActiveMagicEffects;
        public ActorUsedPower[] UsedPowers;
        public LocalMapDiscoveryTilePayload[] ExteriorMapDiscovery;
        public GlobalMapOverlayPayload GlobalMapOverlay;
        public WorldJournalEntry[] JournalEntries;
        public BookReadHistoryEntry[] BookReadHistory;
        public MorrowindQuestJournalSavePayload QuestJournal;
        public MorrowindDialogueSavePayload Dialogue;
        public int[] ActorDeathCounts;
        public MorrowindTimeSavePayload Time;
        public MorrowindWeatherSavePayload Weather;
        public MorrowindCombatSavePayload Combat;
        public MorrowindMagicSavePayload Magic;
        public MorrowindScriptSavePayload Script;
        public PlacedRefStateSavePayload PlacedRefs;
    }

    public struct MorrowindScriptSavePayload
    {
        public uint NextAudioRequestSequence;
        public uint RandomState;
        public MorrowindScriptGlobalValue[] Globals;
        public MorrowindGlobalScriptSavePayload[] GlobalScripts;
        public MorrowindObjectScriptSavePayload[] ObjectScripts;
    }

    public struct MorrowindGlobalScriptSavePayload
    {
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public string DisabledReason;
        public uint TargetPlacedRefId;
        public MorrowindScriptLocalValue[] Locals;
    }

    public struct MorrowindObjectScriptSavePayload
    {
        public uint PlacedRefId;
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public string DisabledReason;
        public MorrowindScriptLocalValue[] Locals;
    }

    public struct PlacedRefStateSavePayload
    {
        public PlacedRefStateEntrySavePayload[] Entries;
        public PlacedRefActorInventorySavePayload[] ActorInventories;
    }

    public struct PlacedRefStateEntrySavePayload
    {
        public uint PlacedRefId;
        public byte HasDisabled;
        public byte Disabled;
        public byte HasLock;
        public int LockLevel;
        public byte Locked;
        public string KeyId;
        public string TrapId;
        public byte HasTransform;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public int2 ExteriorCell;
        public string InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct PlacedRefActorInventorySavePayload
    {
        public uint PlacedRefId;
        public ActorInventoryItem[] Items;
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
        public MorrowindRegionWeatherOverrideSavePayload[] RegionWeatherOverrides;
    }

    public struct MorrowindCombatSavePayload
    {
        public uint RandomState;
        public byte Initialized;
    }

    public struct MorrowindMagicSavePayload
    {
        public uint RandomState;
        public int NextActiveSpellId;
        public byte SelectedSourceKind;
        public SpellDefHandle SelectedSpell;
        public int SelectedInventoryIndex;
        public ContentReference SelectedItemContent;
        public EnchantmentDefHandle SelectedEnchantment;
        public byte Initialized;
    }

    public struct MorrowindRegionWeatherCacheSavePayload
    {
        public int RegionHandleValue;
        public int Weather;
    }

    public struct MorrowindRegionWeatherOverrideSavePayload
    {
        public int RegionHandleValue;
        public byte ClearChance;
        public byte CloudyChance;
        public byte FoggyChance;
        public byte OvercastChance;
        public byte RainChance;
        public byte ThunderChance;
        public byte AshChance;
        public byte BlightChance;
        public byte SnowChance;
        public byte BlizzardChance;
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
