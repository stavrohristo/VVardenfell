using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeContentBlob
    {
        public BlobArray<RuntimeActorDefBlob> Actors;
        public BlobArray<RuntimeActorSpellDefBlob> ActorSpells;
        public BlobArray<RuntimeContainerItemDefBlob> ActorInventoryItems;
        public BlobArray<RuntimeActorAiPackageDefBlob> ActorAiPackages;
        public BlobArray<RuntimeActorTravelDestinationDefBlob> ActorTravelDestinations;
        public BlobArray<RuntimeBaseDefBlob> Activators;
        public BlobArray<RuntimeBaseDefBlob> Doors;
        public BlobArray<RuntimeBaseDefBlob> Containers;
        public BlobArray<ContainerContentRangeDef> ContainerContentRanges;
        public BlobArray<RuntimeContainerItemDefBlob> ContainerItems;
        public BlobArray<RuntimeBaseDefBlob> Items;
        public BlobArray<ItemEquipmentDef> ItemEquipment;
        public BlobArray<RuntimeItemEquipmentBodyPartDefBlob> ItemEquipmentBodyParts;
        public BlobArray<RuntimeLightDefBlob> Lights;
        public BlobArray<RuntimeItemLeveledListDefBlob> ItemLeveledLists;
        public BlobArray<RuntimeItemLeveledListEntryDefBlob> ItemLeveledListEntries;
        public BlobArray<RuntimeItemLeveledListDefBlob> CreatureLeveledLists;
        public BlobArray<RuntimeItemLeveledListEntryDefBlob> CreatureLeveledListEntries;
        public BlobArray<RuntimeSoundDefBlob> Sounds;
        public BlobArray<RuntimeDialogueDefBlob> Dialogues;
        public BlobArray<RuntimeDialogueInfoDefBlob> DialogueInfos;
        public BlobArray<RuntimeDialogueConditionDefBlob> DialogueConditions;
        public BlobArray<RuntimeSpellDefBlob> Spells;
        public BlobArray<RuntimeEnchantmentDefBlob> Enchantments;
        public BlobArray<RuntimeMagicEffectDefBlob> MagicEffects;
        public BlobArray<MagicEffectInstanceDef> MagicEffectInstances;
        public BlobArray<RuntimeRegionDefBlob> Regions;
        public BlobArray<RuntimeRegionSoundRefDefBlob> RegionSoundRefs;
        public BlobArray<RuntimeMusicTrackDefBlob> MusicTracks;
        public AmbientSettingsDef AmbientSettings;
        public WeatherSettingsDef WeatherSettings;
        public BlobArray<RuntimeWeatherDefinitionDefBlob> WeatherDefinitions;
        public RuntimeSkyWeatherVisualSettingsDefBlob SkyWeatherVisualSettings;
        public BlobArray<RuntimeGenericRecordDefBlob> GameSettings;
        public BlobArray<RuntimeGenericRecordDefBlob> Globals;
        public BlobArray<RuntimeClassDefBlob> Classes;
        public BlobArray<RuntimeFactionDefBlob> Factions;
        public BlobArray<RuntimeRaceDefBlob> Races;
        public BlobArray<RuntimeGenericRecordDefBlob> Birthsigns;
        public BlobArray<RuntimeGenericRecordDefBlob> Skills;
        public BlobArray<RuntimeGenericRecordDefBlob> Scripts;
        public BlobArray<RuntimeGenericRecordDefBlob> StartScripts;
        public BlobArray<RuntimeMorrowindScriptProgramDefBlob> MorrowindScriptPrograms;
        public BlobArray<MorrowindScriptInstructionDef> MorrowindScriptInstructions;
        public BlobArray<RuntimeMorrowindScriptLocalDefBlob> MorrowindScriptLocals;
        public BlobArray<RuntimeMorrowindScriptMessageDefBlob> MorrowindScriptMessages;
        public BlobArray<RuntimeExplicitRefTargetDefBlob> ExplicitRefTargets;
        public BlobArray<RuntimeGenericRecordDefBlob> SoundGenerators;
        public BlobArray<RuntimeGenericRecordDefBlob> LandTextures;
        public BlobArray<RuntimeGenericRecordDefBlob> Statics;
        public BlobArray<RuntimeGenericRecordDefBlob> BodyParts;
        public BlobArray<RuntimeActorBodyPartDefBlob> ActorBodyParts;
        public BlobArray<RuntimePathGridDefBlob> PathGrids;
        public BlobArray<PathGridPointDef> PathGridPoints;
        public BlobArray<PathGridConnectionDef> PathGridConnections;
        public BlobArray<PathGridNavigationNodeDef> PathGridNavigationNodes;
        public BlobArray<PathGridNavigationEdgeDef> PathGridNavigationEdges;
        public BlobArray<PathGridNavigationPortalDef> PathGridNavigationPortals;
        public BlobArray<PathGridNavigationAbstractEdgeDef> PathGridNavigationAbstractEdges;
        public BlobArray<PathGridNavigationNeighborDef> PathGridNavigationNeighbors;

        public BlobArray<int> ClassMinorSkills;
        public BlobArray<int> ClassMajorSkills;
        public BlobArray<RaceSkillBonusDef> RaceSkillBonuses;
        public BlobArray<int> RaceMaleAttributes;
        public BlobArray<int> RaceFemaleAttributes;
        public BlobArray<RuntimeContentStringBlob> RacePowerSpellIds;
        public BlobArray<RuntimeContentStringBlob> GenericRecordPowerSpellIds;
        public BlobArray<FactionRankRequirementDef> FactionRankRequirements;
        public BlobArray<int> FactionSkills;
        public BlobArray<RuntimeContentStringBlob> FactionRankNames;
        public BlobArray<RuntimeFactionReactionDefBlob> FactionReactions;
        public BlobArray<RuntimeContentStringBlob> SkyMasserPhaseTextures;
        public BlobArray<RuntimeContentStringBlob> SkySecundaPhaseTextures;
        public BlobArray<RuntimeContentStringBlob> SkyCloudTextures;
        public BlobArray<RuntimeContentStringBlob> SkyPrecipitationTextures;
        public BlobArray<RuntimeContentStringBlob> SkyPrecipitationEffectModels;

        public BlobArray<RuntimeContentHashLookupBlob> ActorIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ActivatorIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> DoorIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ContainerIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ItemIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> LightIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ItemLeveledListIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> CreatureLeveledListIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> SoundIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> DialogueIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> SpellIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> EnchantmentIdLookup;
        public BlobArray<RuntimeContentIntLookupBlob> MagicEffectIndexLookup;
        public BlobArray<RuntimeContentHashLookupBlob> RegionIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> MusicTrackPathLookup;
        public BlobArray<RuntimeContentHashLookupBlob> GameSettingIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> GlobalIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ClassIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> FactionIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> RaceIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> BirthsignIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> SkillIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ScriptIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> StartScriptIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> MorrowindScriptProgramIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> SoundGeneratorIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> LandTextureIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> StaticIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> BodyPartIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ActorBodyPartIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> PathGridIdLookup;
        public BlobArray<RuntimeContentHashLookupBlob> InteriorPathGridHashLookup;
        public BlobArray<RuntimeContentLongLookupBlob> ExteriorPathGridCoordLookup;
        public BlobArray<RuntimeContentExplicitRefLookupBlob> ExplicitRefTargetLookup;
        public BlobArray<RuntimeContentPlaceableLookupBlob> PlaceableLookup;
        public BlobArray<RuntimeContentIntLookupBlob> ItemIndexToEquipmentIndexLookup;
    }

    public struct RuntimeContentStringBlob
    {
        public BlobString Value;
    }

    public struct RuntimeContentHashLookupBlob
    {
        public ulong Hash;
        public int HandleValue;
    }

    public struct RuntimeContentIntLookupBlob
    {
        public int Key;
        public int Value;
    }

    public struct RuntimeContentLongLookupBlob
    {
        public long Key;
        public int Value;
    }

    public struct RuntimeContentExplicitRefLookupBlob
    {
        public ulong Hash;
        public uint PlacedRefId;
    }

    public struct RuntimeContentPlaceableLookupBlob
    {
        public ulong Hash;
        public ContentReference Content;
    }

    public struct RuntimeBaseDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Model;
        public BlobString Icon;
        public BlobString ScriptId;
        public BlobString SoundId;
        public BlobString AuxSoundId;
        public BlobString EnchantId;
        public BlobString Text;
        public ulong IdHash;
        public ulong ModelPathHash;
        public ulong ScriptIdHash;
        public ulong SoundIdHash;
        public ulong AuxSoundIdHash;
        public ulong EnchantIdHash;
        public ulong TextHash;
        public uint Flags;
        public float Float0;
        public int Int0;
        public int Int1;
        public int Int2;
        public int Int3;
    }

    public struct RuntimeItemEquipmentBodyPartDefBlob
    {
        public ItemDefHandle Item;
        public ItemEquipmentPartReference Part;
        public BlobString MaleBodyPartId;
        public BlobString FemaleBodyPartId;
    }

    public struct RuntimeGenericRecordDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Model;
        public BlobString Icon;
        public BlobString ScriptId;
        public BlobString Text;
        public ulong IdHash;
        public ulong ModelPathHash;
        public ulong ScriptIdHash;
        public ulong TextHash;
        public GenericRecordValueKind ValueKind;
        public uint Flags;
        public int Int0;
        public int Int1;
        public int Int2;
        public int FirstPowerSpellIdIndex;
        public int PowerSpellIdCount;
        public float Float0;
        public float Float1;
    }

    public struct RuntimeMorrowindScriptProgramDefBlob
    {
        public BlobString Id;
        public ulong IdHash;
        public int SourceScriptIndex;
        public byte Status;
        public BlobString DisabledReason;
        public int FirstInstructionIndex;
        public int InstructionCount;
        public int FirstLocalIndex;
        public int LocalCount;
        public int MaxStack;
    }

    public struct RuntimeMorrowindScriptLocalDefBlob
    {
        public BlobString Name;
        public ulong NameHash;
        public byte ValueKind;
    }

    public struct RuntimeMorrowindScriptMessageDefBlob
    {
        public BlobString Text;
    }

    public struct RuntimeExplicitRefTargetDefBlob
    {
        public BlobString Id;
        public ulong IdHash;
        public uint PlacedRefId;
    }

    public struct RuntimeClassDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Description;
        public ulong IdHash;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public int Specialization;
        public int FirstMinorSkillIndex;
        public int MinorSkillCount;
        public int FirstMajorSkillIndex;
        public int MajorSkillCount;
        public int Playable;
        public int Services;
    }

    public struct RuntimeFactionReactionDefBlob
    {
        public BlobString FactionId;
        public ulong FactionIdHash;
        public int Reaction;
    }

    public struct RuntimeFactionDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public ulong IdHash;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public int FirstRankRequirementIndex;
        public int RankRequirementCount;
        public int FirstSkillIndex;
        public int SkillCount;
        public int Hidden;
        public int FirstRankNameIndex;
        public int RankNameCount;
        public int FirstReactionIndex;
        public int ReactionCount;
    }

    public struct RuntimeRaceDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Description;
        public ulong IdHash;
        public int FirstSkillBonusIndex;
        public int SkillBonusCount;
        public int FirstMaleAttributeIndex;
        public int MaleAttributeCount;
        public int FirstFemaleAttributeIndex;
        public int FemaleAttributeCount;
        public float MaleHeight;
        public float FemaleHeight;
        public float MaleWeight;
        public float FemaleWeight;
        public int Flags;
        public int FirstPowerSpellIdIndex;
        public int PowerSpellIdCount;
    }

    public struct RuntimeContainerItemDefBlob
    {
        public BlobString ItemId;
        public ulong ItemIdHash;
        public int Count;
    }

    public struct RuntimeActorSpellDefBlob
    {
        public BlobString SpellId;
        public ulong SpellIdHash;
    }

    public struct RuntimeActorAiPackageDefBlob
    {
        public ActorAiPackageType Type;
        public byte ShouldRepeat;
        public float X;
        public float Y;
        public float Z;
        public short Duration;
        public short WanderDistance;
        public byte TimeOfDay;
        public byte Idle0;
        public byte Idle1;
        public byte Idle2;
        public byte Idle3;
        public byte Idle4;
        public byte Idle5;
        public byte Idle6;
        public byte Idle7;
        public BlobString TargetId;
        public BlobString CellName;
        public ulong TargetIdHash;
        public ulong CellNameHash;
    }

    public struct RuntimeActorTravelDestinationDefBlob
    {
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public BlobString CellName;
        public ulong CellNameHash;
    }

    public struct RuntimeItemLeveledListEntryDefBlob
    {
        public BlobString ItemId;
        public ulong ItemIdHash;
        public ushort Level;
    }

    public struct RuntimeItemLeveledListDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public ulong IdHash;
        public int Flags;
        public byte ChanceNone;
        public int FirstEntryIndex;
        public int EntryCount;
    }

    public struct RuntimeActorDefBlob
    {
        public ContentId ContentId;
        public ActorDefKind Kind;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Model;
        public BlobString ScriptId;
        public BlobString RaceId;
        public BlobString ClassId;
        public BlobString FactionId;
        public BlobString HeadId;
        public BlobString HairId;
        public BlobString OriginalId;
        public ulong IdHash;
        public ulong ModelPathHash;
        public ulong ScriptIdHash;
        public ulong RaceIdHash;
        public ulong ClassIdHash;
        public ulong FactionIdHash;
        public ulong HeadIdHash;
        public ulong HairIdHash;
        public ulong OriginalIdHash;
        public uint Flags;
        public int Level;
        public float Scale;
        public byte AutoCalculatedStats;
        public int BloodType;
        public int Disposition;
        public int Reputation;
        public int Rank;
        public int Gold;
        public int CreatureType;
        public int SoulValue;
        public int Combat;
        public int Magic;
        public int Stealth;
        public ActorAttributeDef Attributes;
        public ActorSkillDef Skills;
        public ActorVitalDef Vitals;
        public ActorAiDataDef AiData;
        public int FirstSpellIndex;
        public int SpellCount;
        public int FirstInventoryIndex;
        public int InventoryCount;
        public int FirstAiPackageIndex;
        public int AiPackageCount;
        public int FirstTravelDestinationIndex;
        public int TravelDestinationCount;
    }

    public struct RuntimeLightDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString Name;
        public BlobString Model;
        public BlobString Icon;
        public BlobString ScriptId;
        public BlobString SoundId;
        public ulong IdHash;
        public ulong ModelPathHash;
        public ulong ScriptIdHash;
        public ulong SoundIdHash;
        public float Weight;
        public int Value;
        public int Duration;
        public int Radius;
        public uint ColorRgba;
        public int Flags;
    }

    public struct RuntimeSoundDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString SoundPath;
        public ulong IdHash;
        public ulong SoundPathHash;
        public byte Volume;
        public byte MinRange;
        public byte MaxRange;
    }

    public struct RuntimeDialogueDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString StringId;
        public ulong IdHash;
        public ulong StringIdHash;
        public DialogueDefType Type;
        public int FirstInfoIndex;
        public int InfoCount;
    }

    public struct RuntimeDialogueInfoDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString TopicId;
        public BlobString PrevId;
        public BlobString NextId;
        public BlobString ActorId;
        public BlobString RaceId;
        public BlobString ClassId;
        public BlobString FactionId;
        public BlobString PcFactionId;
        public BlobString CellId;
        public BlobString SoundFile;
        public BlobString Response;
        public BlobString ResultScript;
        public ulong IdHash;
        public ulong TopicIdHash;
        public ulong PrevIdHash;
        public ulong NextIdHash;
        public ulong ActorIdHash;
        public ulong RaceIdHash;
        public ulong ClassIdHash;
        public ulong FactionIdHash;
        public ulong PcFactionIdHash;
        public ulong CellIdHash;
        public ulong SoundFilePathHash;
        public int Type;
        public int DispositionOrJournalIndex;
        public sbyte Rank;
        public sbyte Gender;
        public sbyte PcRank;
        public byte QuestStatus;
        public bool FactionLess;
        public int FirstSelectRuleIndex;
        public int SelectRuleCount;
    }

    public struct RuntimeDialogueConditionDefBlob
    {
        public BlobString Variable;
        public ulong VariableHash;
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
        public byte Index;
        public byte Function;
        public byte Comparison;
    }

    public struct RuntimeSpellDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString Name;
        public ulong IdHash;
        public int SpellType;
        public int Cost;
        public int Flags;
        public int EffectStartIndex;
        public int EffectCount;
    }

    public struct RuntimeEnchantmentDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public ulong IdHash;
        public int EnchantmentType;
        public int Cost;
        public int Charge;
        public int Flags;
        public int EffectStartIndex;
        public int EffectCount;
    }

    public struct RuntimeMagicEffectDefBlob
    {
        public ContentId ContentId;
        public int Index;
        public int School;
        public float BaseCost;
        public int Flags;
        public int Red;
        public int Green;
        public int Blue;
        public float SizeX;
        public float Speed;
        public float SizeCap;
        public BlobString Icon;
        public BlobString ParticleTexture;
        public BlobString CastingObjectId;
        public BlobString HitObjectId;
        public BlobString AreaObjectId;
        public BlobString BoltObjectId;
        public BlobString CastSoundId;
        public BlobString BoltSoundId;
        public BlobString HitSoundId;
        public BlobString AreaSoundId;
        public BlobString Description;
        public ulong IconPathHash;
        public ulong ParticleTexturePathHash;
        public ulong CastingObjectIdHash;
        public ulong HitObjectIdHash;
        public ulong AreaObjectIdHash;
        public ulong BoltObjectIdHash;
        public ulong CastSoundIdHash;
        public ulong BoltSoundIdHash;
        public ulong HitSoundIdHash;
        public ulong AreaSoundIdHash;
    }

    public struct RuntimeRegionSoundRefDefBlob
    {
        public BlobString SoundId;
        public ulong SoundIdHash;
        public byte Chance;
    }

    public struct RuntimeRegionDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString Name;
        public BlobString SleepListId;
        public int MapColorRgba;
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
        public int SoundRefStartIndex;
        public int SoundRefCount;
    }

    public struct RuntimePathGridDefBlob
    {
        public ContentId ContentId;
        public uint RecordTag;
        public BlobString Id;
        public BlobString CellId;
        public int GridX;
        public int GridY;
        public short Granularity;
        public ushort DeclaredPointCount;
        public int FirstPointIndex;
        public int PointCount;
        public int FirstConnectionIndex;
        public int ConnectionCount;
        public int FirstNavigationNodeIndex;
        public int NavigationNodeCount;
        public int FirstNavigationEdgeIndex;
        public int NavigationEdgeCount;
        public int FirstNavigationPortalIndex;
        public int NavigationPortalCount;
        public int FirstNavigationAbstractEdgeIndex;
        public int NavigationAbstractEdgeCount;
        public int FirstNavigationNeighborIndex;
        public int NavigationNeighborCount;
        public int NavigationComponentId;
        public byte IsExterior;
    }

    public struct RuntimeMusicTrackDefBlob
    {
        public ContentId ContentId;
        public BlobString RelativePath;
        public MusicTrackCategory Category;
    }

    public struct RuntimeWeatherDefinitionDefBlob
    {
        public WeatherKind Kind;
        public BlobString Id;
        public BlobString CloudTexture;
        public WeatherColorSetDef SkyColor;
        public WeatherColorSetDef FogColor;
        public WeatherColorSetDef AmbientColor;
        public WeatherColorSetDef SunColor;
        public int SunDiscSunsetColorRgba;
        public float LandFogDayDepth;
        public float LandFogNightDepth;
        public float WindSpeed;
        public float CloudSpeed;
        public float GlareView;
        public float CloudsMaximumPercent;
        public float TransitionDelta;
        public float RainSpeed;
        public float RainEntranceSpeed;
        public int RainMaxRaindrops;
        public float RainDiameter;
        public float RainThreshold;
        public float RainMinHeight;
        public float RainMaxHeight;
        public byte UsingPrecip;
        public byte IsStorm;
        public BlobString RainLoopSoundId;
        public BlobString AmbientLoopSoundId;
        public float ThunderFrequency;
        public float ThunderThreshold;
        public float FlashDecrement;
        public BlobString ThunderSoundId0;
        public BlobString ThunderSoundId1;
        public BlobString ThunderSoundId2;
        public BlobString ThunderSoundId3;
    }

    public struct RuntimeSkyWeatherVisualSettingsDefBlob
    {
        public BlobString SunTexture;
        public BlobString SunGlareTexture;
        public BlobString StarTexture;
        public BlobString MasserShadowTexture;
        public BlobString SecundaShadowTexture;
        public BlobString RainDropTexture;
        public int FirstMasserPhaseTextureIndex;
        public int MasserPhaseTextureCount;
        public int FirstSecundaPhaseTextureIndex;
        public int SecundaPhaseTextureCount;
        public int FirstCloudTextureIndex;
        public int CloudTextureCount;
        public int FirstPrecipitationTextureIndex;
        public int PrecipitationTextureCount;
        public int FirstPrecipitationEffectModelIndex;
        public int PrecipitationEffectModelCount;
    }

    public struct RuntimeActorBodyPartDefBlob
    {
        public ContentId ContentId;
        public BlobString Id;
        public BlobString RaceId;
        public BlobString Model;
        public ActorBodyPartMeshPart Part;
        public ActorBodyPartMeshType Type;
        public byte Female;
        public byte Vampire;
        public byte NotPlayable;
        public byte FirstPerson;
    }
}
