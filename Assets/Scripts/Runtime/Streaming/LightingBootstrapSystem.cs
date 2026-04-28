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

            if (!SystemAPI.HasSingleton<MorrowindDayCycleState>())
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.DayCycleState");
                EntityManager.AddComponentData(entity, CreateDefaultDayCycle());
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
                    DirectionalLightDirection = math.normalize(new float3(0f, 1f, -0.25f)),
                    DirectionalIntensity = 0.85f,
                    SunPercent = 1f,
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
                DirectionalLightDirection = math.normalize(new float3(0f, 1f, -0.25f)),
                DirectionalIntensity = 1.15f,
                SunPercent = 1f,
                FogColorRgb = new float3(0.58f, 0.66f, 0.74f),
                FogDensity = 0.18f,
                FogNearMeters = 320f,
                FogFarMeters = 1400f,
                RegionHandleValue = 0,
                IsInterior = 0,
            };
        }

        public static MorrowindDayCycleState CreateDefaultDayCycle()
        {
            return new MorrowindDayCycleState
            {
                GameHour = 12f,
                DaysPassed = 1,
                GameHoursPerSecond = 0f,

                SunriseTime = 6f,
                SunsetTime = 18f,
                SunriseDuration = 2f,
                SunsetDuration = 2f,

                SunPreSunriseTime = 0f,
                SunPostSunriseTime = 0f,
                SunPreSunsetTime = 1f,
                SunPostSunsetTime = 1.25f,

                AmbientPreSunriseTime = 0.5f,
                AmbientPostSunriseTime = 2f,
                AmbientPreSunsetTime = 1f,
                AmbientPostSunsetTime = 1.25f,

                FogPreSunriseTime = 0.5f,
                FogPostSunriseTime = 1f,
                FogPreSunsetTime = 2f,
                FogPostSunsetTime = 1f,

                AmbientSunriseColorRgb = Rgb(47, 66, 96),
                AmbientDayColorRgb = Rgb(137, 140, 160),
                AmbientSunsetColorRgb = Rgb(68, 75, 96),
                AmbientNightColorRgb = Rgb(32, 35, 42),

                SunSunriseColorRgb = Rgb(242, 159, 119),
                SunDayColorRgb = Rgb(255, 252, 238),
                SunSunsetColorRgb = Rgb(255, 114, 79),
                SunNightColorRgb = Rgb(59, 97, 176),

                FogSunriseColorRgb = Rgb(255, 189, 157),
                FogDayColorRgb = Rgb(206, 227, 255),
                FogSunsetColorRgb = Rgb(255, 189, 157),
                FogNightColorRgb = Rgb(9, 10, 11),

                ExteriorSunIntensityScale = 1.15f,
                ExteriorDayFogDensity = 0.18f,
                ExteriorNightFogDensity = 0.36f,
            };
        }

        static float3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    }

}
