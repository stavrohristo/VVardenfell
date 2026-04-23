using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Holds every system that gates cell visibility by camera view radius. Runs after
    /// transform updates so camera/view LocalToWorld reads are stable for the frame.
    ///
    /// Ordering inside this group:
    ///   1. <c>CameraCellTrackerSystem</c>  — reads Camera.main, writes StreamingConfig.
    ///   2. <c>CellScheduleSystem</c>       — Burst; produces LoadQueue + UnloadList.
    ///   3. <c>CellUnloadSystem</c>         — Burst; disables MMI for cells leaving view.
    ///   4. <c>CellLoadWorkerSystem</c>     — Burst; enables MMI for cells entering view.
    /// </summary>
    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindEnvironmentSystemGroup))]
    public partial class CellStreamingSystemGroup : ComponentSystemGroup
    {
        protected override void OnDestroy()
        {
            // Native collections held inside IComponentData singletons don't get auto-
            // disposed when the world tears down — we own their lifetime. Reach into each
            // singleton entity, dispose the containers, then let WorldResources drop
            // its managed references (textures/materials).
            WorldBootstrap.Uninstall();
            base.OnDestroy();
        }
    }
}
