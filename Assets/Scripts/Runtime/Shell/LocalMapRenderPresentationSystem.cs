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
    public partial class LocalMapRenderPresentationSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _tileQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _tileQuery = GetEntityQuery(
                ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                ComponentType.ReadOnly<ExteriorMapDiscoverySample>());

            RequireForUpdate<LocalMapDiscoveryState>();
            RequireForUpdate<InteriorTransitionState>();
            RequireForUpdate<LoadedCellsMap>();
        }

        protected override void OnDestroy()
        {
            LocalMapPresentationCache.Dispose();
        }

        protected override void OnUpdate()
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

            using var entities = _tileQuery.ToEntityArray(Allocator.Temp);
            using var tiles = _tileQuery.ToComponentDataArray<ExteriorMapDiscoveryTile>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var tile = tiles[i];
                if (math.abs(tile.Cell.x - centerCell.x) > 1 || math.abs(tile.Cell.y - centerCell.y) > 1)
                    continue;

                var samples = EntityManager.GetBuffer<ExteriorMapDiscoverySample>(entities[i]);
                LocalMapPresentationCache.SyncShroudTexture(
                    tile.Cell,
                    tile.Revision,
                    new LocalMapPresentationCache.DynamicBufferReader(samples),
                    maskResolution);
            }

            LocalMapPresentationCache.RenderOnePendingTile(centerCell, renderResolution, loadedCells.Active);
        }
    }
}
