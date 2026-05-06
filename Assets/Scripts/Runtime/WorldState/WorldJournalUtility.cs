using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.WorldState
{
    static class WorldJournalUtility
    {
        public static bool TryGetJournalEntity(EntityManager entityManager, out Entity journalEntity)
        {
            EntityQuery query = JournalQueryCache.Get(entityManager);
            return WorldStateEntityQueryUtility.TryGetExactlyOne(
                query,
                out journalEntity,
                out _,
                "world journal",
                null);
        }

        public static uint AppendLooseItemRemoved(EntityManager entityManager, uint placedRefId, ContentReference content)
        {
            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.LooseItemRemoved,
                PlacedRefId = placedRefId,
                Content = content,
                DeltaCount = 1,
            });
        }

        public static uint AppendContainerDelta(EntityManager entityManager, uint placedRefId, ContentReference content, int deltaCount)
        {
            if (placedRefId == 0u || !content.IsValid || deltaCount == 0)
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.ContainerDelta,
                PlacedRefId = placedRefId,
                Content = content,
                DeltaCount = deltaCount,
            });
        }

        public static uint AppendRuntimeSpawn(EntityManager entityManager, in RuntimeSpawnedRef spawnedRef)
        {
            if (spawnedRef.RuntimeRefId == 0u || !spawnedRef.Content.IsValid)
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.RuntimeSpawned,
                RuntimeRefId = spawnedRef.RuntimeRefId,
                Content = spawnedRef.Content,
                Position = spawnedRef.Position,
                Rotation = spawnedRef.Rotation,
                Scale = spawnedRef.Scale,
                ExteriorCell = spawnedRef.ExteriorCell,
                InteriorCellId = spawnedRef.InteriorCellId,
                InteriorCellHash = spawnedRef.InteriorCellHash,
                IsInterior = spawnedRef.IsInterior,
                PersistencePolicy = spawnedRef.PersistencePolicy,
            });
        }

        public static uint AppendRuntimeDestroyed(EntityManager entityManager, uint runtimeRefId)
        {
            if (!RuntimeSpawnRegistryUtility.IsRuntimeRefId(runtimeRefId))
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.RuntimeDestroyed,
                RuntimeRefId = runtimeRefId,
            });
        }

        public static void RebuildPickedItemProjection(DynamicBuffer<WorldJournalEntry> journal, DynamicBuffer<PickedItemRecord> pickedItems)
        {
            pickedItems.Clear();
            using var seen = new NativeHashSet<uint>(journal.Length, Allocator.Temp);
            for (int i = 0; i < journal.Length; i++)
            {
                var entry = journal[i];
                if (entry.Kind != (byte)WorldJournalEntryKind.LooseItemRemoved
                    || entry.PlacedRefId == 0u
                    || entry.Content.Kind != ContentReferenceKind.Item
                    || !seen.Add(entry.PlacedRefId))
                {
                    continue;
                }

                pickedItems.Add(new PickedItemRecord
                {
                    PlacedRefId = entry.PlacedRefId,
                    Definition = new ItemDefHandle { Value = entry.Content.HandleValue },
                });
            }
        }

        public static void ApplyContainerDeltas(
            uint placedRefId,
            DynamicBuffer<WorldJournalEntry> journal,
            DynamicBuffer<ContainerSessionItem> items)
        {
            if (placedRefId == 0u)
                return;

            for (int i = 0; i < journal.Length; i++)
            {
                var entry = journal[i];
                if (entry.Kind != (byte)WorldJournalEntryKind.ContainerDelta || entry.PlacedRefId != placedRefId)
                    continue;

                ContainerLootUtility.ApplyContainerDelta(items, placedRefId, entry.Content, entry.DeltaCount);
            }
        }

        static uint AppendEntry(EntityManager entityManager, in WorldJournalEntry template)
        {
            if (!TryGetJournalEntity(entityManager, out Entity journalEntity))
                return 0u;

            var state = entityManager.GetComponentData<WorldJournalState>(journalEntity);
            uint sequence = state.NextSequence + 1u;
            state.NextSequence = sequence;
            entityManager.SetComponentData(journalEntity, state);

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var entry = template;
            entry.Sequence = sequence;
            journal.Add(entry);
            return sequence;
        }

        static class JournalQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<WorldJournalState>(),
                    ComponentType.ReadWrite<WorldJournalEntry>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
