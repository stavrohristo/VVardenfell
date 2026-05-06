using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    [UpdateBefore(typeof(RuntimeHudShellPresentationSystem))]
    public partial struct LocalMapRenderPresentationSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _tileQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _tileQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                ComponentType.ReadOnly<ExteriorMapDiscoverySample>());

            systemState.RequireForUpdate<LocalMapDiscoveryState>();
            systemState.RequireForUpdate<InteriorTransitionState>();
            systemState.RequireForUpdate<LoadedCellsMap>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            LocalMapPresentationCache.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var discoveryState = SystemAPI.GetSingleton<LocalMapDiscoveryState>();
            if (SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0)
                return;
            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();

            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var centerCell = new int2(
                (int)math.floor(playerTransform.Position.x / cellMeters),
                (int)math.floor(playerTransform.Position.z / cellMeters));

            int maskResolution = discoveryState.MaskResolution <= 0 ? 64 : discoveryState.MaskResolution;
            int renderResolution = discoveryState.RenderResolution <= 0 ? 256 : discoveryState.RenderResolution;
            LocalMapPresentationCache.PrepareVisibleTiles(centerCell, renderResolution, maskResolution);

            foreach (var (tileRef, samples) in SystemAPI.Query<RefRO<ExteriorMapDiscoveryTile>, DynamicBuffer<ExteriorMapDiscoverySample>>())
            {
                var tile = tileRef.ValueRO;
                if (math.abs(tile.Cell.x - centerCell.x) > 1 || math.abs(tile.Cell.y - centerCell.y) > 1)
                    continue;

                LocalMapPresentationCache.SyncShroudTexture(
                    tile.Cell,
                    tile.Revision,
                    new LocalMapPresentationCache.DynamicBufferReader(samples),
                    maskResolution);
            }

            systemState.EntityManager.CompleteDependencyBeforeRO<Unity.Rendering.MaterialMeshInfo>();
            LocalMapPresentationCache.RenderOnePendingTile(centerCell, renderResolution, loadedCells.Active);
        }
    }
}
