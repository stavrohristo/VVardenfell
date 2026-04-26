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

            mainLight.enabled = math.lengthsq(environment.DirectionalColorRgb) > 0.0001f;
            mainLight.color = ToColor(environment.DirectionalColorRgb);
            mainLight.intensity = environment.IsInterior != 0 ? 0.85f : 1.15f;
        }

        static Color ToColor(float3 rgb) => new(rgb.x, rgb.y, rgb.z, 1f);
    }
}
