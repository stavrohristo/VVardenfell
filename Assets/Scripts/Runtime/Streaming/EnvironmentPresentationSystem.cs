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
        static readonly ProfilerMarker k_SyncEnvironment = new("VV.Lighting.SyncEnvironment");
        static readonly int k_EnvironmentAmbientColorId = Shader.PropertyToID("_VV_EnvironmentAmbientColor");

        protected override void OnCreate()
        {
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<ActiveEnvironmentState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncEnvironment.Auto();

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

            if (!SystemAPI.TryGetSingleton<MainLightSingleton>(out var mainLightRef))
                return;

            var mainLight = mainLightRef.Value.Value;
            if (mainLight == null)
                return;

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
    }
}
