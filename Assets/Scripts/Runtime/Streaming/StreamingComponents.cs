using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Singleton knobs for the streaming pipeline. Owned by the bootstrap entity.
    /// </summary>
    public struct StreamingConfig : IComponentData
    {
        public int ViewRadius;
        public int DistantTerrainRadius;
        public int MaxLoadsPerFrame;
        public int MaxUnloadsPerFrame;
        public bool GateTerrainByRadius;
        public bool ExteriorStreamingPaused;
        public int2 CameraCell;
    }

    /// <summary>
    /// Every exterior cell grid coord that the bake produced. Read-only after bootstrap.
    /// </summary>
    public struct AvailableCells : IComponentData
    {
        public NativeHashSet<int2> Set;
    }

    /// <summary>
    /// Resident exterior cell bookkeeping.
    /// </summary>
    public struct LoadedCellsMap : IComponentData
    {
        public NativeHashSet<int2> Streamed;
        public NativeHashSet<int2> Active;
        public NativeHashMap<int2, byte> SectionStates;
        public uint ActiveRevision;
    }

    public struct RuntimeSectionRegistry : IComponentData
    {
        public NativeHashMap<int2, Entity> ExteriorSections;
        public NativeHashMap<ulong, Entity> InteriorSectionsByHash;
        public NativeHashMap<ulong, FixedString128Bytes> InteriorCellIdsByHash;
    }

    public enum CellSectionLoadState : byte
    {
        Unloaded = 0,
        Loading = 1,
        LoadedInactive = 2,
        Active = 3,
        Failed = 4,
    }

    public struct LoadQueue : IComponentData
    {
        public NativeQueue<int2> Queue;
    }

    public struct UnloadList : IComponentData
    {
        public NativeList<int2> PendingEntityDestroy;
    }

    public struct PendingCellPhysicsLoad : IComponentData
    {
        public NativeList<int2> Cells;
    }

    public struct PendingCellPhysicsUnload : IComponentData
    {
        public NativeList<int2> Cells;
    }
}
