using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Reads <see cref="Camera.main"/>'s world position each frame and writes the
    /// corresponding cell coord into <see cref="StreamingConfig.CameraCell"/>.
    /// Managed because Camera.main / Transform.position are MonoBehaviour APIs.
    /// Everything downstream (schedule, unload) is Burst and consumes this singleton.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup), OrderFirst = true)]
    public partial class CameraCellTrackerSystem : SystemBase
    {
        private EntityQuery _configQuery;

        protected override void OnCreate()
        {
            // WithAllRW: GetSingletonRW requires the query to declare read-write intent.
            _configQuery = SystemAPI.QueryBuilder().WithAllRW<StreamingConfig>().Build();
            RequireForUpdate(_configQuery);
        }

        protected override void OnUpdate()
        {
            var cfg = _configQuery.GetSingletonRW<StreamingConfig>();
            if (cfg.ValueRO.ExteriorStreamingPaused)
                return;

            var cam = Camera.main;
            if (cam == null) return;

            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var p = cam.transform.position;
            var coord = new int2(
                (int)math.floor(p.x / cellM),
                (int)math.floor(p.z / cellM));

            cfg.ValueRW.CameraCell = coord;
        }
    }
}
