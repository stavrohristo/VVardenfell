using System;
using System.Collections.Generic;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;

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
        readonly Dictionary<string, GenericRecordDefHandle> _actorBodyPartsById;
        readonly Dictionary<string, GenericRecordDefHandle> _pathGridsById;
        readonly Dictionary<ulong, GenericRecordDefHandle> _interiorPathGridsByHash;
        readonly Dictionary<long, GenericRecordDefHandle> _pathGridsByExteriorCoord;
        readonly Dictionary<string, ContentReference> _placeablesById;
        readonly int[] _itemEquipmentByItemIndex;

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
        public int WeatherDefinitionCount => Data.WeatherDefinitions?.Length ?? 0;
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
        public int ActorBodyPartCount => Data.ActorBodyParts?.Length ?? 0;
        public int PathGridCount => Data.PathGrids.Length;
        public int PathGridNavigationNodeCount => Data.PathGridNavigationNodes?.Length ?? 0;
        public int PathGridNavigationEdgeCount => Data.PathGridNavigationEdges?.Length ?? 0;
        public int PathGridNavigationPortalCount => Data.PathGridNavigationPortals?.Length ?? 0;
        public int PathGridNavigationAbstractEdgeCount => Data.PathGridNavigationAbstractEdges?.Length ?? 0;
        public int PathGridNavigationNeighborCount => Data.PathGridNavigationNeighbors?.Length ?? 0;

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
            _actorBodyPartsById = BuildIndex(data.ActorBodyParts, def => def.Id, GenericRecordDefHandle.FromIndex);
            _pathGridsById = BuildIndex(data.PathGrids, def => def.Id, GenericRecordDefHandle.FromIndex);
            _interiorPathGridsByHash = BuildInteriorPathGridHashIndex(data.PathGrids);
            _pathGridsByExteriorCoord = BuildPathGridExteriorIndex(data.PathGrids);
            _placeablesById = GameplayContentReferenceIndex.BuildPlaceableIndex(data);
            _itemEquipmentByItemIndex = BuildItemEquipmentIndex(data);
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
        public bool TryGetActorBodyPartHandle(string id, out GenericRecordDefHandle handle) => _actorBodyPartsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetPathGridHandle(string id, out GenericRecordDefHandle handle)
            => _pathGridsById.TryGetValue(ContentId.NormalizeId(id ?? string.Empty), out handle);
        public bool TryGetInteriorPathGridHandle(string cellId, out GenericRecordDefHandle handle)
            => _pathGridsById.TryGetValue(ContentId.NormalizeId(cellId ?? string.Empty), out handle);
        public bool TryGetInteriorPathGridHandle(ulong cellHash, out GenericRecordDefHandle handle)
        {
            handle = default;
            return cellHash != 0UL && _interiorPathGridsByHash.TryGetValue(cellHash, out handle);
        }
        public bool TryGetExteriorPathGridHandle(int gridX, int gridY, out GenericRecordDefHandle handle)
            => _pathGridsByExteriorCoord.TryGetValue(PackExteriorPathGridKey(gridX, gridY), out handle);
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
        public ref readonly WeatherSettingsDef GetWeatherSettings() => ref Data.WeatherSettings;
        public ref readonly SkyWeatherVisualSettingsDef GetSkyWeatherVisualSettings() => ref Data.SkyWeatherVisualSettings;
        public ReadOnlySpan<WeatherDefinitionDef> GetWeatherDefinitions()
            => Data.WeatherDefinitions == null ? ReadOnlySpan<WeatherDefinitionDef>.Empty : new ReadOnlySpan<WeatherDefinitionDef>(Data.WeatherDefinitions);
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
        public ref readonly ActorBodyPartDef GetActorBodyPart(GenericRecordDefHandle handle) => ref Data.ActorBodyParts[handle.Index];
        public ref readonly PathGridDef GetPathGrid(GenericRecordDefHandle handle) => ref Data.PathGrids[handle.Index];
        public ref readonly GenericRecordDef GetGlobal(GenericRecordDefHandle handle) => ref Data.Globals[handle.Index];

        public bool TryGetItemEquipment(ItemDefHandle handle, out ItemEquipmentDef equipment)
        {
            equipment = default;
            if (!handle.IsValid || _itemEquipmentByItemIndex == null || (uint)handle.Index >= (uint)_itemEquipmentByItemIndex.Length)
                return false;

            int equipmentIndex = _itemEquipmentByItemIndex[handle.Index];
            if ((uint)equipmentIndex >= (uint)(Data.ItemEquipment?.Length ?? 0))
                return false;

            equipment = Data.ItemEquipment[equipmentIndex];
            return true;
        }

        public ReadOnlySpan<ItemEquipmentBodyPartDef> GetItemEquipmentBodyParts(in ItemEquipmentDef equipment)
        {
            if (equipment.FirstBodyPartIndex < 0 || equipment.BodyPartCount <= 0 || Data.ItemEquipmentBodyParts == null)
                return ReadOnlySpan<ItemEquipmentBodyPartDef>.Empty;

            if (equipment.FirstBodyPartIndex >= Data.ItemEquipmentBodyParts.Length)
                return ReadOnlySpan<ItemEquipmentBodyPartDef>.Empty;

            int count = Math.Min(equipment.BodyPartCount, Data.ItemEquipmentBodyParts.Length - equipment.FirstBodyPartIndex);
            return new ReadOnlySpan<ItemEquipmentBodyPartDef>(Data.ItemEquipmentBodyParts, equipment.FirstBodyPartIndex, count);
        }

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

        public ReadOnlySpan<ActorAiPackageDef> GetActorAiPackages(ActorDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.Actors.Length)
                return ReadOnlySpan<ActorAiPackageDef>.Empty;

            var actor = Data.Actors[handle.Index];
            if (actor.FirstAiPackageIndex < 0 || actor.AiPackageCount <= 0 || Data.ActorAiPackages == null)
                return ReadOnlySpan<ActorAiPackageDef>.Empty;

            if (actor.FirstAiPackageIndex >= Data.ActorAiPackages.Length)
                return ReadOnlySpan<ActorAiPackageDef>.Empty;

            int count = Math.Min(actor.AiPackageCount, Data.ActorAiPackages.Length - actor.FirstAiPackageIndex);
            return new ReadOnlySpan<ActorAiPackageDef>(Data.ActorAiPackages, actor.FirstAiPackageIndex, count);
        }

        public ReadOnlySpan<ActorTravelDestinationDef> GetActorTravelDestinations(ActorDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.Actors.Length)
                return ReadOnlySpan<ActorTravelDestinationDef>.Empty;

            var actor = Data.Actors[handle.Index];
            if (actor.FirstTravelDestinationIndex < 0 || actor.TravelDestinationCount <= 0 || Data.ActorTravelDestinations == null)
                return ReadOnlySpan<ActorTravelDestinationDef>.Empty;

            if (actor.FirstTravelDestinationIndex >= Data.ActorTravelDestinations.Length)
                return ReadOnlySpan<ActorTravelDestinationDef>.Empty;

            int count = Math.Min(actor.TravelDestinationCount, Data.ActorTravelDestinations.Length - actor.FirstTravelDestinationIndex);
            return new ReadOnlySpan<ActorTravelDestinationDef>(Data.ActorTravelDestinations, actor.FirstTravelDestinationIndex, count);
        }

        public ReadOnlySpan<PathGridPointDef> GetPathGridPoints(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridPointDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstPointIndex < 0 || pathGrid.PointCount <= 0 || Data.PathGridPoints == null)
                return ReadOnlySpan<PathGridPointDef>.Empty;

            if (pathGrid.FirstPointIndex >= Data.PathGridPoints.Length)
                return ReadOnlySpan<PathGridPointDef>.Empty;

            int count = Math.Min(pathGrid.PointCount, Data.PathGridPoints.Length - pathGrid.FirstPointIndex);
            return new ReadOnlySpan<PathGridPointDef>(Data.PathGridPoints, pathGrid.FirstPointIndex, count);
        }

        public ReadOnlySpan<PathGridConnectionDef> GetPathGridConnections(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridConnectionDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstConnectionIndex < 0 || pathGrid.ConnectionCount <= 0 || Data.PathGridConnections == null)
                return ReadOnlySpan<PathGridConnectionDef>.Empty;

            if (pathGrid.FirstConnectionIndex >= Data.PathGridConnections.Length)
                return ReadOnlySpan<PathGridConnectionDef>.Empty;

            int count = Math.Min(pathGrid.ConnectionCount, Data.PathGridConnections.Length - pathGrid.FirstConnectionIndex);
            return new ReadOnlySpan<PathGridConnectionDef>(Data.PathGridConnections, pathGrid.FirstConnectionIndex, count);
        }

        public ReadOnlySpan<PathGridConnectionDef> GetPathGridPointConnections(in PathGridPointDef point)
        {
            if (point.FirstConnectionIndex < 0 || point.ConnectionCount <= 0 || Data.PathGridConnections == null)
                return ReadOnlySpan<PathGridConnectionDef>.Empty;

            if (point.FirstConnectionIndex >= Data.PathGridConnections.Length)
                return ReadOnlySpan<PathGridConnectionDef>.Empty;

            int count = Math.Min(point.ConnectionCount, Data.PathGridConnections.Length - point.FirstConnectionIndex);
            return new ReadOnlySpan<PathGridConnectionDef>(Data.PathGridConnections, point.FirstConnectionIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationNodeDef> GetPathGridNavigationNodes(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridNavigationNodeDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstNavigationNodeIndex < 0 || pathGrid.NavigationNodeCount <= 0 || Data.PathGridNavigationNodes == null)
                return ReadOnlySpan<PathGridNavigationNodeDef>.Empty;

            if (pathGrid.FirstNavigationNodeIndex >= Data.PathGridNavigationNodes.Length)
                return ReadOnlySpan<PathGridNavigationNodeDef>.Empty;

            int count = Math.Min(pathGrid.NavigationNodeCount, Data.PathGridNavigationNodes.Length - pathGrid.FirstNavigationNodeIndex);
            return new ReadOnlySpan<PathGridNavigationNodeDef>(Data.PathGridNavigationNodes, pathGrid.FirstNavigationNodeIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationEdgeDef> GetPathGridNavigationEdges(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridNavigationEdgeDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstNavigationEdgeIndex < 0 || pathGrid.NavigationEdgeCount <= 0 || Data.PathGridNavigationEdges == null)
                return ReadOnlySpan<PathGridNavigationEdgeDef>.Empty;

            if (pathGrid.FirstNavigationEdgeIndex >= Data.PathGridNavigationEdges.Length)
                return ReadOnlySpan<PathGridNavigationEdgeDef>.Empty;

            int count = Math.Min(pathGrid.NavigationEdgeCount, Data.PathGridNavigationEdges.Length - pathGrid.FirstNavigationEdgeIndex);
            return new ReadOnlySpan<PathGridNavigationEdgeDef>(Data.PathGridNavigationEdges, pathGrid.FirstNavigationEdgeIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationEdgeDef> GetPathGridNavigationNodeEdges(in PathGridNavigationNodeDef node)
        {
            if (node.FirstEdgeIndex < 0 || node.EdgeCount <= 0 || Data.PathGridNavigationEdges == null)
                return ReadOnlySpan<PathGridNavigationEdgeDef>.Empty;

            if (node.FirstEdgeIndex >= Data.PathGridNavigationEdges.Length)
                return ReadOnlySpan<PathGridNavigationEdgeDef>.Empty;

            int count = Math.Min(node.EdgeCount, Data.PathGridNavigationEdges.Length - node.FirstEdgeIndex);
            return new ReadOnlySpan<PathGridNavigationEdgeDef>(Data.PathGridNavigationEdges, node.FirstEdgeIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationPortalDef> GetPathGridNavigationPortals(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridNavigationPortalDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstNavigationPortalIndex < 0 || pathGrid.NavigationPortalCount <= 0 || Data.PathGridNavigationPortals == null)
                return ReadOnlySpan<PathGridNavigationPortalDef>.Empty;

            if (pathGrid.FirstNavigationPortalIndex >= Data.PathGridNavigationPortals.Length)
                return ReadOnlySpan<PathGridNavigationPortalDef>.Empty;

            int count = Math.Min(pathGrid.NavigationPortalCount, Data.PathGridNavigationPortals.Length - pathGrid.FirstNavigationPortalIndex);
            return new ReadOnlySpan<PathGridNavigationPortalDef>(Data.PathGridNavigationPortals, pathGrid.FirstNavigationPortalIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationAbstractEdgeDef> GetPathGridNavigationAbstractEdges(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridNavigationAbstractEdgeDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstNavigationAbstractEdgeIndex < 0 || pathGrid.NavigationAbstractEdgeCount <= 0 || Data.PathGridNavigationAbstractEdges == null)
                return ReadOnlySpan<PathGridNavigationAbstractEdgeDef>.Empty;

            if (pathGrid.FirstNavigationAbstractEdgeIndex >= Data.PathGridNavigationAbstractEdges.Length)
                return ReadOnlySpan<PathGridNavigationAbstractEdgeDef>.Empty;

            int count = Math.Min(pathGrid.NavigationAbstractEdgeCount, Data.PathGridNavigationAbstractEdges.Length - pathGrid.FirstNavigationAbstractEdgeIndex);
            return new ReadOnlySpan<PathGridNavigationAbstractEdgeDef>(Data.PathGridNavigationAbstractEdges, pathGrid.FirstNavigationAbstractEdgeIndex, count);
        }

        public ReadOnlySpan<PathGridNavigationNeighborDef> GetPathGridNavigationNeighbors(GenericRecordDefHandle handle)
        {
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= Data.PathGrids.Length)
                return ReadOnlySpan<PathGridNavigationNeighborDef>.Empty;

            var pathGrid = Data.PathGrids[handle.Index];
            if (pathGrid.FirstNavigationNeighborIndex < 0 || pathGrid.NavigationNeighborCount <= 0 || Data.PathGridNavigationNeighbors == null)
                return ReadOnlySpan<PathGridNavigationNeighborDef>.Empty;

            if (pathGrid.FirstNavigationNeighborIndex >= Data.PathGridNavigationNeighbors.Length)
                return ReadOnlySpan<PathGridNavigationNeighborDef>.Empty;

            int count = Math.Min(pathGrid.NavigationNeighborCount, Data.PathGridNavigationNeighbors.Length - pathGrid.FirstNavigationNeighborIndex);
            return new ReadOnlySpan<PathGridNavigationNeighborDef>(Data.PathGridNavigationNeighbors, pathGrid.FirstNavigationNeighborIndex, count);
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

        static Dictionary<long, GenericRecordDefHandle> BuildPathGridExteriorIndex(PathGridDef[] defs)
        {
            var map = new Dictionary<long, GenericRecordDefHandle>(defs?.Length ?? 0);
            if (defs == null)
                return map;

            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].IsExterior != 0)
                    map[PackExteriorPathGridKey(defs[i].GridX, defs[i].GridY)] = GenericRecordDefHandle.FromIndex(i);
            }

            return map;
        }

        static Dictionary<ulong, GenericRecordDefHandle> BuildInteriorPathGridHashIndex(PathGridDef[] defs)
        {
            var map = new Dictionary<ulong, GenericRecordDefHandle>(defs?.Length ?? 0);
            if (defs == null)
                return map;

            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].IsExterior != 0)
                    continue;

                ulong hash = InteriorCellIdHash.Hash(defs[i].Id);
                if (hash != 0UL && !map.ContainsKey(hash))
                    map[hash] = GenericRecordDefHandle.FromIndex(i);
            }

            return map;
        }

        static int[] BuildItemEquipmentIndex(GameplayContentData data)
        {
            int itemCount = data?.Items?.Length ?? 0;
            var result = new int[itemCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = -1;

            var equipment = data?.ItemEquipment;
            if (equipment == null)
                return result;

            for (int i = 0; i < equipment.Length; i++)
            {
                int itemIndex = equipment[i].Item.Index;
                if ((uint)itemIndex < (uint)result.Length)
                    result[itemIndex] = i;
            }

            return result;
        }

        static long PackExteriorPathGridKey(int gridX, int gridY)
            => ((long)gridX << 32) ^ (uint)gridY;

    }
}
