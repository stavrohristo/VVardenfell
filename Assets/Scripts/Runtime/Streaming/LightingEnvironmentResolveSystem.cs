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

        protected override void OnCreate()
        {
            _environmentQuery = GetEntityQuery(ComponentType.ReadWrite<ActiveEnvironmentState>());
            _streamingQuery = GetEntityQuery(ComponentType.ReadOnly<StreamingConfig>());
            RequireForUpdate(_environmentQuery);
            RequireForUpdate(_streamingQuery);
        }

        protected override void OnUpdate()
        {
            using var _ = k_ResolveEnvironment.Auto();

            ref var environment = ref _environmentQuery.GetSingletonRW<ActiveEnvironmentState>().ValueRW;
            var contentDb = RuntimeContentDatabase.Active;
            var streaming = _streamingQuery.GetSingleton<StreamingConfig>();

            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                if (transition.InteriorActive != 0)
                {
                    string interiorCellId = transition.ActiveInteriorCellId.ToString();
                    if (WorldResources.InteriorCells.TryGetValue(interiorCellId, out var interiorCell) && interiorCell != null)
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
                environment = BuildExteriorEnvironment(exteriorCell, contentDb);
                LogEnvironmentContext(
                    isInterior: false,
                    exteriorCell: streaming.CameraCell,
                    interiorCellId: default,
                    sourceLabel: string.IsNullOrEmpty(exteriorCell.Environment.RegionId) ? "exterior fallback" : "region baseline");
                _loggedMissingInteriorCell = false;
                return;
            }

            environment = LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: false);
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
                FogColorRgb = DecodeRgb(env.FogColorRgba),
                FogDensity = density,
                FogNearMeters = fogNear,
                FogFarMeters = fogFar,
                RegionHandleValue = 0,
                IsInterior = 1,
            };
        }

        static ActiveEnvironmentState BuildExteriorEnvironment(CellData cell, RuntimeContentDatabase contentDb)
        {
            var state = LightingBootstrapSystem.CreateFallbackEnvironment(isInterior: false);
            string regionId = cell.Environment.RegionId ?? string.Empty;

            if (contentDb != null && contentDb.TryGetRegionHandle(regionId, out var regionHandle))
            {
                ref readonly var region = ref contentDb.Get(regionHandle);
                state.RegionHandleValue = regionHandle.Value;

                float total =
                    region.ClearChance +
                    region.CloudyChance +
                    region.FoggyChance +
                    region.OvercastChance +
                    region.RainChance +
                    region.ThunderChance +
                    region.AshChance +
                    region.BlightChance +
                    region.SnowChance +
                    region.BlizzardChance;

                if (total > 0f)
                {
                    float cloudiness = (region.CloudyChance + region.FoggyChance + region.OvercastChance) / total;
                    float storminess = (region.RainChance + region.ThunderChance + region.AshChance + region.BlightChance + region.SnowChance + region.BlizzardChance) / total;
                    float mood = math.saturate(cloudiness * 0.7f + storminess);

                    float3 clearAmbient = new(0.44f, 0.45f, 0.46f);
                    float3 stormAmbient = new(0.26f, 0.27f, 0.31f);
                    float3 clearDirectional = new(0.98f, 0.92f, 0.84f);
                    float3 stormDirectional = new(0.56f, 0.58f, 0.64f);
                    float3 clearFog = new(0.60f, 0.69f, 0.78f);
                    float3 stormFog = new(0.30f, 0.33f, 0.38f);

                    state.AmbientColorRgb = math.lerp(clearAmbient, stormAmbient, mood);
                    state.DirectionalColorRgb = math.lerp(clearDirectional, stormDirectional, mood);
                    state.FogColorRgb = math.lerp(clearFog, stormFog, mood);
                    state.FogDensity = math.lerp(0.14f, 0.48f, mood);
                    ComputeFogRange(state.FogDensity, isInterior: false, out state.FogNearMeters, out state.FogFarMeters);
                }
            }

            return state;
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
