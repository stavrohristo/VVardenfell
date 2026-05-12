using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Holds every system that gates cell visibility by camera view radius. Runs after
    /// transform updates so camera/view LocalToWorld reads are stable for the frame.
    ///
    /// Ordering inside this group:
    ///   1. <c>CameraCellTrackerSystem</c>  — reads the player view entity, writes StreamingConfig.
    ///   2. <c>CellScheduleSystem</c>       — Burst; produces LoadQueue + UnloadList.
    ///   3. <c>CellUnloadSystem</c>         — Burst; disables MMI for cells leaving view.
    ///   4. <c>CellLoadWorkerSystem</c>     — Burst; enables MMI for cells entering view.
    /// </summary>
    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class CellStreamingSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override void OnDestroy()
        {
            // Native collections and managed resources held by bootstrap-owned ECS
            // singletons don't get auto-disposed when the world tears down.
            WorldBootstrap.Uninstall();
            base.OnDestroy();
        }
    }
}
