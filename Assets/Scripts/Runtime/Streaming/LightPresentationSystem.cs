using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
        NativeList<LightCandidate> _candidates;
        NativeParallelHashSet<Entity> _selected;
        NativeHashSet<int2> _emptyActiveExteriorCells;
        int _nextSlot;
        bool _hasPresentationContext;
        bool _lastInteriorActive;
        ulong _lastInteriorCellHash;
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

            _candidates = new NativeList<LightCandidate>(MaxPresentedLights, Allocator.Persistent);
            _selected = new NativeParallelHashSet<Entity>(MaxPresentedLights, Allocator.Persistent);
            _emptyActiveExteriorCells = new NativeHashSet<int2>(1, Allocator.Persistent);
            
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

            if (_candidates.IsCreated)
                _candidates.Dispose();
            if (_selected.IsCreated)
                _selected.Dispose();
            if (_emptyActiveExteriorCells.IsCreated)
                _emptyActiveExteriorCells.Dispose();
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncLights.Auto();

            CompleteDependency();
            

            bool interiorActive = false;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                interiorActive = transition.InteriorActive != 0;
                activeInteriorCellHash = transition.ActiveInteriorCellHash;
            }

            int2 cameraCell = default;
            if (SystemAPI.HasSingleton<StreamingConfig>())
                cameraCell = SystemAPI.GetSingleton<StreamingConfig>().CameraCell;

            if (HasPresentationContextChanged(interiorActive, activeInteriorCellHash, cameraCell))
            {
                using var __ = k_ReacquireLights.Auto();
                ReleaseAllPresentedLights();
                _hasPresentationContext = true;
                _lastInteriorActive = interiorActive;
                _lastInteriorCellHash = activeInteriorCellHash;
                _lastCameraCell = cameraCell;
            }

            NativeHashSet<int2> activeExteriorCells = _emptyActiveExteriorCells;
            byte hasActiveExteriorCells = 0;
            if (!interiorActive && SystemAPI.HasSingleton<LoadedCellsMap>())
            {
                activeExteriorCells = SystemAPI.GetSingleton<LoadedCellsMap>().Active;
                hasActiveExteriorCells = 1;
            }

            _candidates.Clear();
            _selected.Clear();

            Camera camera = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            float3 cameraPosition = camera.transform.position;

            int queryCount = _lightQuery.CalculateEntityCount();
            if (_candidates.Capacity < queryCount)
                _candidates.Capacity = queryCount;

            var gatherJob = new GatherLightCandidatesJob
            {
                EntityHandle = GetEntityTypeHandle(),
                LocalToWorldHandle = GetComponentTypeHandle<LocalToWorld>(isReadOnly: true),
                StateHandle = GetComponentTypeHandle<LightInstanceState>(isReadOnly: true),
                FlagsHandle = GetComponentTypeHandle<LightInstanceFlags>(isReadOnly: true),
                LocationHandle = GetComponentTypeHandle<LogicalRefLocation>(isReadOnly: true),
                ActiveExteriorCells = activeExteriorCells,
                CameraPosition = cameraPosition,
                ActiveInteriorCellHash = activeInteriorCellHash,
                InteriorActive = interiorActive,
                HasActiveExteriorCells = hasActiveExteriorCells,
                Candidates = _candidates.AsParallelWriter(),
            };
            Dependency = gatherJob.ScheduleParallel(_lightQuery, Dependency);
            Dependency.Complete();
            Dependency = default;

            _candidates.Sort(new LightCandidateDistanceComparer());

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

        bool HasPresentationContextChanged(bool interiorActive, ulong activeInteriorCellHash, int2 cameraCell)
        {
            if (!_hasPresentationContext)
                return true;

            if (_lastInteriorActive != interiorActive)
                return true;

            if (interiorActive)
                return _lastInteriorCellHash != activeInteriorCellHash;

            return !math.all(_lastCameraCell == cameraCell);
        }

        struct LightCandidateDistanceComparer : IComparer<LightCandidate>
        {
            public int Compare(LightCandidate x, LightCandidate y)
            {
                int distance = x.DistanceSq.CompareTo(y.DistanceSq);
                return distance != 0 ? distance : x.Entity.Index.CompareTo(y.Entity.Index);
            }
        }

        [BurstCompile]
        struct GatherLightCandidatesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;
            [ReadOnly] public ComponentTypeHandle<LightInstanceState> StateHandle;
            [ReadOnly] public ComponentTypeHandle<LightInstanceFlags> FlagsHandle;
            [ReadOnly] public ComponentTypeHandle<LogicalRefLocation> LocationHandle;
            [ReadOnly] public NativeHashSet<int2> ActiveExteriorCells;
            public float3 CameraPosition;
            public ulong ActiveInteriorCellHash;
            public bool InteriorActive;
            public byte HasActiveExteriorCells;
            public NativeList<LightCandidate>.ParallelWriter Candidates;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var localToWorlds = chunk.GetNativeArray(ref LocalToWorldHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var flags = chunk.GetNativeArray(ref FlagsHandle);
                var locations = chunk.GetNativeArray(ref LocationHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var state = states[i];
                    if (state.Enabled == 0 || flags[i].Negative != 0)
                        continue;

                    var location = locations[i];
                    if (InteriorActive)
                    {
                        if (location.IsInterior == 0 || location.InteriorCellHash != ActiveInteriorCellHash)
                            continue;
                    }
                    else
                    {
                        if (location.IsInterior != 0)
                            continue;
                        if (HasActiveExteriorCells != 0 && !ActiveExteriorCells.Contains(location.ExteriorCell))
                            continue;
                    }

                    float3 position = localToWorlds[i].Value.c3.xyz;
                    float cullRange = math.max(48f, state.CurrentRange * 4f);
                    float distanceSq = math.distancesq(position, CameraPosition);
                    if (distanceSq > cullRange * cullRange)
                        continue;

                    Candidates.AddNoResize(new LightCandidate
                    {
                        Entity = entities[i],
                        DistanceSq = distanceSq,
                        Position = position,
                        State = state,
                    });
                }
            }
        }
    }
}
