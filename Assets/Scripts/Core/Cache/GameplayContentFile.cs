using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VVardenfell.Core.Cache
{
    public readonly struct ContentId : IEquatable<ContentId>
    {
        public readonly ulong Value;

        public ContentId(ulong value) => Value = value;

        public static ContentId FromTagAndId(uint tag, string id)
        {
            string normalized = NormalizeId(id);
            string source = $"{tag:x8}:{normalized}";
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);
            return new ContentId(BitConverter.ToUInt64(hash, 0));
        }

        public bool Equals(ContentId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ContentId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("x16");

        public static string NormalizeId(string id) => (id ?? string.Empty).Trim().ToLowerInvariant();
    }

    public enum ActorDefKind : byte
    {
        Npc = 1,
        Creature = 2,
    }

    public enum DialogueDefType : byte
    {
        Topic = 0,
        Voice = 1,
        Greeting = 2,
        Persuasion = 3,
        Journal = 4,
        Unknown = 255,
    }

    public enum DialogueConditionFunction : byte
    {
        FacReactionLowest = 0,
        FacReactionHighest = 1,
        RankRequirement = 2,
        Reputation = 3,
        HealthPercent = 4,
        PcReputation = 5,
        PcLevel = 6,
        PcHealthPercent = 7,
        PcMagicka = 8,
        PcFatigue = 9,
        PcStrength = 10,
        PcBlock = 11,
        PcArmorer = 12,
        PcMediumArmor = 13,
        PcHeavyArmor = 14,
        PcBluntWeapon = 15,
        PcLongBlade = 16,
        PcAxe = 17,
        PcSpear = 18,
        PcAthletics = 19,
        PcEnchant = 20,
        PcDestruction = 21,
        PcAlteration = 22,
        PcIllusion = 23,
        PcConjuration = 24,
        PcMysticism = 25,
        PcRestoration = 26,
        PcAlchemy = 27,
        PcUnarmored = 28,
        PcSecurity = 29,
        PcSneak = 30,
        PcAcrobatics = 31,
        PcLightArmor = 32,
        PcShortBlade = 33,
        PcMarksman = 34,
        PcMercantile = 35,
        PcSpeechcraft = 36,
        PcHandToHand = 37,
        PcGender = 38,
        PcExpelled = 39,
        PcCommonDisease = 40,
        PcBlightDisease = 41,
        PcClothingModifier = 42,
        PcCrimeLevel = 43,
        SameSex = 44,
        SameRace = 45,
        SameFaction = 46,
        FactionRankDifference = 47,
        Detected = 48,
        Alarmed = 49,
        Choice = 50,
        PcIntelligence = 51,
        PcWillpower = 52,
        PcAgility = 53,
        PcSpeed = 54,
        PcEndurance = 55,
        PcPersonality = 56,
        PcLuck = 57,
        PcCorprus = 58,
        Weather = 59,
        PcVampire = 60,
        Level = 61,
        Attacked = 62,
        TalkedToPc = 63,
        PcHealth = 64,
        CreatureTarget = 65,
        FriendHit = 66,
        Fight = 67,
        Hello = 68,
        Alarm = 69,
        Flee = 70,
        ShouldAttack = 71,
        Werewolf = 72,
        PcWerewolfKills = 73,
        Global = 74,
        Local = 75,
        Journal = 76,
        Item = 77,
        Dead = 78,
        NotId = 79,
        NotFaction = 80,
        NotClass = 81,
        NotRace = 82,
        NotCell = 83,
        NotLocal = 84,
        None = 255,
    }

    public enum DialogueConditionComparison : byte
    {
        Equal = (byte)'0',
        NotEqual = (byte)'1',
        Greater = (byte)'2',
        GreaterOrEqual = (byte)'3',
        Less = (byte)'4',
        LessOrEqual = (byte)'5',
    }

    public enum MusicTrackCategory : byte
    {
        Explore = 1,
        Battle = 2,
        Special = 3,
    }

    public struct ActorDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static ActorDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct ActivatorDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static ActivatorDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct DoorDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static DoorDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct ContainerDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static ContainerDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct ItemDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static ItemDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct LightDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static LightDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct ItemLeveledListDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static ItemLeveledListDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct CreatureLeveledListDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static CreatureLeveledListDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct SoundDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static SoundDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct DialogueDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static DialogueDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct DialogueInfoDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static DialogueInfoDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct SpellDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static SpellDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct EnchantmentDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static EnchantmentDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct MagicEffectDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static MagicEffectDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct RegionDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static RegionDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct MusicTrackDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static MusicTrackDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct GenericRecordDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static GenericRecordDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct MorrowindScriptProgramDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static MorrowindScriptProgramDefHandle FromIndex(int index) => new() { Value = index + 1 }; }

    public enum ContentReferenceKind : byte
    {
        None = 0,
        Actor = 1,
        Activator = 2,
        Door = 3,
        Container = 4,
        Item = 5,
        Light = 6,
        Static = 7,
        LeveledCreature = 8,
        LeveledItem = 9,
    }

    public struct ContentReference
    {
        public ContentReferenceKind Kind;
        public int HandleValue;

        public bool IsValid => Kind != ContentReferenceKind.None && HandleValue > 0;
    }

    public struct BaseDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Model;
        public string Icon;
        public string ScriptId;
        public string SoundId;
        public string AuxSoundId;
        public string EnchantId;
        public uint Flags;
        public float Float0;
        public int Int0;
        public int Int1;
    }

    public enum ItemEquipmentKind : byte
    {
        None = 0,
        Weapon = 1,
        Armor = 2,
        Clothing = 3,
    }

    public enum ItemEquipmentSlot : byte
    {
        None = 0,
        Weapon = 1,
        Helmet = 2,
        Cuirass = 3,
        LeftPauldron = 4,
        RightPauldron = 5,
        Greaves = 6,
        Boots = 7,
        LeftHand = 8,
        RightHand = 9,
        Shield = 10,
        Pants = 11,
        Shoes = 12,
        Shirt = 13,
        Belt = 14,
        Robe = 15,
        Skirt = 16,
        Ring = 17,
        Amulet = 18,
    }

    public enum ItemEquipmentPartReference : byte
    {
        Head = 0,
        Hair = 1,
        Neck = 2,
        Cuirass = 3,
        Groin = 4,
        Skirt = 5,
        RightHand = 6,
        LeftHand = 7,
        RightWrist = 8,
        LeftWrist = 9,
        Shield = 10,
        RightForearm = 11,
        LeftForearm = 12,
        RightUpperarm = 13,
        LeftUpperarm = 14,
        RightFoot = 15,
        LeftFoot = 16,
        RightAnkle = 17,
        LeftAnkle = 18,
        RightKnee = 19,
        LeftKnee = 20,
        RightLeg = 21,
        LeftLeg = 22,
        RightPauldron = 23,
        LeftPauldron = 24,
        Weapon = 25,
        Tail = 26,
    }

    public struct ItemEquipmentDef
    {
        public ItemDefHandle Item;
        public ItemEquipmentKind Kind;
        public ItemEquipmentSlot Slot;
        public int Type;
        public int Value;
        public float Weight;
        public int Health;
        public int Armor;
        public int EnchantCapacity;
        public int DamageMin;
        public int DamageMax;
        public int FirstBodyPartIndex;
        public int BodyPartCount;
    }

    public struct ItemEquipmentBodyPartDef
    {
        public ItemDefHandle Item;
        public ItemEquipmentPartReference Part;
        public string MaleBodyPartId;
        public string FemaleBodyPartId;
    }

    public struct GenericRecordDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Model;
        public string Icon;
        public string ScriptId;
        public string Text;
        public uint Flags;
        public int Int0;
        public int Int1;
        public int Int2;
        public float Float0;
        public float Float1;
    }

    public enum MorrowindScriptProgramStatus : byte
    {
        None = 0,
        Compiled = 1,
        DisabledUnsupported = 2,
        FailedInvalid = 3,
    }

    public enum MorrowindScriptValueKind : byte
    {
        Integer = 1,
        Float = 2,
    }

    public enum MorrowindScriptOpcode : byte
    {
        Nop = 0,
        Return = 1,
        PushInt = 2,
        PushFloat = 3,
        GetLocal = 4,
        SetLocalInt = 5,
        SetLocalFloat = 6,
        GetGlobal = 7,
        SetGlobalInt = 8,
        SetGlobalFloat = 9,
        Add = 10,
        Subtract = 11,
        Multiply = 12,
        Divide = 13,
        CompareEqual = 14,
        CompareNotEqual = 15,
        CompareLess = 16,
        CompareLessOrEqual = 17,
        CompareGreater = 18,
        CompareGreaterOrEqual = 19,
        Jump = 20,
        JumpIfZero = 21,
        EmitAudioRequest = 22,
        GetDistancePlayer = 23,
        GetCellChanged = 24,
        GetSoundPlaying = 25,
        GetMenuMode = 26,
        GetJournalIndex = 27,
        GetSecondsPassed = 28,
        Negate = 29,
        GetOnActivate = 30,
        Activate = 31,
        Rotate = 32,
        GetDisabled = 33,
        RequestSetDisabled = 34,
        SetAngle = 35,
        Journal = 36,
        StopScript = 37,
        SetJournalIndex = 38,
        AddTopic = 39,
        FillJournal = 40,
    }

    public enum MorrowindScriptRefTargetMode : byte
    {
        Self = 0,
        PlacedRef = 1,
    }

    public enum MorrowindScriptAudioKind : byte
    {
        None = 0,
        PlaySound = 1,
        PlaySound3D = 2,
        PlayLoopSound3D = 3,
        PlayLoopSound3DVP = 4,
        PlaySound3DVP = 5,
    }

    public struct MorrowindScriptProgramDef
    {
        public string Id;
        public int SourceScriptIndex;
        public byte Status;
        public string DisabledReason;
        public int FirstInstructionIndex;
        public int InstructionCount;
        public int FirstLocalIndex;
        public int LocalCount;
        public int MaxStack;
    }

    public struct MorrowindScriptInstructionDef
    {
        public byte Opcode;
        public byte Operand0;
        public short Operand1;
        public int Int0;
        public int Int1;
        public int Int2;
        public float Float0;
        public float Float1;
    }

    public struct MorrowindScriptLocalDef
    {
        public string Name;
        public byte ValueKind;
    }

    public struct ClassDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Description;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public int Specialization;
        public int[] MinorSkills;
        public int[] MajorSkills;
        public int Playable;
        public int Services;
    }

    public struct RaceSkillBonusDef
    {
        public int Skill;
        public int Bonus;
    }

    public struct RaceDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Description;
        public RaceSkillBonusDef[] SkillBonuses;
        public int[] MaleAttributes;
        public int[] FemaleAttributes;
        public float MaleHeight;
        public float FemaleHeight;
        public float MaleWeight;
        public float FemaleWeight;
        public int Flags;
        public string[] PowerSpellIds;
    }

    public struct FactionRankRequirementDef
    {
        public int Attribute1;
        public int Attribute2;
        public int PrimarySkill;
        public int FavoredSkill;
        public int Reaction;
    }

    public struct FactionReactionDef
    {
        public string FactionId;
        public int Reaction;
    }

    public struct FactionDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public FactionRankRequirementDef[] RankRequirements;
        public int[] Skills;
        public int Hidden;
        public string[] RankNames;
        public FactionReactionDef[] Reactions;
    }

    public struct ContainerContentRangeDef
    {
        public int FirstItemIndex;
        public int ItemCount;
    }

    public struct ContainerItemDef
    {
        public string ItemId;
        public int Count;
    }

    public struct ActorAttributeDef
    {
        public int Strength;
        public int Intelligence;
        public int Willpower;
        public int Agility;
        public int Speed;
        public int Endurance;
        public int Personality;
        public int Luck;
    }

    public struct ActorSkillDef
    {
        public int Block;
        public int Armorer;
        public int MediumArmor;
        public int HeavyArmor;
        public int BluntWeapon;
        public int LongBlade;
        public int Axe;
        public int Spear;
        public int Athletics;
        public int Enchant;
        public int Destruction;
        public int Alteration;
        public int Illusion;
        public int Conjuration;
        public int Mysticism;
        public int Restoration;
        public int Alchemy;
        public int Unarmored;
        public int Security;
        public int Sneak;
        public int Acrobatics;
        public int LightArmor;
        public int ShortBlade;
        public int Marksman;
        public int Mercantile;
        public int Speechcraft;
        public int HandToHand;
    }

    public struct ActorVitalDef
    {
        public int Health;
        public int Magicka;
        public int Fatigue;
    }

    public struct ActorAiDataDef
    {
        public int Hello;
        public byte Fight;
        public byte Flee;
        public byte Alarm;
        public int Services;
    }

    public struct ActorSpellDef
    {
        public string SpellId;
    }

    public enum ActorAiPackageType : byte
    {
        Wander = 1,
        Travel = 2,
        Follow = 3,
        Escort = 4,
        Activate = 5,
    }

    public struct ActorAiPackageDef
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
        public string TargetId;
        public string CellName;
    }

    public struct ActorTravelDestinationDef
    {
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public string CellName;
    }

    public struct ItemLeveledListEntryDef
    {
        public string ItemId;
        public ushort Level;
    }

    public struct ItemLeveledListDef
    {
        public ContentId ContentId;
        public string Id;
        public int Flags;
        public byte ChanceNone;
        public int FirstEntryIndex;
        public int EntryCount;
    }

    public struct ActorDef
    {
        public ContentId ContentId;
        public ActorDefKind Kind;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Model;
        public string ScriptId;
        public string RaceId;
        public string ClassId;
        public string FactionId;
        public string HeadId;
        public string HairId;
        public string OriginalId;
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

    public struct LightDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string Name;
        public string Model;
        public string Icon;
        public string ScriptId;
        public string SoundId;
        public float Weight;
        public int Value;
        public int Duration;
        public int Radius;
        public uint ColorRgba;
        public int Flags;
    }

    public struct SoundDef
    {
        public ContentId ContentId;
        public string Id;
        public string SoundPath;
        public byte Volume;
        public byte MinRange;
        public byte MaxRange;
    }

    public struct DialogueDef
    {
        public ContentId ContentId;
        public string Id;
        public string StringId;
        public DialogueDefType Type;
        public int FirstInfoIndex;
        public int InfoCount;
    }

    public struct DialogueInfoDef
    {
        public ContentId ContentId;
        public string Id;
        public string TopicId;
        public string PrevId;
        public string NextId;
        public string ActorId;
        public string RaceId;
        public string ClassId;
        public string FactionId;
        public string PcFactionId;
        public string CellId;
        public string SoundFile;
        public string Response;
        public string ResultScript;
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

    public struct DialogueConditionDef
    {
        public string Variable;
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
        public byte Index;
        public byte Function;
        public byte Comparison;
    }

    public struct MagicEffectInstanceDef
    {
        public short EffectId;
        public sbyte Skill;
        public sbyte Attribute;
        public int Range;
        public int Area;
        public int Duration;
        public int MagnitudeMin;
        public int MagnitudeMax;
    }

    public struct SpellDef
    {
        public ContentId ContentId;
        public string Id;
        public string Name;
        public int SpellType;
        public int Cost;
        public int Flags;
        public int EffectStartIndex;
        public int EffectCount;
    }

    public struct EnchantmentDef
    {
        public ContentId ContentId;
        public string Id;
        public int EnchantmentType;
        public int Cost;
        public int Charge;
        public int Flags;
        public int EffectStartIndex;
        public int EffectCount;
    }

    public struct MagicEffectDef
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
        public string Icon;
        public string ParticleTexture;
        public string CastingObjectId;
        public string HitObjectId;
        public string AreaObjectId;
        public string BoltObjectId;
        public string CastSoundId;
        public string BoltSoundId;
        public string HitSoundId;
        public string AreaSoundId;
        public string Description;
    }

    public struct RegionSoundRefDef
    {
        public string SoundId;
        public byte Chance;
    }

    public struct RegionDef
    {
        public ContentId ContentId;
        public string Id;
        public string Name;
        public string SleepListId;
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

    public struct PathGridDef
    {
        public ContentId ContentId;
        public uint RecordTag;
        public string Id;
        public string CellId;
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

    public struct PathGridPointDef
    {
        public int SourceX;
        public int SourceY;
        public int SourceZ;
        public float UnityX;
        public float UnityY;
        public float UnityZ;
        public byte Autogenerated;
        public byte SourceConnectionCount;
        public int FirstConnectionIndex;
        public int ConnectionCount;
    }

    public struct PathGridConnectionDef
    {
        public int FromPointIndex;
        public int ToPointIndex;
    }

    public enum PathGridNavigationEdgeKind : byte
    {
        Authored = 1,
        ExteriorBorder = 2,
        IntraPathGrid = 3,
    }

    public struct PathGridNavigationNodeDef
    {
        public int PathGridIndex;
        public int PointIndex;
        public int SourceX;
        public int SourceY;
        public int SourceZ;
        public float UnityX;
        public float UnityY;
        public float UnityZ;
        public int FirstEdgeIndex;
        public int EdgeCount;
        public int ComponentId;
        public byte IsPortal;
    }

    public struct PathGridNavigationEdgeDef
    {
        public int FromNodeIndex;
        public int ToNodeIndex;
        public float Cost;
        public PathGridNavigationEdgeKind Kind;
    }

    public struct PathGridNavigationPortalDef
    {
        public int PathGridIndex;
        public int NodeIndex;
        public int PointIndex;
        public int FirstAbstractEdgeIndex;
        public int AbstractEdgeCount;
        public int ComponentId;
    }

    public struct PathGridNavigationAbstractEdgeDef
    {
        public int FromPortalIndex;
        public int ToPortalIndex;
        public float Cost;
        public PathGridNavigationEdgeKind Kind;
    }

    public struct PathGridNavigationNeighborDef
    {
        public int PathGridIndex;
        public int NeighborPathGridIndex;
        public int BorderEdgeCount;
        public float MinCost;
    }

    public struct MusicTrackDef
    {
        public ContentId ContentId;
        public string RelativePath;
        public MusicTrackCategory Category;
    }

    public struct AmbientSettingsDef
    {
        public float MinSecondsBetweenEnvironmentalSounds;
        public float MaxSecondsBetweenEnvironmentalSounds;
    }

    public enum WeatherKind : byte
    {
        Clear = 0,
        Cloudy = 1,
        Foggy = 2,
        Overcast = 3,
        Rain = 4,
        Thunderstorm = 5,
        Ashstorm = 6,
        Blight = 7,
        Snow = 8,
        Blizzard = 9,
    }

    public struct WeatherColorSetDef
    {
        public int SunriseRgba;
        public int DayRgba;
        public int SunsetRgba;
        public int NightRgba;
    }

    public struct WeatherDefinitionDef
    {
        public WeatherKind Kind;
        public string Id;
        public string CloudTexture;
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
        public string RainLoopSoundId;
        public string AmbientLoopSoundId;
        public float ThunderFrequency;
        public float ThunderThreshold;
        public float FlashDecrement;
        public string ThunderSoundId0;
        public string ThunderSoundId1;
        public string ThunderSoundId2;
        public string ThunderSoundId3;
    }

    public struct WeatherSettingsDef
    {
        public float SunriseTime;
        public float SunsetTime;
        public float SunriseDuration;
        public float SunsetDuration;
        public float HoursBetweenWeatherChanges;
        public float PrecipGravity;
        public float SunGlareFaderMax;
        public float SunGlareFaderAngleMax;
        public int SunGlareFaderColorRgba;
        public float SunPreSunriseTime;
        public float SunPostSunriseTime;
        public float SunPreSunsetTime;
        public float SunPostSunsetTime;
        public float AmbientPreSunriseTime;
        public float AmbientPostSunriseTime;
        public float AmbientPreSunsetTime;
        public float AmbientPostSunsetTime;
        public float FogPreSunriseTime;
        public float FogPostSunriseTime;
        public float FogPreSunsetTime;
        public float FogPostSunsetTime;
        public float SkyPreSunriseTime;
        public float SkyPostSunriseTime;
        public float SkyPreSunsetTime;
        public float SkyPostSunsetTime;
        public float StarsPostSunsetStart;
        public float StarsPreSunriseFinish;
        public float StarsFadingDuration;
        public MoonSettingsDef MasserMoon;
        public MoonSettingsDef SecundaMoon;
    }

    public struct MoonSettingsDef
    {
        public float Size;
        public float AxisOffset;
        public float Speed;
        public float DailyIncrement;
        public float FadeStartAngle;
        public float FadeEndAngle;
        public float MoonShadowEarlyFadeAngle;
        public float FadeInStart;
        public float FadeInFinish;
        public float FadeOutStart;
        public float FadeOutFinish;
    }

    public struct SkyWeatherVisualSettingsDef
    {
        public string SunTexture;
        public string SunGlareTexture;
        public string StarTexture;
        public string MasserShadowTexture;
        public string SecundaShadowTexture;
        public string RainDropTexture;
        public string[] MasserPhaseTextures;
        public string[] SecundaPhaseTextures;
        public string[] CloudTextures;
        public string[] PrecipitationTextures;
        public string[] PrecipitationEffectModels;
    }

    public sealed class GameplayContentData
    {
        public ActorDef[] Actors = Array.Empty<ActorDef>();
        public ActorSpellDef[] ActorSpells = Array.Empty<ActorSpellDef>();
        public ContainerItemDef[] ActorInventoryItems = Array.Empty<ContainerItemDef>();
        public ActorAiPackageDef[] ActorAiPackages = Array.Empty<ActorAiPackageDef>();
        public ActorTravelDestinationDef[] ActorTravelDestinations = Array.Empty<ActorTravelDestinationDef>();
        public BaseDef[] Activators = Array.Empty<BaseDef>();
        public BaseDef[] Doors = Array.Empty<BaseDef>();
        public BaseDef[] Containers = Array.Empty<BaseDef>();
        public ContainerContentRangeDef[] ContainerContentRanges = Array.Empty<ContainerContentRangeDef>();
        public ContainerItemDef[] ContainerItems = Array.Empty<ContainerItemDef>();
        public BaseDef[] Items = Array.Empty<BaseDef>();
        public ItemEquipmentDef[] ItemEquipment = Array.Empty<ItemEquipmentDef>();
        public ItemEquipmentBodyPartDef[] ItemEquipmentBodyParts = Array.Empty<ItemEquipmentBodyPartDef>();
        public LightDef[] Lights = Array.Empty<LightDef>();
        public ItemLeveledListDef[] ItemLeveledLists = Array.Empty<ItemLeveledListDef>();
        public ItemLeveledListEntryDef[] ItemLeveledListEntries = Array.Empty<ItemLeveledListEntryDef>();
        public ItemLeveledListDef[] CreatureLeveledLists = Array.Empty<ItemLeveledListDef>();
        public ItemLeveledListEntryDef[] CreatureLeveledListEntries = Array.Empty<ItemLeveledListEntryDef>();
        public SoundDef[] Sounds = Array.Empty<SoundDef>();
        public DialogueDef[] Dialogues = Array.Empty<DialogueDef>();
        public DialogueInfoDef[] DialogueInfos = Array.Empty<DialogueInfoDef>();
        public DialogueConditionDef[] DialogueConditions = Array.Empty<DialogueConditionDef>();
        public SpellDef[] Spells = Array.Empty<SpellDef>();
        public EnchantmentDef[] Enchantments = Array.Empty<EnchantmentDef>();
        public MagicEffectDef[] MagicEffects = Array.Empty<MagicEffectDef>();
        public MagicEffectInstanceDef[] MagicEffectInstances = Array.Empty<MagicEffectInstanceDef>();
        public RegionDef[] Regions = Array.Empty<RegionDef>();
        public RegionSoundRefDef[] RegionSoundRefs = Array.Empty<RegionSoundRefDef>();
        public MusicTrackDef[] MusicTracks = Array.Empty<MusicTrackDef>();
        public AmbientSettingsDef AmbientSettings;
        public WeatherSettingsDef WeatherSettings;
        public WeatherDefinitionDef[] WeatherDefinitions = Array.Empty<WeatherDefinitionDef>();
        public SkyWeatherVisualSettingsDef SkyWeatherVisualSettings;
        public GenericRecordDef[] GameSettings = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] Globals = Array.Empty<GenericRecordDef>();
        public ClassDef[] Classes = Array.Empty<ClassDef>();
        public FactionDef[] Factions = Array.Empty<FactionDef>();
        public RaceDef[] Races = Array.Empty<RaceDef>();
        public GenericRecordDef[] Birthsigns = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] Skills = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] Scripts = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] StartScripts = Array.Empty<GenericRecordDef>();
        public MorrowindScriptProgramDef[] MorrowindScriptPrograms = Array.Empty<MorrowindScriptProgramDef>();
        public MorrowindScriptInstructionDef[] MorrowindScriptInstructions = Array.Empty<MorrowindScriptInstructionDef>();
        public MorrowindScriptLocalDef[] MorrowindScriptLocals = Array.Empty<MorrowindScriptLocalDef>();
        public GenericRecordDef[] SoundGenerators = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] LandTextures = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] Statics = Array.Empty<GenericRecordDef>();
        public GenericRecordDef[] BodyParts = Array.Empty<GenericRecordDef>();
        public ActorBodyPartDef[] ActorBodyParts = Array.Empty<ActorBodyPartDef>();
        public PathGridDef[] PathGrids = Array.Empty<PathGridDef>();
        public PathGridPointDef[] PathGridPoints = Array.Empty<PathGridPointDef>();
        public PathGridConnectionDef[] PathGridConnections = Array.Empty<PathGridConnectionDef>();
        public PathGridNavigationNodeDef[] PathGridNavigationNodes = Array.Empty<PathGridNavigationNodeDef>();
        public PathGridNavigationEdgeDef[] PathGridNavigationEdges = Array.Empty<PathGridNavigationEdgeDef>();
        public PathGridNavigationPortalDef[] PathGridNavigationPortals = Array.Empty<PathGridNavigationPortalDef>();
        public PathGridNavigationAbstractEdgeDef[] PathGridNavigationAbstractEdges = Array.Empty<PathGridNavigationAbstractEdgeDef>();
        public PathGridNavigationNeighborDef[] PathGridNavigationNeighbors = Array.Empty<PathGridNavigationNeighborDef>();
    }

    public static partial class GameplayContentFile
    {
        const uint Magic = 0x43475656u; // 'VVGC'


        public static void Write(string path, GameplayContentData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);

            w.Write(Magic);
            w.Write(CacheFormat.FormatVersion);
            w.Write(CacheFormat.GameplayContentVersion);

            WriteActorArray(w, data?.Actors);
            WriteActorSpellArray(w, data?.ActorSpells);
            WriteContainerItemArray(w, data?.ActorInventoryItems);
            WriteActorAiPackageArray(w, data?.ActorAiPackages);
            WriteActorTravelDestinationArray(w, data?.ActorTravelDestinations);
            WriteBaseDefArray(w, data?.Activators);
            WriteBaseDefArray(w, data?.Doors);
            WriteBaseDefArray(w, data?.Containers);
            WriteContainerContentRangeArray(w, data?.ContainerContentRanges);
            WriteContainerItemArray(w, data?.ContainerItems);
            WriteBaseDefArray(w, data?.Items);
            WriteItemEquipmentArray(w, data?.ItemEquipment);
            WriteItemEquipmentBodyPartArray(w, data?.ItemEquipmentBodyParts);
            WriteLightArray(w, data?.Lights);
            WriteItemLeveledListArray(w, data?.ItemLeveledLists);
            WriteItemLeveledListEntryArray(w, data?.ItemLeveledListEntries);
            WriteItemLeveledListArray(w, data?.CreatureLeveledLists);
            WriteItemLeveledListEntryArray(w, data?.CreatureLeveledListEntries);
            WriteSoundArray(w, data?.Sounds);
            WriteDialogueArray(w, data?.Dialogues);
            WriteDialogueInfoArray(w, data?.DialogueInfos);
            WriteDialogueConditionArray(w, data?.DialogueConditions);
            WriteSpellArray(w, data?.Spells);
            WriteEnchantmentArray(w, data?.Enchantments);
            WriteMagicEffectArray(w, data?.MagicEffects);
            WriteMagicEffectInstanceArray(w, data?.MagicEffectInstances);
            WriteRegionArray(w, data?.Regions);
            WriteRegionSoundRefArray(w, data?.RegionSoundRefs);
            WriteMusicTrackArray(w, data?.MusicTracks);
            WriteAmbientSettings(w, data?.AmbientSettings ?? default);
            WriteWeatherSettings(w, data?.WeatherSettings ?? default);
            WriteWeatherDefinitionArray(w, data?.WeatherDefinitions);
            WriteSkyWeatherVisualSettings(w, data?.SkyWeatherVisualSettings ?? default);
            WriteGenericRecordArray(w, data?.GameSettings);
            WriteGenericRecordArray(w, data?.Globals);
            WriteClassArray(w, data?.Classes);
            WriteFactionArray(w, data?.Factions);
            WriteRaceArray(w, data?.Races);
            WriteGenericRecordArray(w, data?.Birthsigns);
            WriteGenericRecordArray(w, data?.Skills);
            WriteGenericRecordArray(w, data?.Scripts);
            WriteGenericRecordArray(w, data?.StartScripts);
            WriteMorrowindScriptProgramArray(w, data?.MorrowindScriptPrograms);
            WriteMorrowindScriptInstructionArray(w, data?.MorrowindScriptInstructions);
            WriteMorrowindScriptLocalArray(w, data?.MorrowindScriptLocals);
            WriteGenericRecordArray(w, data?.SoundGenerators);
            WriteGenericRecordArray(w, data?.LandTextures);
            WriteGenericRecordArray(w, data?.Statics);
            WriteGenericRecordArray(w, data?.BodyParts);
            WriteActorBodyPartArray(w, data?.ActorBodyParts);
            WritePathGridArray(w, data?.PathGrids);
            WritePathGridPointArray(w, data?.PathGridPoints);
            WritePathGridConnectionArray(w, data?.PathGridConnections);
            WritePathGridNavigationNodeArray(w, data?.PathGridNavigationNodes);
            WritePathGridNavigationEdgeArray(w, data?.PathGridNavigationEdges);
            WritePathGridNavigationPortalArray(w, data?.PathGridNavigationPortals);
            WritePathGridNavigationAbstractEdgeArray(w, data?.PathGridNavigationAbstractEdges);
            WritePathGridNavigationNeighborArray(w, data?.PathGridNavigationNeighbors);
        }


        public static GameplayContentData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad gameplay content magic 0x{magic:X8} in '{path}'.");

            uint formatVersion = r.ReadUInt32();
            if (formatVersion != CacheFormat.FormatVersion)
                throw new InvalidDataException($"Unsupported gameplay content format version {formatVersion} in '{path}'.");

            uint contentVersion = r.ReadUInt32();
            if (contentVersion != CacheFormat.GameplayContentVersion)
                throw new InvalidDataException($"Unsupported gameplay content version {contentVersion} in '{path}'.");

            return new GameplayContentData
            {
                Actors = ReadActorArray(r),
                ActorSpells = ReadActorSpellArray(r),
                ActorInventoryItems = ReadContainerItemArray(r),
                ActorAiPackages = ReadActorAiPackageArray(r),
                ActorTravelDestinations = ReadActorTravelDestinationArray(r),
                Activators = ReadBaseDefArray(r),
                Doors = ReadBaseDefArray(r),
                Containers = ReadBaseDefArray(r),
                ContainerContentRanges = ReadContainerContentRangeArray(r),
                ContainerItems = ReadContainerItemArray(r),
                Items = ReadBaseDefArray(r),
                ItemEquipment = ReadItemEquipmentArray(r),
                ItemEquipmentBodyParts = ReadItemEquipmentBodyPartArray(r),
                Lights = ReadLightArray(r),
                ItemLeveledLists = ReadItemLeveledListArray(r),
                ItemLeveledListEntries = ReadItemLeveledListEntryArray(r),
                CreatureLeveledLists = ReadItemLeveledListArray(r),
                CreatureLeveledListEntries = ReadItemLeveledListEntryArray(r),
                Sounds = ReadSoundArray(r),
                Dialogues = ReadDialogueArray(r),
                DialogueInfos = ReadDialogueInfoArray(r),
                DialogueConditions = ReadDialogueConditionArray(r),
                Spells = ReadSpellArray(r),
                Enchantments = ReadEnchantmentArray(r),
                MagicEffects = ReadMagicEffectArray(r),
                MagicEffectInstances = ReadMagicEffectInstanceArray(r),
                Regions = ReadRegionArray(r),
                RegionSoundRefs = ReadRegionSoundRefArray(r),
                MusicTracks = ReadMusicTrackArray(r),
                AmbientSettings = ReadAmbientSettings(r),
                WeatherSettings = ReadWeatherSettings(r),
                WeatherDefinitions = ReadWeatherDefinitionArray(r),
                SkyWeatherVisualSettings = ReadSkyWeatherVisualSettings(r),
                GameSettings = ReadGenericRecordArray(r),
                Globals = ReadGenericRecordArray(r),
                Classes = ReadClassArray(r),
                Factions = ReadFactionArray(r),
                Races = ReadRaceArray(r),
                Birthsigns = ReadGenericRecordArray(r),
                Skills = ReadGenericRecordArray(r),
                Scripts = ReadGenericRecordArray(r),
                StartScripts = ReadGenericRecordArray(r),
                MorrowindScriptPrograms = ReadMorrowindScriptProgramArray(r),
                MorrowindScriptInstructions = ReadMorrowindScriptInstructionArray(r),
                MorrowindScriptLocals = ReadMorrowindScriptLocalArray(r),
                SoundGenerators = ReadGenericRecordArray(r),
                LandTextures = ReadGenericRecordArray(r),
                Statics = ReadGenericRecordArray(r),
                BodyParts = ReadGenericRecordArray(r),
                ActorBodyParts = ReadActorBodyPartArray(r),
                PathGrids = ReadPathGridArray(r),
                PathGridPoints = ReadPathGridPointArray(r),
                PathGridConnections = ReadPathGridConnectionArray(r),
                PathGridNavigationNodes = ReadPathGridNavigationNodeArray(r),
                PathGridNavigationEdges = ReadPathGridNavigationEdgeArray(r),
                PathGridNavigationPortals = ReadPathGridNavigationPortalArray(r),
                PathGridNavigationAbstractEdges = ReadPathGridNavigationAbstractEdgeArray(r),
                PathGridNavigationNeighbors = ReadPathGridNavigationNeighborArray(r),
            };
        }


        static void WriteContentId(BinaryWriter w, ContentId contentId) => w.Write(contentId.Value);
        static ContentId ReadContentId(BinaryReader r) => new(r.ReadUInt64());
        static void WriteString(BinaryWriter w, string value) => w.Write(value ?? string.Empty);
        static string ReadString(BinaryReader r) => r.ReadString();


        static void WriteBaseDef(BinaryWriter w, BaseDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Model);
            WriteString(w, value.Icon);
            WriteString(w, value.ScriptId);
            WriteString(w, value.SoundId);
            WriteString(w, value.AuxSoundId);
            WriteString(w, value.EnchantId);
            w.Write(value.Flags);
            w.Write(value.Float0);
            w.Write(value.Int0);
            w.Write(value.Int1);
        }


    }
}
