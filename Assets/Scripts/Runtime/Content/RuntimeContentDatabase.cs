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
        readonly Dictionary<string, SoundDefHandle> _soundsById;
        readonly Dictionary<string, DialogueDefHandle> _dialoguesById;
        readonly Dictionary<string, SpellDefHandle> _spellsById;
        readonly Dictionary<string, EnchantmentDefHandle> _enchantmentsById;
        readonly Dictionary<int, MagicEffectDefHandle> _magicEffectsByIndex;
        readonly Dictionary<string, RegionDefHandle> _regionsById;
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
        public int SoundCount => Data.Sounds.Length;
        public int DialogueCount => Data.Dialogues.Length;
        public int DialogueInfoCount => Data.DialogueInfos.Length;
        public int SpellCount => Data.Spells.Length;
        public int EnchantmentCount => Data.Enchantments.Length;
        public int MagicEffectCount => Data.MagicEffects.Length;
        public int RegionCount => Data.Regions.Length;
        public int MusicTrackCount => Data.MusicTracks.Length;

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
            _soundsById = BuildIndex(data.Sounds, def => def.Id, SoundDefHandle.FromIndex);
            _dialoguesById = BuildIndex(data.Dialogues, def => def.Id, DialogueDefHandle.FromIndex);
            _spellsById = BuildIndex(data.Spells, def => def.Id, SpellDefHandle.FromIndex);
            _enchantmentsById = BuildIndex(data.Enchantments, def => def.Id, EnchantmentDefHandle.FromIndex);
            _magicEffectsByIndex = BuildMagicEffectIndex(data.MagicEffects);
            _regionsById = BuildIndex(data.Regions, def => def.Id, RegionDefHandle.FromIndex);
            _placeablesById = BuildPlaceableIndex(data);
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
        public bool TryGetSoundHandle(string id, out SoundDefHandle handle) => _soundsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetDialogueHandle(string id, out DialogueDefHandle handle) => _dialoguesById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetSpellHandle(string id, out SpellDefHandle handle) => _spellsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetEnchantmentHandle(string id, out EnchantmentDefHandle handle) => _enchantmentsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryGetMagicEffectHandle(int index, out MagicEffectDefHandle handle) => _magicEffectsByIndex.TryGetValue(index, out handle);
        public bool TryGetRegionHandle(string id, out RegionDefHandle handle) => _regionsById.TryGetValue(id ?? string.Empty, out handle);
        public bool TryResolvePlaceable(string id, out ContentReference contentRef) => _placeablesById.TryGetValue(id ?? string.Empty, out contentRef);
        public bool IsValid(ContentReference contentRef)
        {
            if (!contentRef.IsValid)
                return false;

            return contentRef.Kind switch
            {
                ContentReferenceKind.Actor => contentRef.HandleValue <= Data.Actors.Length,
                ContentReferenceKind.Activator => contentRef.HandleValue <= Data.Activators.Length,
                ContentReferenceKind.Door => contentRef.HandleValue <= Data.Doors.Length,
                ContentReferenceKind.Container => contentRef.HandleValue <= Data.Containers.Length,
                ContentReferenceKind.Item => contentRef.HandleValue <= Data.Items.Length,
                ContentReferenceKind.Light => contentRef.HandleValue <= Data.Lights.Length,
                _ => false,
            };
        }

        public ref readonly ActorDef Get(ActorDefHandle handle) => ref Data.Actors[handle.Index];
        public ref readonly BaseDef Get(ActivatorDefHandle handle) => ref Data.Activators[handle.Index];
        public ref readonly BaseDef Get(DoorDefHandle handle) => ref Data.Doors[handle.Index];
        public ref readonly BaseDef Get(ContainerDefHandle handle) => ref Data.Containers[handle.Index];
        public ref readonly BaseDef Get(ItemDefHandle handle) => ref Data.Items[handle.Index];
        public ref readonly LightDef Get(LightDefHandle handle) => ref Data.Lights[handle.Index];
        public ref readonly SoundDef Get(SoundDefHandle handle) => ref Data.Sounds[handle.Index];
        public ref readonly DialogueDef Get(DialogueDefHandle handle) => ref Data.Dialogues[handle.Index];
        public ref readonly SpellDef Get(SpellDefHandle handle) => ref Data.Spells[handle.Index];
        public ref readonly EnchantmentDef Get(EnchantmentDefHandle handle) => ref Data.Enchantments[handle.Index];
        public ref readonly MagicEffectDef Get(MagicEffectDefHandle handle) => ref Data.MagicEffects[handle.Index];
        public ref readonly RegionDef Get(RegionDefHandle handle) => ref Data.Regions[handle.Index];

        public ReadOnlySpan<DialogueInfoDef> GetDialogueInfos(DialogueDefHandle handle)
        {
            ref readonly var dialogue = ref Get(handle);
            if (dialogue.FirstInfoIndex < 0 || dialogue.InfoCount <= 0)
                return ReadOnlySpan<DialogueInfoDef>.Empty;
            return new ReadOnlySpan<DialogueInfoDef>(Data.DialogueInfos, dialogue.FirstInfoIndex, dialogue.InfoCount);
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

        static Dictionary<string, ContentReference> BuildPlaceableIndex(GameplayContentData data)
        {
            var map = new Dictionary<string, ContentReference>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < data.Actors.Length; i++)
            {
                var handle = ActorDefHandle.FromIndex(i);
                Add(map, data.Actors[i].Id, ContentReferenceKind.Actor, handle.Value);
            }
            for (int i = 0; i < data.Activators.Length; i++)
            {
                var handle = ActivatorDefHandle.FromIndex(i);
                Add(map, data.Activators[i].Id, ContentReferenceKind.Activator, handle.Value);
            }
            for (int i = 0; i < data.Doors.Length; i++)
            {
                var handle = DoorDefHandle.FromIndex(i);
                Add(map, data.Doors[i].Id, ContentReferenceKind.Door, handle.Value);
            }
            for (int i = 0; i < data.Containers.Length; i++)
            {
                var handle = ContainerDefHandle.FromIndex(i);
                Add(map, data.Containers[i].Id, ContentReferenceKind.Container, handle.Value);
            }
            for (int i = 0; i < data.Items.Length; i++)
            {
                var handle = ItemDefHandle.FromIndex(i);
                Add(map, data.Items[i].Id, ContentReferenceKind.Item, handle.Value);
            }
            for (int i = 0; i < data.Lights.Length; i++)
            {
                var handle = LightDefHandle.FromIndex(i);
                Add(map, data.Lights[i].Id, ContentReferenceKind.Light, handle.Value);
            }

            return map;
        }

        static void Add(Dictionary<string, ContentReference> map, string id, ContentReferenceKind kind, int handleValue)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            map[id] = new ContentReference
            {
                Kind = kind,
                HandleValue = handleValue,
            };
        }
    }
}
