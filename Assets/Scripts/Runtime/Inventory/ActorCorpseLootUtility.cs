using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    static class ActorCorpseLootUtility
    {
        public static bool IsDeadLootableActor(EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.HasComponent<PassiveActorPresence>(actor))
            {
                return false;
            }

            if (!entityManager.HasComponent<ActorDead>(actor))
                return false;

            if (!entityManager.IsComponentEnabled<ActorDead>(actor))
                return false;

            if (!entityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][Corpse] Actor ref={PlacedRefId(entityManager, actor)} is dead but has no ActorVitalSet.");

            var vitals = entityManager.GetComponentData<ActorVitalSet>(actor);
            if (vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][Corpse] Actor ref={PlacedRefId(entityManager, actor)} is marked dead but has positive health.");

            return true;
        }

        public static void RequireDeadLootableActor(EntityManager entityManager, Entity actor, uint placedRefId)
        {
            if (!IsDeadLootableActor(entityManager, actor))
                throw new InvalidOperationException($"[VVardenfell][Corpse] Actor ref={placedRefId} is not a dead lootable actor.");
            if (!entityManager.HasComponent<PlacedRefIdentity>(actor))
                throw new InvalidOperationException($"[VVardenfell][Corpse] Actor ref={placedRefId} has no PlacedRefIdentity.");
            if (!entityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][Corpse] Actor ref={placedRefId} has no ActorSpawnSource.");
        }

        public static string ResolveTitle(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null || !entityManager.Exists(actor))
                return "Corpse";

            var source = entityManager.HasComponent<ActorSpawnSource>(actor)
                ? entityManager.GetComponentData<ActorSpawnSource>(actor)
                : default;
            return RuntimeContentMetadataResolver.ResolveActorDisplayName(ref contentBlob, source.Definition, "Corpse");
        }

        public static void EnsureSessionInitialized(
            EntityManager entityManager,
            DynamicBuffer<ContainerSessionHeader> headers,
            DynamicBuffer<ContainerSessionItem> items,
            Entity actor,
            uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Corpse] Cannot open corpse loot without a placed ref id.");

            if (ContainerLootUtility.FindHeaderIndex(headers, placedRefId) >= 0)
                return;

            headers.Add(new ContainerSessionHeader
            {
                PlacedRefId = placedRefId,
                Definition = default,
            });

            if (entityManager.HasBuffer<ActorInventoryItem>(actor))
            {
                var inventory = entityManager.GetBuffer<ActorInventoryItem>(actor, true);
                for (int i = 0; i < inventory.Length; i++)
                {
                    var entry = inventory[i];
                    if (entry.Count <= 0 || !entry.Content.IsValid)
                        continue;

                    ContainerLootUtility.AddOrIncrementContainerStack(
                        items,
                        placedRefId,
                        entry.Content,
                        entry.SoulId,
                        entry.SoulActorHandleValue,
                        entry.Count);
                }
            }

            ScriptVisibleSaveStateUtility.ApplyContainerOverlay(entityManager, placedRefId, items);
        }

        static uint PlacedRefId(EntityManager entityManager, Entity entity)
            => entity != Entity.Null && entityManager.Exists(entity) && entityManager.HasComponent<PlacedRefIdentity>(entity)
                ? entityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
