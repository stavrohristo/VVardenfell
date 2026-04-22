using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
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

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
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
                Debug.Log(
                    $"[VVardenfell][Lighting] resolved interior environment '{interiorCellId}' via {sourceLabel}.");
                return;
            }

            Debug.Log(
                $"[VVardenfell][Lighting] resolved exterior environment at ({exteriorCell.x},{exteriorCell.y}) via {sourceLabel}.");
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class LightInstanceAnimationSystem : SystemBase
    {
        static readonly ProfilerMarker k_AnimateLights = new("VV.Lighting.AnimateInstances");

        protected override void OnCreate()
        {
            RequireForUpdate<StreamingConfig>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_AnimateLights.Auto();

            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (stateRef, flagsRef) in SystemAPI.Query<RefRW<LightInstanceState>, RefRO<LightInstanceFlags>>())
            {
                ref var state = ref stateRef.ValueRW;
                ref readonly var flags = ref flagsRef.ValueRO;

                state.AnimationTime += deltaTime;

                if (state.Enabled == 0)
                {
                    state.CurrentIntensity = 0f;
                    state.CurrentRange = state.BaseRange;
                    continue;
                }

                float modulation = 1f;
                if (flags.FlickerSlow != 0)
                    modulation = ComputeFlicker(state.AnimationTime, 2.7f, 0.72f);
                else if (flags.Flicker != 0)
                    modulation = ComputeFlicker(state.AnimationTime, 6.5f, 0.65f);
                else if (flags.PulseSlow != 0)
                    modulation = ComputePulse(state.AnimationTime, 1.6f, 0.22f);
                else if (flags.Pulse != 0)
                    modulation = ComputePulse(state.AnimationTime, 3.2f, 0.26f);

                state.CurrentIntensity = state.BaseIntensity * modulation;
                state.CurrentRange = state.BaseRange * math.lerp(0.92f, 1.06f, modulation);
            }
        }

        static float ComputePulse(float time, float speed, float amplitude)
        {
            float wave = 0.5f + 0.5f * math.sin(time * speed);
            return math.lerp(1f - amplitude, 1f + amplitude, wave);
        }

        static float ComputeFlicker(float time, float speed, float floor)
        {
            float a = 0.5f + 0.5f * math.sin(time * speed);
            float b = 0.5f + 0.5f * math.sin(time * (speed * 2.31f) + 1.13f);
            float c = 0.5f + 0.5f * math.sin(time * (speed * 4.13f) + 2.47f);
            float wave = math.saturate(a * 0.55f + b * 0.30f + c * 0.15f);
            return math.lerp(floor, 1.05f, wave);
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
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
                Debug.Log($"[VVardenfell][Lighting] suppressed {_suppressedDirectionalLights.Count} scene directional light(s); runtime environment lighting now owns the sun/interior directional state.");
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

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LightPresentationSystem : SystemBase
    {
        const int MaxPresentedLights = 48;

        static readonly ProfilerMarker k_SyncLights = new("VV.Lighting.SyncPresentedLights");
        static readonly ProfilerMarker k_ReleaseLights = new("VV.Lighting.ReleasePresentedLights");
        static readonly ProfilerMarker k_ReacquireLights = new("VV.Lighting.ReacquirePresentedLights");

        sealed class PresentedLight
        {
            public int Slot;
            public GameObject GameObject;
            public Light Light;
        }

        struct LightCandidate
        {
            public Entity Entity;
            public float DistanceSq;
            public float3 Position;
            public LightInstanceState State;
        }

        EntityQuery _lightQuery;
        GameObject _root;
        readonly Dictionary<Entity, PresentedLight> _activeLights = new();
        readonly Stack<PresentedLight> _lightPool = new();
        readonly List<Entity> _releaseList = new();
        readonly List<LightCandidate> _candidates = new();
        readonly HashSet<Entity> _selected = new();
        int _nextSlot;
        bool _hasPresentationContext;
        bool _lastInteriorActive;
        FixedString128Bytes _lastInteriorCellId;
        int2 _lastCameraCell;

        protected override void OnCreate()
        {
            _lightQuery = GetEntityQuery(
                ComponentType.ReadOnly<LightSourceAuthoring>(),
                ComponentType.ReadOnly<LightInstanceState>(),
                ComponentType.ReadOnly<LightInstanceFlags>(),
                ComponentType.ReadOnly<LightPresentationLink>(),
                ComponentType.ReadOnly<LogicalRefLocation>(),
                ComponentType.ReadOnly<LocalToWorld>());
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<ActiveEnvironmentState>();
        }

        protected override void OnDestroy()
        {
            foreach (var pair in _activeLights)
                ReleasePresentedLight(pair.Key, pair.Value);
            _activeLights.Clear();

            while (_lightPool.Count > 0)
            {
                var pooled = _lightPool.Pop();
                if (pooled.GameObject != null)
                    Object.Destroy(pooled.GameObject);
            }

            if (_root != null)
                Object.Destroy(_root);
            _root = null;
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncLights.Auto();

            CompleteDependency();
            EnsureRoot();

            var cam = Camera.main;
            if (cam == null)
            {
                ReleaseAllPresentedLights();
                return;
            }

            bool interiorActive = false;
            FixedString128Bytes activeInteriorCellId = default;
            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                interiorActive = transition.InteriorActive != 0;
                activeInteriorCellId = transition.ActiveInteriorCellId;
            }

            int2 cameraCell = default;
            if (SystemAPI.HasSingleton<StreamingConfig>())
                cameraCell = SystemAPI.GetSingleton<StreamingConfig>().CameraCell;

            if (HasPresentationContextChanged(interiorActive, activeInteriorCellId, cameraCell))
            {
                using var __ = k_ReacquireLights.Auto();
                Debug.Log(
                    interiorActive
                        ? $"[VVardenfell][Lighting] reacquiring point lights for interior '{activeInteriorCellId}'."
                        : $"[VVardenfell][Lighting] reacquiring point lights for exterior cell ({cameraCell.x},{cameraCell.y}).");
                ReleaseAllPresentedLights();
                _hasPresentationContext = true;
                _lastInteriorActive = interiorActive;
                _lastInteriorCellId = activeInteriorCellId;
                _lastCameraCell = cameraCell;
            }

            NativeHashSet<int2> activeExteriorCells = default;
            if (!interiorActive && SystemAPI.HasSingleton<LoadedCellsMap>())
                activeExteriorCells = SystemAPI.GetSingleton<LoadedCellsMap>().Active;

            _candidates.Clear();
            _selected.Clear();

            using var entities = _lightQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var localToWorlds = _lightQuery.ToComponentDataArray<LocalToWorld>(Unity.Collections.Allocator.Temp);
            using var states = _lightQuery.ToComponentDataArray<LightInstanceState>(Unity.Collections.Allocator.Temp);
            using var flags = _lightQuery.ToComponentDataArray<LightInstanceFlags>(Unity.Collections.Allocator.Temp);
            using var locations = _lightQuery.ToComponentDataArray<LogicalRefLocation>(Unity.Collections.Allocator.Temp);

            float3 cameraPosition = cam.transform.position;

            for (int i = 0; i < entities.Length; i++)
            {
                if (states[i].Enabled == 0 || flags[i].Negative != 0)
                    continue;

                if (interiorActive)
                {
                    if (locations[i].IsInterior == 0 || !locations[i].InteriorCellId.Equals(activeInteriorCellId))
                        continue;
                }
                else
                {
                    if (locations[i].IsInterior != 0)
                        continue;
                    if (activeExteriorCells.IsCreated && !activeExteriorCells.Contains(locations[i].ExteriorCell))
                        continue;
                }

                float3 position = localToWorlds[i].Value.c3.xyz;
                float cullRange = math.max(48f, states[i].CurrentRange * 4f);
                float distanceSq = math.distancesq(position, cameraPosition);
                if (distanceSq > cullRange * cullRange)
                    continue;

                _candidates.Add(new LightCandidate
                {
                    Entity = entities[i],
                    DistanceSq = distanceSq,
                    Position = position,
                    State = states[i],
                });
            }

            _candidates.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

            int limit = math.min(MaxPresentedLights, _candidates.Count);
            for (int i = 0; i < limit; i++)
            {
                var candidate = _candidates[i];
                _selected.Add(candidate.Entity);
                PresentLight(candidate);
            }

            _releaseList.Clear();
            foreach (var pair in _activeLights)
            {
                if (!_selected.Contains(pair.Key) || !EntityManager.Exists(pair.Key))
                    _releaseList.Add(pair.Key);
            }

            for (int i = 0; i < _releaseList.Count; i++)
            {
                var entity = _releaseList[i];
                if (_activeLights.TryGetValue(entity, out var presented))
                {
                    ReleasePresentedLight(entity, presented);
                    _activeLights.Remove(entity);
                }
            }
        }

        void EnsureRoot()
        {
            if (_root != null)
                return;

            _root = new GameObject("VVardenfell.RuntimePointLights");
            Object.DontDestroyOnLoad(_root);
        }

        void PresentLight(in LightCandidate candidate)
        {
            if (!_activeLights.TryGetValue(candidate.Entity, out var presented))
            {
                presented = AcquirePresentedLight();
                _activeLights[candidate.Entity] = presented;
                if (EntityManager.Exists(candidate.Entity) && EntityManager.HasComponent<LightPresentationLink>(candidate.Entity))
                    EntityManager.SetComponentData(candidate.Entity, new LightPresentationLink { Slot = presented.Slot });
            }

            presented.GameObject.transform.position = candidate.Position;
            presented.Light.enabled = true;
            presented.Light.color = new Color(
                candidate.State.BaseColorRgb.x,
                candidate.State.BaseColorRgb.y,
                candidate.State.BaseColorRgb.z,
                1f);
            presented.Light.intensity = candidate.State.CurrentIntensity;
            presented.Light.range = candidate.State.CurrentRange;
        }

        PresentedLight AcquirePresentedLight()
        {
            if (_lightPool.Count > 0)
            {
                var pooled = _lightPool.Pop();
                if (pooled.GameObject != null)
                    pooled.GameObject.SetActive(true);
                return pooled;
            }

            var go = new GameObject($"PointLight_{_nextSlot:D3}");
            go.transform.SetParent(_root.transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            light.range = 8f;
            light.intensity = 1f;

            return new PresentedLight
            {
                Slot = _nextSlot++,
                GameObject = go,
                Light = light,
            };
        }

        void ReleaseAllPresentedLights()
        {
            using var _ = k_ReleaseLights.Auto();

            _releaseList.Clear();
            foreach (var pair in _activeLights)
                _releaseList.Add(pair.Key);

            for (int i = 0; i < _releaseList.Count; i++)
            {
                var entity = _releaseList[i];
                if (_activeLights.TryGetValue(entity, out var presented))
                {
                    ReleasePresentedLight(entity, presented);
                    _activeLights.Remove(entity);
                }
            }
        }

        void ReleasePresentedLight(Entity entity, PresentedLight presented)
        {
            if (EntityManager.Exists(entity) && EntityManager.HasComponent<LightPresentationLink>(entity))
                EntityManager.SetComponentData(entity, new LightPresentationLink { Slot = -1 });

            if (presented.GameObject != null)
            {
                presented.Light.enabled = false;
                presented.GameObject.SetActive(false);
            }

            _lightPool.Push(presented);
        }

        bool HasPresentationContextChanged(bool interiorActive, FixedString128Bytes activeInteriorCellId, int2 cameraCell)
        {
            if (!_hasPresentationContext)
                return true;

            if (_lastInteriorActive != interiorActive)
                return true;

            if (interiorActive)
                return !_lastInteriorCellId.Equals(activeInteriorCellId);

            return !math.all(_lastCameraCell == cameraCell);
        }
    }
}
