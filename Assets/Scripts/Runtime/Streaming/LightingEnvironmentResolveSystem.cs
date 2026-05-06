using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    public partial struct LightingEnvironmentResolveSystem : ISystem
    {
        static readonly ProfilerMarker k_ResolveEnvironment = new("VV.Lighting.ResolveEnvironment");

        bool _hasLoggedContext;
        bool _lastInteriorActive;
        int2 _lastExteriorCell;
        FixedString128Bytes _lastInteriorCellId;
        bool _loggedMissingInteriorCell;
        FixedString128Bytes _missingInteriorCellId;

        EntityQuery _environmentQuery;
        EntityQuery _streamingQuery;
        EntityQuery _dayCycleQuery;
        EntityQuery _timeQuery;
        EntityQuery _weatherQuery;
        EntityQuery _videoSettingsQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _environmentQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<ActiveEnvironmentState>());
            _streamingQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<StreamingConfig>());
            _dayCycleQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindDayCycleState>());
            _timeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindTimeState>());
            _weatherQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindWeatherState>());
            _videoSettingsQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeVideoSettings>());
            systemState.RequireForUpdate(_environmentQuery);
            systemState.RequireForUpdate(_streamingQuery);
            systemState.RequireForUpdate(_dayCycleQuery);
            systemState.RequireForUpdate(_timeQuery);
            systemState.RequireForUpdate(_weatherQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            using var _ = k_ResolveEnvironment.Auto();

            ref var environment = ref _environmentQuery.GetSingletonRW<ActiveEnvironmentState>().ValueRW;
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Lighting environment resolve requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] Lighting environment resolve requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            var streaming = _streamingQuery.GetSingleton<StreamingConfig>();
            var dayCycle = _dayCycleQuery.GetSingleton<MorrowindDayCycleState>();
            var time = _timeQuery.GetSingleton<MorrowindTimeState>();
            var weather = _weatherQuery.GetSingleton<MorrowindWeatherState>();
            float fogDistanceScale = ResolveFogDistanceScale();

            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                if (transition.InteriorActive != 0)
                {
                    if (RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, transition.ActiveInteriorCellHash, out int interiorCellIndex))
                    {
                        ref RuntimeWorldCellDefBlob interiorCell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, interiorCellIndex);
                        environment = BuildInteriorEnvironment(in interiorCell.Environment, fogDistanceScale);
                        LogEnvironmentContext(
                            isInterior: true,
                            exteriorCell: default,
                            interiorCellId: transition.ActiveInteriorCellId,
                            sourceLabel: interiorCell.Environment.HasMood != 0 ? "interior mood" : "interior fallback");
                        _loggedMissingInteriorCell = false;
                        return;
                    }

                    environment = ApplyFogDistanceScale(
                        LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: true),
                        fogDistanceScale);
                    if (!_loggedMissingInteriorCell || !_missingInteriorCellId.Equals(transition.ActiveInteriorCellId))
                    {
                        _loggedMissingInteriorCell = true;
                        _missingInteriorCellId = transition.ActiveInteriorCellId;
                        Debug.LogWarning(
                            $"[VVardenfell][Lighting] active interior '{transition.ActiveInteriorCellId}' had no preloaded cell/environment payload; using fallback interior lighting.");
                    }
                    LogEnvironmentContext(
                        isInterior: true,
                        exteriorCell: default,
                        interiorCellId: transition.ActiveInteriorCellId,
                        sourceLabel: "missing interior fallback");
                    return;
                }
            }

            if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, streaming.CameraCell, out int exteriorCellIndex))
            {
                ref RuntimeWorldCellDefBlob exteriorCell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, exteriorCellIndex);
                environment = BuildExteriorEnvironment(in exteriorCell.Environment, ref content, dayCycle, time, weather, fogDistanceScale);
                LogEnvironmentContext(
                    isInterior: false,
                    exteriorCell: streaming.CameraCell,
                    interiorCellId: default,
                    sourceLabel: exteriorCell.Environment.RegionIdHash == 0UL ? "exterior fallback" : "region baseline");
                _loggedMissingInteriorCell = false;
                return;
            }

            environment = BuildExteriorEnvironment(default, ref content, dayCycle, time, weather, fogDistanceScale);
            LogEnvironmentContext(
                isInterior: false,
                exteriorCell: streaming.CameraCell,
                interiorCellId: default,
                sourceLabel: "missing exterior fallback");
        }

        static ActiveEnvironmentState BuildInteriorEnvironment(in RuntimeWorldCellEnvironmentDefBlob env, float fogDistanceScale)
        {
            var fallback = ApplyFogDistanceScale(
                LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: true),
                fogDistanceScale);
            if (env.HasMood == 0)
                return fallback;

            float density = math.clamp(env.FogDensity, 0f, 1f);
            ComputeFogRange(density, isInterior: true, out float fogNear, out float fogFar);

            return new ActiveEnvironmentState
            {
                AmbientColorRgb = DecodeRgb(env.AmbientColorRgba),
                DirectionalColorRgb = DecodeRgb(env.DirectionalColorRgba),
                DirectionalLightDirection = math.normalize(new float3(0f, 1f, -0.25f)),
                DirectionalIntensity = 0.85f,
                SunPercent = 1f,
                FogColorRgb = DecodeRgb(env.FogColorRgba),
                FogDensity = density,
                FogNearMeters = fogNear * fogDistanceScale,
                FogFarMeters = fogFar * fogDistanceScale,
                RegionHandleValue = 0,
                IsInterior = 1,
            };
        }

        static ActiveEnvironmentState BuildExteriorEnvironment(
            in RuntimeWorldCellEnvironmentDefBlob cellEnvironment,
            ref RuntimeContentBlob content,
            MorrowindDayCycleState dayCycle,
            MorrowindTimeState time,
            MorrowindWeatherState weather,
            float fogDistanceScale)
        {
            int regionHandleValue = 0;

            if (cellEnvironment.RegionIdHash != 0UL
                && RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref content, cellEnvironment.RegionIdHash, out var regionHandle))
            {
                regionHandleValue = regionHandle.Value;
            }

            var current = ResolveWeather(ref content, weather.CurrentWeather);
            int nextIndex = weather.NextWeather >= 0 ? weather.NextWeather : -1;
            var next = nextIndex >= 0 ? ResolveWeather(ref content, nextIndex) : current;
            var weatherSettings = content.WeatherSettings;
            var blend = MorrowindWeatherEvaluationUtility.Evaluate(dayCycle, weatherSettings, current, weather.CurrentWeather, next, nextIndex, weather.Transition, time.GameHour);
            var day = blend.Evaluation;

            float windDimmer = math.saturate(blend.WindSpeed / 70f);
            float sunDimmer = math.lerp(1f, 0.35f, windDimmer);
            float directionalIntensity = day.SunPercent
                * math.max(0f, dayCycle.ExteriorSunIntensityScale)
                * math.max(0f, dayCycle.MainLightIntensityScale)
                * sunDimmer;
            float baseFogDensity = math.lerp(dayCycle.ExteriorDayFogDensity, dayCycle.ExteriorNightFogDensity, 1f - day.SunPercent);
            float fogDensity = math.saturate(math.max(baseFogDensity, day.FogDepth / 3f));
            ComputeFogRange(fogDensity, isInterior: false, out float fogNear, out float fogFar);

            return new ActiveEnvironmentState
            {
                AmbientColorRgb = day.AmbientColorRgb,
                DirectionalColorRgb = day.SunColorRgb,
                DirectionalLightDirection = day.SunDirectionToLight,
                DirectionalIntensity = directionalIntensity,
                SunPercent = day.SunPercent,
                FogColorRgb = day.FogColorRgb,
                FogDensity = fogDensity,
                FogNearMeters = fogNear * fogDistanceScale,
                FogFarMeters = fogFar * fogDistanceScale,
                RegionHandleValue = regionHandleValue,
                IsInterior = 0,
            };
        }

        static WeatherDefinitionDef ResolveWeather(ref RuntimeContentBlob content, int index)
        {
            if ((uint)index >= (uint)content.WeatherDefinitions.Length)
                throw new System.InvalidOperationException($"[VVardenfell][ContentBlob] Invalid weather definition index {index}; length {content.WeatherDefinitions.Length}.");
            return RuntimeContentBlobUtility.RequireWeatherDefinition(ref content, index);
        }

        static float3 DecodeRgb(uint value)
        {
            return new float3(
                ((value >> 0) & 0xFFu) / 255f,
                ((value >> 8) & 0xFFu) / 255f,
                ((value >> 16) & 0xFFu) / 255f);
        }

        static void ComputeFogRange(float density, bool isInterior, out float fogNear, out float fogFar)
        {
            float clampedDensity = math.saturate(density);
            if (isInterior)
            {
                fogFar = math.lerp(160f, 32f, clampedDensity);
                fogNear = fogFar * 0.4f;
                return;
            }

            fogFar = math.lerp(1800f, 240f, clampedDensity);
            fogNear = math.lerp(fogFar * 0.7f, fogFar * 0.18f, clampedDensity);
        }

        float ResolveFogDistanceScale()
        {
            if (_videoSettingsQuery.IsEmptyIgnoreFilter)
                return RuntimeVideoSettings.DefaultFogDistanceScale;

            var settings = _videoSettingsQuery.GetSingleton<RuntimeVideoSettings>();
            return RuntimeVideoSettings.NormalizeFogDistanceScale(settings.FogDistanceScale);
        }

        static ActiveEnvironmentState ApplyFogDistanceScale(ActiveEnvironmentState environment, float fogDistanceScale)
        {
            environment.FogNearMeters *= fogDistanceScale;
            environment.FogFarMeters *= fogDistanceScale;
            return environment;
        }

        void LogEnvironmentContext(bool isInterior, int2 exteriorCell, FixedString128Bytes interiorCellId, string sourceLabel)
        {
            bool changed = !_hasLoggedContext
                || _lastInteriorActive != isInterior
                || (isInterior
                    ? !_lastInteriorCellId.Equals(interiorCellId)
                    : !math.all(_lastExteriorCell == exteriorCell));

            if (!changed)
                return;

            _hasLoggedContext = true;
            _lastInteriorActive = isInterior;
            _lastExteriorCell = exteriorCell;
            _lastInteriorCellId = interiorCellId;

            if (isInterior)
            {
                return;
            }

        }
    }
}
