using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VVardenfell.Core.Cache
{

    public static partial class GameplayContentFile
    {
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


        static void WriteItemEquipment(BinaryWriter w, ItemEquipmentDef value)
        {
            w.Write(value.Item.Value);
            w.Write((byte)value.Kind);
            w.Write((byte)value.Slot);
            w.Write(value.Type);
            w.Write(value.Value);
            w.Write(value.Weight);
            w.Write(value.Health);
            w.Write(value.Armor);
            w.Write(value.EnchantCapacity);
            w.Write(value.DamageMin);
            w.Write(value.DamageMax);
            w.Write(value.ChopMin);
            w.Write(value.ChopMax);
            w.Write(value.SlashMin);
            w.Write(value.SlashMax);
            w.Write(value.ThrustMin);
            w.Write(value.ThrustMax);
            w.Write(value.WeaponSpeed);
            w.Write(value.WeaponReach);
            w.Write(value.WeaponFlags);
            w.Write(value.FirstBodyPartIndex);
            w.Write(value.BodyPartCount);
        }


        static ItemEquipmentDef ReadItemEquipment(BinaryReader r)
        {
            return new ItemEquipmentDef
            {
                Item = new ItemDefHandle { Value = r.ReadInt32() },
                Kind = (ItemEquipmentKind)r.ReadByte(),
                Slot = (ItemEquipmentSlot)r.ReadByte(),
                Type = r.ReadInt32(),
                Value = r.ReadInt32(),
                Weight = r.ReadSingle(),
                Health = r.ReadInt32(),
                Armor = r.ReadInt32(),
                EnchantCapacity = r.ReadInt32(),
                DamageMin = r.ReadInt32(),
                DamageMax = r.ReadInt32(),
                ChopMin = r.ReadInt32(),
                ChopMax = r.ReadInt32(),
                SlashMin = r.ReadInt32(),
                SlashMax = r.ReadInt32(),
                ThrustMin = r.ReadInt32(),
                ThrustMax = r.ReadInt32(),
                WeaponSpeed = r.ReadSingle(),
                WeaponReach = r.ReadSingle(),
                WeaponFlags = r.ReadUInt32(),
                FirstBodyPartIndex = r.ReadInt32(),
                BodyPartCount = r.ReadInt32(),
            };
        }


        static void WriteItemEquipmentBodyPart(BinaryWriter w, ItemEquipmentBodyPartDef value)
        {
            w.Write(value.Item.Value);
            w.Write((byte)value.Part);
            WriteString(w, value.MaleBodyPartId);
            WriteString(w, value.FemaleBodyPartId);
        }


        static ItemEquipmentBodyPartDef ReadItemEquipmentBodyPart(BinaryReader r)
        {
            return new ItemEquipmentBodyPartDef
            {
                Item = new ItemDefHandle { Value = r.ReadInt32() },
                Part = (ItemEquipmentPartReference)r.ReadByte(),
                MaleBodyPartId = ReadString(r),
                FemaleBodyPartId = ReadString(r),
            };
        }


        static void WriteGenericRecord(BinaryWriter w, GenericRecordDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Model);
            WriteString(w, value.Icon);
            WriteString(w, value.ScriptId);
            WriteString(w, value.Text);
            w.Write((byte)value.ValueKind);
            w.Write(value.Flags);
            w.Write(value.Int0);
            w.Write(value.Int1);
            w.Write(value.Int2);
            w.Write(value.Float0);
            w.Write(value.Float1);
        }


        static GenericRecordDef ReadGenericRecord(BinaryReader r)
        {
            return new GenericRecordDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Model = ReadString(r),
                Icon = ReadString(r),
                ScriptId = ReadString(r),
                Text = ReadString(r),
                ValueKind = (GenericRecordValueKind)r.ReadByte(),
                Flags = r.ReadUInt32(),
                Int0 = r.ReadInt32(),
                Int1 = r.ReadInt32(),
                Int2 = r.ReadInt32(),
                Float0 = r.ReadSingle(),
                Float1 = r.ReadSingle(),
            };
        }


        static void WriteMorrowindScriptProgram(BinaryWriter w, MorrowindScriptProgramDef value)
        {
            WriteString(w, value.Id);
            w.Write(value.SourceScriptIndex);
            w.Write(value.Status);
            WriteString(w, value.DisabledReason);
            w.Write(value.FirstInstructionIndex);
            w.Write(value.InstructionCount);
            w.Write(value.FirstLocalIndex);
            w.Write(value.LocalCount);
            w.Write(value.MaxStack);
        }


        static MorrowindScriptProgramDef ReadMorrowindScriptProgram(BinaryReader r)
        {
            return new MorrowindScriptProgramDef
            {
                Id = ReadString(r),
                SourceScriptIndex = r.ReadInt32(),
                Status = r.ReadByte(),
                DisabledReason = ReadString(r),
                FirstInstructionIndex = r.ReadInt32(),
                InstructionCount = r.ReadInt32(),
                FirstLocalIndex = r.ReadInt32(),
                LocalCount = r.ReadInt32(),
                MaxStack = r.ReadInt32(),
            };
        }


        static void WriteMorrowindScriptInstruction(BinaryWriter w, MorrowindScriptInstructionDef value)
        {
            w.Write(value.Opcode);
            w.Write(value.Operand0);
            w.Write(value.Operand1);
            w.Write(value.Int0);
            w.Write(value.Int1);
            w.Write(value.Int2);
            w.Write(value.Float0);
            w.Write(value.Float1);
            w.Write(value.Float2);
            w.Write(value.Float3);
        }


        static MorrowindScriptInstructionDef ReadMorrowindScriptInstruction(BinaryReader r)
        {
            return new MorrowindScriptInstructionDef
            {
                Opcode = r.ReadByte(),
                Operand0 = r.ReadByte(),
                Operand1 = r.ReadInt16(),
                Int0 = r.ReadInt32(),
                Int1 = r.ReadInt32(),
                Int2 = r.ReadInt32(),
                Float0 = r.ReadSingle(),
                Float1 = r.ReadSingle(),
                Float2 = r.ReadSingle(),
                Float3 = r.ReadSingle(),
            };
        }


        static void WriteMorrowindScriptLocal(BinaryWriter w, MorrowindScriptLocalDef value)
        {
            WriteString(w, value.Name);
            w.Write(value.ValueKind);
        }


        static MorrowindScriptLocalDef ReadMorrowindScriptLocal(BinaryReader r)
        {
            return new MorrowindScriptLocalDef
            {
                Name = ReadString(r),
                ValueKind = r.ReadByte(),
            };
        }


        static void WriteMorrowindScriptMessage(BinaryWriter w, MorrowindScriptMessageDef value)
        {
            WriteString(w, value.Text);
        }


        static MorrowindScriptMessageDef ReadMorrowindScriptMessage(BinaryReader r)
        {
            return new MorrowindScriptMessageDef
            {
                Text = ReadString(r),
            };
        }


        static void WriteActorBodyPart(BinaryWriter w, ActorBodyPartDef value)
        {
            WriteContentId(w, value.ContentId);
            WriteString(w, value.Id);
            WriteString(w, value.RaceId);
            WriteString(w, value.Model);
            w.Write((byte)value.Part);
            w.Write((byte)value.Type);
            w.Write(value.Female);
            w.Write(value.Vampire);
            w.Write(value.NotPlayable);
            w.Write(value.FirstPerson);
        }


        static ActorBodyPartDef ReadActorBodyPart(BinaryReader r)
        {
            return new ActorBodyPartDef
            {
                ContentId = ReadContentId(r),
                Id = ReadString(r),
                RaceId = ReadString(r),
                Model = ReadString(r),
                Part = (ActorBodyPartMeshPart)r.ReadByte(),
                Type = (ActorBodyPartMeshType)r.ReadByte(),
                Female = r.ReadByte(),
                Vampire = r.ReadByte(),
                NotPlayable = r.ReadByte(),
                FirstPerson = r.ReadByte(),
            };
        }


        static void WritePathGrid(BinaryWriter w, PathGridDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.CellId);
            w.Write(value.GridX);
            w.Write(value.GridY);
            w.Write(value.Granularity);
            w.Write(value.DeclaredPointCount);
            w.Write(value.FirstPointIndex);
            w.Write(value.PointCount);
            w.Write(value.FirstConnectionIndex);
            w.Write(value.ConnectionCount);
            w.Write(value.FirstNavigationNodeIndex);
            w.Write(value.NavigationNodeCount);
            w.Write(value.FirstNavigationEdgeIndex);
            w.Write(value.NavigationEdgeCount);
            w.Write(value.FirstNavigationPortalIndex);
            w.Write(value.NavigationPortalCount);
            w.Write(value.FirstNavigationAbstractEdgeIndex);
            w.Write(value.NavigationAbstractEdgeCount);
            w.Write(value.FirstNavigationNeighborIndex);
            w.Write(value.NavigationNeighborCount);
            w.Write(value.NavigationComponentId);
            w.Write(value.IsExterior);
        }


        static PathGridDef ReadPathGrid(BinaryReader r)
        {
            return new PathGridDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                CellId = ReadString(r),
                GridX = r.ReadInt32(),
                GridY = r.ReadInt32(),
                Granularity = r.ReadInt16(),
                DeclaredPointCount = r.ReadUInt16(),
                FirstPointIndex = r.ReadInt32(),
                PointCount = r.ReadInt32(),
                FirstConnectionIndex = r.ReadInt32(),
                ConnectionCount = r.ReadInt32(),
                FirstNavigationNodeIndex = r.ReadInt32(),
                NavigationNodeCount = r.ReadInt32(),
                FirstNavigationEdgeIndex = r.ReadInt32(),
                NavigationEdgeCount = r.ReadInt32(),
                FirstNavigationPortalIndex = r.ReadInt32(),
                NavigationPortalCount = r.ReadInt32(),
                FirstNavigationAbstractEdgeIndex = r.ReadInt32(),
                NavigationAbstractEdgeCount = r.ReadInt32(),
                FirstNavigationNeighborIndex = r.ReadInt32(),
                NavigationNeighborCount = r.ReadInt32(),
                NavigationComponentId = r.ReadInt32(),
                IsExterior = r.ReadByte(),
            };
        }


        static void WritePathGridPoint(BinaryWriter w, PathGridPointDef value)
        {
            w.Write(value.SourceX);
            w.Write(value.SourceY);
            w.Write(value.SourceZ);
            w.Write(value.UnityX);
            w.Write(value.UnityY);
            w.Write(value.UnityZ);
            w.Write(value.Autogenerated);
            w.Write(value.SourceConnectionCount);
            w.Write(value.FirstConnectionIndex);
            w.Write(value.ConnectionCount);
        }


        static PathGridPointDef ReadPathGridPoint(BinaryReader r)
        {
            return new PathGridPointDef
            {
                SourceX = r.ReadInt32(),
                SourceY = r.ReadInt32(),
                SourceZ = r.ReadInt32(),
                UnityX = r.ReadSingle(),
                UnityY = r.ReadSingle(),
                UnityZ = r.ReadSingle(),
                Autogenerated = r.ReadByte(),
                SourceConnectionCount = r.ReadByte(),
                FirstConnectionIndex = r.ReadInt32(),
                ConnectionCount = r.ReadInt32(),
            };
        }


        static void WritePathGridConnection(BinaryWriter w, PathGridConnectionDef value)
        {
            w.Write(value.FromPointIndex);
            w.Write(value.ToPointIndex);
        }


        static PathGridConnectionDef ReadPathGridConnection(BinaryReader r)
        {
            return new PathGridConnectionDef
            {
                FromPointIndex = r.ReadInt32(),
                ToPointIndex = r.ReadInt32(),
            };
        }


        static void WritePathGridNavigationNode(BinaryWriter w, PathGridNavigationNodeDef value)
        {
            w.Write(value.PathGridIndex);
            w.Write(value.PointIndex);
            w.Write(value.SourceX);
            w.Write(value.SourceY);
            w.Write(value.SourceZ);
            w.Write(value.UnityX);
            w.Write(value.UnityY);
            w.Write(value.UnityZ);
            w.Write(value.FirstEdgeIndex);
            w.Write(value.EdgeCount);
            w.Write(value.ComponentId);
            w.Write(value.IsPortal);
        }


        static PathGridNavigationNodeDef ReadPathGridNavigationNode(BinaryReader r)
        {
            return new PathGridNavigationNodeDef
            {
                PathGridIndex = r.ReadInt32(),
                PointIndex = r.ReadInt32(),
                SourceX = r.ReadInt32(),
                SourceY = r.ReadInt32(),
                SourceZ = r.ReadInt32(),
                UnityX = r.ReadSingle(),
                UnityY = r.ReadSingle(),
                UnityZ = r.ReadSingle(),
                FirstEdgeIndex = r.ReadInt32(),
                EdgeCount = r.ReadInt32(),
                ComponentId = r.ReadInt32(),
                IsPortal = r.ReadByte(),
            };
        }


        static void WritePathGridNavigationEdge(BinaryWriter w, PathGridNavigationEdgeDef value)
        {
            w.Write(value.FromNodeIndex);
            w.Write(value.ToNodeIndex);
            w.Write(value.Cost);
            w.Write((byte)value.Kind);
        }


        static PathGridNavigationEdgeDef ReadPathGridNavigationEdge(BinaryReader r)
        {
            return new PathGridNavigationEdgeDef
            {
                FromNodeIndex = r.ReadInt32(),
                ToNodeIndex = r.ReadInt32(),
                Cost = r.ReadSingle(),
                Kind = (PathGridNavigationEdgeKind)r.ReadByte(),
            };
        }


        static void WritePathGridNavigationPortal(BinaryWriter w, PathGridNavigationPortalDef value)
        {
            w.Write(value.PathGridIndex);
            w.Write(value.NodeIndex);
            w.Write(value.PointIndex);
            w.Write(value.FirstAbstractEdgeIndex);
            w.Write(value.AbstractEdgeCount);
            w.Write(value.ComponentId);
        }


        static PathGridNavigationPortalDef ReadPathGridNavigationPortal(BinaryReader r)
        {
            return new PathGridNavigationPortalDef
            {
                PathGridIndex = r.ReadInt32(),
                NodeIndex = r.ReadInt32(),
                PointIndex = r.ReadInt32(),
                FirstAbstractEdgeIndex = r.ReadInt32(),
                AbstractEdgeCount = r.ReadInt32(),
                ComponentId = r.ReadInt32(),
            };
        }


        static void WritePathGridNavigationAbstractEdge(BinaryWriter w, PathGridNavigationAbstractEdgeDef value)
        {
            w.Write(value.FromPortalIndex);
            w.Write(value.ToPortalIndex);
            w.Write(value.Cost);
            w.Write((byte)value.Kind);
        }


        static PathGridNavigationAbstractEdgeDef ReadPathGridNavigationAbstractEdge(BinaryReader r)
        {
            return new PathGridNavigationAbstractEdgeDef
            {
                FromPortalIndex = r.ReadInt32(),
                ToPortalIndex = r.ReadInt32(),
                Cost = r.ReadSingle(),
                Kind = (PathGridNavigationEdgeKind)r.ReadByte(),
            };
        }


        static void WritePathGridNavigationNeighbor(BinaryWriter w, PathGridNavigationNeighborDef value)
        {
            w.Write(value.PathGridIndex);
            w.Write(value.NeighborPathGridIndex);
            w.Write(value.BorderEdgeCount);
            w.Write(value.MinCost);
        }


        static PathGridNavigationNeighborDef ReadPathGridNavigationNeighbor(BinaryReader r)
        {
            return new PathGridNavigationNeighborDef
            {
                PathGridIndex = r.ReadInt32(),
                NeighborPathGridIndex = r.ReadInt32(),
                BorderEdgeCount = r.ReadInt32(),
                MinCost = r.ReadSingle(),
            };
        }


        static void WriteClass(BinaryWriter w, ClassDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Description);
            w.Write(value.FavoredAttribute0);
            w.Write(value.FavoredAttribute1);
            w.Write(value.Specialization);
            WriteIntArray(w, value.MinorSkills);
            WriteIntArray(w, value.MajorSkills);
            w.Write(value.Playable);
            w.Write(value.Services);
        }


        static ClassDef ReadClass(BinaryReader r)
        {
            return new ClassDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Description = ReadString(r),
                FavoredAttribute0 = r.ReadInt32(),
                FavoredAttribute1 = r.ReadInt32(),
                Specialization = r.ReadInt32(),
                MinorSkills = ReadIntArray(r),
                MajorSkills = ReadIntArray(r),
                Playable = r.ReadInt32(),
                Services = r.ReadInt32(),
            };
        }


        static void WriteRace(BinaryWriter w, RaceDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            WriteString(w, value.Description);
            WriteRaceSkillBonusArray(w, value.SkillBonuses);
            WriteIntArray(w, value.MaleAttributes);
            WriteIntArray(w, value.FemaleAttributes);
            w.Write(value.MaleHeight);
            w.Write(value.FemaleHeight);
            w.Write(value.MaleWeight);
            w.Write(value.FemaleWeight);
            w.Write(value.Flags);
            WriteStringArray(w, value.PowerSpellIds);
        }


        static RaceDef ReadRace(BinaryReader r)
        {
            return new RaceDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                Description = ReadString(r),
                SkillBonuses = ReadRaceSkillBonusArray(r),
                MaleAttributes = ReadIntArray(r),
                FemaleAttributes = ReadIntArray(r),
                MaleHeight = r.ReadSingle(),
                FemaleHeight = r.ReadSingle(),
                MaleWeight = r.ReadSingle(),
                FemaleWeight = r.ReadSingle(),
                Flags = r.ReadInt32(),
                PowerSpellIds = ReadStringArray(r),
            };
        }


        static void WriteRaceSkillBonus(BinaryWriter w, RaceSkillBonusDef value)
        {
            w.Write(value.Skill);
            w.Write(value.Bonus);
        }


        static RaceSkillBonusDef ReadRaceSkillBonus(BinaryReader r)
        {
            return new RaceSkillBonusDef
            {
                Skill = r.ReadInt32(),
                Bonus = r.ReadInt32(),
            };
        }


        static void WriteFaction(BinaryWriter w, FactionDef value)
        {
            WriteContentId(w, value.ContentId);
            w.Write(value.RecordTag);
            WriteString(w, value.Id);
            WriteString(w, value.Name);
            w.Write(value.FavoredAttribute0);
            w.Write(value.FavoredAttribute1);
            WriteFactionRankRequirementArray(w, value.RankRequirements);
            WriteIntArray(w, value.Skills);
            w.Write(value.Hidden);
            WriteStringArray(w, value.RankNames);
            WriteFactionReactionArray(w, value.Reactions);
        }


        static FactionDef ReadFaction(BinaryReader r)
        {
            return new FactionDef
            {
                ContentId = ReadContentId(r),
                RecordTag = r.ReadUInt32(),
                Id = ReadString(r),
                Name = ReadString(r),
                FavoredAttribute0 = r.ReadInt32(),
                FavoredAttribute1 = r.ReadInt32(),
                RankRequirements = ReadFactionRankRequirementArray(r),
                Skills = ReadIntArray(r),
                Hidden = r.ReadInt32(),
                RankNames = ReadStringArray(r),
                Reactions = ReadFactionReactionArray(r),
            };
        }


        static void WriteFactionRankRequirement(BinaryWriter w, FactionRankRequirementDef value)
        {
            w.Write(value.Attribute1);
            w.Write(value.Attribute2);
            w.Write(value.PrimarySkill);
            w.Write(value.FavoredSkill);
            w.Write(value.Reaction);
        }


        static FactionRankRequirementDef ReadFactionRankRequirement(BinaryReader r)
        {
            return new FactionRankRequirementDef
            {
                Attribute1 = r.ReadInt32(),
                Attribute2 = r.ReadInt32(),
                PrimarySkill = r.ReadInt32(),
                FavoredSkill = r.ReadInt32(),
                Reaction = r.ReadInt32(),
            };
        }


        static void WriteFactionReaction(BinaryWriter w, FactionReactionDef value)
        {
            WriteString(w, value.FactionId);
            w.Write(value.Reaction);
        }


        static FactionReactionDef ReadFactionReaction(BinaryReader r)
        {
            return new FactionReactionDef
            {
                FactionId = ReadString(r),
                Reaction = r.ReadInt32(),
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
            w.Write(value.AutoCalculatedStats);
            w.Write(value.BloodType);
            w.Write(value.Disposition);
            w.Write(value.Reputation);
            w.Write(value.Rank);
            w.Write(value.Gold);
            w.Write(value.CreatureType);
            w.Write(value.SoulValue);
            w.Write(value.Combat);
            w.Write(value.Magic);
            w.Write(value.Stealth);
            WriteActorAttributes(w, value.Attributes);
            WriteActorSkills(w, value.Skills);
            WriteActorVitals(w, value.Vitals);
            WriteActorAiData(w, value.AiData);
            w.Write(value.FirstSpellIndex);
            w.Write(value.SpellCount);
            w.Write(value.FirstInventoryIndex);
            w.Write(value.InventoryCount);
            w.Write(value.FirstAiPackageIndex);
            w.Write(value.AiPackageCount);
            w.Write(value.FirstTravelDestinationIndex);
            w.Write(value.TravelDestinationCount);
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
                AutoCalculatedStats = r.ReadByte(),
                BloodType = r.ReadInt32(),
                Disposition = r.ReadInt32(),
                Reputation = r.ReadInt32(),
                Rank = r.ReadInt32(),
                Gold = r.ReadInt32(),
                CreatureType = r.ReadInt32(),
                SoulValue = r.ReadInt32(),
                Combat = r.ReadInt32(),
                Magic = r.ReadInt32(),
                Stealth = r.ReadInt32(),
                Attributes = ReadActorAttributes(r),
                Skills = ReadActorSkills(r),
                Vitals = ReadActorVitals(r),
                AiData = ReadActorAiData(r),
                FirstSpellIndex = r.ReadInt32(),
                SpellCount = r.ReadInt32(),
                FirstInventoryIndex = r.ReadInt32(),
                InventoryCount = r.ReadInt32(),
                FirstAiPackageIndex = r.ReadInt32(),
                AiPackageCount = r.ReadInt32(),
                FirstTravelDestinationIndex = r.ReadInt32(),
                TravelDestinationCount = r.ReadInt32(),
            };
        }


        static void WriteActorAttributes(BinaryWriter w, ActorAttributeDef value)
        {
            w.Write(value.Strength);
            w.Write(value.Intelligence);
            w.Write(value.Willpower);
            w.Write(value.Agility);
            w.Write(value.Speed);
            w.Write(value.Endurance);
            w.Write(value.Personality);
            w.Write(value.Luck);
        }


        static ActorAttributeDef ReadActorAttributes(BinaryReader r)
        {
            return new ActorAttributeDef
            {
                Strength = r.ReadInt32(),
                Intelligence = r.ReadInt32(),
                Willpower = r.ReadInt32(),
                Agility = r.ReadInt32(),
                Speed = r.ReadInt32(),
                Endurance = r.ReadInt32(),
                Personality = r.ReadInt32(),
                Luck = r.ReadInt32(),
            };
        }


        static void WriteActorSkills(BinaryWriter w, ActorSkillDef value)
        {
            w.Write(value.Block);
            w.Write(value.Armorer);
            w.Write(value.MediumArmor);
            w.Write(value.HeavyArmor);
            w.Write(value.BluntWeapon);
            w.Write(value.LongBlade);
            w.Write(value.Axe);
            w.Write(value.Spear);
            w.Write(value.Athletics);
            w.Write(value.Enchant);
            w.Write(value.Destruction);
            w.Write(value.Alteration);
            w.Write(value.Illusion);
            w.Write(value.Conjuration);
            w.Write(value.Mysticism);
            w.Write(value.Restoration);
            w.Write(value.Alchemy);
            w.Write(value.Unarmored);
            w.Write(value.Security);
            w.Write(value.Sneak);
            w.Write(value.Acrobatics);
            w.Write(value.LightArmor);
            w.Write(value.ShortBlade);
            w.Write(value.Marksman);
            w.Write(value.Mercantile);
            w.Write(value.Speechcraft);
            w.Write(value.HandToHand);
        }


        static ActorSkillDef ReadActorSkills(BinaryReader r)
        {
            return new ActorSkillDef
            {
                Block = r.ReadInt32(),
                Armorer = r.ReadInt32(),
                MediumArmor = r.ReadInt32(),
                HeavyArmor = r.ReadInt32(),
                BluntWeapon = r.ReadInt32(),
                LongBlade = r.ReadInt32(),
                Axe = r.ReadInt32(),
                Spear = r.ReadInt32(),
                Athletics = r.ReadInt32(),
                Enchant = r.ReadInt32(),
                Destruction = r.ReadInt32(),
                Alteration = r.ReadInt32(),
                Illusion = r.ReadInt32(),
                Conjuration = r.ReadInt32(),
                Mysticism = r.ReadInt32(),
                Restoration = r.ReadInt32(),
                Alchemy = r.ReadInt32(),
                Unarmored = r.ReadInt32(),
                Security = r.ReadInt32(),
                Sneak = r.ReadInt32(),
                Acrobatics = r.ReadInt32(),
                LightArmor = r.ReadInt32(),
                ShortBlade = r.ReadInt32(),
                Marksman = r.ReadInt32(),
                Mercantile = r.ReadInt32(),
                Speechcraft = r.ReadInt32(),
                HandToHand = r.ReadInt32(),
            };
        }


        static void WriteActorVitals(BinaryWriter w, ActorVitalDef value)
        {
            w.Write(value.Health);
            w.Write(value.Magicka);
            w.Write(value.Fatigue);
        }


        static ActorVitalDef ReadActorVitals(BinaryReader r)
        {
            return new ActorVitalDef
            {
                Health = r.ReadInt32(),
                Magicka = r.ReadInt32(),
                Fatigue = r.ReadInt32(),
            };
        }


        static void WriteActorAiData(BinaryWriter w, ActorAiDataDef value)
        {
            w.Write(value.Hello);
            w.Write(value.Fight);
            w.Write(value.Flee);
            w.Write(value.Alarm);
            w.Write(value.Services);
        }


        static ActorAiDataDef ReadActorAiData(BinaryReader r)
        {
            return new ActorAiDataDef
            {
                Hello = r.ReadInt32(),
                Fight = r.ReadByte(),
                Flee = r.ReadByte(),
                Alarm = r.ReadByte(),
                Services = r.ReadInt32(),
            };
        }


        static void WriteActorSpell(BinaryWriter w, ActorSpellDef value)
            => WriteString(w, value.SpellId);


        static ActorSpellDef ReadActorSpell(BinaryReader r)
        {
            return new ActorSpellDef
            {
                SpellId = ReadString(r),
            };
        }


        static void WriteActorAiPackage(BinaryWriter w, ActorAiPackageDef value)
        {
            w.Write((byte)value.Type);
            w.Write(value.ShouldRepeat);
            w.Write(value.X);
            w.Write(value.Y);
            w.Write(value.Z);
            w.Write(value.Duration);
            w.Write(value.WanderDistance);
            w.Write(value.TimeOfDay);
            w.Write(value.Idle0);
            w.Write(value.Idle1);
            w.Write(value.Idle2);
            w.Write(value.Idle3);
            w.Write(value.Idle4);
            w.Write(value.Idle5);
            w.Write(value.Idle6);
            w.Write(value.Idle7);
            WriteString(w, value.TargetId);
            WriteString(w, value.CellName);
        }


        }
    }
