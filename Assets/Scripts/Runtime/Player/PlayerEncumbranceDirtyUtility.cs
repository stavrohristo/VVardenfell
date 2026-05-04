using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Player
{
    static class PlayerEncumbranceDirtyUtility
    {
        public static void EnsureMarker(EntityManager entityManager, Entity player, bool enabled)
        {
            if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<PlayerTag>(player))
                throw new InvalidOperationException("[VVardenfell][Player] Player encumbrance dirty marker requires a live player entity.");

            if (!entityManager.HasComponent<PlayerEncumbranceDirty>(player))
                entityManager.AddComponent<PlayerEncumbranceDirty>(player);
            entityManager.SetComponentEnabled<PlayerEncumbranceDirty>(player, enabled);
        }

        public static void MarkPlayerDirty(EntityManager entityManager)
            => entityManager.SetComponentEnabled<PlayerEncumbranceDirty>(RequirePlayerEntity(entityManager), true);

        public static void MarkIfPlayer(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<PlayerTag>(entity))
                return;

            if (!entityManager.HasComponent<PlayerEncumbranceDirty>(entity))
                throw new InvalidOperationException("[VVardenfell][Player] Player entity is missing PlayerEncumbranceDirty marker.");

            entityManager.SetComponentEnabled<PlayerEncumbranceDirty>(entity, true);
        }

        static Entity RequirePlayerEntity(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<PlayerEncumbranceDirty>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            int count = query.CalculateEntityCount();
            if (count != 1)
                throw new InvalidOperationException($"[VVardenfell][Player] Expected exactly one player with PlayerEncumbranceDirty marker; found {count}.");

            return query.GetSingletonEntity();
        }
    }
}
