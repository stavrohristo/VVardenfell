using Unity.Entities;
using VVardenfell.Core.Config;

namespace VVardenfell.Runtime.Streaming
{
    public static class RuntimeVideoSettingsUtility
    {
        public static RuntimeVideoSettings FromConfig(MorrowindConfig config)
        {
            return new RuntimeVideoSettings
            {
                FogDistanceScale = RuntimeVideoSettings.NormalizeFogDistanceScale(
                    config != null ? config.FogDistanceScale : RuntimeVideoSettings.DefaultFogDistanceScale),
            };
        }

        public static void ApplyFogDistanceScale(float scale)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            var settings = new RuntimeVideoSettings
            {
                FogDistanceScale = RuntimeVideoSettings.NormalizeFogDistanceScale(scale),
            };
            EntityManager em = world.EntityManager;
            EntityQuery query = RuntimeVideoSettingsQueryCache.Get(em);
            if (query.IsEmptyIgnoreFilter)
            {
                Entity entity = em.CreateEntity(typeof(RuntimeVideoSettings));
                em.SetName(entity, "VVardenfell.RuntimeVideoSettings");
                em.SetComponentData(entity, settings);
                return;
            }

            em.SetComponentData(query.GetSingletonEntity(), settings);
        }

        static class RuntimeVideoSettingsQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                if (s_QueryCreated)
                    s_Query.Dispose();

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<RuntimeVideoSettings>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
