using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Singleton knobs for the streaming pipeline. Owned by the bootstrap entity.
    /// ViewRadius: cells kept resident in a (2r+1)² window.
    /// MaxLoadsPerFrame / MaxUnloadsPerFrame: amortisation budgets.
    /// CameraCell: updated every frame by <see cref="CameraCellTrackerSystem"/>.
    /// </summary>
    public struct StreamingConfig : IComponentData
    {
        public int ViewRadius;
        public int MaxLoadsPerFrame;
        public int MaxUnloadsPerFrame;
        public bool GateTerrainByRadius;
        public bool ExteriorStreamingPaused;
        public int2 CameraCell;
    }

    /// <summary>
    /// Every exterior cell grid coord that the bake produced. Read-only after
    /// bootstrap; used by <see cref="CellScheduleSystem"/> to filter the view window
    /// so we only queue cells that actually have baked data.
    /// </summary>
    public struct AvailableCells : IComponentData
    {
        public NativeHashSet<int2> Set;
    }

    /// <summary>
    /// Ever-loaded cells: coord → terrain entity (or Entity.Null if the cell had no terrain).
    /// Entries are appended in <see cref="Runtime.Streaming.CellLoadWorkerSystem"/> on first visit
    /// and **never removed** — unload now toggles the <see cref="Unity.Rendering.MaterialMeshInfo"/>
    /// enable bit on each entity instead of destroying it, so entities persist across unload/reload
    /// cycles and <see cref="Active"/> tracks who's currently rendering.
    ///
    /// Trade-off: memory grows with the set of cells ever visited (bounded in MW at ~1400). The
    /// win is no structural churn on unload → no chunk layout scrambling → stable BRG batching.
    /// </summary>
    public struct LoadedCellsMap : IComponentData
    {
        /// <summary>Every cell that has entities in ECS. Value = terrain entity (or Entity.Null).</summary>
        public NativeHashMap<int2, Entity> Map;

        /// <summary>Subset of <see cref="Map"/>: cells whose entities are currently rendering.</summary>
        public NativeHashSet<int2> Active;
    }

    /// <summary>
    /// Coords that <see cref="CellScheduleSystem"/> decided we should load.
    /// Managed side drains it in <see cref="Runtime.Streaming.CellLoadWorkerSystem"/>.
    /// </summary>
    public struct LoadQueue : IComponentData
    {
        public NativeQueue<int2> Queue;
    }

    /// <summary>
    /// Coords the scheduler marked for unload this frame. <see cref="CellUnloadSystem"/>
    /// drains the list, disabling MaterialMeshInfo on each cell's entities.
    /// </summary>
    public struct UnloadList : IComponentData
    {
        public NativeList<int2> PendingEntityDestroy; // filled by schedule, drained by unload
    }

    /// <summary>Diagnostic breadcrumb on terrain entities. Not read by systems.</summary>
    public struct CellCoord : IComponentData
    {
        public int2 Value;
    }

    /// <summary>
    /// Cell-membership tag present on every entity (terrain + refs) that belongs to a cell.
    /// Plain <see cref="IComponentData"/> on purpose — making this shared (keyed by <c>int2</c>)
    /// fragments the ECS archetype chunks one-per-cell, which in turn fragments BRG batches
    /// one-per-cell and explodes the ref draw-call count (e.g. 68k draws for 330k instances
    /// at ViewRadius=32). As a regular component, every ref lives in a handful of chunks
    /// regardless of cell, so BRG can merge same-<c>MaterialMeshInfo</c> runs across cells.
    ///
    /// Unload reads this per-entity in <see cref="CellUnloadSystem"/> — a Burst scan of
    /// ~300k entities is sub-millisecond and runs a few times a second at most.
    /// </summary>
    public struct CellLink : IComponentData
    {
        public int2 Value;
    }

}
