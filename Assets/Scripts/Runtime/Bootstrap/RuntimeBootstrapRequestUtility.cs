using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class RuntimeBootstrapRequestUtility
    {
        public static void PublishAll(EntityManager entityManager)
        {
            Publish<AudioBootstrapRequest>(entityManager, "VVardenfell.AudioBootstrapRequest");
            Publish<LightingBootstrapRequest>(entityManager, "VVardenfell.LightingBootstrapRequest");
            Publish<MorrowindTimeBootstrapRequest>(entityManager, "VVardenfell.TimeBootstrapRequest");
            Publish<MorrowindMovementSettingsBootstrapRequest>(entityManager, "VVardenfell.MovementSettingsBootstrapRequest");
            Publish<PathGridTraversalSettingsBootstrapRequest>(entityManager, "VVardenfell.PathGridTraversalSettingsBootstrapRequest");
            Publish<ActorAnimationRuntimeSettingsBootstrapRequest>(entityManager, "VVardenfell.ActorAnimationRuntimeSettingsBootstrapRequest");
            Publish<ActorAnimationBlobCatalogBootstrapRequest>(entityManager, "VVardenfell.ActorAnimationBlobCatalogBootstrapRequest");
            Publish<ObjectAnimationBlobCatalogBootstrapRequest>(entityManager, "VVardenfell.ObjectAnimationBlobCatalogBootstrapRequest");
            Publish<InteractionRuntimeBootstrapRequest>(entityManager, "VVardenfell.InteractionRuntimeBootstrapRequest");
            Publish<RuntimeShellBootstrapRequest>(entityManager, "VVardenfell.RuntimeShellBootstrapRequest");
            Publish<BookRuntimeBootstrapRequest>(entityManager, "VVardenfell.BookRuntimeBootstrapRequest");
            Publish<ContainerLootBootstrapRequest>(entityManager, "VVardenfell.ContainerLootBootstrapRequest");
            Publish<RuntimeSpawnBootstrapRequest>(entityManager, "VVardenfell.RuntimeSpawnBootstrapRequest");
            Publish<WorldJournalBootstrapRequest>(entityManager, "VVardenfell.WorldJournalBootstrapRequest");
            Publish<DeferredPhysicsQueryBootstrapRequest>(entityManager, "VVardenfell.DeferredPhysicsQueryBootstrapRequest");
            Publish<RuntimePhysicsMutationBootstrapRequest>(entityManager, "VVardenfell.RuntimePhysicsMutationBootstrapRequest");
            Publish<MorrowindOwnedPhysicsBootstrapRequest>(entityManager, "VVardenfell.OwnedPhysicsBootstrapRequest");
            Publish<RuntimePhysicsLifetimeBootstrapRequest>(entityManager, "VVardenfell.RuntimePhysicsLifetimeBootstrapRequest");
            Publish<MorrowindCombatRuntimeBootstrapRequest>(entityManager, "VVardenfell.CombatRuntimeBootstrapRequest");
            Publish<MorrowindScriptRuntimeBootstrapRequest>(entityManager, "VVardenfell.ScriptRuntimeBootstrapRequest");
        }

        public static void Publish<T>(EntityManager entityManager, string entityName)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = entityManager.CreateEntity();
            if (!string.IsNullOrWhiteSpace(entityName))
                entityManager.SetName(entity, entityName);
            entityManager.AddComponent<T>(entity);
        }

        public static void Consume<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }
    }
}
