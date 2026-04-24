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
    public partial class LocalMapDiscoverySystem : SystemBase
    {
        const int DefaultMaskResolution = 64;
        const float DefaultRevealRadiusFraction = 0.17f;

        EntityQuery _playerQuery;
        EntityQuery _tileQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _tileQuery = GetEntityQuery(
                ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                ComponentType.ReadWrite<ExteriorMapDiscoverySample>());

            RequireForUpdate<LocalMapDiscoveryState>();
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<InteriorTransitionState>();
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
            if (transition.InteriorActive != 0)
                return;

            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            var stateEntity = SystemAPI.GetSingletonEntity<LocalMapDiscoveryState>();
            var state = EntityManager.GetComponentData<LocalMapDiscoveryState>(stateEntity);
            NormalizeState(ref state);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var playerCell = new int2(
                (int)math.floor(playerTransform.Position.x / cellMeters),
                (int)math.floor(playerTransform.Position.z / cellMeters));
            float localX = math.saturate((playerTransform.Position.x - playerCell.x * cellMeters) / cellMeters);
            float localY = math.saturate((playerTransform.Position.z - playerCell.y * cellMeters) / cellMeters);

            bool anyChanged = false;
            int resolution = math.max(1, state.MaskResolution);
            float radius = math.max(0.001f, state.RevealRadiusFraction);
            float radiusSq = radius * radius;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    float centerX = localX - ox;
                    float centerY = localY - oy;
                    if (!CircleTouchesUnitSquare(centerX, centerY, radius))
                        continue;

                    Entity tileEntity = EnsureTile(playerCell + new int2(ox, oy), resolution);
                    var tile = EntityManager.GetComponentData<ExteriorMapDiscoveryTile>(tileEntity);
                    var samples = EntityManager.GetBuffer<ExteriorMapDiscoverySample>(tileEntity);
                    if (Reveal(samples, resolution, centerX, centerY, radiusSq))
                    {
                        tile.Revision++;
                        tile.Dirty = 1;
                        EntityManager.SetComponentData(tileEntity, tile);
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
                state.Revision++;

            EntityManager.SetComponentData(stateEntity, state);
        }

        Entity EnsureTile(int2 cell, int resolution)
        {
            using var entities = _tileQuery.ToEntityArray(Allocator.Temp);
            using var tiles = _tileQuery.ToComponentDataArray<ExteriorMapDiscoveryTile>(Allocator.Temp);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i].Cell.x == cell.x && tiles[i].Cell.y == cell.y)
                    return entities[i];
            }

            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, $"LocalMapDiscovery({cell.x},{cell.y})");
            EntityManager.AddComponentData(entity, new ExteriorMapDiscoveryTile
            {
                Cell = cell,
                Revision = 1,
                Dirty = 1,
            });

            var buffer = EntityManager.AddBuffer<ExteriorMapDiscoverySample>(entity);
            buffer.ResizeUninitialized(resolution * resolution);
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = new ExteriorMapDiscoverySample { Alpha = byte.MaxValue };
            return entity;
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

        static void NormalizeState(ref LocalMapDiscoveryState state)
        {
            if (state.MaskResolution <= 0)
                state.MaskResolution = DefaultMaskResolution;
            if (state.RenderResolution <= 0)
                state.RenderResolution = 256;
            if (state.RevealRadiusFraction <= 0f)
                state.RevealRadiusFraction = DefaultRevealRadiusFraction;
        }
    }
}
