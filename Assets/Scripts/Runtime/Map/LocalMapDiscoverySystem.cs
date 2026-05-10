using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Map
{
    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(CellStreamingSystemGroup))]
    public partial struct LocalMapDiscoverySystem : ISystem
    {
        const int DefaultMaskResolution = 64;
        const float DefaultRevealRadiusFraction = 0.17f;

        EntityQuery _playerQuery;
        EntityQuery _tileQuery;
        NativeParallelHashMap<int2, Entity> _tileLookup;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _tileQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                ComponentType.ReadWrite<ExteriorMapDiscoverySample>());

            systemState.RequireForUpdate<LocalMapDiscoveryState>();
            systemState.RequireForUpdate<StreamingConfig>();
            systemState.RequireForUpdate<InteriorTransitionState>();

            _tileLookup = new NativeParallelHashMap<int2, Entity>(64, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_tileLookup.IsCreated)
                _tileLookup.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
            if (transition.InteriorActive != 0)
                return;

            _playerQuery.CompleteDependency();
            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            var stateEntity = SystemAPI.GetSingletonEntity<LocalMapDiscoveryState>();
            var state = systemState.EntityManager.GetComponentData<LocalMapDiscoveryState>(stateEntity);
            bool normalized = NormalizeState(ref state);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var playerCell = new int2(
                (int)math.floor(playerTransform.Position.x / cellMeters),
                (int)math.floor(playerTransform.Position.z / cellMeters));
            float localX = math.saturate((playerTransform.Position.x - playerCell.x * cellMeters) / cellMeters);
            float localY = math.saturate((playerTransform.Position.z - playerCell.y * cellMeters) / cellMeters);

            int resolution = math.max(1, state.MaskResolution);
            float radius = math.max(0.001f, state.RevealRadiusFraction);
            var playerSample = new int2(
                math.clamp((int)math.floor(localX * resolution), 0, resolution - 1),
                math.clamp((int)math.floor(localY * resolution), 0, resolution - 1));
            if (!ShouldReveal(in state, playerCell, playerSample, resolution, radius))
            {
                if (normalized)
                    systemState.EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            float centerLocalX = (playerSample.x + 0.5f) / resolution;
            float centerLocalY = (playerSample.y + 0.5f) / resolution;
            bool anyChanged = false;
            float radiusSq = radius * radius;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    float centerX = centerLocalX - ox;
                    float centerY = centerLocalY - oy;
                    if (!CircleTouchesUnitSquare(centerX, centerY, radius))
                        continue;

                    Entity tileEntity = EnsureTile(ref systemState, playerCell + new int2(ox, oy), resolution);
                    var tile = systemState.EntityManager.GetComponentData<ExteriorMapDiscoveryTile>(tileEntity);
                    var samples = systemState.EntityManager.GetBuffer<ExteriorMapDiscoverySample>(tileEntity);
                    if (Reveal(samples, resolution, centerX, centerY, radiusSq))
                    {
                        tile.Revision++;
                        tile.Dirty = 1;
                        systemState.EntityManager.SetComponentData(tileEntity, tile);
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
                state.Revision++;
            state.LastRevealCell = playerCell;
            state.LastRevealSample = playerSample;
            state.LastRevealMaskResolution = resolution;
            state.LastRevealRadiusFraction = radius;
            state.HasLastRevealSample = 1;

            systemState.EntityManager.SetComponentData(stateEntity, state);
        }

        Entity EnsureTile(ref SystemState systemState, int2 cell, int resolution)
        {
            if (_tileLookup.IsCreated
                && _tileLookup.TryGetValue(cell, out Entity existing)
                && systemState.EntityManager.Exists(existing)
                && systemState.EntityManager.HasComponent<ExteriorMapDiscoveryTile>(existing))
            {
                return existing;
            }

            RebuildTileLookup(ref systemState);
            if (_tileLookup.TryGetValue(cell, out existing)
                && systemState.EntityManager.Exists(existing)
                && systemState.EntityManager.HasComponent<ExteriorMapDiscoveryTile>(existing))
            {
                return existing;
            }

            var entity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.AddComponentData(entity, new ExteriorMapDiscoveryTile
            {
                Cell = cell,
                Revision = 1,
                Dirty = 1,
            });

            var buffer = systemState.EntityManager.AddBuffer<ExteriorMapDiscoverySample>(entity);
            buffer.ResizeUninitialized(resolution * resolution);
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = new ExteriorMapDiscoverySample { Alpha = byte.MaxValue };
            _tileLookup.TryAdd(cell, entity);
            return entity;
        }

        void RebuildTileLookup(ref SystemState systemState)
        {
            int count = math.max(64, _tileQuery.CalculateEntityCount());
            if (!_tileLookup.IsCreated || _tileLookup.Capacity < count)
            {
                if (_tileLookup.IsCreated)
                    _tileLookup.Dispose();
                _tileLookup = new NativeParallelHashMap<int2, Entity>(count, Allocator.Persistent);
            }
            else
            {
                _tileLookup.Clear();
            }

            foreach (var (tile, entity) in SystemAPI.Query<RefRO<ExteriorMapDiscoveryTile>>().WithEntityAccess())
                _tileLookup.TryAdd(tile.ValueRO.Cell, entity);
        }

        static bool Reveal(DynamicBuffer<ExteriorMapDiscoverySample> samples, int resolution, float centerX, float centerY, float radiusSq)
        {
            bool changed = false;
            for (int y = 0; y < resolution; y++)
            {
                float py = (y + 0.5f) / resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float px = (x + 0.5f) / resolution;
                    float dx = px - centerX;
                    float dy = py - centerY;
                    float t = math.saturate((dx * dx + dy * dy) / radiusSq);
                    byte alpha = (byte)math.round(t * byte.MaxValue);
                    int index = y * resolution + x;
                    if (alpha >= samples[index].Alpha)
                        continue;

                    samples[index] = new ExteriorMapDiscoverySample { Alpha = alpha };
                    changed = true;
                }
            }

            return changed;
        }

        static bool CircleTouchesUnitSquare(float centerX, float centerY, float radius)
        {
            float nearestX = math.clamp(centerX, 0f, 1f);
            float nearestY = math.clamp(centerY, 0f, 1f);
            float dx = centerX - nearestX;
            float dy = centerY - nearestY;
            return dx * dx + dy * dy <= radius * radius;
        }

        static bool ShouldReveal(
            in LocalMapDiscoveryState state,
            int2 playerCell,
            int2 playerSample,
            int resolution,
            float radius)
        {
            if (state.HasLastRevealSample == 0)
                return true;
            if (math.any(state.LastRevealCell != playerCell))
                return true;
            if (math.any(state.LastRevealSample != playerSample))
                return true;
            if (state.LastRevealMaskResolution != resolution)
                return true;
            return state.LastRevealRadiusFraction != radius;
        }

        static bool NormalizeState(ref LocalMapDiscoveryState state)
        {
            bool changed = false;
            if (state.MaskResolution <= 0)
            {
                state.MaskResolution = DefaultMaskResolution;
                changed = true;
            }
            if (state.RenderResolution <= 0)
            {
                state.RenderResolution = 256;
                changed = true;
            }
            if (state.RevealRadiusFraction <= 0f)
            {
                state.RevealRadiusFraction = DefaultRevealRadiusFraction;
                changed = true;
            }

            return changed;
        }
    }
}
