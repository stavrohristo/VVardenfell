using System;
using System.Collections.Generic;
using Unity.Profiling;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Content
{
    public sealed class RuntimeContentDatabase
    {
        static readonly ProfilerMarker k_Load = new("VV.RuntimeContent.Load");

        readonly Dictionary<string, ActorDefHandle> _actorsById;
        readonly Dictionary<string, ActivatorDefHandle> _activatorsById;
        readonly Dictionary<string, DoorDefHandle> _doorsById;
        readonly Dictionary<string, ContainerDefHandle> _containersById;
        readonly Dictionary<string, ItemDefHandle> _itemsById;
        readonly Dictionary<string, LightDefHandle> _lightsById;
        readonly Dictionary<string, ItemLeveledListDefHandle> _itemLeveledListsById;
        readonly Dictionary<string, CreatureLeveledListDefHandle> _creatureLeveledListsById;
        readonly Dictionary<string, SoundDefHandle> _soundsById;
        readonly Dictionary<string, DialogueDefHandle> _dialoguesById;
        readonly Dictionary<string, SpellDefHandle> _spellsById;
        readonly Dictionary<string, EnchantmentDefHandle> _enchantmentsById;
        readonly Dictionary<int, MagicEffectDefHandle> _magicEffectsByIndex;
        readonly Dictionary<string, RegionDefHandle> _regionsById;
        readonly Dictionary<string, MusicTrackDefHandle> _musicTracksByRelativePath;
        readonly Dictionary<string, GenericRecordDefHandle> _gameSettingsById;
        readonly Dictionary<string, GenericRecordDefHandle> _globalsById;
        readonly Dictionary<string, GenericRecordDefHandle> _classesById;
        readonly Dictionary<string, GenericRecordDefHandle> _factionsById;
        readonly Dictionary<string, GenericRecordDefHandle> _racesById;
        readonly Dictionary<string, GenericRecordDefHandle> _birthsignsById;
        readonly Dictionary<string, GenericRecordDefHandle> _skillsById;
        readonly Dictionary<string, GenericRecordDefHandle> _scriptsById;
        readonly Dictionary<string, GenericRecordDefHandle> _startScriptsById;
        readonly Dictionary<string, GenericRecordDefHandle> _soundGeneratorsById;
        readonly Dictionary<string, GenericRecordDefHandle> _landTexturesById;
        readonly Dictionary<string, GenericRecordDefHandle> _staticsById;
        readonly Dictionary<string, GenericRecordDefHandle> _bodyPartsById;
        readonly Dictionary<string, GenericRecordDefHandle> _pathGridsById;
        readonly Dictionary<string, ContentReference> _placeablesById;

        public static RuntimeContentDatabase Active { get; private set; }

        public GameplayContentManifest Manifest { get; }
        public GameplayContentData Data { get; }

        public int ActorCount => Data.Actors.Length;
        public int ActivatorCount => Data.Activators.Length;
        public int DoorCount => Data.Doors.Length;
        public int ContainerCount => Data.Containers.Length;
        public int ItemCount => Data.Items.Length;
        public int LightCount => Data.Lights.Length;
        public int ItemLeveledListCount => Data.ItemLeveledLists.Length;
        public int CreatureLeveledListCount => Data.CreatureLeveledLists.Length;
        public int SoundCount => Data.Sounds.Length;
        public int DialogueCount => Data.Dialogues.Length;
        public int DialogueInfoCount => Data.DialogueInfos.Length;
        public int SpellCount => Data.Spells.Length;
        public int EnchantmentCount => Data.Enchantments.Length;
        public int MagicEffectCount => Data.MagicEffects.Length;
        public int RegionCount => Data.Regions.Length;
        public int MusicTrackCount => Data.MusicTracks.Length;
        public int AmbientSettingsCount => Manifest?.AmbientSettingsCount ?? 0;
        public int GameSettingCount => Data.GameSettings.Length;
        public int GlobalCount => Data.Globals.Length;
        public int ClassCount => Data.Classes.Length;
        public int FactionCount => Data.Factions.Length;
        public int RaceCount => Data.Races.Length;
        public int BirthsignCount => Data.Birthsigns.Length;
        public int SkillCount => Data.Skills.Length;
        public int ScriptCount => Data.Scripts.Length;
        public int StartScriptCount => Data.StartScripts.Length;
        public int SoundGeneratorCount => Data.SoundGenerators.Length;
        public int LandTextureCount => Data.LandTextures.Length;
        public int StaticCount => Data.Statics.Length;
        public int BodyPartCount => Data.BodyParts.Length;
        public int PathGridCount => Data.PathGrids.Length;

        RuntimeContentDatabase(GameplayContentManifest manifest, GameplayContentData data)
        {
            Manifest = manifest;
            Data = data ?? throw new ArgumentNullException(nameof(data));

            _actorsById = BuildIndex(data.Actors, actor => actor.Id, ActorDefHandle.FromIndex);
            _activatorsById = BuildIndex(data.Activators, def => def.Id, ActivatorDefHandle.FromIndex);
            _doorsById = BuildIndex(data.Doors, def => def.Id, DoorDefHandle.FromIndex);
            _containersById = BuildIndex(data.Containers, def => def.Id, ContainerDefHandle.FromIndex);
            _itemsById = BuildIndex(data.Items, def => def.Id, ItemDefHandle.FromIndex);
            _lightsById = BuildIndex(data.Lights, def => def.Id, LightDefHandle.FromIndex);
            _itemLeveledListsById = BuildIndex(data.ItemLeveledLists, def => def.Id, ItemLeveledListDefHandle.FromIndex);
            _creatureLeveledListsById = BuildIndex(data.CreatureLeveledLists, def => def.Id, CreatureLeveledListDefHandle.FromIndex);
            _soundsById = BuildIndex(data.Sounds, def => def.Id, SoundDefHandle.FromIndex);
            _dialoguesById = BuildIndex(data.Dialogues, def => def.Id, DialogueDefHandle.FromIndex);
            _spellsById = BuildIndex(data.Spells, def => def.Id, SpellDefHandle.FromIndex);
            _enchantmentsById = BuildIndex(data.Enchantments, def => def.Id, EnchantmentDefHandle.FromIndex);
            _magicEffectsByIndex = BuildMagicEffectIndex(data.MagicEffects);
            _regionsById = BuildIndex(data.Regions, def => def.Id, RegionDefHandle.FromIndex);
            _musicTracksByRelativePath = BuildIndex(data.MusicTracks, def => def.RelativePath, MusicTrackDefHandle.FromIndex);
            _gameSettingsById = BuildIndex(data.GameSettings, def => def.Id, GenericRecordDefHandle.FromIndex);
            _globalsById = BuildIndex(data.Globals, def => def.Id, GenericRecordDefHandle.FromIndex);
            _classesById = BuildIndex(data.Classes, def => def.Id, GenericRecordDefHandle.FromIndex);
            _factionsById = BuildIndex(data.Factions, def => def.Id, GenericRecordDefHandle.FromIndex);
            _racesById = BuildIndex(data.Races, def => def.Id, GenericRecordDefHandle.FromIndex);
            _birthsignsById = BuildIndex(data.Birthsigns, def => def.Id, GenericRecordDefHandle.FromIndex);
            _skillsById = BuildIndex(data.Skills, def => def.Id, GenericRecordDefHandle.FromIndex);
            _scriptsById = BuildIndex(data.Scripts, def => def.Id, GenericRecordDefHandle.FromIndex);
            _startScriptsById = BuildIndex(data.StartScripts, def => def.Id, GenericRecordDefHandle.FromIndex);
            _soundGeneratorsById = BuildIndex(data.SoundGenerators, def => def.Id, GenericRecordDefHandle.FromIndex);
            _landTexturesById = BuildIndex(data.LandTextures, def => def.Id, GenericRecordDefHandle.FromIndex);
            _staticsById = BuildIndex(data.Statics, def => def.Id, GenericRecordDefHandle.FromIndex);
            _bodyPartsById = BuildIndex(data.BodyParts, def => def.Id, GenericRecordDefHandle.FromIndex);
            _pathGridsById = BuildIndex(data.PathGrids, def => def.Id, GenericRecordDefHandle.FromIndex);
            _placeablesById = GameplayContentReferenceIndex.BuildPlaceableIndex(data);
        }

        public static RuntimeContentDatabase LoadFromCache()
        {
            using var _ = k_Load.Auto();

            if (!GameplayContentManifest.TryRead(CachePaths.GameplayContentManifest, out var manifest))
                throw new InvalidOperationException($"Gameplay content manifest unreadable at '{CachePaths.GameplayContentManifest}'.");

            var data = GameplayContentFile.Read(CachePaths.GameplayContent);
            var db = new RuntimeContentDatabase(manifest, data);
            Active = db;
            return db;
        }

        public static void Clear() => Active = null;

        public bool TryGetActorHandle(string id, out ActorDefHandle handle) => _actorsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetActivatorHandle(string id, out ActivatorDefHandle handle) => _activatorsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetDoorHandle(string id, out DoorDefHandle handle) => _doorsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetContainerHandle(string id, out ContainerDefHandle handle) => _containersById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetItemHandle(string id, out ItemDefHandle handle) => _itemsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetLightHandle(string id, out LightDefHandle handle) => _lightsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetItemLeveledListHandle(string id, out ItemLeveledListDefHandle handle) => _itemLeveledListsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetCreatureLeveledListHandle(string id, out CreatureLeveledListDefHandle handle) => _creatureLeveledListsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetSoundHandle(string id, out SoundDefHandle handle) => _soundsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetDialogueHandle(string id, out DialogueDefHandle handle) => _dialoguesById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetSpellHandle(string id, out SpellDefHandle handle) => _spellsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetEnchantmentHandle(string id, out EnchantmentDefHandle handle) => _enchantmentsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetMagicEffectHandle(int index, out MagicEffectDefHandle handle) => _magicEffectsByIndex.TryGetValue(index, out handle);
        public bool TryGetRegionHandle(string id, out RegionDefHandle handle) => _regionsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetMusicTrackHandle(string relativePath, out MusicTrackDefHandle handle) => _musicTracksByRelativePath.TryGetValue(relativePath ?? string.Empty, out handle);
        public bool TryGetGameSettingHandle(string id, out GenericRecordDefHandle handle) => _gameSettingsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetStaticHandle(string id, out GenericRecordDefHandle handle) => _staticsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetClassHandle(string id, out GenericRecordDefHandle handle) => _classesById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetFactionHandle(string id, out GenericRecordDefHandle handle) => _factionsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetRaceHandle(string id, out GenericRecordDefHandle handle) => _racesById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetBirthsignHandle(string id, out GenericRecordDefHandle handle) => _birthsignsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetSkillHandle(string id, out GenericRecordDefHandle handle) => _skillsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetScriptHandle(string id, out GenericRecordDefHandle handle) => _scriptsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetStartScriptHandle(string id, out GenericRecordDefHandle handle) => _startScriptsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetSoundGeneratorHandle(string id, out GenericRecordDefHandle handle) => _soundGeneratorsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetLandTextureHandle(string id, out GenericRecordDefHandle handle) => _landTexturesById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetBodyPartHandle(string id, out GenericRecordDefHandle handle) => _bodyPartsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetPathGridHandle(string id, out GenericRecordDefHandle handle) => _pathGridsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetGlobalHandle(string id, out GenericRecordDefHandle handle) => _globalsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryResolvePlaceable(string id, out ContentReference contentRef) => _placeablesById.TryGetValue(id ?? string.Empty, out contentRef);
        public bool TryGetGameSettingFloat(string id, out float value)
        {
            if (TryGetGameSettingHandle(id, out var handle) && handle.IsValid)
            {
                value = GetGameSetting(handle).Float0;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetGameSettingString(string id, out string value)
        {
            if (TryGetGameSettingHandle(id, out var handle) && handle.IsValid)
            {
                value = GetGameSetting(handle).Text;
                return !string.IsNullOrWhiteSpace(value);
            }

            value = default;
            return false;
        }

        public bool IsValid(ContentReference contentRef)
        {
            if (!contentRef.IsValid)
                return false;

            return GameplayContentReferenceIndex.IsValid(Data, contentRef);
        }

        public ref readonly ActorDef Get(ActorDefHandle handle) => ref Data.Actors[handle.Index];
        public ref readonly BaseDef Get(ActivatorDefHandle handle) => ref Data.Activators[handle.Index];
        public ref readonly BaseDef Get(DoorDefHandle handle) => ref Data.Doors[handle.Index];
        public ref readonly BaseDef Get(ContainerDefHandle handle) => ref Data.Containers[handle.Index];
        public ref readonly BaseDef Get(ItemDefHandle handle) => ref Data.Items[handle.Index];
        public ref readonly LightDef Get(LightDefHandle handle) => ref Data.Lights[handle.Index];
        public ref readonly ItemLeveledListDef Get(ItemLeveledListDefHandle handle) => ref Data.ItemLeveledLists[handle.Index];
        public ref readonly ItemLeveledListDef Get(CreatureLeveledListDefHandle handle) => ref Data.CreatureLeveledLists[handle.Index];
        public ref readonly SoundDef Get(SoundDefHandle handle) => ref Data.Sounds[handle.Index];
        public ref readonly DialogueDef Get(DialogueDefHandle handle) => ref Data.Dialogues[handle.Index];
        public ref readonly SpellDef Get(SpellDefHandle handle) => ref Data.Spells[handle.Index];
        public ref readonly EnchantmentDef Get(EnchantmentDefHandle handle) => ref Data.Enchantments[handle.Index];
        public ref readonly MagicEffectDef Get(MagicEffectDefHandle handle) => ref Data.MagicEffects[handle.Index];
        public ref readonly RegionDef Get(RegionDefHandle handle) => ref Data.Regions[handle.Index];
        public ref readonly MusicTrackDef Get(MusicTrackDefHandle handle) => ref Data.MusicTracks[handle.Index];
        public ref readonly AmbientSettingsDef GetAmbientSettings() => ref Data.AmbientSettings;
        public ref readonly GenericRecordDef GetGameSetting(GenericRecordDefHandle handle) => ref Data.GameSettings[handle.Index];
        public ref readonly GenericRecordDef GetStatic(GenericRecordDefHandle handle) => ref Data.Statics[handle.Index];
        public ref readonly ClassDef GetClass(GenericRecordDefHandle handle) => ref Data.Classes[handle.Index];
        public ref readonly FactionDef GetFaction(GenericRecordDefHandle handle) => ref Data.Factions[handle.Index];
        public ref readonly RaceDef GetRace(GenericRecordDefHandle handle) => ref Data.Races[handle.Index];
        public ref readonly GenericRecordDef GetBirthsign(GenericRecordDefHandle handle) => ref Data.Birthsigns[handle.Index];
        public ref readonly GenericRecordDef GetSkill(GenericRecordDefHandle handle) => ref Data.Skills[handle.Index];
        public ref readonly GenericRecordDef GetScript(GenericRecordDefHandle handle) => ref Data.Scripts[handle.Index];
        public ref readonly GenericRecordDef GetStartScript(GenericRecordDefHandle handle) => ref Data.StartScripts[handle.Index];
        public ref readonly GenericRecordDef GetSoundGenerator(GenericRecordDefHandle handle) => ref Data.SoundGenerators[handle.Index];
        public ref readonly GenericRecordDef GetLandTexture(GenericRecordDefHandle handle) => ref Data.LandTextures[handle.Index];
        public ref readonly GenericRecordDef GetBodyPart(GenericRecordDefHandle handle) => ref Data.BodyParts[handle.Index];
        public ref readonly GenericRecordDef GetPathGrid(GenericRecordDefHandle handle) => ref Data.PathGrids[handle.Index];
        public ref readonly GenericRecordDef GetGlobal(GenericRecordDefHandle handle) => ref Data.Globals[handle.Index];

        public ReadOnlySpan<ContainerItemDef> GetContainerItems(ContainerDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.ContainerContentRanges.Length)
                return ReadOnlySpan<ContainerItemDef>.Empty;

            var range = Data.ContainerContentRanges[handle.Index];
            if (range.FirstItemIndex < 0 || range.ItemCount <= 0)
                return ReadOnlySpan<ContainerItemDef>.Empty;

            return new ReadOnlySpan<ContainerItemDef>(Data.ContainerItems, range.FirstItemIndex, range.ItemCount);
        }

        public ReadOnlySpan<ActorSpellDef> GetActorSpells(ActorDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.Actors.Length)
                return ReadOnlySpan<ActorSpellDef>.Empty;

            var actor = Data.Actors[handle.Index];
            if (actor.FirstSpellIndex < 0 || actor.SpellCount <= 0 || Data.ActorSpells == null)
                return ReadOnlySpan<ActorSpellDef>.Empty;

            if (actor.FirstSpellIndex >= Data.ActorSpells.Length)
                return ReadOnlySpan<ActorSpellDef>.Empty;

            int count = Math.Min(actor.SpellCount, Data.ActorSpells.Length - actor.FirstSpellIndex);
            return new ReadOnlySpan<ActorSpellDef>(Data.ActorSpells, actor.FirstSpellIndex, count);
        }

        public ReadOnlySpan<ContainerItemDef> GetActorInventoryItems(ActorDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.Actors.Length)
                return ReadOnlySpan<ContainerItemDef>.Empty;

            var actor = Data.Actors[handle.Index];
            if (actor.FirstInventoryIndex < 0 || actor.InventoryCount <= 0 || Data.ActorInventoryItems == null)
                return ReadOnlySpan<ContainerItemDef>.Empty;

            if (actor.FirstInventoryIndex >= Data.ActorInventoryItems.Length)
                return ReadOnlySpan<ContainerItemDef>.Empty;

            int count = Math.Min(actor.InventoryCount, Data.ActorInventoryItems.Length - actor.FirstInventoryIndex);
            return new ReadOnlySpan<ContainerItemDef>(Data.ActorInventoryItems, actor.FirstInventoryIndex, count);
        }

        public ReadOnlySpan<DialogueInfoDef> GetDialogueInfos(DialogueDefHandle handle)
        {
            ref readonly var dialogue = ref Get(handle);
            if (dialogue.FirstInfoIndex < 0 || dialogue.InfoCount <= 0)
                return ReadOnlySpan<DialogueInfoDef>.Empty;
            return new ReadOnlySpan<DialogueInfoDef>(Data.DialogueInfos, dialogue.FirstInfoIndex, dialogue.InfoCount);
        }

        public ReadOnlySpan<RegionSoundRefDef> GetRegionSoundRefs(RegionDefHandle handle)
        {
            ref readonly var region = ref Get(handle);
            if (region.SoundRefStartIndex < 0 || region.SoundRefCount <= 0)
                return ReadOnlySpan<RegionSoundRefDef>.Empty;
            return new ReadOnlySpan<RegionSoundRefDef>(Data.RegionSoundRefs, region.SoundRefStartIndex, region.SoundRefCount);
        }

        public ReadOnlySpan<ItemLeveledListEntryDef> GetItemLeveledListEntries(ItemLeveledListDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.ItemLeveledLists.Length)
                return ReadOnlySpan<ItemLeveledListEntryDef>.Empty;

            var list = Data.ItemLeveledLists[handle.Index];
            if (list.FirstEntryIndex < 0 || list.EntryCount <= 0)
                return ReadOnlySpan<ItemLeveledListEntryDef>.Empty;

            return new ReadOnlySpan<ItemLeveledListEntryDef>(Data.ItemLeveledListEntries, list.FirstEntryIndex, list.EntryCount);
        }

        public ReadOnlySpan<ItemLeveledListEntryDef> GetCreatureLeveledListEntries(CreatureLeveledListDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.CreatureLeveledLists.Length)
                return ReadOnlySpan<ItemLeveledListEntryDef>.Empty;

            var list = Data.CreatureLeveledLists[handle.Index];
            if (list.FirstEntryIndex < 0 || list.EntryCount <= 0)
                return ReadOnlySpan<ItemLeveledListEntryDef>.Empty;

            return new ReadOnlySpan<ItemLeveledListEntryDef>(Data.CreatureLeveledListEntries, list.FirstEntryIndex, list.EntryCount);
        }

        public bool TryGetFirstMusicTrackByCategory(MusicTrackCategory category, out MusicTrackDefHandle handle)
        {
            var tracks = Data.MusicTracks;
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Category != category)
                    continue;

                handle = MusicTrackDefHandle.FromIndex(i);
                return true;
            }

            handle = default;
            return false;
        }

        static Dictionary<string, THandle> BuildIndex<TDef, THandle>(TDef[] defs, Func<TDef, string> keySelector, Func<int, THandle> handleFactory)
        {
            var map = new Dictionary<string, THandle>(defs?.Length ?? 0, StringComparer.OrdinalIgnoreCase);
            if (defs == null)
                return map;

            for (int i = 0; i < defs.Length; i++)
            {
                string id = keySelector(defs[i]);
                if (!string.IsNullOrWhiteSpace(id))
                    map[id] = handleFactory(i);
            }

            return map;
        }

        static Dictionary<int, MagicEffectDefHandle> BuildMagicEffectIndex(MagicEffectDef[] defs)
        {
            var map = new Dictionary<int, MagicEffectDefHandle>(defs?.Length ?? 0);
            if (defs == null)
                return map;

            for (int i = 0; i < defs.Length; i++)
                map[defs[i].Index] = MagicEffectDefHandle.FromIndex(i);
            return map;
        }

    }
}
