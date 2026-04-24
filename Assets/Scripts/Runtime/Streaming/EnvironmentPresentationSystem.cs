using System.Collections.Generic;
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

        GameObject _root;
        Light _directionalLight;
        readonly List<Light> _suppressedDirectionalLights = new();
        bool _sceneDirectionalsSuppressed;

        protected override void OnCreate()
        {
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<ActiveEnvironmentState>();
        }

        protected override void OnDestroy()
        {
            RestoreSceneDirectionalLights();

            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _directionalLight = null;
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncEnvironment.Auto();

            CompleteDependency();

            EnsureDirectionalLight();
            SuppressSceneDirectionalLights();
            var environment = SystemAPI.GetSingleton<ActiveEnvironmentState>();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ToColor(environment.AmbientColorRgb);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = environment.FogFarMeters > environment.FogNearMeters;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ToColor(environment.FogColorRgb);
            RenderSettings.fogStartDistance = environment.FogNearMeters;
            RenderSettings.fogEndDistance = environment.FogFarMeters;

            if (_directionalLight != null)
            {
                _directionalLight.enabled = math.lengthsq(environment.DirectionalColorRgb) > 0.0001f;
                _directionalLight.color = ToColor(environment.DirectionalColorRgb);
                _directionalLight.intensity = environment.IsInterior != 0 ? 0.85f : 1.15f;
                _directionalLight.transform.rotation = Quaternion.Euler(
                    environment.IsInterior != 0 ? 45f : 52f,
                    environment.IsInterior != 0 ? -45f : -36f,
                    0f);
            }
        }

        void EnsureDirectionalLight()
        {
            if (_directionalLight != null)
                return;

            _root = new GameObject("VVardenfell.RuntimeLighting");
            Object.DontDestroyOnLoad(_root);

            var directionalGo = new GameObject("RuntimeDirectionalLight");
            directionalGo.transform.SetParent(_root.transform, false);
            _directionalLight = directionalGo.AddComponent<Light>();
            _directionalLight.type = LightType.Directional;
            _directionalLight.shadows = LightShadows.None;
            _directionalLight.renderMode = LightRenderMode.Auto;
        }

        void SuppressSceneDirectionalLights()
        {
            if (_sceneDirectionalsSuppressed)
                return;

#if UNITY_2023_1_OR_NEWER
            var sceneLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
#else
            var sceneLights = Object.FindObjectsOfType<Light>(true);
#endif
            _suppressedDirectionalLights.Clear();
            for (int i = 0; i < sceneLights.Length; i++)
            {
                var candidate = sceneLights[i];
                if (candidate == null || candidate == _directionalLight || candidate.type != LightType.Directional)
                    continue;

                if (!candidate.enabled)
                    continue;

                candidate.enabled = false;
                _suppressedDirectionalLights.Add(candidate);
            }

            if (_suppressedDirectionalLights.Count > 0)
            {
                _sceneDirectionalsSuppressed = true;
            }
        }

        void RestoreSceneDirectionalLights()
        {
            if (!_sceneDirectionalsSuppressed)
                return;

            for (int i = 0; i < _suppressedDirectionalLights.Count; i++)
            {
                var candidate = _suppressedDirectionalLights[i];
                if (candidate != null)
                    candidate.enabled = true;
            }

            _suppressedDirectionalLights.Clear();
            _sceneDirectionalsSuppressed = false;
        }

        static Color ToColor(float3 rgb) => new(rgb.x, rgb.y, rgb.z, 1f);
    }
}
