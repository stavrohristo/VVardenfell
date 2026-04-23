using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Tracks the player view cell for exterior streaming. Menu/bootstrap cameras must not
    /// drive world streaming, otherwise the real player start cell can unload before spawn.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup), OrderFirst = true)]
    public partial class CameraCellTrackerSystem : SystemBase
    {
        private EntityQuery _configQuery;
        private EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            // WithAllRW: GetSingletonRW requires the query to declare read-write intent.
            _configQuery = SystemAPI.QueryBuilder().WithAllRW<StreamingConfig>().Build();
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            RequireForUpdate(_configQuery);
        }

        protected override void OnUpdate()
        {
            var cfg = _configQuery.GetSingletonRW<StreamingConfig>();
            if (cfg.ValueRO.ExteriorStreamingPaused)
                return;

            if (_viewQuery.IsEmptyIgnoreFilter)
                return;

            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var p = _viewQuery.GetSingleton<LocalToWorld>().Position;
            var coord = new int2(
                (int)math.floor(p.x / cellM),
                (int)math.floor(p.z / cellM));

            cfg.ValueRW.CameraCell = coord;
        }
    }
}
