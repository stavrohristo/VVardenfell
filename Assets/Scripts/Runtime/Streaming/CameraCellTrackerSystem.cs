using Unity.Burst;
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
    [BurstCompile]
    [UpdateInGroup(typeof(CellStreamingSystemGroup), OrderFirst = true)]
    public partial struct CameraCellTrackerSystem : ISystem
    {
        private EntityQuery _configQuery;
        private EntityQuery _viewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            // WithAllRW: GetSingletonRW requires the query to declare read-write intent.
            _configQuery = SystemAPI.QueryBuilder().WithAllRW<StreamingConfig>().Build();
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            systemState.RequireForUpdate(_configQuery);
            systemState.RequireForUpdate(_viewQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var cfg = _configQuery.GetSingletonRW<StreamingConfig>();
            if (cfg.ValueRO.ExteriorStreamingPaused)
                return;

            if (_viewQuery.IsEmptyIgnoreFilter)
                return;

            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var p = _viewQuery.GetSingleton<LocalToWorld>().Position;
            var coord = new int2(
                (int)math.floor(p.x / cellM),
                (int)math.floor(p.z / cellM));

            if (math.all(cfg.ValueRO.CameraCell == coord))
                return;

            cfg.ValueRW.CameraCell = coord;
        }
    }
}
