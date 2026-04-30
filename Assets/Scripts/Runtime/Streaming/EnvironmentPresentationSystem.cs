using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class EnvironmentPresentationSystem : SystemBase
    {
        static readonly int k_EnvironmentAmbientColorId = Shader.PropertyToID("_VV_EnvironmentAmbientColor");
        static readonly int k_SkyColorId = Shader.PropertyToID("_VV_SkyColor");
        static readonly int k_SunDiscColorId = Shader.PropertyToID("_VV_SunDiscColor");
        static readonly int k_SkySunDirectionId = Shader.PropertyToID("_VV_SkySunDirection");
        static readonly int k_MoonStarOpacityId = Shader.PropertyToID("_VV_MoonStarOpacity");
        static readonly int k_CloudWeatherId = Shader.PropertyToID("_VV_CloudWeather");
        static readonly int k_PrecipitationWeatherId = Shader.PropertyToID("_VV_PrecipitationWeather");
        static readonly int k_LightningWeatherId = Shader.PropertyToID("_VV_LightningWeather");
        static readonly int k_MasserWeatherId = Shader.PropertyToID("_VV_MasserWeather");
        static readonly int k_SecundaWeatherId = Shader.PropertyToID("_VV_SecundaWeather");
        static readonly int k_SkyWeatherTimingId = Shader.PropertyToID("_VV_SkyWeatherTiming");
        static readonly int k_FogRangeId = Shader.PropertyToID("_VV_FogRange");
        static readonly int k_FogColorId = Shader.PropertyToID("_VV_FogColor");

        protected override void OnCreate()
        {
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<ActiveEnvironmentState>();
            RequireForUpdate<MainLightSingleton>();
            RequireForUpdate<ActiveSkyWeatherState>();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var environment = SystemAPI.GetSingleton<ActiveEnvironmentState>();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ToColor(environment.AmbientColorRgb);
            RenderSettings.ambientIntensity = 1f;
            Shader.SetGlobalVector(k_EnvironmentAmbientColorId, new Vector4(
                environment.AmbientColorRgb.x,
                environment.AmbientColorRgb.y,
                environment.AmbientColorRgb.z,
                1f));
            RenderSettings.fog = environment.FogFarMeters > environment.FogNearMeters;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ToColor(environment.FogColorRgb);
            RenderSettings.fogStartDistance = environment.FogNearMeters;
            RenderSettings.fogEndDistance = environment.FogFarMeters;
            RenderSettings.fogDensity = environment.FogDensity;
            SyncRadialFogGlobals(environment);

            var sky = SystemAPI.GetSingletonRW<ActiveSkyWeatherState>().ValueRO;
            
            Shader.SetGlobalVector(k_SkyColorId, ToVector4(sky.SkyColorRgb, 1f));
            Shader.SetGlobalVector(k_SunDiscColorId, ToVector4(sky.SunDiscColorRgb, 1f));
            Shader.SetGlobalVector(k_SkySunDirectionId, ToVector4(math.normalizesafe(sky.SkySunWorldDirection, math.up()), sky.Glare));
            Shader.SetGlobalVector(k_MoonStarOpacityId, new Vector4(sky.MoonOpacity, sky.StarOpacity, sky.IsNight, sky.IsStorm));
            Shader.SetGlobalVector(k_CloudWeatherId, new Vector4(sky.CloudOpacity, sky.CloudSpeed, sky.WindSpeed, sky.WeatherTransition));
            Shader.SetGlobalVector(k_PrecipitationWeatherId, new Vector4(sky.PrecipitationIntensity, sky.RainSpeed, sky.RainDiameter, sky.RainMaxHeight));
            Shader.SetGlobalVector(k_LightningWeatherId, new Vector4(sky.LightningBrightness, sky.ThunderSequence, sky.WeatherKind, sky.NextWeatherKind));
            Shader.SetGlobalVector(k_MasserWeatherId, new Vector4(sky.MasserOpacity, sky.MasserShadowBlend, sky.MasserPhase, sky.MasserSize));
            Shader.SetGlobalVector(k_SecundaWeatherId, new Vector4(sky.SecundaOpacity, sky.SecundaShadowBlend, sky.SecundaPhase, sky.SecundaSize));
            Shader.SetGlobalVector(k_SkyWeatherTimingId, new Vector4(sky.StarRotationDegrees, sky.CloudUvOffset, sky.SunDiscOpacity, sky.IsInterior));


            var mainLight = SystemAPI.GetSingletonRW<MainLightSingleton>().ValueRO.Value.Value;

            bool hasDirectionalLight = environment.DirectionalIntensity > 0.0001f
                && math.lengthsq(environment.DirectionalColorRgb) > 0.0001f;
            mainLight.enabled = hasDirectionalLight;
            mainLight.color = ToColor(environment.DirectionalColorRgb);
            mainLight.intensity = hasDirectionalLight ? environment.DirectionalIntensity : 0f;

            if (math.lengthsq(environment.DirectionalLightDirection) > 0.0001f)
            {
                Vector3 lightForward = ToVector3(-math.normalize(environment.DirectionalLightDirection));
                mainLight.transform.rotation = Quaternion.LookRotation(lightForward, Vector3.up);
            }
        }

        static Color ToColor(float3 rgb) => new(rgb.x, rgb.y, rgb.z, 1f);

        static Vector3 ToVector3(float3 value) => new(value.x, value.y, value.z);

        static void SyncRadialFogGlobals(ActiveEnvironmentState environment)
        {
            bool enabled = environment.FogFarMeters > environment.FogNearMeters;
            float range = Mathf.Max(0.001f, environment.FogFarMeters - environment.FogNearMeters);
            Shader.SetGlobalVector(k_FogRangeId, new Vector4(
                environment.FogNearMeters,
                environment.FogFarMeters,
                1f / range,
                enabled ? 1f : 0f));
            Shader.SetGlobalVector(k_FogColorId, ToShaderColorVector(environment.FogColorRgb, 1f));
        }

        static Vector4 ToVector4(float3 rgb, float w) => new(rgb.x, rgb.y, rgb.z, w);

        static Vector4 ToShaderColorVector(float3 rgb, float a)
        {
            var color = new Color(rgb.x, rgb.y, rgb.z, a);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            return new Vector4(color.r, color.g, color.b, color.a);
        }
    }
}
