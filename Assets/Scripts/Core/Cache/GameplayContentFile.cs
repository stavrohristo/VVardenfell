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
    public struct SoundDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static SoundDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct DialogueDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static DialogueDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct DialogueInfoDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static DialogueInfoDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct SpellDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static SpellDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct EnchantmentDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static EnchantmentDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct MagicEffectDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static MagicEffectDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct RegionDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static RegionDefHandle FromIndex(int index) => new() { Value = index + 1 }; }
    public struct MusicTrackDefHandle { public int Value; public bool IsValid => Value > 0; public int Index => Value - 1; public static MusicTrackDefHandle FromIndex(int index) => new() { Value = index + 1 }; }

    public enum ContentReferenceKind : byte
    {
        None = 0,
        Actor = 1,
        Activator = 2,
        Door = 3,
        Container = 4,
        Item = 5,
        Light = 6,
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
        public int SelectRuleCount;
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

    public struct MusicTrackDef
    {
        public ContentId ContentId;
        public string RelativePath;
        public MusicTrackCategory Category;
    }

    public sealed class GameplayContentData
    {
        public ActorDef[] Actors = Array.Empty<ActorDef>();
        public BaseDef[] Activators = Array.Empty<BaseDef>();
        public BaseDef[] Doors = Array.Empty<BaseDef>();
        public BaseDef[] Containers = Array.Empty<BaseDef>();
        public BaseDef[] Items = Array.Empty<BaseDef>();
        public LightDef[] Lights = Array.Empty<LightDef>();
        public SoundDef[] Sounds = Array.Empty<SoundDef>();
        public DialogueDef[] Dialogues = Array.Empty<DialogueDef>();
        public DialogueInfoDef[] DialogueInfos = Array.Empty<DialogueInfoDef>();
        public SpellDef[] Spells = Array.Empty<SpellDef>();
        public EnchantmentDef[] Enchantments = Array.Empty<EnchantmentDef>();
        public MagicEffectDef[] MagicEffects = Array.Empty<MagicEffectDef>();
        public MagicEffectInstanceDef[] MagicEffectInstances = Array.Empty<MagicEffectInstanceDef>();
        public RegionDef[] Regions = Array.Empty<RegionDef>();
        public RegionSoundRefDef[] RegionSoundRefs = Array.Empty<RegionSoundRefDef>();
        public MusicTrackDef[] MusicTracks = Array.Empty<MusicTrackDef>();
    }

    public static class GameplayContentFile
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
            WriteBaseDefArray(w, data?.Activators);
            WriteBaseDefArray(w, data?.Doors);
            WriteBaseDefArray(w, data?.Containers);
            WriteBaseDefArray(w, data?.Items);
            WriteLightArray(w, data?.Lights);
            WriteSoundArray(w, data?.Sounds);
            WriteDialogueArray(w, data?.Dialogues);
            WriteDialogueInfoArray(w, data?.DialogueInfos);
            WriteSpellArray(w, data?.Spells);
            WriteEnchantmentArray(w, data?.Enchantments);
            WriteMagicEffectArray(w, data?.MagicEffects);
            WriteMagicEffectInstanceArray(w, data?.MagicEffectInstances);
            WriteRegionArray(w, data?.Regions);
            WriteRegionSoundRefArray(w, data?.RegionSoundRefs);
            WriteMusicTrackArray(w, data?.MusicTracks);
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
                Activators = ReadBaseDefArray(r),
                Doors = ReadBaseDefArray(r),
                Containers = ReadBaseDefArray(r),
                Items = ReadBaseDefArray(r),
                Lights = ReadLightArray(r),
                Sounds = ReadSoundArray(r),
                Dialogues = ReadDialogueArray(r),
                DialogueInfos = ReadDialogueInfoArray(r),
                Spells = ReadSpellArray(r),
                Enchantments = ReadEnchantmentArray(r),
                MagicEffects = ReadMagicEffectArray(r),
                MagicEffectInstances = ReadMagicEffectInstanceArray(r),
                Regions = ReadRegionArray(r),
                RegionSoundRefs = ReadRegionSoundRefArray(r),
                MusicTracks = ReadMusicTrackArray(r),
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

        static BaseDef ReadBaseDef(BinaryReader r)
        {
            return new BaseDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Model = ReadString(r),
                Icon = ReadString(r),
                ScriptId = ReadString(r),
                SoundId = ReadString(r),
                AuxSoundId = ReadString(r),
                EnchantId = ReadString(r),
                Flags = r.ReadUInt32(),
                Float0 = r.ReadSingle(),
                Int0 = r.ReadInt32(),
                Int1 = r.ReadInt32(),
            };
        }

        static void WriteActor(BinaryWriter w, ActorDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write((byte)value.Kind);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Model);
            WriteString(w, value.ScriptId);
            WriteString(w, value.RaceId);
            WriteString(w, value.ClassId);
            WriteString(w, value.FactionId);
            WriteString(w, value.HeadId);
            WriteString(w, value.HairId);
            WriteString(w, value.OriginalId);
            w.Write(value.Flags);
            w.Write(value.Level);
            w.Write(value.Scale);
        }

        static ActorDef ReadActor(BinaryReader r)
        {
            return new ActorDef
            {
                ContentId = ReadContentId(r),
                Kind = (ActorDefKind)r.ReadByte(),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Model = ReadString(r),
                ScriptId = ReadString(r),
                RaceId = ReadString(r),
                ClassId = ReadString(r),
                FactionId = ReadString(r),
                HeadId = ReadString(r),
                HairId = ReadString(r),
                OriginalId = ReadString(r),
                Flags = r.ReadUInt32(),
                Level = r.ReadInt32(),
                Scale = r.ReadSingle(),
            };
        }

        static void WriteLight(BinaryWriter w, LightDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Model);
            WriteString(w, value.Icon);
            WriteString(w, value.ScriptId);
            WriteString(w, value.SoundId);
            w.Write(value.Weight);
            w.Write(value.Value);
            w.Write(value.Duration);
            w.Write(value.Radius);
            w.Write(value.ColorRgba);
            w.Write(value.Flags);
        }

        static LightDef ReadLight(BinaryReader r)
        {
            return new LightDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Model = ReadString(r),
                Icon = ReadString(r),
                ScriptId = ReadString(r),
                SoundId = ReadString(r),
                Weight = r.ReadSingle(),
                Value = r.ReadInt32(),
                Duration = r.ReadInt32(),
                Radius = r.ReadInt32(),
                ColorRgba = r.ReadUInt32(),
                Flags = r.ReadInt32(),
            };
        }

        static void WriteSound(BinaryWriter w, SoundDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.SoundPath);
            w.Write(value.Volume);
            w.Write(value.MinRange);
            w.Write(value.MaxRange);
        }

        static SoundDef ReadSound(BinaryReader r)
        {
            return new SoundDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                SoundPath = ReadString(r),
                Volume = r.ReadByte(),
                MinRange = r.ReadByte(),
                MaxRange = r.ReadByte(),
            };
        }

        static void WriteDialogue(BinaryWriter w, DialogueDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.StringId);
            w.Write((byte)value.Type);
            w.Write(value.FirstInfoIndex);
            w.Write(value.InfoCount);
        }

        static DialogueDef ReadDialogue(BinaryReader r)
        {
            return new DialogueDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                StringId = ReadString(r),
                Type = (DialogueDefType)r.ReadByte(),
                FirstInfoIndex = r.ReadInt32(),
                InfoCount = r.ReadInt32(),
            };
        }

        static void WriteDialogueInfo(BinaryWriter w, DialogueInfoDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.TopicId);
            WriteString(w, value.PrevId);
            WriteString(w, value.NextId);
            WriteString(w, value.ActorId);
            WriteString(w, value.RaceId);
            WriteString(w, value.ClassId);
            WriteString(w, value.FactionId);
            WriteString(w, value.PcFactionId);
            WriteString(w, value.CellId);
            WriteString(w, value.SoundFile);
            WriteString(w, value.Response);
            WriteString(w, value.ResultScript);
            w.Write(value.Type);
            w.Write(value.DispositionOrJournalIndex);
            w.Write(value.Rank);
            w.Write(value.Gender);
            w.Write(value.PcRank);
            w.Write(value.QuestStatus);
            w.Write(value.FactionLess);
            w.Write(value.SelectRuleCount);
        }

        static DialogueInfoDef ReadDialogueInfo(BinaryReader r)
        {
            return new DialogueInfoDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                TopicId = ReadString(r),
                PrevId = ReadString(r),
                NextId = ReadString(r),
                ActorId = ReadString(r),
                RaceId = ReadString(r),
                ClassId = ReadString(r),
                FactionId = ReadString(r),
                PcFactionId = ReadString(r),
                CellId = ReadString(r),
                SoundFile = ReadString(r),
                Response = ReadString(r),
                ResultScript = ReadString(r),
                Type = r.ReadInt32(),
                DispositionOrJournalIndex = r.ReadInt32(),
                Rank = r.ReadSByte(),
                Gender = r.ReadSByte(),
                PcRank = r.ReadSByte(),
                QuestStatus = r.ReadByte(),
                FactionLess = r.ReadBoolean(),
                SelectRuleCount = r.ReadInt32(),
            };
        }

        static void WriteMagicEffectInstance(BinaryWriter w, MagicEffectInstanceDef value)
        {
            w.Write(value.EffectId);
            w.Write(value.Skill);
            w.Write(value.Attribute);
            w.Write(value.Range);
            w.Write(value.Area);
            w.Write(value.Duration);
            w.Write(value.MagnitudeMin);
            w.Write(value.MagnitudeMax);
        }

        static MagicEffectInstanceDef ReadMagicEffectInstance(BinaryReader r)
        {
            return new MagicEffectInstanceDef
            {
                EffectId = r.ReadInt16(),
                Skill = r.ReadSByte(),
                Attribute = r.ReadSByte(),
                Range = r.ReadInt32(),
                Area = r.ReadInt32(),
                Duration = r.ReadInt32(),
                MagnitudeMin = r.ReadInt32(),
                MagnitudeMax = r.ReadInt32(),
            };
        }

        static void WriteSpell(BinaryWriter w, SpellDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            w.Write(value.SpellType);
            w.Write(value.Cost);
            w.Write(value.Flags);
            w.Write(value.EffectStartIndex);
            w.Write(value.EffectCount);
        }

        static SpellDef ReadSpell(BinaryReader r)
        {
            return new SpellDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                Name = ReadString(r),
                SpellType = r.ReadInt32(),
                Cost = r.ReadInt32(),
                Flags = r.ReadInt32(),
                EffectStartIndex = r.ReadInt32(),
                EffectCount = r.ReadInt32(),
            };
        }

        static void WriteEnchantment(BinaryWriter w, EnchantmentDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            w.Write(value.EnchantmentType);
            w.Write(value.Cost);
            w.Write(value.Charge);
            w.Write(value.Flags);
            w.Write(value.EffectStartIndex);
            w.Write(value.EffectCount);
        }

        static EnchantmentDef ReadEnchantment(BinaryReader r)
        {
            return new EnchantmentDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                EnchantmentType = r.ReadInt32(),
                Cost = r.ReadInt32(),
                Charge = r.ReadInt32(),
                Flags = r.ReadInt32(),
                EffectStartIndex = r.ReadInt32(),
                EffectCount = r.ReadInt32(),
            };
        }

        static void WriteMagicEffect(BinaryWriter w, MagicEffectDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.Index);
            w.Write(value.School);
            w.Write(value.BaseCost);
            w.Write(value.Flags);
            w.Write(value.Red);
            w.Write(value.Green);
            w.Write(value.Blue);
            w.Write(value.SizeX);
            w.Write(value.Speed);
            w.Write(value.SizeCap);
            WriteString(w, value.Icon);
            WriteString(w, value.ParticleTexture);
            WriteString(w, value.CastingObjectId);
            WriteString(w, value.HitObjectId);
            WriteString(w, value.AreaObjectId);
            WriteString(w, value.BoltObjectId);
            WriteString(w, value.CastSoundId);
            WriteString(w, value.BoltSoundId);
            WriteString(w, value.HitSoundId);
            WriteString(w, value.AreaSoundId);
            WriteString(w, value.Description);
        }

        static MagicEffectDef ReadMagicEffect(BinaryReader r)
        {
            return new MagicEffectDef
            {
                ContentId = ReadContentId(r),
                Index = r.ReadInt32(),
                School = r.ReadInt32(),
                BaseCost = r.ReadSingle(),
                Flags = r.ReadInt32(),
                Red = r.ReadInt32(),
                Green = r.ReadInt32(),
                Blue = r.ReadInt32(),
                SizeX = r.ReadSingle(),
                Speed = r.ReadSingle(),
                SizeCap = r.ReadSingle(),
                Icon = ReadString(r),
                ParticleTexture = ReadString(r),
                CastingObjectId = ReadString(r),
                HitObjectId = ReadString(r),
                AreaObjectId = ReadString(r),
                BoltObjectId = ReadString(r),
                CastSoundId = ReadString(r),
                BoltSoundId = ReadString(r),
                HitSoundId = ReadString(r),
                AreaSoundId = ReadString(r),
                Description = ReadString(r),
            };
        }

        static void WriteRegion(BinaryWriter w, RegionDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.SleepListId);
            w.Write(value.MapColorRgba);
            w.Write(value.ClearChance);
            w.Write(value.CloudyChance);
            w.Write(value.FoggyChance);
            w.Write(value.OvercastChance);
            w.Write(value.RainChance);
            w.Write(value.ThunderChance);
            w.Write(value.AshChance);
            w.Write(value.BlightChance);
            w.Write(value.SnowChance);
            w.Write(value.BlizzardChance);
            w.Write(value.SoundRefStartIndex);
            w.Write(value.SoundRefCount);
        }

        static RegionDef ReadRegion(BinaryReader r)
        {
            return new RegionDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                Name = ReadString(r),
                SleepListId = ReadString(r),
                MapColorRgba = r.ReadInt32(),
                ClearChance = r.ReadByte(),
                CloudyChance = r.ReadByte(),
                FoggyChance = r.ReadByte(),
                OvercastChance = r.ReadByte(),
                RainChance = r.ReadByte(),
                ThunderChance = r.ReadByte(),
                AshChance = r.ReadByte(),
                BlightChance = r.ReadByte(),
                SnowChance = r.ReadByte(),
                BlizzardChance = r.ReadByte(),
                SoundRefStartIndex = r.ReadInt32(),
                SoundRefCount = r.ReadInt32(),
            };
        }

        static void WriteRegionSoundRef(BinaryWriter w, RegionSoundRefDef value)
        {
            WriteString(w, value.SoundId);
            w.Write(value.Chance);
        }

        static RegionSoundRefDef ReadRegionSoundRef(BinaryReader r)
        {
            return new RegionSoundRefDef
            {
                SoundId = ReadString(r),
                Chance = r.ReadByte(),
            };
        }

        static void WriteMusicTrack(BinaryWriter w, MusicTrackDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.RelativePath);
            w.Write((byte)value.Category);
        }

        static MusicTrackDef ReadMusicTrack(BinaryReader r)
        {
            return new MusicTrackDef
            {
                ContentId = ReadContentId(r),
                RelativePath = ReadString(r),
                Category = (MusicTrackCategory)r.ReadByte(),
            };
        }

        static void WriteBaseDefArray(BinaryWriter w, BaseDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteBaseDef(w, values[i]);
        }

        static BaseDef[] ReadBaseDefArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new BaseDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadBaseDef(r);
            return values;
        }

        static void WriteActorArray(BinaryWriter w, ActorDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteActor(w, values[i]);
        }

        static ActorDef[] ReadActorArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new ActorDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadActor(r);
            return values;
        }

        static void WriteLightArray(BinaryWriter w, LightDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteLight(w, values[i]);
        }

        static LightDef[] ReadLightArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new LightDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadLight(r);
            return values;
        }

        static void WriteSoundArray(BinaryWriter w, SoundDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteSound(w, values[i]);
        }

        static SoundDef[] ReadSoundArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new SoundDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadSound(r);
            return values;
        }

        static void WriteDialogueArray(BinaryWriter w, DialogueDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteDialogue(w, values[i]);
        }

        static DialogueDef[] ReadDialogueArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new DialogueDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadDialogue(r);
            return values;
        }

        static void WriteDialogueInfoArray(BinaryWriter w, DialogueInfoDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteDialogueInfo(w, values[i]);
        }

        static DialogueInfoDef[] ReadDialogueInfoArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new DialogueInfoDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadDialogueInfo(r);
            return values;
        }

        static void WriteSpellArray(BinaryWriter w, SpellDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteSpell(w, values[i]);
        }

        static SpellDef[] ReadSpellArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new SpellDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadSpell(r);
            return values;
        }

        static void WriteEnchantmentArray(BinaryWriter w, EnchantmentDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteEnchantment(w, values[i]);
        }

        static EnchantmentDef[] ReadEnchantmentArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new EnchantmentDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadEnchantment(r);
            return values;
        }

        static void WriteMagicEffectArray(BinaryWriter w, MagicEffectDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteMagicEffect(w, values[i]);
        }

        static MagicEffectDef[] ReadMagicEffectArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new MagicEffectDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadMagicEffect(r);
            return values;
        }

        static void WriteMagicEffectInstanceArray(BinaryWriter w, MagicEffectInstanceDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteMagicEffectInstance(w, values[i]);
        }

        static MagicEffectInstanceDef[] ReadMagicEffectInstanceArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new MagicEffectInstanceDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadMagicEffectInstance(r);
            return values;
        }

        static void WriteRegionArray(BinaryWriter w, RegionDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteRegion(w, values[i]);
        }

        static RegionDef[] ReadRegionArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new RegionDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadRegion(r);
            return values;
        }

        static void WriteRegionSoundRefArray(BinaryWriter w, RegionSoundRefDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteRegionSoundRef(w, values[i]);
        }

        static RegionSoundRefDef[] ReadRegionSoundRefArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new RegionSoundRefDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadRegionSoundRef(r);
            return values;
        }

        static void WriteMusicTrackArray(BinaryWriter w, MusicTrackDef[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                WriteMusicTrack(w, values[i]);
        }

        static MusicTrackDef[] ReadMusicTrackArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new MusicTrackDef[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadMusicTrack(r);
            return values;
        }
    }
}
