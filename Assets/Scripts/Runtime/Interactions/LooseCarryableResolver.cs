using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.Interactions
{
    static class LooseCarryableResolver
    {
        public static bool TryResolveContent(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity logicalEntity,
            out ContentReference content)
        {
            return TryResolveContent(ref contentBlob, entityManager, logicalEntity, out content, out _);
        }

        public static bool TryResolveContent(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity logicalEntity,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (!entityManager.Exists(logicalEntity))
                return false;

            if (entityManager.HasComponent<ItemPickupAuthoring>(logicalEntity))
            {
                var authoring = entityManager.GetComponentData<ItemPickupAuthoring>(logicalEntity);
                content = ContainerLootUtility.ToContentReference(authoring.Definition);
                return content.IsValid;
            }

            if (entityManager.HasComponent<LightSourceAuthoring>(logicalEntity))
            {
                if (!entityManager.HasComponent<LightInstanceFlags>(logicalEntity))
                    return false;

                var flags = entityManager.GetComponentData<LightInstanceFlags>(logicalEntity);
                if (flags.Carry == 0)
                    return false;

                var authoring = entityManager.GetComponentData<LightSourceAuthoring>(logicalEntity);
                content = ContainerLootUtility.ToContentReference(authoring.Definition);
                return content.IsValid;
            }

            if (entityManager.HasComponent<LeveledItemAuthoring>(logicalEntity))
            {
                if (!entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                    return false;

                uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
                if (placedRefId == 0u)
                    return false;

                var authoring = entityManager.GetComponentData<LeveledItemAuthoring>(logicalEntity);
                int playerLevel = MorrowindLeveledItemResolverUtility.ResolvePlayerLevel(entityManager);
                return MorrowindLeveledItemResolverUtility.TryResolveOne(
                    ref contentBlob,
                    authoring.Definition,
                    playerLevel,
                    MorrowindLeveledItemResolverUtility.BuildResolutionSeed(placedRefId, 0, 0),
                    out content)
                    && content.IsValid;
            }

            return false;
        }

        public static bool TryResolveMetadata(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity logicalEntity,
            out CarryableMetadata metadata)
        {
            metadata = default;
            return TryResolveContent(ref contentBlob, entityManager, logicalEntity, out ContentReference content)
                && RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, content, out metadata);
        }

        public static string ResolveDisplayName(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity logicalEntity,
            string fallback = "item")
        {
            return TryResolveMetadata(ref contentBlob, entityManager, logicalEntity, out CarryableMetadata metadata)
                ? metadata.DisplayName
                : fallback;
        }
    }
}
