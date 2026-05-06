using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Config;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct LightingBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<LightingBootstrapRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Lighting bootstrap requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            if (!SystemAPI.HasSingleton<ActiveEnvironmentState>())
            {
                var entity = systemState.EntityManager.CreateEntity();
                systemState.EntityManager.SetName(entity, "VVardenfell.LightingState");
                systemState.EntityManager.AddComponentData(entity, CreateFallbackEnvironment(isInterior: false));
            }

            if (!SystemAPI.HasSingleton<MorrowindDayCycleState>())
            {
                var entity = systemState.EntityManager.CreateEntity();
                systemState.EntityManager.SetName(entity, "VVardenfell.DayCycleState");
                systemState.EntityManager.AddComponentData(entity, CreateDefaultDayCycle(ref content));
            }

            if (!SystemAPI.HasSingleton<ActiveSkyWeatherState>())
            {
                var entity = systemState.EntityManager.CreateEntity();
                systemState.EntityManager.SetName(entity, "VVardenfell.SkyWeatherState");
                systemState.EntityManager.AddComponentData(entity, CreateFallbackSkyWeather());
            }

            if (!SystemAPI.HasSingleton<RuntimeVideoSettings>())
            {
                var entity = systemState.EntityManager.CreateEntity();
                systemState.EntityManager.SetName(entity, "VVardenfell.RuntimeVideoSettings");
                systemState.EntityManager.AddComponentData(entity, ResolveRuntimeVideoSettings());
            }

            RuntimeBootstrapRequestUtility.Consume<LightingBootstrapRequest>(systemState.EntityManager);
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
            var contentBlob = RuntimeContentBlobReferenceUtility.RequireBlob("Lighting bootstrap");
            ref RuntimeContentBlob content = ref contentBlob.Value;
            return CreateDefaultDayCycle(ref content);
        }

        public static MorrowindDayCycleState CreateDefaultDayCycle(ref RuntimeContentBlob content)
        {
            var settings = content.WeatherSettings;
            if (settings.SunriseTime <= 0f && settings.SunsetTime <= 0f)
                settings = MorrowindDayCycleUtility.CreateFallbackWeatherSettings(default);

            return new MorrowindDayCycleState
            {
                SunriseTime = settings.SunriseTime > 0f ? settings.SunriseTime : 6f,
                SunsetTime = settings.SunsetTime > 0f ? settings.SunsetTime : 18f,
                SunriseDuration = settings.SunriseDuration > 0f ? settings.SunriseDuration : 2f,
                SunsetDuration = settings.SunsetDuration > 0f ? settings.SunsetDuration : 2f,

                SunPreSunriseTime = settings.SunPreSunriseTime,
                SunPostSunriseTime = settings.SunPostSunriseTime,
                SunPreSunsetTime = settings.SunPreSunsetTime > 0f ? settings.SunPreSunsetTime : 1f,
                SunPostSunsetTime = settings.SunPostSunsetTime > 0f ? settings.SunPostSunsetTime : 1.25f,

                AmbientPreSunriseTime = settings.AmbientPreSunriseTime > 0f ? settings.AmbientPreSunriseTime : 0.5f,
                AmbientPostSunriseTime = settings.AmbientPostSunriseTime > 0f ? settings.AmbientPostSunriseTime : 2f,
                AmbientPreSunsetTime = settings.AmbientPreSunsetTime > 0f ? settings.AmbientPreSunsetTime : 1f,
                AmbientPostSunsetTime = settings.AmbientPostSunsetTime > 0f ? settings.AmbientPostSunsetTime : 1.25f,

                FogPreSunriseTime = settings.FogPreSunriseTime > 0f ? settings.FogPreSunriseTime : 0.5f,
                FogPostSunriseTime = settings.FogPostSunriseTime > 0f ? settings.FogPostSunriseTime : 1f,
                FogPreSunsetTime = settings.FogPreSunsetTime > 0f ? settings.FogPreSunsetTime : 2f,
                FogPostSunsetTime = settings.FogPostSunsetTime > 0f ? settings.FogPostSunsetTime : 1f,

                SkyPreSunriseTime = settings.SkyPreSunriseTime > 0f ? settings.SkyPreSunriseTime : 0.5f,
                SkyPostSunriseTime = settings.SkyPostSunriseTime > 0f ? settings.SkyPostSunriseTime : 0.5f,
                SkyPreSunsetTime = settings.SkyPreSunsetTime > 0f ? settings.SkyPreSunsetTime : 1f,
                SkyPostSunsetTime = settings.SkyPostSunsetTime > 0f ? settings.SkyPostSunsetTime : 1f,

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
                SkySunPitchOffsetDegrees = 0f,
                MainLightIntensityScale = 1f,
                MoonMaxOpacity = 1f,
                MoonBrightnessScale = 2.75f,
                MoonEmissionScale = 1.75f,
                MoonMaskScale = 1f,
                StarMaxOpacity = 1f,
                StarBrightnessScale = 1.6f,
                StarTextureOpacityScale = 1f,
                StarTextureScale = 7f,
                PrecipitationIntensityScale = 1f,
                PrecipitationEmissionScale = 1f,
                PrecipitationWorldScale = WorldScale.MwUnitsToMeters,
                PrecipitationParticleSizeScale = 1f,
                LightningIntensityScale = 1f,
            };
        }

        public static ActiveSkyWeatherState CreateFallbackSkyWeather()
        {
            return new ActiveSkyWeatherState
            {
                SkyColorRgb = new float3(0.37f, 0.53f, 0.8f),
                SunDiscColorRgb = new float3(1f, 0.95f, 0.85f),
                SkySunWorldDirection = math.up(),
                UnityLightDirection = math.up(),
                MasserWorldDirection = math.normalize(new float3(0.35f, 0.8f, 0.45f)),
                SecundaWorldDirection = math.normalize(new float3(-0.45f, 0.7f, 0.55f)),
                MoonOpacity = 0f,
                MasserOpacity = 0f,
                SecundaOpacity = 0f,
                MasserShadowBlend = 0f,
                SecundaShadowBlend = 0f,
                MasserSize = 55f,
                SecundaSize = 20f,
                MasserPhase = 0,
                SecundaPhase = 0,
                StarOpacity = 0f,
                StarRotationDegrees = 0f,
                SunDiscOpacity = 1f,
                CloudOpacity = 0f,
                CloudSpeed = 0f,
                CloudUvOffset = 0f,
                CurrentCloudTextureIndex = (int)WeatherKind.Clear,
                NextCloudTextureIndex = (int)WeatherKind.Clear,
                WindSpeed = 0f,
                StormDirection = math.normalize(new float3(1f, 0f, 0.35f)),
                PrecipitationKind = (int)WeatherKind.Clear,
                PrecipitationIntensity = 0f,
                PrecipitationAlpha = 0f,
                RainSpeed = 0f,
                RainEntranceSpeed = 7f,
                RainDiameter = 600f,
                RainMinHeight = 200f,
                RainMaxHeight = 700f,
                RainMaxRaindrops = 450,
                WeatherKind = (int)WeatherKind.Clear,
                NextWeatherKind = (int)WeatherKind.Clear,
            };
        }

        static float3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

        static RuntimeVideoSettings ResolveRuntimeVideoSettings()
        {
            return ConfigStorage.TryLoad(out var config) && config != null
                ? RuntimeVideoSettingsUtility.FromConfig(config)
                : RuntimeVideoSettings.CreateDefault();
        }
    }

}
