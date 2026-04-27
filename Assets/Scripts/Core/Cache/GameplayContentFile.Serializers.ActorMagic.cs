using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VVardenfell.Core.Cache
{

    public static partial class GameplayContentFile
    {
        static ActorAiPackageDef ReadActorAiPackage(BinaryReader r)
        {
            return new ActorAiPackageDef
            {
                Type = (ActorAiPackageType)r.ReadByte(),
                ShouldRepeat = r.ReadByte(),
                X = r.ReadSingle(),
                Y = r.ReadSingle(),
                Z = r.ReadSingle(),
                Duration = r.ReadInt16(),
                WanderDistance = r.ReadInt16(),
                TimeOfDay = r.ReadByte(),
                Idle0 = r.ReadByte(),
                Idle1 = r.ReadByte(),
                Idle2 = r.ReadByte(),
                Idle3 = r.ReadByte(),
                Idle4 = r.ReadByte(),
                Idle5 = r.ReadByte(),
                Idle6 = r.ReadByte(),
                Idle7 = r.ReadByte(),
                TargetId = ReadString(r),
                CellName = ReadString(r),
            };
        }


        static void WriteActorTravelDestination(BinaryWriter w, ActorTravelDestinationDef value)
        {
            w.Write(value.PosX);
            w.Write(value.PosY);
            w.Write(value.PosZ);
            w.Write(value.RotX);
            w.Write(value.RotY);
            w.Write(value.RotZ);
            WriteString(w, value.CellName);
        }


        static ActorTravelDestinationDef ReadActorTravelDestination(BinaryReader r)
        {
            return new ActorTravelDestinationDef
            {
                PosX = r.ReadSingle(),
                PosY = r.ReadSingle(),
                PosZ = r.ReadSingle(),
                RotX = r.ReadSingle(),
                RotY = r.ReadSingle(),
                RotZ = r.ReadSingle(),
                CellName = ReadString(r),
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


        static void WriteItemLeveledList(BinaryWriter w, ItemLeveledListDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            w.Write(value.Flags);
            w.Write(value.ChanceNone);
            w.Write(value.FirstEntryIndex);
            w.Write(value.EntryCount);
        }


        static ItemLeveledListDef ReadItemLeveledList(BinaryReader r)
        {
            return new ItemLeveledListDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                Flags = r.ReadInt32(),
                ChanceNone = r.ReadByte(),
                FirstEntryIndex = r.ReadInt32(),
                EntryCount = r.ReadInt32(),
            };
        }


        static void WriteItemLeveledListEntry(BinaryWriter w, ItemLeveledListEntryDef value)
        {
            WriteString(w, value.ItemId);
            w.Write(value.Level);
        }


        static ItemLeveledListEntryDef ReadItemLeveledListEntry(BinaryReader r)
        {
            return new ItemLeveledListEntryDef
            {
                ItemId = ReadString(r),
                Level = r.ReadUInt16(),
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


        static void WriteAmbientSettings(BinaryWriter w, AmbientSettingsDef value)
        {
            w.Write(value.MinSecondsBetweenEnvironmentalSounds);
            w.Write(value.MaxSecondsBetweenEnvironmentalSounds);
        }


        static void WriteContainerContentRange(BinaryWriter w, ContainerContentRangeDef value)
        {
            w.Write(value.FirstItemIndex);
            w.Write(value.ItemCount);
        }


        static ContainerContentRangeDef ReadContainerContentRange(BinaryReader r)
        {
            return new ContainerContentRangeDef
            {
                FirstItemIndex = r.ReadInt32(),
                ItemCount = r.ReadInt32(),
            };
        }


        static void WriteContainerItem(BinaryWriter w, ContainerItemDef value)
        {
            WriteString(w, value.ItemId);
            w.Write(value.Count);
        }


        static ContainerItemDef ReadContainerItem(BinaryReader r)
        {
            return new ContainerItemDef
            {
                ItemId = ReadString(r),
                Count = r.ReadInt32(),
            };
        }


        static AmbientSettingsDef ReadAmbientSettings(BinaryReader r)
        {
            return new AmbientSettingsDef
            {
                MinSecondsBetweenEnvironmentalSounds = r.ReadSingle(),
                MaxSecondsBetweenEnvironmentalSounds = r.ReadSingle(),
            };
        }


        static void WriteArray<T>(BinaryWriter w, T[] values, Action<BinaryWriter, T> writeElement)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                writeElement(w, values[i]);
        }


        static T[] ReadArray<T>(BinaryReader r, Func<BinaryReader, T> readElement)
        {
            int count = r.ReadInt32();
            var values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = readElement(r);
            return values;
        }


        static void WriteInt32Value(BinaryWriter w, int value)
            => w.Write(value);


        static int ReadInt32Value(BinaryReader r)
            => r.ReadInt32();


        static void WriteBaseDefArray(BinaryWriter w, BaseDef[] values)
            => WriteArray<BaseDef>(w, values, WriteBaseDef);


        static BaseDef[] ReadBaseDefArray(BinaryReader r)
            => ReadArray<BaseDef>(r, ReadBaseDef);


        static void WriteItemEquipmentArray(BinaryWriter w, ItemEquipmentDef[] values)
            => WriteArray<ItemEquipmentDef>(w, values, WriteItemEquipment);


        static ItemEquipmentDef[] ReadItemEquipmentArray(BinaryReader r)
            => ReadArray<ItemEquipmentDef>(r, ReadItemEquipment);


        static void WriteItemEquipmentBodyPartArray(BinaryWriter w, ItemEquipmentBodyPartDef[] values)
            => WriteArray<ItemEquipmentBodyPartDef>(w, values, WriteItemEquipmentBodyPart);


        static ItemEquipmentBodyPartDef[] ReadItemEquipmentBodyPartArray(BinaryReader r)
            => ReadArray<ItemEquipmentBodyPartDef>(r, ReadItemEquipmentBodyPart);


        static void WriteGenericRecordArray(BinaryWriter w, GenericRecordDef[] values)
            => WriteArray<GenericRecordDef>(w, values, WriteGenericRecord);


        static GenericRecordDef[] ReadGenericRecordArray(BinaryReader r)
            => ReadArray<GenericRecordDef>(r, ReadGenericRecord);


        static void WriteActorBodyPartArray(BinaryWriter w, ActorBodyPartDef[] values)
            => WriteArray<ActorBodyPartDef>(w, values, WriteActorBodyPart);


        static ActorBodyPartDef[] ReadActorBodyPartArray(BinaryReader r)
            => ReadArray<ActorBodyPartDef>(r, ReadActorBodyPart);


        static void WritePathGridArray(BinaryWriter w, PathGridDef[] values)
            => WriteArray<PathGridDef>(w, values, WritePathGrid);


        static PathGridDef[] ReadPathGridArray(BinaryReader r)
            => ReadArray<PathGridDef>(r, ReadPathGrid);


        static void WritePathGridPointArray(BinaryWriter w, PathGridPointDef[] values)
            => WriteArray<PathGridPointDef>(w, values, WritePathGridPoint);


        static PathGridPointDef[] ReadPathGridPointArray(BinaryReader r)
            => ReadArray<PathGridPointDef>(r, ReadPathGridPoint);


        static void WritePathGridConnectionArray(BinaryWriter w, PathGridConnectionDef[] values)
            => WriteArray<PathGridConnectionDef>(w, values, WritePathGridConnection);


        static PathGridConnectionDef[] ReadPathGridConnectionArray(BinaryReader r)
            => ReadArray<PathGridConnectionDef>(r, ReadPathGridConnection);


        static void WritePathGridNavigationNodeArray(BinaryWriter w, PathGridNavigationNodeDef[] values)
            => WriteArray<PathGridNavigationNodeDef>(w, values, WritePathGridNavigationNode);


        static PathGridNavigationNodeDef[] ReadPathGridNavigationNodeArray(BinaryReader r)
            => ReadArray<PathGridNavigationNodeDef>(r, ReadPathGridNavigationNode);


        static void WritePathGridNavigationEdgeArray(BinaryWriter w, PathGridNavigationEdgeDef[] values)
            => WriteArray<PathGridNavigationEdgeDef>(w, values, WritePathGridNavigationEdge);


        static PathGridNavigationEdgeDef[] ReadPathGridNavigationEdgeArray(BinaryReader r)
            => ReadArray<PathGridNavigationEdgeDef>(r, ReadPathGridNavigationEdge);


        static void WritePathGridNavigationPortalArray(BinaryWriter w, PathGridNavigationPortalDef[] values)
            => WriteArray<PathGridNavigationPortalDef>(w, values, WritePathGridNavigationPortal);


        static PathGridNavigationPortalDef[] ReadPathGridNavigationPortalArray(BinaryReader r)
            => ReadArray<PathGridNavigationPortalDef>(r, ReadPathGridNavigationPortal);


        static void WritePathGridNavigationAbstractEdgeArray(BinaryWriter w, PathGridNavigationAbstractEdgeDef[] values)
            => WriteArray<PathGridNavigationAbstractEdgeDef>(w, values, WritePathGridNavigationAbstractEdge);


        static PathGridNavigationAbstractEdgeDef[] ReadPathGridNavigationAbstractEdgeArray(BinaryReader r)
            => ReadArray<PathGridNavigationAbstractEdgeDef>(r, ReadPathGridNavigationAbstractEdge);


        static void WritePathGridNavigationNeighborArray(BinaryWriter w, PathGridNavigationNeighborDef[] values)
            => WriteArray<PathGridNavigationNeighborDef>(w, values, WritePathGridNavigationNeighbor);


        static PathGridNavigationNeighborDef[] ReadPathGridNavigationNeighborArray(BinaryReader r)
            => ReadArray<PathGridNavigationNeighborDef>(r, ReadPathGridNavigationNeighbor);


        static void WriteClassArray(BinaryWriter w, ClassDef[] values)
            => WriteArray<ClassDef>(w, values, WriteClass);


        static ClassDef[] ReadClassArray(BinaryReader r)
            => ReadArray<ClassDef>(r, ReadClass);


        static void WriteRaceArray(BinaryWriter w, RaceDef[] values)
            => WriteArray<RaceDef>(w, values, WriteRace);


        static RaceDef[] ReadRaceArray(BinaryReader r)
            => ReadArray<RaceDef>(r, ReadRace);


        static void WriteFactionArray(BinaryWriter w, FactionDef[] values)
            => WriteArray<FactionDef>(w, values, WriteFaction);


        static FactionDef[] ReadFactionArray(BinaryReader r)
            => ReadArray<FactionDef>(r, ReadFaction);


        static void WriteIntArray(BinaryWriter w, int[] values)
            => WriteArray<int>(w, values, WriteInt32Value);


        static int[] ReadIntArray(BinaryReader r)
            => ReadArray<int>(r, ReadInt32Value);


        }
    }
