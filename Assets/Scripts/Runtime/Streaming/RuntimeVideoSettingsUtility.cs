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
            using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<RuntimeVideoSettings>());
            if (query.IsEmptyIgnoreFilter)
            {
                Entity entity = em.CreateEntity(typeof(RuntimeVideoSettings));
                em.SetName(entity, "VVardenfell.RuntimeVideoSettings");
                em.SetComponentData(entity, settings);
                return;
            }

            em.SetComponentData(query.GetSingletonEntity(), settings);
        }
    }
}
