using Unity.Entities;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Holds every system that gates cell visibility by camera view radius. Runs inside
    /// the default <see cref="SimulationSystemGroup"/>, ahead of Entities.Graphics'
    /// presentation group so newly-activated cells render the same frame.
    ///
    /// Ordering inside this group:
    ///   1. <c>CameraCellTrackerSystem</c>  — reads Camera.main, writes StreamingConfig.
    ///   2. <c>CellScheduleSystem</c>       — Burst; produces LoadQueue + UnloadList.
    ///   3. <c>CellUnloadSystem</c>         — Burst; disables MMI for cells leaving view.
    ///   4. <c>CellLoadWorkerSystem</c>     — Burst; enables MMI for cells entering view.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
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
