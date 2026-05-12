using System;
using Unity.Entities;

namespace VVardenfell.Runtime.Vfx
{
    public sealed class RuntimeVfxPresentationResources : IComponentData, IDisposable
    {
        public MorrowindVfxResources Resources;

        public void Dispose()
        {
            Resources?.Dispose();
            Resources = null;
        }

        public static RuntimeVfxPresentationResources Require(EntityManager entityManager)
        {
            var query = RuntimeVfxPresentationResourcesQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][VFX] RuntimeVfxPresentationResources singleton is missing.");

            var resources = entityManager.GetComponentData<RuntimeVfxPresentationResources>(query.GetSingletonEntity());
            if (resources == null)
                throw new InvalidOperationException("[VVardenfell][VFX] RuntimeVfxPresentationResources is null.");
            return resources;
        }

        public static bool TryGetDefault(out RuntimeVfxPresentationResources resources)
        {
            resources = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            var query = RuntimeVfxPresentationResourcesQueryCache.Get(world.EntityManager);
            if (query.CalculateEntityCount() != 1)
                return false;

            resources = world.EntityManager.GetComponentData<RuntimeVfxPresentationResources>(query.GetSingletonEntity());
            return resources != null;
        }

        static class RuntimeVfxPresentationResourcesQueryCache
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
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeVfxPresentationResources>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
