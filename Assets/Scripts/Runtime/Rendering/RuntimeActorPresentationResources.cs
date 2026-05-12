using System;
using Unity.Entities;
using VVardenfell.Runtime.Animation;

namespace VVardenfell.Runtime.Rendering
{
    public sealed class RuntimeActorPresentationResources : IComponentData, IDisposable
    {
        public ActorGpuAnimationResources GpuAnimation;
        public ActorEntitiesGraphicsRenderResources EntitiesGraphicsRenderer;
        public float ShadowCasterDistance = 64f;
        public float ShadowCasterPadding = 8f;
        public int MaxActorShadowCasters = 128;

        public void Dispose()
        {
            GpuAnimation?.Dispose();
            GpuAnimation = null;
            EntitiesGraphicsRenderer?.Dispose();
            EntitiesGraphicsRenderer = null;
            ShadowCasterDistance = 64f;
            ShadowCasterPadding = 8f;
            MaxActorShadowCasters = 128;
        }

        public void Dispose(EntityManager entityManager)
        {
            GpuAnimation?.Dispose();
            GpuAnimation = null;
            EntitiesGraphicsRenderer?.Dispose(entityManager);
            EntitiesGraphicsRenderer = null;
            ShadowCasterDistance = 64f;
            ShadowCasterPadding = 8f;
            MaxActorShadowCasters = 128;
        }

        public static RuntimeActorPresentationResources Require(EntityManager entityManager)
        {
            var query = RuntimeActorPresentationResourcesQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][ActorPresentation] RuntimeActorPresentationResources singleton is missing.");

            var resources = entityManager.GetComponentData<RuntimeActorPresentationResources>(query.GetSingletonEntity());
            if (resources == null)
                throw new InvalidOperationException("[VVardenfell][ActorPresentation] RuntimeActorPresentationResources is null.");
            return resources;
        }

        public static bool TryGetDefault(out RuntimeActorPresentationResources resources)
        {
            resources = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            var query = RuntimeActorPresentationResourcesQueryCache.Get(world.EntityManager);
            if (query.CalculateEntityCount() != 1)
                return false;

            resources = world.EntityManager.GetComponentData<RuntimeActorPresentationResources>(query.GetSingletonEntity());
            return resources != null;
        }

        static class RuntimeActorPresentationResourcesQueryCache
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
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeActorPresentationResources>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
