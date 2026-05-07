using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeContentBlobBuilder
    {
        public static BlobAssetReference<RuntimeContentBlob> Build(GameplayContentData source)
        {
            source ??= new GameplayContentData();

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref RuntimeContentBlob root = ref builder.ConstructRoot<RuntimeContentBlob>();
                var genericRecordPowerSpellIds = new List<string>();

                CopyActorArray(ref builder, ref root.Actors, source.Actors);
            CopyActorSpellArray(ref builder, ref root.ActorSpells, source.ActorSpells);
            CopyContainerItemArray(ref builder, ref root.ActorInventoryItems, source.ActorInventoryItems);
            CopyActorAiPackageArray(ref builder, ref root.ActorAiPackages, source.ActorAiPackages);
            CopyActorTravelDestinationArray(ref builder, ref root.ActorTravelDestinations, source.ActorTravelDestinations);
            CopyBaseArray(ref builder, ref root.Activators, source.Activators);
            CopyBaseArray(ref builder, ref root.Doors, source.Doors);
            CopyBaseArray(ref builder, ref root.Containers, source.Containers);
            CopyUnmanagedArray(ref builder, ref root.ContainerContentRanges, source.ContainerContentRanges);
            CopyContainerItemArray(ref builder, ref root.ContainerItems, source.ContainerItems);
            CopyBaseArray(ref builder, ref root.Items, source.Items);
            CopyUnmanagedArray(ref builder, ref root.ItemEquipment, source.ItemEquipment);
            CopyItemEquipmentBodyPartArray(ref builder, ref root.ItemEquipmentBodyParts, source.ItemEquipmentBodyParts);
            CopyLightArray(ref builder, ref root.Lights, source.Lights);
            CopyItemLeveledListArray(ref builder, ref root.ItemLeveledLists, source.ItemLeveledLists);
            CopyItemLeveledListEntryArray(ref builder, ref root.ItemLeveledListEntries, source.ItemLeveledListEntries);
            CopyItemLeveledListArray(ref builder, ref root.CreatureLeveledLists, source.CreatureLeveledLists);
            CopyItemLeveledListEntryArray(ref builder, ref root.CreatureLeveledListEntries, source.CreatureLeveledListEntries);
            CopySoundArray(ref builder, ref root.Sounds, source.Sounds);
            CopyDialogueArray(ref builder, ref root.Dialogues, source.Dialogues);
            CopyDialogueInfoArray(ref builder, ref root.DialogueInfos, source.DialogueInfos);
            CopyDialogueConditionArray(ref builder, ref root.DialogueConditions, source.DialogueConditions);
            CopySpellArray(ref builder, ref root.Spells, source.Spells);
            CopyEnchantmentArray(ref builder, ref root.Enchantments, source.Enchantments);
            CopyMagicEffectArray(ref builder, ref root.MagicEffects, source.MagicEffects);
            CopyUnmanagedArray(ref builder, ref root.MagicEffectInstances, source.MagicEffectInstances);
            CopyRegionArray(ref builder, ref root.Regions, source.Regions);
            CopyRegionSoundRefArray(ref builder, ref root.RegionSoundRefs, source.RegionSoundRefs);
            CopyMusicTrackArray(ref builder, ref root.MusicTracks, source.MusicTracks);
            root.AmbientSettings = source.AmbientSettings;
            root.WeatherSettings = source.WeatherSettings;
            CopyWeatherDefinitionArray(ref builder, ref root.WeatherDefinitions, source.WeatherDefinitions);
            CopySkyWeatherVisualSettings(ref builder, ref root, source.SkyWeatherVisualSettings);
            CopyGenericRecordArray(ref builder, ref root.GameSettings, source.GameSettings, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.Globals, source.Globals, genericRecordPowerSpellIds);
            CopyClassArray(ref builder, ref root, source.Classes);
            CopyFactionArray(ref builder, ref root, source.Factions);
            CopyRaceArray(ref builder, ref root, source.Races);
            CopyGenericRecordArray(ref builder, ref root.Birthsigns, source.Birthsigns, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.Skills, source.Skills, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.Scripts, source.Scripts, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.StartScripts, source.StartScripts, genericRecordPowerSpellIds);
            CopyMorrowindScriptProgramArray(ref builder, ref root.MorrowindScriptPrograms, source.MorrowindScriptPrograms);
            CopyUnmanagedArray(ref builder, ref root.MorrowindScriptInstructions, source.MorrowindScriptInstructions);
            CopyMorrowindScriptLocalArray(ref builder, ref root.MorrowindScriptLocals, source.MorrowindScriptLocals);
            CopyMorrowindScriptMessageArray(ref builder, ref root.MorrowindScriptMessages, source.MorrowindScriptMessages);
            CopyExplicitRefTargetArray(ref builder, ref root.ExplicitRefTargets, source.ExplicitRefTargets);
            CopyGenericRecordArray(ref builder, ref root.SoundGenerators, source.SoundGenerators, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.LandTextures, source.LandTextures, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.Statics, source.Statics, genericRecordPowerSpellIds);
            CopyGenericRecordArray(ref builder, ref root.BodyParts, source.BodyParts, genericRecordPowerSpellIds);
            CopyActorBodyPartArray(ref builder, ref root.ActorBodyParts, source.ActorBodyParts);
            CopyPathGridArray(ref builder, ref root.PathGrids, source.PathGrids);
            CopyUnmanagedArray(ref builder, ref root.PathGridPoints, source.PathGridPoints);
            CopyUnmanagedArray(ref builder, ref root.PathGridConnections, source.PathGridConnections);
            CopyUnmanagedArray(ref builder, ref root.PathGridNavigationNodes, source.PathGridNavigationNodes);
            CopyUnmanagedArray(ref builder, ref root.PathGridNavigationEdges, source.PathGridNavigationEdges);
            CopyUnmanagedArray(ref builder, ref root.PathGridNavigationPortals, source.PathGridNavigationPortals);
            CopyUnmanagedArray(ref builder, ref root.PathGridNavigationAbstractEdges, source.PathGridNavigationAbstractEdges);
            CopyUnmanagedArray(ref builder, ref root.PathGridNavigationNeighbors, source.PathGridNavigationNeighbors);
            CopyStringArray(ref builder, ref root.GenericRecordPowerSpellIds, genericRecordPowerSpellIds.ToArray());

                BuildLookups(ref builder, ref root, source);
                ValidateChildRanges(source);

                return builder.CreateBlobAssetReference<RuntimeContentBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        static void CopyUnmanagedArray<T>(ref BlobBuilder builder, ref BlobArray<T> target, T[] source)
            where T : unmanaged
        {
            source ??= Array.Empty<T>();
            BlobBuilderArray<T> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
                dst[i] = source[i];
        }

        static void SetString(ref BlobBuilder builder, ref BlobString target, string value)
            => builder.AllocateString(ref target, value ?? string.Empty);

        static ulong HashId(string value)
            => RuntimeContentStableHash.HashId(value);

        static ulong HashPath(string value)
            => RuntimeContentStableHash.HashPath(value);

        static ulong HashInteriorCellId(string value)
            => RuntimeContentStableHash.HashInteriorCellId(value);

        static void CopyStringArray(
            ref BlobBuilder builder,
            ref BlobArray<RuntimeContentStringBlob> target,
            string[] source)
        {
            source ??= Array.Empty<string>();
            BlobBuilderArray<RuntimeContentStringBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
                SetString(ref builder, ref dst[i].Value, source[i]);
        }

        static void CopyBaseArray(ref BlobBuilder builder, ref BlobArray<RuntimeBaseDefBlob> target, BaseDef[] source)
        {
            source ??= Array.Empty<BaseDef>();
            BlobBuilderArray<RuntimeBaseDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeBaseDefBlob d = ref dst[i];
                BaseDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.Flags = s.Flags;
                d.Float0 = s.Float0;
                d.Int0 = s.Int0;
                d.Int1 = s.Int1;
                d.Int2 = s.Int2;
                d.Int3 = s.Int3;
                d.IdHash = HashId(s.Id);
                d.ModelPathHash = HashPath(s.Model);
                d.ScriptIdHash = HashId(s.ScriptId);
                d.SoundIdHash = HashId(s.SoundId);
                d.AuxSoundIdHash = HashId(s.AuxSoundId);
                d.EnchantIdHash = HashId(s.EnchantId);
                d.TextHash = HashId(s.Text);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Model, s.Model);
                SetString(ref builder, ref d.Icon, s.Icon);
                SetString(ref builder, ref d.ScriptId, s.ScriptId);
                SetString(ref builder, ref d.SoundId, s.SoundId);
                SetString(ref builder, ref d.AuxSoundId, s.AuxSoundId);
                SetString(ref builder, ref d.EnchantId, s.EnchantId);
                SetString(ref builder, ref d.Text, s.Text);
            }
        }

        static void CopyItemEquipmentBodyPartArray(ref BlobBuilder builder, ref BlobArray<RuntimeItemEquipmentBodyPartDefBlob> target, ItemEquipmentBodyPartDef[] source)
        {
            source ??= Array.Empty<ItemEquipmentBodyPartDef>();
            BlobBuilderArray<RuntimeItemEquipmentBodyPartDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeItemEquipmentBodyPartDefBlob d = ref dst[i];
                ItemEquipmentBodyPartDef s = source[i];
                d.Item = s.Item;
                d.Part = s.Part;
                SetString(ref builder, ref d.MaleBodyPartId, s.MaleBodyPartId);
                SetString(ref builder, ref d.FemaleBodyPartId, s.FemaleBodyPartId);
            }
        }

        static void CopyGenericRecordArray(
            ref BlobBuilder builder,
            ref BlobArray<RuntimeGenericRecordDefBlob> target,
            GenericRecordDef[] source,
            List<string> powerSpellIds)
        {
            source ??= Array.Empty<GenericRecordDef>();
            powerSpellIds ??= new List<string>();
            BlobBuilderArray<RuntimeGenericRecordDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob d = ref dst[i];
                GenericRecordDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.ValueKind = s.ValueKind;
                d.Flags = s.Flags;
                d.Int0 = s.Int0;
                d.Int1 = s.Int1;
                d.Int2 = s.Int2;
                d.FirstPowerSpellIdIndex = powerSpellIds.Count;
                AddRange(powerSpellIds, s.PowerSpellIds);
                d.PowerSpellIdCount = powerSpellIds.Count - d.FirstPowerSpellIdIndex;
                d.Float0 = s.Float0;
                d.Float1 = s.Float1;
                d.IdHash = HashId(s.Id);
                d.ModelPathHash = HashPath(s.Model);
                d.ScriptIdHash = HashId(s.ScriptId);
                d.TextHash = HashId(s.Text);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Model, s.Model);
                SetString(ref builder, ref d.Icon, s.Icon);
                SetString(ref builder, ref d.ScriptId, s.ScriptId);
                SetString(ref builder, ref d.Text, s.Text);
            }
        }

        static void CopyMorrowindScriptProgramArray(ref BlobBuilder builder, ref BlobArray<RuntimeMorrowindScriptProgramDefBlob> target, MorrowindScriptProgramDef[] source)
        {
            source ??= Array.Empty<MorrowindScriptProgramDef>();
            BlobBuilderArray<RuntimeMorrowindScriptProgramDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeMorrowindScriptProgramDefBlob d = ref dst[i];
                MorrowindScriptProgramDef s = source[i];
                d.SourceScriptIndex = s.SourceScriptIndex;
                d.Status = s.Status;
                d.FirstInstructionIndex = s.FirstInstructionIndex;
                d.InstructionCount = s.InstructionCount;
                d.FirstLocalIndex = s.FirstLocalIndex;
                d.LocalCount = s.LocalCount;
                d.MaxStack = s.MaxStack;
                d.IdHash = HashId(s.Id);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.DisabledReason, s.DisabledReason);
            }
        }

        static void CopyMorrowindScriptLocalArray(ref BlobBuilder builder, ref BlobArray<RuntimeMorrowindScriptLocalDefBlob> target, MorrowindScriptLocalDef[] source)
        {
            source ??= Array.Empty<MorrowindScriptLocalDef>();
            BlobBuilderArray<RuntimeMorrowindScriptLocalDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ValueKind = source[i].ValueKind;
                dst[i].NameHash = HashId(source[i].Name);
                SetString(ref builder, ref dst[i].Name, source[i].Name);
            }
        }

        static void CopyMorrowindScriptMessageArray(ref BlobBuilder builder, ref BlobArray<RuntimeMorrowindScriptMessageDefBlob> target, MorrowindScriptMessageDef[] source)
        {
            source ??= Array.Empty<MorrowindScriptMessageDef>();
            BlobBuilderArray<RuntimeMorrowindScriptMessageDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
                SetString(ref builder, ref dst[i].Text, source[i].Text);
        }

        static void CopyExplicitRefTargetArray(ref BlobBuilder builder, ref BlobArray<RuntimeExplicitRefTargetDefBlob> target, ExplicitRefTargetDef[] source)
        {
            source ??= Array.Empty<ExplicitRefTargetDef>();
            BlobBuilderArray<RuntimeExplicitRefTargetDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].PlacedRefId = source[i].PlacedRefId;
                dst[i].IdHash = HashId(source[i].Id);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
            }
        }

        static void CopyClassArray(ref BlobBuilder builder, ref RuntimeContentBlob root, ClassDef[] source)
        {
            source ??= Array.Empty<ClassDef>();
            var minorSkills = new List<int>();
            var majorSkills = new List<int>();
            BlobBuilderArray<RuntimeClassDefBlob> dst = builder.Allocate(ref root.Classes, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeClassDefBlob d = ref dst[i];
                ClassDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.FavoredAttribute0 = s.FavoredAttribute0;
                d.FavoredAttribute1 = s.FavoredAttribute1;
                d.Specialization = s.Specialization;
                d.Playable = s.Playable;
                d.Services = s.Services;
                d.FirstMinorSkillIndex = minorSkills.Count;
                AddRange(minorSkills, s.MinorSkills);
                d.MinorSkillCount = minorSkills.Count - d.FirstMinorSkillIndex;
                d.FirstMajorSkillIndex = majorSkills.Count;
                AddRange(majorSkills, s.MajorSkills);
                d.MajorSkillCount = majorSkills.Count - d.FirstMajorSkillIndex;
                d.IdHash = HashId(s.Id);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Description, s.Description);
            }

            CopyUnmanagedArray(ref builder, ref root.ClassMinorSkills, minorSkills.ToArray());
            CopyUnmanagedArray(ref builder, ref root.ClassMajorSkills, majorSkills.ToArray());
        }

        static void CopyFactionArray(ref BlobBuilder builder, ref RuntimeContentBlob root, FactionDef[] source)
        {
            source ??= Array.Empty<FactionDef>();
            var rankRequirements = new List<FactionRankRequirementDef>();
            var skills = new List<int>();
            var rankNames = new List<string>();
            var reactions = new List<FactionReactionDef>();
            BlobBuilderArray<RuntimeFactionDefBlob> dst = builder.Allocate(ref root.Factions, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeFactionDefBlob d = ref dst[i];
                FactionDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.FavoredAttribute0 = s.FavoredAttribute0;
                d.FavoredAttribute1 = s.FavoredAttribute1;
                d.Hidden = s.Hidden;
                d.FirstRankRequirementIndex = rankRequirements.Count;
                AddRange(rankRequirements, s.RankRequirements);
                d.RankRequirementCount = rankRequirements.Count - d.FirstRankRequirementIndex;
                d.FirstSkillIndex = skills.Count;
                AddRange(skills, s.Skills);
                d.SkillCount = skills.Count - d.FirstSkillIndex;
                d.FirstRankNameIndex = rankNames.Count;
                AddRange(rankNames, s.RankNames);
                d.RankNameCount = rankNames.Count - d.FirstRankNameIndex;
                d.FirstReactionIndex = reactions.Count;
                AddRange(reactions, s.Reactions);
                d.ReactionCount = reactions.Count - d.FirstReactionIndex;
                d.IdHash = HashId(s.Id);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
            }

            CopyUnmanagedArray(ref builder, ref root.FactionRankRequirements, rankRequirements.ToArray());
            CopyUnmanagedArray(ref builder, ref root.FactionSkills, skills.ToArray());
            CopyStringArray(ref builder, ref root.FactionRankNames, rankNames.ToArray());
            CopyFactionReactionArray(ref builder, ref root.FactionReactions, reactions.ToArray());
        }

        static void CopyFactionReactionArray(ref BlobBuilder builder, ref BlobArray<RuntimeFactionReactionDefBlob> target, FactionReactionDef[] source)
        {
            source ??= Array.Empty<FactionReactionDef>();
            BlobBuilderArray<RuntimeFactionReactionDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].Reaction = source[i].Reaction;
                dst[i].FactionIdHash = HashId(source[i].FactionId);
                SetString(ref builder, ref dst[i].FactionId, source[i].FactionId);
            }
        }

        static void CopyRaceArray(ref BlobBuilder builder, ref RuntimeContentBlob root, RaceDef[] source)
        {
            source ??= Array.Empty<RaceDef>();
            var skillBonuses = new List<RaceSkillBonusDef>();
            var maleAttributes = new List<int>();
            var femaleAttributes = new List<int>();
            var powerSpellIds = new List<string>();
            BlobBuilderArray<RuntimeRaceDefBlob> dst = builder.Allocate(ref root.Races, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeRaceDefBlob d = ref dst[i];
                RaceDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.MaleHeight = s.MaleHeight;
                d.FemaleHeight = s.FemaleHeight;
                d.MaleWeight = s.MaleWeight;
                d.FemaleWeight = s.FemaleWeight;
                d.Flags = s.Flags;
                d.FirstSkillBonusIndex = skillBonuses.Count;
                AddRange(skillBonuses, s.SkillBonuses);
                d.SkillBonusCount = skillBonuses.Count - d.FirstSkillBonusIndex;
                d.FirstMaleAttributeIndex = maleAttributes.Count;
                AddRange(maleAttributes, s.MaleAttributes);
                d.MaleAttributeCount = maleAttributes.Count - d.FirstMaleAttributeIndex;
                d.FirstFemaleAttributeIndex = femaleAttributes.Count;
                AddRange(femaleAttributes, s.FemaleAttributes);
                d.FemaleAttributeCount = femaleAttributes.Count - d.FirstFemaleAttributeIndex;
                d.FirstPowerSpellIdIndex = powerSpellIds.Count;
                AddRange(powerSpellIds, s.PowerSpellIds);
                d.PowerSpellIdCount = powerSpellIds.Count - d.FirstPowerSpellIdIndex;
                d.IdHash = HashId(s.Id);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Description, s.Description);
            }

            CopyUnmanagedArray(ref builder, ref root.RaceSkillBonuses, skillBonuses.ToArray());
            CopyUnmanagedArray(ref builder, ref root.RaceMaleAttributes, maleAttributes.ToArray());
            CopyUnmanagedArray(ref builder, ref root.RaceFemaleAttributes, femaleAttributes.ToArray());
            CopyStringArray(ref builder, ref root.RacePowerSpellIds, powerSpellIds.ToArray());
        }

        static void CopyContainerItemArray(ref BlobBuilder builder, ref BlobArray<RuntimeContainerItemDefBlob> target, ContainerItemDef[] source)
        {
            source ??= Array.Empty<ContainerItemDef>();
            BlobBuilderArray<RuntimeContainerItemDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].Count = source[i].Count;
                dst[i].ItemIdHash = HashId(source[i].ItemId);
                SetString(ref builder, ref dst[i].ItemId, source[i].ItemId);
            }
        }

        static void CopyActorSpellArray(ref BlobBuilder builder, ref BlobArray<RuntimeActorSpellDefBlob> target, ActorSpellDef[] source)
        {
            source ??= Array.Empty<ActorSpellDef>();
            BlobBuilderArray<RuntimeActorSpellDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].SpellIdHash = HashId(source[i].SpellId);
                SetString(ref builder, ref dst[i].SpellId, source[i].SpellId);
            }
        }

        static void CopyActorAiPackageArray(ref BlobBuilder builder, ref BlobArray<RuntimeActorAiPackageDefBlob> target, ActorAiPackageDef[] source)
        {
            source ??= Array.Empty<ActorAiPackageDef>();
            BlobBuilderArray<RuntimeActorAiPackageDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeActorAiPackageDefBlob d = ref dst[i];
                ActorAiPackageDef s = source[i];
                d.Type = s.Type;
                d.ShouldRepeat = s.ShouldRepeat;
                d.X = s.X;
                d.Y = s.Y;
                d.Z = s.Z;
                d.Duration = s.Duration;
                d.WanderDistance = s.WanderDistance;
                d.TimeOfDay = s.TimeOfDay;
                d.Idle0 = s.Idle0;
                d.Idle1 = s.Idle1;
                d.Idle2 = s.Idle2;
                d.Idle3 = s.Idle3;
                d.Idle4 = s.Idle4;
                d.Idle5 = s.Idle5;
                d.Idle6 = s.Idle6;
                d.Idle7 = s.Idle7;
                d.TargetIdHash = HashId(s.TargetId);
                d.CellNameHash = HashInteriorCellId(s.CellName);
                SetString(ref builder, ref d.TargetId, s.TargetId);
                SetString(ref builder, ref d.CellName, s.CellName);
            }
        }

        static void CopyActorTravelDestinationArray(ref BlobBuilder builder, ref BlobArray<RuntimeActorTravelDestinationDefBlob> target, ActorTravelDestinationDef[] source)
        {
            source ??= Array.Empty<ActorTravelDestinationDef>();
            BlobBuilderArray<RuntimeActorTravelDestinationDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].PosX = source[i].PosX;
                dst[i].PosY = source[i].PosY;
                dst[i].PosZ = source[i].PosZ;
                dst[i].RotX = source[i].RotX;
                dst[i].RotY = source[i].RotY;
                dst[i].RotZ = source[i].RotZ;
                dst[i].CellNameHash = HashInteriorCellId(source[i].CellName);
                SetString(ref builder, ref dst[i].CellName, source[i].CellName);
            }
        }

        static void CopyItemLeveledListEntryArray(ref BlobBuilder builder, ref BlobArray<RuntimeItemLeveledListEntryDefBlob> target, ItemLeveledListEntryDef[] source)
        {
            source ??= Array.Empty<ItemLeveledListEntryDef>();
            BlobBuilderArray<RuntimeItemLeveledListEntryDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].Level = source[i].Level;
                dst[i].ItemIdHash = HashId(source[i].ItemId);
                SetString(ref builder, ref dst[i].ItemId, source[i].ItemId);
            }
        }

        static void CopyItemLeveledListArray(ref BlobBuilder builder, ref BlobArray<RuntimeItemLeveledListDefBlob> target, ItemLeveledListDef[] source)
        {
            source ??= Array.Empty<ItemLeveledListDef>();
            BlobBuilderArray<RuntimeItemLeveledListDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].Flags = source[i].Flags;
                dst[i].ChanceNone = source[i].ChanceNone;
                dst[i].FirstEntryIndex = source[i].FirstEntryIndex;
                dst[i].EntryCount = source[i].EntryCount;
                dst[i].IdHash = HashId(source[i].Id);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
            }
        }

        static void CopyActorArray(ref BlobBuilder builder, ref BlobArray<RuntimeActorDefBlob> target, ActorDef[] source)
        {
            source ??= Array.Empty<ActorDef>();
            BlobBuilderArray<RuntimeActorDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeActorDefBlob d = ref dst[i];
                ActorDef s = source[i];
                d.ContentId = s.ContentId;
                d.Kind = s.Kind;
                d.RecordTag = s.RecordTag;
                d.Flags = s.Flags;
                d.Level = s.Level;
                d.Scale = s.Scale;
                d.AutoCalculatedStats = s.AutoCalculatedStats;
                d.BloodType = s.BloodType;
                d.Disposition = s.Disposition;
                d.Reputation = s.Reputation;
                d.Rank = s.Rank;
                d.Gold = s.Gold;
                d.CreatureType = s.CreatureType;
                d.SoulValue = s.SoulValue;
                d.Combat = s.Combat;
                d.Magic = s.Magic;
                d.Stealth = s.Stealth;
                d.Attributes = s.Attributes;
                d.Skills = s.Skills;
                d.Vitals = s.Vitals;
                d.AiData = s.AiData;
                d.FirstSpellIndex = s.FirstSpellIndex;
                d.SpellCount = s.SpellCount;
                d.FirstInventoryIndex = s.FirstInventoryIndex;
                d.InventoryCount = s.InventoryCount;
                d.FirstAiPackageIndex = s.FirstAiPackageIndex;
                d.AiPackageCount = s.AiPackageCount;
                d.FirstTravelDestinationIndex = s.FirstTravelDestinationIndex;
                d.TravelDestinationCount = s.TravelDestinationCount;
                d.IdHash = HashId(s.Id);
                d.ModelPathHash = HashPath(s.Model);
                d.ScriptIdHash = HashId(s.ScriptId);
                d.RaceIdHash = HashId(s.RaceId);
                d.ClassIdHash = HashId(s.ClassId);
                d.FactionIdHash = HashId(s.FactionId);
                d.HeadIdHash = HashId(s.HeadId);
                d.HairIdHash = HashId(s.HairId);
                d.OriginalIdHash = HashId(s.OriginalId);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Model, s.Model);
                SetString(ref builder, ref d.ScriptId, s.ScriptId);
                SetString(ref builder, ref d.RaceId, s.RaceId);
                SetString(ref builder, ref d.ClassId, s.ClassId);
                SetString(ref builder, ref d.FactionId, s.FactionId);
                SetString(ref builder, ref d.HeadId, s.HeadId);
                SetString(ref builder, ref d.HairId, s.HairId);
                SetString(ref builder, ref d.OriginalId, s.OriginalId);
            }
        }

        static void CopyLightArray(ref BlobBuilder builder, ref BlobArray<RuntimeLightDefBlob> target, LightDef[] source)
        {
            source ??= Array.Empty<LightDef>();
            BlobBuilderArray<RuntimeLightDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeLightDefBlob d = ref dst[i];
                LightDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.Weight = s.Weight;
                d.Value = s.Value;
                d.Duration = s.Duration;
                d.Radius = s.Radius;
                d.ColorRgba = s.ColorRgba;
                d.Flags = s.Flags;
                d.IdHash = HashId(s.Id);
                d.ModelPathHash = HashPath(s.Model);
                d.ScriptIdHash = HashId(s.ScriptId);
                d.SoundIdHash = HashId(s.SoundId);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.Model, s.Model);
                SetString(ref builder, ref d.Icon, s.Icon);
                SetString(ref builder, ref d.ScriptId, s.ScriptId);
                SetString(ref builder, ref d.SoundId, s.SoundId);
            }
        }

        static void CopySoundArray(ref BlobBuilder builder, ref BlobArray<RuntimeSoundDefBlob> target, SoundDef[] source)
        {
            source ??= Array.Empty<SoundDef>();
            BlobBuilderArray<RuntimeSoundDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].Volume = source[i].Volume;
                dst[i].MinRange = source[i].MinRange;
                dst[i].MaxRange = source[i].MaxRange;
                dst[i].IdHash = HashId(source[i].Id);
                dst[i].SoundPathHash = HashPath(source[i].SoundPath);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
                SetString(ref builder, ref dst[i].SoundPath, source[i].SoundPath);
            }
        }

        static void CopyDialogueArray(ref BlobBuilder builder, ref BlobArray<RuntimeDialogueDefBlob> target, DialogueDef[] source)
        {
            source ??= Array.Empty<DialogueDef>();
            BlobBuilderArray<RuntimeDialogueDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].Type = source[i].Type;
                dst[i].FirstInfoIndex = source[i].FirstInfoIndex;
                dst[i].InfoCount = source[i].InfoCount;
                dst[i].IdHash = HashId(source[i].Id);
                dst[i].StringIdHash = HashId(source[i].StringId);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
                SetString(ref builder, ref dst[i].StringId, source[i].StringId);
            }
        }

        static void CopyDialogueInfoArray(ref BlobBuilder builder, ref BlobArray<RuntimeDialogueInfoDefBlob> target, DialogueInfoDef[] source)
        {
            source ??= Array.Empty<DialogueInfoDef>();
            BlobBuilderArray<RuntimeDialogueInfoDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeDialogueInfoDefBlob d = ref dst[i];
                DialogueInfoDef s = source[i];
                d.ContentId = s.ContentId;
                d.Type = s.Type;
                d.DispositionOrJournalIndex = s.DispositionOrJournalIndex;
                d.Rank = s.Rank;
                d.Gender = s.Gender;
                d.PcRank = s.PcRank;
                d.QuestStatus = s.QuestStatus;
                d.FactionLess = s.FactionLess;
                d.FirstSelectRuleIndex = s.FirstSelectRuleIndex;
                d.SelectRuleCount = s.SelectRuleCount;
                d.IdHash = HashId(s.Id);
                d.TopicIdHash = HashId(s.TopicId);
                d.PrevIdHash = HashId(s.PrevId);
                d.NextIdHash = HashId(s.NextId);
                d.ActorIdHash = HashId(s.ActorId);
                d.RaceIdHash = HashId(s.RaceId);
                d.ClassIdHash = HashId(s.ClassId);
                d.FactionIdHash = HashId(s.FactionId);
                d.PcFactionIdHash = HashId(s.PcFactionId);
                d.CellIdHash = HashInteriorCellId(s.CellId);
                d.SoundFilePathHash = HashPath(s.SoundFile);
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.TopicId, s.TopicId);
                SetString(ref builder, ref d.PrevId, s.PrevId);
                SetString(ref builder, ref d.NextId, s.NextId);
                SetString(ref builder, ref d.ActorId, s.ActorId);
                SetString(ref builder, ref d.RaceId, s.RaceId);
                SetString(ref builder, ref d.ClassId, s.ClassId);
                SetString(ref builder, ref d.FactionId, s.FactionId);
                SetString(ref builder, ref d.PcFactionId, s.PcFactionId);
                SetString(ref builder, ref d.CellId, s.CellId);
                SetString(ref builder, ref d.SoundFile, s.SoundFile);
                SetString(ref builder, ref d.Response, s.Response);
                SetString(ref builder, ref d.ResultScript, s.ResultScript);
            }
        }

        static void CopyDialogueConditionArray(ref BlobBuilder builder, ref BlobArray<RuntimeDialogueConditionDefBlob> target, DialogueConditionDef[] source)
        {
            source ??= Array.Empty<DialogueConditionDef>();
            BlobBuilderArray<RuntimeDialogueConditionDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].IntValue = source[i].IntValue;
                dst[i].FloatValue = source[i].FloatValue;
                dst[i].ValueKind = source[i].ValueKind;
                dst[i].Index = source[i].Index;
                dst[i].Function = source[i].Function;
                dst[i].Comparison = source[i].Comparison;
                dst[i].VariableHash = HashId(source[i].Variable);
                SetString(ref builder, ref dst[i].Variable, source[i].Variable);
            }
        }

        static void CopySpellArray(ref BlobBuilder builder, ref BlobArray<RuntimeSpellDefBlob> target, SpellDef[] source)
        {
            source ??= Array.Empty<SpellDef>();
            BlobBuilderArray<RuntimeSpellDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].SpellType = source[i].SpellType;
                dst[i].Cost = source[i].Cost;
                dst[i].Flags = source[i].Flags;
                dst[i].EffectStartIndex = source[i].EffectStartIndex;
                dst[i].EffectCount = source[i].EffectCount;
                dst[i].IdHash = HashId(source[i].Id);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
                SetString(ref builder, ref dst[i].Name, source[i].Name);
            }
        }

        static void CopyEnchantmentArray(ref BlobBuilder builder, ref BlobArray<RuntimeEnchantmentDefBlob> target, EnchantmentDef[] source)
        {
            source ??= Array.Empty<EnchantmentDef>();
            BlobBuilderArray<RuntimeEnchantmentDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].EnchantmentType = source[i].EnchantmentType;
                dst[i].Cost = source[i].Cost;
                dst[i].Charge = source[i].Charge;
                dst[i].Flags = source[i].Flags;
                dst[i].EffectStartIndex = source[i].EffectStartIndex;
                dst[i].EffectCount = source[i].EffectCount;
                dst[i].IdHash = HashId(source[i].Id);
                SetString(ref builder, ref dst[i].Id, source[i].Id);
            }
        }

        static void CopyMagicEffectArray(ref BlobBuilder builder, ref BlobArray<RuntimeMagicEffectDefBlob> target, MagicEffectDef[] source)
        {
            source ??= Array.Empty<MagicEffectDef>();
            BlobBuilderArray<RuntimeMagicEffectDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeMagicEffectDefBlob d = ref dst[i];
                MagicEffectDef s = source[i];
                d.ContentId = s.ContentId;
                d.Index = s.Index;
                d.School = s.School;
                d.BaseCost = s.BaseCost;
                d.Flags = s.Flags;
                d.Red = s.Red;
                d.Green = s.Green;
                d.Blue = s.Blue;
                d.SizeX = s.SizeX;
                d.Speed = s.Speed;
                d.SizeCap = s.SizeCap;
                d.IconPathHash = HashPath(s.Icon);
                d.ParticleTexturePathHash = HashPath(s.ParticleTexture);
                d.CastingObjectIdHash = HashId(s.CastingObjectId);
                d.HitObjectIdHash = HashId(s.HitObjectId);
                d.AreaObjectIdHash = HashId(s.AreaObjectId);
                d.BoltObjectIdHash = HashId(s.BoltObjectId);
                d.CastSoundIdHash = HashId(s.CastSoundId);
                d.BoltSoundIdHash = HashId(s.BoltSoundId);
                d.HitSoundIdHash = HashId(s.HitSoundId);
                d.AreaSoundIdHash = HashId(s.AreaSoundId);
                SetString(ref builder, ref d.Icon, s.Icon);
                SetString(ref builder, ref d.ParticleTexture, s.ParticleTexture);
                SetString(ref builder, ref d.CastingObjectId, s.CastingObjectId);
                SetString(ref builder, ref d.HitObjectId, s.HitObjectId);
                SetString(ref builder, ref d.AreaObjectId, s.AreaObjectId);
                SetString(ref builder, ref d.BoltObjectId, s.BoltObjectId);
                SetString(ref builder, ref d.CastSoundId, s.CastSoundId);
                SetString(ref builder, ref d.BoltSoundId, s.BoltSoundId);
                SetString(ref builder, ref d.HitSoundId, s.HitSoundId);
                SetString(ref builder, ref d.AreaSoundId, s.AreaSoundId);
                SetString(ref builder, ref d.Description, s.Description);
            }
        }

        static void CopyRegionSoundRefArray(ref BlobBuilder builder, ref BlobArray<RuntimeRegionSoundRefDefBlob> target, RegionSoundRefDef[] source)
        {
            source ??= Array.Empty<RegionSoundRefDef>();
            BlobBuilderArray<RuntimeRegionSoundRefDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].Chance = source[i].Chance;
                dst[i].SoundIdHash = HashId(source[i].SoundId);
                SetString(ref builder, ref dst[i].SoundId, source[i].SoundId);
            }
        }

        static void CopyRegionArray(ref BlobBuilder builder, ref BlobArray<RuntimeRegionDefBlob> target, RegionDef[] source)
        {
            source ??= Array.Empty<RegionDef>();
            BlobBuilderArray<RuntimeRegionDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeRegionDefBlob d = ref dst[i];
                RegionDef s = source[i];
                d.ContentId = s.ContentId;
                d.MapColorRgba = s.MapColorRgba;
                d.ClearChance = s.ClearChance;
                d.CloudyChance = s.CloudyChance;
                d.FoggyChance = s.FoggyChance;
                d.OvercastChance = s.OvercastChance;
                d.RainChance = s.RainChance;
                d.ThunderChance = s.ThunderChance;
                d.AshChance = s.AshChance;
                d.BlightChance = s.BlightChance;
                d.SnowChance = s.SnowChance;
                d.BlizzardChance = s.BlizzardChance;
                d.SoundRefStartIndex = s.SoundRefStartIndex;
                d.SoundRefCount = s.SoundRefCount;
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.Name, s.Name);
                SetString(ref builder, ref d.SleepListId, s.SleepListId);
            }
        }

        static void CopyPathGridArray(ref BlobBuilder builder, ref BlobArray<RuntimePathGridDefBlob> target, PathGridDef[] source)
        {
            source ??= Array.Empty<PathGridDef>();
            BlobBuilderArray<RuntimePathGridDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimePathGridDefBlob d = ref dst[i];
                PathGridDef s = source[i];
                d.ContentId = s.ContentId;
                d.RecordTag = s.RecordTag;
                d.GridX = s.GridX;
                d.GridY = s.GridY;
                d.Granularity = s.Granularity;
                d.DeclaredPointCount = s.DeclaredPointCount;
                d.FirstPointIndex = s.FirstPointIndex;
                d.PointCount = s.PointCount;
                d.FirstConnectionIndex = s.FirstConnectionIndex;
                d.ConnectionCount = s.ConnectionCount;
                d.FirstNavigationNodeIndex = s.FirstNavigationNodeIndex;
                d.NavigationNodeCount = s.NavigationNodeCount;
                d.FirstNavigationEdgeIndex = s.FirstNavigationEdgeIndex;
                d.NavigationEdgeCount = s.NavigationEdgeCount;
                d.FirstNavigationPortalIndex = s.FirstNavigationPortalIndex;
                d.NavigationPortalCount = s.NavigationPortalCount;
                d.FirstNavigationAbstractEdgeIndex = s.FirstNavigationAbstractEdgeIndex;
                d.NavigationAbstractEdgeCount = s.NavigationAbstractEdgeCount;
                d.FirstNavigationNeighborIndex = s.FirstNavigationNeighborIndex;
                d.NavigationNeighborCount = s.NavigationNeighborCount;
                d.NavigationComponentId = s.NavigationComponentId;
                d.IsExterior = s.IsExterior;
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.CellId, s.CellId);
            }
        }

        static void CopyMusicTrackArray(ref BlobBuilder builder, ref BlobArray<RuntimeMusicTrackDefBlob> target, MusicTrackDef[] source)
        {
            source ??= Array.Empty<MusicTrackDef>();
            BlobBuilderArray<RuntimeMusicTrackDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].Category = source[i].Category;
                SetString(ref builder, ref dst[i].RelativePath, source[i].RelativePath);
            }
        }

        static void CopyWeatherDefinitionArray(ref BlobBuilder builder, ref BlobArray<RuntimeWeatherDefinitionDefBlob> target, WeatherDefinitionDef[] source)
        {
            source ??= Array.Empty<WeatherDefinitionDef>();
            BlobBuilderArray<RuntimeWeatherDefinitionDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                ref RuntimeWeatherDefinitionDefBlob d = ref dst[i];
                WeatherDefinitionDef s = source[i];
                d.Kind = s.Kind;
                d.SkyColor = s.SkyColor;
                d.FogColor = s.FogColor;
                d.AmbientColor = s.AmbientColor;
                d.SunColor = s.SunColor;
                d.SunDiscSunsetColorRgba = s.SunDiscSunsetColorRgba;
                d.LandFogDayDepth = s.LandFogDayDepth;
                d.LandFogNightDepth = s.LandFogNightDepth;
                d.WindSpeed = s.WindSpeed;
                d.CloudSpeed = s.CloudSpeed;
                d.GlareView = s.GlareView;
                d.CloudsMaximumPercent = s.CloudsMaximumPercent;
                d.TransitionDelta = s.TransitionDelta;
                d.RainSpeed = s.RainSpeed;
                d.RainEntranceSpeed = s.RainEntranceSpeed;
                d.RainMaxRaindrops = s.RainMaxRaindrops;
                d.RainDiameter = s.RainDiameter;
                d.RainThreshold = s.RainThreshold;
                d.RainMinHeight = s.RainMinHeight;
                d.RainMaxHeight = s.RainMaxHeight;
                d.UsingPrecip = s.UsingPrecip;
                d.IsStorm = s.IsStorm;
                d.ThunderFrequency = s.ThunderFrequency;
                d.ThunderThreshold = s.ThunderThreshold;
                d.FlashDecrement = s.FlashDecrement;
                SetString(ref builder, ref d.Id, s.Id);
                SetString(ref builder, ref d.CloudTexture, s.CloudTexture);
                SetString(ref builder, ref d.RainLoopSoundId, s.RainLoopSoundId);
                SetString(ref builder, ref d.AmbientLoopSoundId, s.AmbientLoopSoundId);
                SetString(ref builder, ref d.ThunderSoundId0, s.ThunderSoundId0);
                SetString(ref builder, ref d.ThunderSoundId1, s.ThunderSoundId1);
                SetString(ref builder, ref d.ThunderSoundId2, s.ThunderSoundId2);
                SetString(ref builder, ref d.ThunderSoundId3, s.ThunderSoundId3);
            }
        }

        static void CopySkyWeatherVisualSettings(ref BlobBuilder builder, ref RuntimeContentBlob root, SkyWeatherVisualSettingsDef source)
        {
            ref RuntimeSkyWeatherVisualSettingsDefBlob d = ref root.SkyWeatherVisualSettings;
            SetString(ref builder, ref d.SunTexture, source.SunTexture);
            SetString(ref builder, ref d.SunGlareTexture, source.SunGlareTexture);
            SetString(ref builder, ref d.StarTexture, source.StarTexture);
            SetString(ref builder, ref d.MasserShadowTexture, source.MasserShadowTexture);
            SetString(ref builder, ref d.SecundaShadowTexture, source.SecundaShadowTexture);
            SetString(ref builder, ref d.RainDropTexture, source.RainDropTexture);

            d.FirstMasserPhaseTextureIndex = 0;
            d.MasserPhaseTextureCount = source.MasserPhaseTextures?.Length ?? 0;
            d.FirstSecundaPhaseTextureIndex = 0;
            d.SecundaPhaseTextureCount = source.SecundaPhaseTextures?.Length ?? 0;
            d.FirstCloudTextureIndex = 0;
            d.CloudTextureCount = source.CloudTextures?.Length ?? 0;
            d.FirstPrecipitationTextureIndex = 0;
            d.PrecipitationTextureCount = source.PrecipitationTextures?.Length ?? 0;
            d.FirstPrecipitationEffectModelIndex = 0;
            d.PrecipitationEffectModelCount = source.PrecipitationEffectModels?.Length ?? 0;

            CopyStringArray(ref builder, ref root.SkyMasserPhaseTextures, source.MasserPhaseTextures);
            CopyStringArray(ref builder, ref root.SkySecundaPhaseTextures, source.SecundaPhaseTextures);
            CopyStringArray(ref builder, ref root.SkyCloudTextures, source.CloudTextures);
            CopyStringArray(ref builder, ref root.SkyPrecipitationTextures, source.PrecipitationTextures);
            CopyStringArray(ref builder, ref root.SkyPrecipitationEffectModels, source.PrecipitationEffectModels);
        }

        static void CopyActorBodyPartArray(ref BlobBuilder builder, ref BlobArray<RuntimeActorBodyPartDefBlob> target, ActorBodyPartDef[] source)
        {
            source ??= Array.Empty<ActorBodyPartDef>();
            BlobBuilderArray<RuntimeActorBodyPartDefBlob> dst = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                dst[i].ContentId = source[i].ContentId;
                dst[i].Part = source[i].Part;
                dst[i].Type = source[i].Type;
                dst[i].Female = source[i].Female;
                dst[i].Vampire = source[i].Vampire;
                dst[i].NotPlayable = source[i].NotPlayable;
                dst[i].FirstPerson = source[i].FirstPerson;
                SetString(ref builder, ref dst[i].Id, source[i].Id);
                SetString(ref builder, ref dst[i].RaceId, source[i].RaceId);
                SetString(ref builder, ref dst[i].Model, source[i].Model);
            }
        }

        static void BuildLookups(ref BlobBuilder builder, ref RuntimeContentBlob root, GameplayContentData source)
        {
            CopyHashLookup(ref builder, ref root.ActorIdLookup, BuildIdLookup(source.Actors, d => d.Id, i => ActorDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ActivatorIdLookup, BuildIdLookup(source.Activators, d => d.Id, i => ActivatorDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.DoorIdLookup, BuildIdLookup(source.Doors, d => d.Id, i => DoorDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ContainerIdLookup, BuildIdLookup(source.Containers, d => d.Id, i => ContainerDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ItemIdLookup, BuildIdLookup(source.Items, d => d.Id, i => ItemDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.LightIdLookup, BuildIdLookup(source.Lights, d => d.Id, i => LightDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ItemLeveledListIdLookup, BuildIdLookup(source.ItemLeveledLists, d => d.Id, i => ItemLeveledListDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.CreatureLeveledListIdLookup, BuildIdLookup(source.CreatureLeveledLists, d => d.Id, i => CreatureLeveledListDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.SoundIdLookup, BuildIdLookup(source.Sounds, d => d.Id, i => SoundDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.DialogueIdLookup, BuildIdLookup(source.Dialogues, d => d.Id, i => DialogueDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.SpellIdLookup, BuildIdLookup(source.Spells, d => d.Id, i => SpellDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.EnchantmentIdLookup, BuildIdLookup(source.Enchantments, d => d.Id, i => EnchantmentDefHandle.FromIndex(i).Value));
            CopyIntLookup(ref builder, ref root.MagicEffectIndexLookup, BuildMagicEffectLookup(source.MagicEffects));
            CopyHashLookup(ref builder, ref root.RegionIdLookup, BuildIdLookup(source.Regions, d => d.Id, i => RegionDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.MusicTrackPathLookup, BuildPathLookup(source.MusicTracks, d => d.RelativePath, i => MusicTrackDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.GameSettingIdLookup, BuildIdLookup(source.GameSettings, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.GlobalIdLookup, BuildIdLookup(source.Globals, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ClassIdLookup, BuildIdLookup(source.Classes, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.FactionIdLookup, BuildIdLookup(source.Factions, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.RaceIdLookup, BuildIdLookup(source.Races, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.BirthsignIdLookup, BuildIdLookup(source.Birthsigns, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.SkillIdLookup, BuildIdLookup(source.Skills, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ScriptIdLookup, BuildIdLookup(source.Scripts, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.StartScriptIdLookup, BuildIdLookup(source.StartScripts, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.MorrowindScriptProgramIdLookup, BuildIdLookup(source.MorrowindScriptPrograms, d => d.Id, i => MorrowindScriptProgramDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.SoundGeneratorIdLookup, BuildIdLookup(source.SoundGenerators, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.LandTextureIdLookup, BuildIdLookup(source.LandTextures, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.StaticIdLookup, BuildIdLookup(source.Statics, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.BodyPartIdLookup, BuildIdLookup(source.BodyParts, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.ActorBodyPartIdLookup, BuildIdLookup(source.ActorBodyParts, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.PathGridIdLookup, BuildIdLookup(source.PathGrids, d => d.Id, i => GenericRecordDefHandle.FromIndex(i).Value));
            CopyHashLookup(ref builder, ref root.InteriorPathGridHashLookup, BuildInteriorPathGridLookup(source.PathGrids));
            CopyLongLookup(ref builder, ref root.ExteriorPathGridCoordLookup, BuildExteriorPathGridLookup(source.PathGrids));
            CopyExplicitRefLookup(ref builder, ref root.ExplicitRefTargetLookup, BuildExplicitRefLookup(source.ExplicitRefTargets));
            CopyPlaceableLookup(ref builder, ref root.PlaceableLookup, BuildPlaceableLookup(source));
            CopyIntLookup(ref builder, ref root.ItemIndexToEquipmentIndexLookup, BuildItemEquipmentLookup(source));
        }

        static List<RuntimeContentHashLookupBlob> BuildIdLookup<T>(
            T[] source,
            Func<T, string> idSelector,
            Func<int, int> handleValueFactory)
        {
            source ??= Array.Empty<T>();
            var results = new List<RuntimeContentHashLookupBlob>();
            var seen = new Dictionary<ulong, string>();
            for (int i = 0; i < source.Length; i++)
            {
                string normalized = ContentId.NormalizeId(idSelector(source[i]) ?? string.Empty);
                ulong hash = RuntimeContentStableHash.HashNormalized(normalized);
                if (hash == 0UL)
                    continue;

                AddUniqueHash(seen, hash, normalized, typeof(T).Name);
                results.Add(new RuntimeContentHashLookupBlob { Hash = hash, HandleValue = handleValueFactory(i) });
            }

            results.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return results;
        }

        static List<RuntimeContentHashLookupBlob> BuildPathLookup<T>(
            T[] source,
            Func<T, string> idSelector,
            Func<int, int> handleValueFactory)
        {
            source ??= Array.Empty<T>();
            var results = new List<RuntimeContentHashLookupBlob>();
            var seen = new Dictionary<ulong, string>();
            for (int i = 0; i < source.Length; i++)
            {
                string normalized = RuntimeContentStableHash.NormalizePath(idSelector(source[i]) ?? string.Empty);
                ulong hash = RuntimeContentStableHash.HashNormalized(normalized);
                if (hash == 0UL)
                    continue;

                AddUniqueHash(seen, hash, normalized, typeof(T).Name);
                results.Add(new RuntimeContentHashLookupBlob { Hash = hash, HandleValue = handleValueFactory(i) });
            }

            results.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return results;
        }

        static List<RuntimeContentIntLookupBlob> BuildMagicEffectLookup(MagicEffectDef[] source)
        {
            source ??= Array.Empty<MagicEffectDef>();
            var results = new List<RuntimeContentIntLookupBlob>(source.Length);
            var seen = new HashSet<int>();
            for (int i = 0; i < source.Length; i++)
            {
                if (!seen.Add(source[i].Index))
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Duplicate magic effect index {source[i].Index}.");

                results.Add(new RuntimeContentIntLookupBlob
                {
                    Key = source[i].Index,
                    Value = MagicEffectDefHandle.FromIndex(i).Value,
                });
            }

            results.Sort((a, b) => a.Key.CompareTo(b.Key));
            return results;
        }

        static List<RuntimeContentHashLookupBlob> BuildInteriorPathGridLookup(PathGridDef[] source)
        {
            source ??= Array.Empty<PathGridDef>();
            var results = new List<RuntimeContentHashLookupBlob>();
            var seen = new HashSet<ulong>();
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].IsExterior != 0)
                    continue;

                ulong hash = RuntimeContentStableHash.HashInteriorCellId(source[i].Id);
                if (hash == 0UL)
                    continue;
                if (!seen.Add(hash))
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Duplicate interior path grid hash 0x{hash:X16} for '{source[i].Id}'.");

                results.Add(new RuntimeContentHashLookupBlob
                {
                    Hash = hash,
                    HandleValue = GenericRecordDefHandle.FromIndex(i).Value,
                });
            }

            results.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return results;
        }

        static List<RuntimeContentLongLookupBlob> BuildExteriorPathGridLookup(PathGridDef[] source)
        {
            source ??= Array.Empty<PathGridDef>();
            var results = new List<RuntimeContentLongLookupBlob>();
            var seen = new HashSet<long>();
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].IsExterior == 0)
                    continue;

                long key = PackExteriorPathGridKey(source[i].GridX, source[i].GridY);
                if (!seen.Add(key))
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Duplicate exterior path grid at ({source[i].GridX}, {source[i].GridY}).");

                results.Add(new RuntimeContentLongLookupBlob
                {
                    Key = key,
                    Value = GenericRecordDefHandle.FromIndex(i).Value,
                });
            }

            results.Sort((a, b) => a.Key.CompareTo(b.Key));
            return results;
        }

        static List<RuntimeContentExplicitRefLookupBlob> BuildExplicitRefLookup(ExplicitRefTargetDef[] source)
        {
            source ??= Array.Empty<ExplicitRefTargetDef>();
            var results = new List<RuntimeContentExplicitRefLookupBlob>();
            var seen = new Dictionary<ulong, string>();
            for (int i = 0; i < source.Length; i++)
            {
                string normalized = ContentId.NormalizeId(source[i].Id ?? string.Empty);
                ulong hash = RuntimeContentStableHash.HashNormalized(normalized);
                if (hash == 0UL || source[i].PlacedRefId == 0u)
                    continue;

                AddUniqueHash(seen, hash, normalized, nameof(ExplicitRefTargetDef));
                results.Add(new RuntimeContentExplicitRefLookupBlob { Hash = hash, PlacedRefId = source[i].PlacedRefId });
            }

            results.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return results;
        }

        static List<RuntimeContentPlaceableLookupBlob> BuildPlaceableLookup(GameplayContentData source)
        {
            var lookup = GameplayContentReferenceIndex.BuildPlaceableIndex(source);
            var results = new List<RuntimeContentPlaceableLookupBlob>(lookup.Count);
            var seen = new Dictionary<ulong, string>();
            foreach (var pair in lookup)
            {
                string normalized = ContentId.NormalizeId(pair.Key);
                ulong hash = RuntimeContentStableHash.HashNormalized(normalized);
                if (hash == 0UL)
                    continue;

                AddUniqueHash(seen, hash, normalized, "placeable");
                results.Add(new RuntimeContentPlaceableLookupBlob { Hash = hash, Content = pair.Value });
            }

            results.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return results;
        }

        static List<RuntimeContentIntLookupBlob> BuildItemEquipmentLookup(GameplayContentData source)
        {
            int itemCount = source.Items?.Length ?? 0;
            var results = new List<RuntimeContentIntLookupBlob>();
            var seen = new HashSet<int>();
            ItemEquipmentDef[] equipment = source.ItemEquipment ?? Array.Empty<ItemEquipmentDef>();
            for (int i = 0; i < equipment.Length; i++)
            {
                int itemIndex = equipment[i].Item.Index;
                if ((uint)itemIndex >= (uint)itemCount)
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Item equipment {i} references invalid item handle {equipment[i].Item.Value}.");
                if (!seen.Add(itemIndex))
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Multiple equipment records reference item index {itemIndex}.");

                results.Add(new RuntimeContentIntLookupBlob { Key = itemIndex, Value = i });
            }

            results.Sort((a, b) => a.Key.CompareTo(b.Key));
            return results;
        }

        static void CopyHashLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentHashLookupBlob> target, List<RuntimeContentHashLookupBlob> source)
            => CopyUnmanagedArray(ref builder, ref target, source.ToArray());

        static void CopyIntLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentIntLookupBlob> target, List<RuntimeContentIntLookupBlob> source)
            => CopyUnmanagedArray(ref builder, ref target, source.ToArray());

        static void CopyLongLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentLongLookupBlob> target, List<RuntimeContentLongLookupBlob> source)
            => CopyUnmanagedArray(ref builder, ref target, source.ToArray());

        static void CopyExplicitRefLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentExplicitRefLookupBlob> target, List<RuntimeContentExplicitRefLookupBlob> source)
            => CopyUnmanagedArray(ref builder, ref target, source.ToArray());

        static void CopyPlaceableLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentPlaceableLookupBlob> target, List<RuntimeContentPlaceableLookupBlob> source)
            => CopyUnmanagedArray(ref builder, ref target, source.ToArray());

        static void AddUniqueHash(Dictionary<ulong, string> seen, ulong hash, string normalized, string context)
        {
            if (!seen.TryGetValue(hash, out string existing))
            {
                seen.Add(hash, normalized);
                return;
            }

            if (string.Equals(existing, normalized, StringComparison.Ordinal))
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Duplicate {context} id '{normalized}'.");

            throw new InvalidOperationException(
                $"[VVardenfell][ContentBlob] Hash collision in {context}: '{existing}' and '{normalized}' both hash to 0x{hash:X16}.");
        }

        static void AddRange<T>(List<T> target, T[] values)
        {
            if (values == null || values.Length == 0)
                return;
            target.AddRange(values);
        }

        static void ValidateChildRanges(GameplayContentData source)
        {
            ValidateRanges(source.Actors, source.ActorSpells?.Length ?? 0, d => d.FirstSpellIndex, d => d.SpellCount, "actor spells");
            ValidateRanges(source.Actors, source.ActorInventoryItems?.Length ?? 0, d => d.FirstInventoryIndex, d => d.InventoryCount, "actor inventory");
            ValidateRanges(source.Actors, source.ActorAiPackages?.Length ?? 0, d => d.FirstAiPackageIndex, d => d.AiPackageCount, "actor ai packages");
            ValidateRanges(source.Actors, source.ActorTravelDestinations?.Length ?? 0, d => d.FirstTravelDestinationIndex, d => d.TravelDestinationCount, "actor travel destinations");
            ValidateRanges(source.ItemEquipment, source.ItemEquipmentBodyParts?.Length ?? 0, d => d.FirstBodyPartIndex, d => d.BodyPartCount, "item equipment body parts");
            ValidateRanges(source.Dialogues, source.DialogueInfos?.Length ?? 0, d => d.FirstInfoIndex, d => d.InfoCount, "dialogue infos");
            ValidateRanges(source.DialogueInfos, source.DialogueConditions?.Length ?? 0, d => d.FirstSelectRuleIndex, d => d.SelectRuleCount, "dialogue conditions");
            ValidateRanges(source.Spells, source.MagicEffectInstances?.Length ?? 0, d => d.EffectStartIndex, d => d.EffectCount, "spell effects");
            ValidateRanges(source.Enchantments, source.MagicEffectInstances?.Length ?? 0, d => d.EffectStartIndex, d => d.EffectCount, "enchantment effects");
            ValidateRanges(source.Regions, source.RegionSoundRefs?.Length ?? 0, d => d.SoundRefStartIndex, d => d.SoundRefCount, "region sounds");
            ValidateRanges(source.ItemLeveledLists, source.ItemLeveledListEntries?.Length ?? 0, d => d.FirstEntryIndex, d => d.EntryCount, "item leveled list entries");
            ValidateRanges(source.CreatureLeveledLists, source.CreatureLeveledListEntries?.Length ?? 0, d => d.FirstEntryIndex, d => d.EntryCount, "creature leveled list entries");
            ValidateRanges(source.PathGrids, source.PathGridPoints?.Length ?? 0, d => d.FirstPointIndex, d => d.PointCount, "path grid points");
            ValidateRanges(source.PathGrids, source.PathGridConnections?.Length ?? 0, d => d.FirstConnectionIndex, d => d.ConnectionCount, "path grid connections");
            ValidateRanges(source.PathGridPoints, source.PathGridConnections?.Length ?? 0, d => d.FirstConnectionIndex, d => d.ConnectionCount, "path grid point connections");
            ValidateRanges(source.PathGrids, source.PathGridNavigationNodes?.Length ?? 0, d => d.FirstNavigationNodeIndex, d => d.NavigationNodeCount, "path grid navigation nodes");
            ValidateRanges(source.PathGrids, source.PathGridNavigationEdges?.Length ?? 0, d => d.FirstNavigationEdgeIndex, d => d.NavigationEdgeCount, "path grid navigation edges");
            ValidateRanges(source.PathGridNavigationNodes, source.PathGridNavigationEdges?.Length ?? 0, d => d.FirstEdgeIndex, d => d.EdgeCount, "path grid navigation node edges");
            ValidateRanges(source.PathGrids, source.PathGridNavigationPortals?.Length ?? 0, d => d.FirstNavigationPortalIndex, d => d.NavigationPortalCount, "path grid navigation portals");
            ValidateRanges(source.PathGrids, source.PathGridNavigationAbstractEdges?.Length ?? 0, d => d.FirstNavigationAbstractEdgeIndex, d => d.NavigationAbstractEdgeCount, "path grid abstract edges");
            ValidateRanges(source.PathGrids, source.PathGridNavigationNeighbors?.Length ?? 0, d => d.FirstNavigationNeighborIndex, d => d.NavigationNeighborCount, "path grid neighbors");
        }

        static void ValidateRanges<T>(T[] owners, int childCount, Func<T, int> firstSelector, Func<T, int> countSelector, string context)
        {
            owners ??= Array.Empty<T>();
            for (int i = 0; i < owners.Length; i++)
            {
                int first = firstSelector(owners[i]);
                int count = countSelector(owners[i]);
                if (count < 0)
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Negative {context} count {count} at owner {i}.");
                if (count == 0)
                    continue;
                if (first < 0 || first > childCount || first + count > childCount)
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid {context} range ({first}, {count}) at owner {i}; child count {childCount}.");
            }
        }

        static long PackExteriorPathGridKey(int gridX, int gridY)
            => ((long)gridX << 32) ^ (uint)gridY;
    }
}
