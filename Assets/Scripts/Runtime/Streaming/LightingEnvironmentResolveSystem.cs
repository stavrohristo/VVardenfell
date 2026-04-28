using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    public partial class LightingEnvironmentResolveSystem : SystemBase
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

        protected override void OnCreate()
        {
            _environmentQuery = GetEntityQuery(ComponentType.ReadWrite<ActiveEnvironmentState>());
            _streamingQuery = GetEntityQuery(ComponentType.ReadOnly<StreamingConfig>());
            _dayCycleQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindDayCycleState>());
            _timeQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindTimeState>());
            _weatherQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindWeatherState>());
            RequireForUpdate(_environmentQuery);
            RequireForUpdate(_streamingQuery);
            RequireForUpdate(_dayCycleQuery);
            RequireForUpdate(_timeQuery);
            RequireForUpdate(_weatherQuery);
        }

        protected override void OnUpdate()
        {
            using var _ = k_ResolveEnvironment.Auto();

            ref var environment = ref _environmentQuery.GetSingletonRW<ActiveEnvironmentState>().ValueRW;
            var contentDb = RuntimeContentDatabase.Active;
            var streaming = _streamingQuery.GetSingleton<StreamingConfig>();
            var dayCycle = _dayCycleQuery.GetSingleton<MorrowindDayCycleState>();
            var time = _timeQuery.GetSingleton<MorrowindTimeState>();
            var weather = _weatherQuery.GetSingleton<MorrowindWeatherState>();

            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                if (transition.InteriorActive != 0)
                {
                    if (WorldResources.TryGetInteriorCell(transition.ActiveInteriorCellHash, out var interiorCell))
                    {
                        environment = BuildInteriorEnvironment(interiorCell);
                        LogEnvironmentContext(
                            isInterior: true,
                            exteriorCell: default,
                            interiorCellId: transition.ActiveInteriorCellId,
                            sourceLabel: interiorCell.Environment.HasMood != 0 ? "interior mood" : "interior fallback");
                        _loggedMissingInteriorCell = false;
                        return;
                    }

                    environment = LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: true);
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

            if (WorldResources.Cells.TryGetValue(streaming.CameraCell, out var exteriorCell) && exteriorCell != null)
            {
                environment = BuildExteriorEnvironment(exteriorCell, contentDb, dayCycle, time, weather);
                LogEnvironmentContext(
                    isInterior: false,
                    exteriorCell: streaming.CameraCell,
                    interiorCellId: default,
                    sourceLabel: string.IsNullOrEmpty(exteriorCell.Environment.RegionId) ? "exterior fallback" : "region baseline");
                _loggedMissingInteriorCell = false;
                return;
            }

            environment = BuildExteriorEnvironment(null, contentDb, dayCycle, time, weather);
            LogEnvironmentContext(
                isInterior: false,
                exteriorCell: streaming.CameraCell,
                interiorCellId: default,
                sourceLabel: "missing exterior fallback");
        }

        static ActiveEnvironmentState BuildInteriorEnvironment(CellData cell)
        {
            var fallback = LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: true);
            var env = cell.Environment;
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
                FogNearMeters = fogNear,
                FogFarMeters = fogFar,
                RegionHandleValue = 0,
                IsInterior = 1,
            };
        }

        static ActiveEnvironmentState BuildExteriorEnvironment(CellData cell, RuntimeContentDatabase contentDb, MorrowindDayCycleState dayCycle, MorrowindTimeState time, MorrowindWeatherState weather)
        {
            int regionHandleValue = 0;
            string regionId = cell?.Environment.RegionId ?? string.Empty;

            if (contentDb != null && contentDb.TryGetRegionHandle(regionId, out var regionHandle))
            {
                regionHandleValue = regionHandle.Value;
            }

            var current = ResolveWeather(contentDb, weather.CurrentWeather);
            int nextIndex = weather.NextWeather >= 0 ? weather.NextWeather : -1;
            var next = nextIndex >= 0 ? ResolveWeather(contentDb, nextIndex) : current;
            var weatherSettings = contentDb?.Data?.WeatherSettings ?? MorrowindDayCycleUtility.CreateFallbackWeatherSettings(dayCycle);
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
                FogNearMeters = fogNear,
                FogFarMeters = fogFar,
                RegionHandleValue = regionHandleValue,
                IsInterior = 0,
            };
        }

        static WeatherDefinitionDef ResolveWeather(RuntimeContentDatabase contentDb, int index)
        {
            var defs = contentDb?.Data?.WeatherDefinitions;
            if (defs != null && (uint)index < (uint)defs.Length)
                return defs[index];
            return MorrowindDayCycleUtility.CreateFallbackClearWeather();
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
