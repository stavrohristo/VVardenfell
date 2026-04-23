using System;
using System.Collections.Generic;

namespace VVardenfell.Core.Cache
{
    public static class GameplayContentReferenceIndex
    {
        public static Dictionary<string, ContentReference> BuildPlaceableIndex(GameplayContentData data)
        {
            var map = new Dictionary<string, ContentReference>(StringComparer.OrdinalIgnoreCase);
            if (data == null)
                return map;

            for (int i = 0; i < data.Actors.Length; i++)
                Add(map, data.Actors[i].Id, ContentReferenceKind.Actor, ActorDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Activators.Length; i++)
                Add(map, data.Activators[i].Id, ContentReferenceKind.Activator, ActivatorDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Doors.Length; i++)
                Add(map, data.Doors[i].Id, ContentReferenceKind.Door, DoorDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Containers.Length; i++)
                Add(map, data.Containers[i].Id, ContentReferenceKind.Container, ContainerDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Items.Length; i++)
                Add(map, data.Items[i].Id, ContentReferenceKind.Item, ItemDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Lights.Length; i++)
                Add(map, data.Lights[i].Id, ContentReferenceKind.Light, LightDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.Statics.Length; i++)
                Add(map, data.Statics[i].Id, ContentReferenceKind.Static, GenericRecordDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.CreatureLeveledLists.Length; i++)
                Add(map, data.CreatureLeveledLists[i].Id, ContentReferenceKind.LeveledCreature, CreatureLeveledListDefHandle.FromIndex(i).Value);
            for (int i = 0; i < data.ItemLeveledLists.Length; i++)
                Add(map, data.ItemLeveledLists[i].Id, ContentReferenceKind.LeveledItem, ItemLeveledListDefHandle.FromIndex(i).Value);

            return map;
        }

        public static bool TryResolvePlaceable(IReadOnlyDictionary<string, ContentReference> index, string id, out ContentReference contentRef)
        {
            contentRef = default;
            return index != null && !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id, out contentRef);
        }

        public static bool IsValid(GameplayContentData data, ContentReference contentRef)
        {
            if (data == null || !contentRef.IsValid)
                return false;

            return contentRef.Kind switch
            {
                ContentReferenceKind.Actor => contentRef.HandleValue <= data.Actors.Length,
                ContentReferenceKind.Activator => contentRef.HandleValue <= data.Activators.Length,
                ContentReferenceKind.Door => contentRef.HandleValue <= data.Doors.Length,
                ContentReferenceKind.Container => contentRef.HandleValue <= data.Containers.Length,
                ContentReferenceKind.Item => contentRef.HandleValue <= data.Items.Length,
                ContentReferenceKind.Light => contentRef.HandleValue <= data.Lights.Length,
                ContentReferenceKind.Static => contentRef.HandleValue <= data.Statics.Length,
                ContentReferenceKind.LeveledCreature => contentRef.HandleValue <= data.CreatureLeveledLists.Length,
                ContentReferenceKind.LeveledItem => contentRef.HandleValue <= data.ItemLeveledLists.Length,
                _ => false,
            };
        }

        static void Add(Dictionary<string, ContentReference> map, string id, ContentReferenceKind kind, int handleValue)
        {
            if (string.IsNullOrWhiteSpace(id) || handleValue <= 0)
                return;

            map[id] = new ContentReference
            {
                Kind = kind,
                HandleValue = handleValue,
            };
        }
    }
}
