using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class LightPresentationSystem : SystemBase
    {
        const int MaxPresentedLights = 48;
        const int MaxPresentedShadowCastingLights = 4;

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
            RequireForUpdate<MainCameraSingleton>();
            
            _root = new GameObject("VVardenfell.RuntimePointLights");
            Object.DontDestroyOnLoad(_root);
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

            var camSingleton = SystemAPI.GetSingleton<MainCameraSingleton>();
            float3 cameraPosition = camSingleton.Ref.Value.transform.position;

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
                PresentLight(candidate, i < MaxPresentedShadowCastingLights);
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

        void PresentLight(in LightCandidate candidate, bool castsShadow)
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
            presented.Light.shadows = castsShadow ? LightShadows.Hard : LightShadows.None;
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
