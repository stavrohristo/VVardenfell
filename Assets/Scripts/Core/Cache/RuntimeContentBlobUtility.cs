using System;
using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeContentBlobUtility
    {
        public static ref RuntimeActorDefBlob Get(ref RuntimeContentBlob blob, ActorDefHandle handle)
            => ref Require(ref blob.Actors, handle.Index, nameof(ActorDefHandle));
        public static ref RuntimeBaseDefBlob Get(ref RuntimeContentBlob blob, ActivatorDefHandle handle)
            => ref Require(ref blob.Activators, handle.Index, nameof(ActivatorDefHandle));
        public static ref RuntimeBaseDefBlob Get(ref RuntimeContentBlob blob, DoorDefHandle handle)
            => ref Require(ref blob.Doors, handle.Index, nameof(DoorDefHandle));
        public static ref RuntimeBaseDefBlob Get(ref RuntimeContentBlob blob, ContainerDefHandle handle)
            => ref Require(ref blob.Containers, handle.Index, nameof(ContainerDefHandle));
        public static ref RuntimeBaseDefBlob Get(ref RuntimeContentBlob blob, ItemDefHandle handle)
            => ref Require(ref blob.Items, handle.Index, nameof(ItemDefHandle));
        public static ref RuntimeLightDefBlob Get(ref RuntimeContentBlob blob, LightDefHandle handle)
            => ref Require(ref blob.Lights, handle.Index, nameof(LightDefHandle));
        public static ref RuntimeItemLeveledListDefBlob Get(ref RuntimeContentBlob blob, ItemLeveledListDefHandle handle)
            => ref Require(ref blob.ItemLeveledLists, handle.Index, nameof(ItemLeveledListDefHandle));
        public static ref RuntimeItemLeveledListDefBlob Get(ref RuntimeContentBlob blob, CreatureLeveledListDefHandle handle)
            => ref Require(ref blob.CreatureLeveledLists, handle.Index, nameof(CreatureLeveledListDefHandle));
        public static ref RuntimeSoundDefBlob Get(ref RuntimeContentBlob blob, SoundDefHandle handle)
            => ref Require(ref blob.Sounds, handle.Index, nameof(SoundDefHandle));
        public static ref RuntimeDialogueDefBlob Get(ref RuntimeContentBlob blob, DialogueDefHandle handle)
            => ref Require(ref blob.Dialogues, handle.Index, nameof(DialogueDefHandle));
        public static ref RuntimeSpellDefBlob Get(ref RuntimeContentBlob blob, SpellDefHandle handle)
            => ref Require(ref blob.Spells, handle.Index, nameof(SpellDefHandle));
        public static ref RuntimeEnchantmentDefBlob Get(ref RuntimeContentBlob blob, EnchantmentDefHandle handle)
            => ref Require(ref blob.Enchantments, handle.Index, nameof(EnchantmentDefHandle));
        public static ref RuntimeMagicEffectDefBlob Get(ref RuntimeContentBlob blob, MagicEffectDefHandle handle)
            => ref Require(ref blob.MagicEffects, handle.Index, nameof(MagicEffectDefHandle));
        public static ref RuntimeRegionDefBlob Get(ref RuntimeContentBlob blob, RegionDefHandle handle)
            => ref Require(ref blob.Regions, handle.Index, nameof(RegionDefHandle));
        public static ref RuntimeMusicTrackDefBlob Get(ref RuntimeContentBlob blob, MusicTrackDefHandle handle)
            => ref Require(ref blob.MusicTracks, handle.Index, nameof(MusicTrackDefHandle));
        public static ref RuntimeGenericRecordDefBlob GetGameSetting(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.GameSettings, handle.Index, "game setting");
        public static ref RuntimeGenericRecordDefBlob GetGlobal(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Globals, handle.Index, "global");
        public static ref RuntimeClassDefBlob GetClass(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Classes, handle.Index, "class");
        public static ref RuntimeFactionDefBlob GetFaction(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Factions, handle.Index, "faction");
        public static ref RuntimeRaceDefBlob GetRace(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Races, handle.Index, "race");
        public static ref RuntimeGenericRecordDefBlob GetStatic(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Statics, handle.Index, "static");
        public static ref RuntimeGenericRecordDefBlob GetScript(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.Scripts, handle.Index, "script");
        public static ref RuntimeMorrowindScriptProgramDefBlob Get(ref RuntimeContentBlob blob, MorrowindScriptProgramDefHandle handle)
            => ref Require(ref blob.MorrowindScriptPrograms, handle.Index, nameof(MorrowindScriptProgramDefHandle));
        public static ref RuntimePathGridDefBlob GetPathGrid(ref RuntimeContentBlob blob, GenericRecordDefHandle handle)
            => ref Require(ref blob.PathGrids, handle.Index, "path grid");

        public static bool TryGetActorHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out ActorDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ActorIdLookup, hash, out int index))
                return false;
            handle = ActorDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetActivatorHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out ActivatorDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ActivatorIdLookup, hash, out int index))
                return false;
            handle = ActivatorDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetDoorHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out DoorDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.DoorIdLookup, hash, out int index))
                return false;
            handle = DoorDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetContainerHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out ContainerDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ContainerIdLookup, hash, out int index))
                return false;
            handle = ContainerDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetItemHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out ItemDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ItemIdLookup, hash, out int index))
                return false;
            handle = ItemDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetLightHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out LightDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.LightIdLookup, hash, out int index))
                return false;
            handle = LightDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetItemLeveledListHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out ItemLeveledListDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ItemLeveledListIdLookup, hash, out int index))
                return false;
            handle = ItemLeveledListDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetCreatureLeveledListHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out CreatureLeveledListDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.CreatureLeveledListIdLookup, hash, out int index))
                return false;
            handle = CreatureLeveledListDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetSpellHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out SpellDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.SpellIdLookup, hash, out int index))
                return false;
            handle = SpellDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetEnchantmentHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out EnchantmentDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.EnchantmentIdLookup, hash, out int index))
                return false;
            handle = EnchantmentDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetSoundHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out SoundDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.SoundIdLookup, hash, out int index))
                return false;
            handle = SoundDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetDialogueHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out DialogueDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.DialogueIdLookup, hash, out int index))
                return false;
            handle = DialogueDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetRegionHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out RegionDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.RegionIdLookup, hash, out int index))
                return false;
            handle = RegionDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetGameSettingHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.GameSettingIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetGlobalHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.GlobalIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetClassHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ClassIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetFactionHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.FactionIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetRaceHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.RaceIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetStaticHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.StaticIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetScriptHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ScriptIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetMorrowindScriptProgramHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out MorrowindScriptProgramDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.MorrowindScriptProgramIdLookup, hash, out int index))
                return false;
            handle = MorrowindScriptProgramDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetMagicEffectHandleByIndex(ref RuntimeContentBlob blob, int index, out MagicEffectDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.MagicEffectIndexLookup, index, out int handleIndex))
                return false;
            handle = MagicEffectDefHandle.FromIndex(handleIndex);
            return true;
        }

        public static bool TryGetPathGridHandleByIdHash(ref RuntimeContentBlob blob, ulong hash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.PathGridIdLookup, hash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetInteriorPathGridHandleByCellHash(ref RuntimeContentBlob blob, ulong cellHash, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.InteriorPathGridHashLookup, cellHash, out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static bool TryGetExteriorPathGridHandle(ref RuntimeContentBlob blob, int gridX, int gridY, out GenericRecordDefHandle handle)
        {
            handle = default;
            if (!TryGetHandleIndex(ref blob.ExteriorPathGridCoordLookup, PackExteriorPathGridKey(gridX, gridY), out int index))
                return false;
            handle = GenericRecordDefHandle.FromIndex(index);
            return true;
        }

        public static float RequireGameSettingFloatByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob gmst = ref RequireGameSettingByIdHash(ref blob, idHash);
            if (gmst.ValueKind == GenericRecordValueKind.Float)
                return gmst.Float0;
            if (gmst.ValueKind == GenericRecordValueKind.Integer)
                return gmst.Int0;
            throw new InvalidOperationException($"[VVardenfell][ContentBlob] GMST hash {idHash} is not numeric.");
        }

        public static int RequireGameSettingIntByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob gmst = ref RequireGameSettingByIdHash(ref blob, idHash);
            if (gmst.ValueKind == GenericRecordValueKind.Integer)
                return gmst.Int0;
            throw new InvalidOperationException($"[VVardenfell][ContentBlob] GMST hash {idHash} is not an integer.");
        }

        public static float RequireGlobalFloatByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob global = ref RequireGlobalByIdHash(ref blob, idHash);
            if (global.ValueKind == GenericRecordValueKind.Float)
                return global.Float0;
            if (global.ValueKind == GenericRecordValueKind.Integer)
                return global.Int0;
            if (global.ValueKind == GenericRecordValueKind.None)
                return global.Float0;
            throw new InvalidOperationException($"[VVardenfell][ContentBlob] Global hash {idHash} is not numeric.");
        }

        public static int RequireGlobalIntByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob global = ref RequireGlobalByIdHash(ref blob, idHash);
            if (global.ValueKind == GenericRecordValueKind.Integer)
                return global.Int0;
            if (global.ValueKind == GenericRecordValueKind.Float)
                return (int)global.Float0;
            if (global.ValueKind == GenericRecordValueKind.None)
                return global.Int0;
            throw new InvalidOperationException($"[VVardenfell][ContentBlob] Global hash {idHash} is not numeric.");
        }

        public static string RequireGameSettingStringByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob gmst = ref RequireGameSettingByIdHash(ref blob, idHash);
            if (gmst.ValueKind != GenericRecordValueKind.String)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] GMST hash {idHash} is not a string.");

            string value = gmst.Text.ToString();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] GMST hash {idHash} has no string value.");
            return value;
        }

        public static string RequireGameSettingStringAllowEmptyByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            ref RuntimeGenericRecordDefBlob gmst = ref RequireGameSettingByIdHash(ref blob, idHash);
            if (gmst.ValueKind != GenericRecordValueKind.String)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] GMST hash {idHash} is not a string.");
            return gmst.Text.ToString();
        }

        public static bool TryGetItemEquipment(ref RuntimeContentBlob blob, ItemDefHandle item, out ItemEquipmentDef equipment)
        {
            equipment = default;
            if (!item.IsValid)
                return false;
            if (!TryFind(ref blob.ItemIndexToEquipmentIndexLookup, item.Index, out int equipmentIndex))
                return false;
            if ((uint)equipmentIndex >= (uint)blob.ItemEquipment.Length)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Item equipment index {equipmentIndex} is outside blob range {blob.ItemEquipment.Length}.");
            equipment = blob.ItemEquipment[equipmentIndex];
            return true;
        }

        public static bool TryGetItemEquipment(ref RuntimeContentBlob blob, in ContentReference content, out ItemEquipmentDef equipment)
        {
            equipment = default;
            if (content.Kind != ContentReferenceKind.Item)
                return false;
            return TryGetItemEquipment(ref blob, ItemDefHandle.FromIndex(content.HandleValue - 1), out equipment);
        }

        public static ItemEquipmentDef RequireItemEquipment(ref RuntimeContentBlob blob, in ContentReference content, string context)
        {
            if (!TryGetItemEquipment(ref blob, content, out ItemEquipmentDef equipment))
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] {context} requires item equipment for {Describe(content)}.");
            return equipment;
        }

        public static bool TryResolvePlaceableByIdHash(ref RuntimeContentBlob blob, ulong hash, out ContentReference content)
        {
            content = default;
            if (hash == 0UL || !TryFindPlaceable(ref blob.PlaceableLookup, hash, out content))
                return false;
            if (!IsValid(ref blob, content))
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Placeable hash {hash} resolved to invalid {Describe(content)}.");
            return true;
        }

        public static bool TryGetExplicitRefTargetByIdHash(ref RuntimeContentBlob blob, ulong hash, out uint placedRefId)
        {
            placedRefId = 0u;
            if (hash == 0UL || !TryFindExplicitRef(ref blob.ExplicitRefTargetLookup, hash, out placedRefId))
                return false;
            return placedRefId != 0u;
        }

        public static bool IsValid(ref RuntimeContentBlob blob, in ContentReference content)
        {
            if (!content.IsValid)
                return false;
            return content.Kind switch
            {
                ContentReferenceKind.Actor => (uint)(content.HandleValue - 1) < (uint)blob.Actors.Length,
                ContentReferenceKind.Activator => (uint)(content.HandleValue - 1) < (uint)blob.Activators.Length,
                ContentReferenceKind.Container => (uint)(content.HandleValue - 1) < (uint)blob.Containers.Length,
                ContentReferenceKind.Door => (uint)(content.HandleValue - 1) < (uint)blob.Doors.Length,
                ContentReferenceKind.Item => (uint)(content.HandleValue - 1) < (uint)blob.Items.Length,
                ContentReferenceKind.Light => (uint)(content.HandleValue - 1) < (uint)blob.Lights.Length,
                ContentReferenceKind.Static => (uint)(content.HandleValue - 1) < (uint)blob.Statics.Length,
                ContentReferenceKind.LeveledCreature => (uint)(content.HandleValue - 1) < (uint)blob.CreatureLeveledLists.Length,
                ContentReferenceKind.LeveledItem => (uint)(content.HandleValue - 1) < (uint)blob.ItemLeveledLists.Length,
                _ => false,
            };
        }

        public static float RequireCarryWeight(ref RuntimeContentBlob blob, in ContentReference content)
        {
            switch (content.Kind)
            {
                case ContentReferenceKind.Item:
                {
                    ref RuntimeBaseDefBlob item = ref Get(ref blob, ItemDefHandle.FromIndex(content.HandleValue - 1));
                    return item.Float0;
                }
                case ContentReferenceKind.Light:
                {
                    ref RuntimeLightDefBlob light = ref Get(ref blob, LightDefHandle.FromIndex(content.HandleValue - 1));
                    return light.Weight;
                }
                default:
                    throw new InvalidOperationException($"[VVardenfell][ContentBlob] Carry weight is unsupported for {Describe(content)}.");
            }
        }

        public static void RequireRange(int first, int count, int length, string context)
        {
            if (count == 0 && first == -1)
                return;
            if (first < 0 || count < 0 || first > length || count > length - first)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid {context} range start={first} count={count} length={length}.");
        }

        public static BlobArray<RuntimeItemEquipmentBodyPartDefBlob> GetItemEquipmentBodyParts(ref RuntimeContentBlob blob)
            => blob.ItemEquipmentBodyParts;

        public static BlobArray<RuntimeContainerItemDefBlob> GetActorInventoryItems(ref RuntimeContentBlob blob, ActorDefHandle handle)
        {
            ref RuntimeActorDefBlob actor = ref Get(ref blob, handle);
            RequireRange(actor.FirstInventoryIndex, actor.InventoryCount, blob.ActorInventoryItems.Length, "actor inventory");
            return blob.ActorInventoryItems;
        }

        public static ref BlobArray<RuntimeContainerItemDefBlob> GetContainerItems(ref RuntimeContentBlob blob, ContainerDefHandle handle, out int first, out int count)
        {
            if (!handle.IsValid)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Container item range requires a valid container handle.");
            if ((uint)handle.Index >= (uint)blob.ContainerContentRanges.Length)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Container content range index {handle.Index} is outside blob range {blob.ContainerContentRanges.Length}.");

            ContainerContentRangeDef range = blob.ContainerContentRanges[handle.Index];
            RequireRange(range.FirstItemIndex, range.ItemCount, blob.ContainerItems.Length, "container item");
            first = range.FirstItemIndex;
            count = range.ItemCount;
            return ref blob.ContainerItems;
        }

        public static ref BlobArray<RuntimeItemLeveledListEntryDefBlob> GetItemLeveledListEntries(ref RuntimeContentBlob blob, ItemLeveledListDefHandle handle, out int first, out int count)
        {
            ref RuntimeItemLeveledListDefBlob list = ref Get(ref blob, handle);
            RequireRange(list.FirstEntryIndex, list.EntryCount, blob.ItemLeveledListEntries.Length, "item leveled-list entry");
            first = list.FirstEntryIndex;
            count = list.EntryCount;
            return ref blob.ItemLeveledListEntries;
        }

        public static BlobArray<RuntimeActorAiPackageDefBlob> GetActorAiPackages(ref RuntimeContentBlob blob, ActorDefHandle handle)
        {
            ref RuntimeActorDefBlob actor = ref Get(ref blob, handle);
            RequireRange(actor.FirstAiPackageIndex, actor.AiPackageCount, blob.ActorAiPackages.Length, "actor AI package");
            return blob.ActorAiPackages;
        }

        public static RuntimeItemEquipmentBodyPartDefBlob GetItemEquipmentBodyPart(ref RuntimeContentBlob blob, in ItemEquipmentDef equipment, int relativeIndex)
        {
            RequireRange(equipment.FirstBodyPartIndex, equipment.BodyPartCount, blob.ItemEquipmentBodyParts.Length, "item equipment body part");
            if ((uint)relativeIndex >= (uint)equipment.BodyPartCount)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid item equipment body part relative index {relativeIndex}; count {equipment.BodyPartCount}.");
            return blob.ItemEquipmentBodyParts[equipment.FirstBodyPartIndex + relativeIndex];
        }

        public static BlobArray<MagicEffectInstanceDef> GetMagicEffectInstances(ref RuntimeContentBlob blob)
            => blob.MagicEffectInstances;

        public static ref BlobArray<RuntimeDialogueInfoDefBlob> GetDialogueInfos(ref RuntimeContentBlob blob, DialogueDefHandle handle, out int first, out int count)
        {
            ref RuntimeDialogueDefBlob dialogue = ref Get(ref blob, handle);
            RequireRange(dialogue.FirstInfoIndex, dialogue.InfoCount, blob.DialogueInfos.Length, "dialogue info");
            first = dialogue.FirstInfoIndex;
            count = dialogue.InfoCount;
            return ref blob.DialogueInfos;
        }

        public static ref BlobArray<RuntimeDialogueConditionDefBlob> GetDialogueConditions(ref RuntimeContentBlob blob, ref RuntimeDialogueInfoDefBlob info, out int first, out int count)
        {
            RequireRange(info.FirstSelectRuleIndex, info.SelectRuleCount, blob.DialogueConditions.Length, "dialogue condition");
            first = info.FirstSelectRuleIndex;
            count = info.SelectRuleCount;
            return ref blob.DialogueConditions;
        }

        public static ref BlobArray<RuntimeFactionReactionDefBlob> GetFactionReactions(ref RuntimeContentBlob blob, GenericRecordDefHandle handle, out int first, out int count)
        {
            ref RuntimeFactionDefBlob faction = ref GetFaction(ref blob, handle);
            RequireRange(faction.FirstReactionIndex, faction.ReactionCount, blob.FactionReactions.Length, "faction reaction");
            first = faction.FirstReactionIndex;
            count = faction.ReactionCount;
            return ref blob.FactionReactions;
        }

        public static ref BlobArray<RuntimeRegionSoundRefDefBlob> GetRegionSoundRefs(ref RuntimeContentBlob blob, RegionDefHandle handle, out int first, out int count)
        {
            ref RuntimeRegionDefBlob region = ref Get(ref blob, handle);
            RequireRange(region.SoundRefStartIndex, region.SoundRefCount, blob.RegionSoundRefs.Length, "region sound ref");
            first = region.SoundRefStartIndex;
            count = region.SoundRefCount;
            return ref blob.RegionSoundRefs;
        }

        public static ref BlobArray<RuntimeMorrowindScriptLocalDefBlob> GetMorrowindScriptLocals(ref RuntimeContentBlob blob, MorrowindScriptProgramDefHandle handle)
        {
            ref RuntimeMorrowindScriptProgramDefBlob program = ref Get(ref blob, handle);
            RequireRange(program.FirstLocalIndex, program.LocalCount, blob.MorrowindScriptLocals.Length, "script local");
            return ref blob.MorrowindScriptLocals;
        }

        public static WeatherDefinitionDef RequireWeatherDefinition(ref RuntimeContentBlob blob, int index)
        {
            if ((uint)index >= (uint)blob.WeatherDefinitions.Length)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid weather definition index {index}; length {blob.WeatherDefinitions.Length}.");
            return ToWeatherDefinition(ref blob.WeatherDefinitions[index]);
        }

        public static WeatherDefinitionDef ResolveWeatherDefinitionOrClearFallback(ref RuntimeContentBlob blob, int index)
        {
            if ((uint)index < (uint)blob.WeatherDefinitions.Length)
                return ToWeatherDefinition(ref blob.WeatherDefinitions[index]);
            return new WeatherDefinitionDef
            {
                Kind = WeatherKind.Clear,
            };
        }

        public static int ClampWeatherIndex(ref RuntimeContentBlob blob, int index)
        {
            if (blob.WeatherDefinitions.Length <= 0)
                return 0;
            return Math.Clamp(index, 0, blob.WeatherDefinitions.Length - 1);
        }

        public static float ResolveWeatherTransitionDelta(ref RuntimeContentBlob blob, int weatherIndex)
        {
            if ((uint)weatherIndex >= (uint)blob.WeatherDefinitions.Length)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid weather definition index {weatherIndex}; length {blob.WeatherDefinitions.Length}.");
            float delta = blob.WeatherDefinitions[weatherIndex].TransitionDelta;
            return delta > 0f ? delta : 0.015f;
        }

        static ref RuntimeGenericRecordDefBlob RequireGameSettingByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            if (!TryGetGameSettingHandleByIdHash(ref blob, idHash, out GenericRecordDefHandle handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Missing GMST hash {idHash}.");
            return ref Require(ref blob.GameSettings, handle.Index, "game setting");
        }

        static ref RuntimeGenericRecordDefBlob RequireGlobalByIdHash(ref RuntimeContentBlob blob, ulong idHash)
        {
            if (!TryGetGlobalHandleByIdHash(ref blob, idHash, out GenericRecordDefHandle handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Missing global hash {idHash}.");
            return ref Require(ref blob.Globals, handle.Index, "global");
        }

        static WeatherDefinitionDef ToWeatherDefinition(ref RuntimeWeatherDefinitionDefBlob source)
        {
            return new WeatherDefinitionDef
            {
                Kind = source.Kind,
                Id = source.Id.ToString(),
                CloudTexture = source.CloudTexture.ToString(),
                SkyColor = source.SkyColor,
                FogColor = source.FogColor,
                AmbientColor = source.AmbientColor,
                SunColor = source.SunColor,
                SunDiscSunsetColorRgba = source.SunDiscSunsetColorRgba,
                LandFogDayDepth = source.LandFogDayDepth,
                LandFogNightDepth = source.LandFogNightDepth,
                WindSpeed = source.WindSpeed,
                CloudSpeed = source.CloudSpeed,
                GlareView = source.GlareView,
                CloudsMaximumPercent = source.CloudsMaximumPercent,
                TransitionDelta = source.TransitionDelta,
                RainSpeed = source.RainSpeed,
                RainEntranceSpeed = source.RainEntranceSpeed,
                RainMaxRaindrops = source.RainMaxRaindrops,
                RainDiameter = source.RainDiameter,
                RainThreshold = source.RainThreshold,
                RainMinHeight = source.RainMinHeight,
                RainMaxHeight = source.RainMaxHeight,
                UsingPrecip = source.UsingPrecip,
                IsStorm = source.IsStorm,
                RainLoopSoundId = source.RainLoopSoundId.ToString(),
                AmbientLoopSoundId = source.AmbientLoopSoundId.ToString(),
                ThunderFrequency = source.ThunderFrequency,
                ThunderThreshold = source.ThunderThreshold,
                FlashDecrement = source.FlashDecrement,
                ThunderSoundId0 = source.ThunderSoundId0.ToString(),
                ThunderSoundId1 = source.ThunderSoundId1.ToString(),
                ThunderSoundId2 = source.ThunderSoundId2.ToString(),
                ThunderSoundId3 = source.ThunderSoundId3.ToString(),
            };
        }

        static ref T Require<T>(ref BlobArray<T> array, int index, string context)
            where T : unmanaged
        {
            if ((uint)index >= (uint)array.Length)
                throw new InvalidOperationException($"[VVardenfell][ContentBlob] Invalid {context} index {index}; length {array.Length}.");
            return ref array[index];
        }

        static bool TryGetHandleIndex(ref BlobArray<RuntimeContentHashLookupBlob> lookup, ulong hash, out int index)
        {
            index = default;
            if (hash == 0UL || !TryFind(ref lookup, hash, out int value))
                return false;
            index = value - 1;
            return true;
        }

        static bool TryGetHandleIndex(ref BlobArray<RuntimeContentIntLookupBlob> lookup, int key, out int index)
        {
            index = default;
            if (!TryFind(ref lookup, key, out int value))
                return false;
            index = value - 1;
            return true;
        }

        static bool TryGetHandleIndex(ref BlobArray<RuntimeContentLongLookupBlob> lookup, long key, out int index)
        {
            index = default;
            if (!TryFind(ref lookup, key, out int value))
                return false;
            index = value - 1;
            return true;
        }

        static bool TryFind(ref BlobArray<RuntimeContentHashLookupBlob> lookup, ulong hash, out int handleValue)
        {
            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong value = lookup[mid].Hash;
                if (value == hash)
                {
                    handleValue = lookup[mid].HandleValue;
                    return true;
                }
                if (value < hash) lo = mid + 1;
                else hi = mid - 1;
            }
            handleValue = default;
            return false;
        }

        static bool TryFind(ref BlobArray<RuntimeContentIntLookupBlob> lookup, int key, out int value)
        {
            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int current = lookup[mid].Key;
                if (current == key)
                {
                    value = lookup[mid].Value;
                    return true;
                }
                if (current < key) lo = mid + 1;
                else hi = mid - 1;
            }
            value = default;
            return false;
        }

        static bool TryFind(ref BlobArray<RuntimeContentLongLookupBlob> lookup, long key, out int value)
        {
            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                long current = lookup[mid].Key;
                if (current == key)
                {
                    value = lookup[mid].Value;
                    return true;
                }
                if (current < key) lo = mid + 1;
                else hi = mid - 1;
            }
            value = default;
            return false;
        }

        static bool TryFindPlaceable(ref BlobArray<RuntimeContentPlaceableLookupBlob> lookup, ulong hash, out ContentReference content)
        {
            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong current = lookup[mid].Hash;
                if (current == hash)
                {
                    content = lookup[mid].Content;
                    return true;
                }
                if (current < hash) lo = mid + 1;
                else hi = mid - 1;
            }
            content = default;
            return false;
        }

        static bool TryFindExplicitRef(ref BlobArray<RuntimeContentExplicitRefLookupBlob> lookup, ulong hash, out uint placedRefId)
        {
            int lo = 0;
            int hi = lookup.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong current = lookup[mid].Hash;
                if (current == hash)
                {
                    placedRefId = lookup[mid].PlacedRefId;
                    return true;
                }
                if (current < hash) lo = mid + 1;
                else hi = mid - 1;
            }

            placedRefId = 0u;
            return false;
        }

        static string Describe(in ContentReference content)
            => $"{content.Kind}:{content.HandleValue}";

        static long PackExteriorPathGridKey(int gridX, int gridY)
            => ((long)gridX << 32) ^ (uint)gridY;
    }
}


