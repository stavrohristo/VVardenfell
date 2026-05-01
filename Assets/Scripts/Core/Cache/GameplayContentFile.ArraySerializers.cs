using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VVardenfell.Core.Cache
{

    public static partial class GameplayContentFile
    {
        static void WriteStringArray(BinaryWriter w, string[] values)
            => WriteArray<string>(w, values, WriteString);


        static string[] ReadStringArray(BinaryReader r)
            => ReadArray<string>(r, ReadString);


        static void WriteRaceSkillBonusArray(BinaryWriter w, RaceSkillBonusDef[] values)
            => WriteArray<RaceSkillBonusDef>(w, values, WriteRaceSkillBonus);


        static RaceSkillBonusDef[] ReadRaceSkillBonusArray(BinaryReader r)
            => ReadArray<RaceSkillBonusDef>(r, ReadRaceSkillBonus);


        static void WriteFactionRankRequirementArray(BinaryWriter w, FactionRankRequirementDef[] values)
            => WriteArray<FactionRankRequirementDef>(w, values, WriteFactionRankRequirement);


        static FactionRankRequirementDef[] ReadFactionRankRequirementArray(BinaryReader r)
            => ReadArray<FactionRankRequirementDef>(r, ReadFactionRankRequirement);


        static void WriteFactionReactionArray(BinaryWriter w, FactionReactionDef[] values)
            => WriteArray<FactionReactionDef>(w, values, WriteFactionReaction);


        static FactionReactionDef[] ReadFactionReactionArray(BinaryReader r)
            => ReadArray<FactionReactionDef>(r, ReadFactionReaction);


        static void WriteContainerContentRangeArray(BinaryWriter w, ContainerContentRangeDef[] values)
            => WriteArray<ContainerContentRangeDef>(w, values, WriteContainerContentRange);


        static ContainerContentRangeDef[] ReadContainerContentRangeArray(BinaryReader r)
            => ReadArray<ContainerContentRangeDef>(r, ReadContainerContentRange);


        static void WriteContainerItemArray(BinaryWriter w, ContainerItemDef[] values)
            => WriteArray<ContainerItemDef>(w, values, WriteContainerItem);


        static ContainerItemDef[] ReadContainerItemArray(BinaryReader r)
            => ReadArray<ContainerItemDef>(r, ReadContainerItem);


        static void WriteActorArray(BinaryWriter w, ActorDef[] values)
            => WriteArray<ActorDef>(w, values, WriteActor);


        static ActorDef[] ReadActorArray(BinaryReader r)
            => ReadArray<ActorDef>(r, ReadActor);


        static void WriteActorSpellArray(BinaryWriter w, ActorSpellDef[] values)
            => WriteArray<ActorSpellDef>(w, values, WriteActorSpell);


        static ActorSpellDef[] ReadActorSpellArray(BinaryReader r)
            => ReadArray<ActorSpellDef>(r, ReadActorSpell);


        static void WriteActorAiPackageArray(BinaryWriter w, ActorAiPackageDef[] values)
            => WriteArray<ActorAiPackageDef>(w, values, WriteActorAiPackage);


        static ActorAiPackageDef[] ReadActorAiPackageArray(BinaryReader r)
            => ReadArray<ActorAiPackageDef>(r, ReadActorAiPackage);


        static void WriteActorTravelDestinationArray(BinaryWriter w, ActorTravelDestinationDef[] values)
            => WriteArray<ActorTravelDestinationDef>(w, values, WriteActorTravelDestination);


        static ActorTravelDestinationDef[] ReadActorTravelDestinationArray(BinaryReader r)
            => ReadArray<ActorTravelDestinationDef>(r, ReadActorTravelDestination);


        static void WriteLightArray(BinaryWriter w, LightDef[] values)
            => WriteArray<LightDef>(w, values, WriteLight);


        static LightDef[] ReadLightArray(BinaryReader r)
            => ReadArray<LightDef>(r, ReadLight);


        static void WriteItemLeveledListArray(BinaryWriter w, ItemLeveledListDef[] values)
            => WriteArray<ItemLeveledListDef>(w, values, WriteItemLeveledList);


        static ItemLeveledListDef[] ReadItemLeveledListArray(BinaryReader r)
            => ReadArray<ItemLeveledListDef>(r, ReadItemLeveledList);


        static void WriteItemLeveledListEntryArray(BinaryWriter w, ItemLeveledListEntryDef[] values)
            => WriteArray<ItemLeveledListEntryDef>(w, values, WriteItemLeveledListEntry);


        static ItemLeveledListEntryDef[] ReadItemLeveledListEntryArray(BinaryReader r)
            => ReadArray<ItemLeveledListEntryDef>(r, ReadItemLeveledListEntry);


        static void WriteSoundArray(BinaryWriter w, SoundDef[] values)
            => WriteArray<SoundDef>(w, values, WriteSound);


        static SoundDef[] ReadSoundArray(BinaryReader r)
            => ReadArray<SoundDef>(r, ReadSound);


        static void WriteDialogueArray(BinaryWriter w, DialogueDef[] values)
            => WriteArray<DialogueDef>(w, values, WriteDialogue);


        static DialogueDef[] ReadDialogueArray(BinaryReader r)
            => ReadArray<DialogueDef>(r, ReadDialogue);


        static void WriteDialogueInfoArray(BinaryWriter w, DialogueInfoDef[] values)
            => WriteArray<DialogueInfoDef>(w, values, WriteDialogueInfo);


        static DialogueInfoDef[] ReadDialogueInfoArray(BinaryReader r)
            => ReadArray<DialogueInfoDef>(r, ReadDialogueInfo);


        static void WriteDialogueConditionArray(BinaryWriter w, DialogueConditionDef[] values)
            => WriteArray<DialogueConditionDef>(w, values, WriteDialogueCondition);


        static DialogueConditionDef[] ReadDialogueConditionArray(BinaryReader r)
            => ReadArray<DialogueConditionDef>(r, ReadDialogueCondition);


        static void WriteSpellArray(BinaryWriter w, SpellDef[] values)
            => WriteArray<SpellDef>(w, values, WriteSpell);


        static SpellDef[] ReadSpellArray(BinaryReader r)
            => ReadArray<SpellDef>(r, ReadSpell);


        static void WriteEnchantmentArray(BinaryWriter w, EnchantmentDef[] values)
            => WriteArray<EnchantmentDef>(w, values, WriteEnchantment);


        static EnchantmentDef[] ReadEnchantmentArray(BinaryReader r)
            => ReadArray<EnchantmentDef>(r, ReadEnchantment);


        static void WriteMagicEffectArray(BinaryWriter w, MagicEffectDef[] values)
            => WriteArray<MagicEffectDef>(w, values, WriteMagicEffect);


        static MagicEffectDef[] ReadMagicEffectArray(BinaryReader r)
            => ReadArray<MagicEffectDef>(r, ReadMagicEffect);


        static void WriteMagicEffectInstanceArray(BinaryWriter w, MagicEffectInstanceDef[] values)
            => WriteArray<MagicEffectInstanceDef>(w, values, WriteMagicEffectInstance);


        static MagicEffectInstanceDef[] ReadMagicEffectInstanceArray(BinaryReader r)
            => ReadArray<MagicEffectInstanceDef>(r, ReadMagicEffectInstance);


        static void WriteRegionArray(BinaryWriter w, RegionDef[] values)
            => WriteArray<RegionDef>(w, values, WriteRegion);


        static RegionDef[] ReadRegionArray(BinaryReader r)
            => ReadArray<RegionDef>(r, ReadRegion);


        static void WriteRegionSoundRefArray(BinaryWriter w, RegionSoundRefDef[] values)
            => WriteArray<RegionSoundRefDef>(w, values, WriteRegionSoundRef);


        static RegionSoundRefDef[] ReadRegionSoundRefArray(BinaryReader r)
            => ReadArray<RegionSoundRefDef>(r, ReadRegionSoundRef);


        static void WriteMusicTrackArray(BinaryWriter w, MusicTrackDef[] values)
            => WriteArray<MusicTrackDef>(w, values, WriteMusicTrack);


        static MusicTrackDef[] ReadMusicTrackArray(BinaryReader r)
            => ReadArray<MusicTrackDef>(r, ReadMusicTrack);


        static void WriteWeatherDefinitionArray(BinaryWriter w, WeatherDefinitionDef[] values)
            => WriteArray<WeatherDefinitionDef>(w, values, WriteWeatherDefinition);


        static WeatherDefinitionDef[] ReadWeatherDefinitionArray(BinaryReader r)
            => ReadArray<WeatherDefinitionDef>(r, ReadWeatherDefinition);

        }
    }
