using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class LightingBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<ActiveEnvironmentState>())
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.LightingState");
                EntityManager.AddComponentData(entity, CreateFallbackEnvironment(isInterior: false));
            }
        }

        protected override void OnUpdate()
        {
        }

        internal static ActiveEnvironmentState CreateFallbackEnvironment(bool isInterior)
        {
            if (isInterior)
            {
                return new ActiveEnvironmentState
                {
                    AmbientColorRgb = new float3(0.22f, 0.22f, 0.24f),
                    DirectionalColorRgb = new float3(0.48f, 0.45f, 0.38f),
                    FogColorRgb = new float3(0.05f, 0.05f, 0.06f),
                    FogDensity = 0.22f,
                    FogNearMeters = 22f,
                    FogFarMeters = 68f,
                    RegionHandleValue = 0,
                    IsInterior = 1,
                };
            }

            return new ActiveEnvironmentState
            {
                AmbientColorRgb = new float3(0.42f, 0.44f, 0.46f),
                DirectionalColorRgb = new float3(0.95f, 0.90f, 0.82f),
                FogColorRgb = new float3(0.58f, 0.66f, 0.74f),
                FogDensity = 0.18f,
                FogNearMeters = 320f,
                FogFarMeters = 1400f,
                RegionHandleValue = 0,
                IsInterior = 0,
            };
        }
    }
}
