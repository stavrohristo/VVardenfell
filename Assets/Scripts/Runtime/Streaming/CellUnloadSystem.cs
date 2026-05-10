using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// "Unload" a cell by flipping the <see cref="MaterialMeshInfo"/> enable bit off on
    /// every entity with <c>CellLink.Value == coord</c>. BRG skips entities whose MMI is
    /// disabled, so rendering stops immediately. Entities stay in their chunks — no
    /// structural change, no chunk-layout scrambling, no re-Instantiate cost when the
    /// camera returns (just another bit flip to re-enable).
    ///
    /// Trade-off: memory grows with the set of cells ever visited (bounded in MW at
    /// ~1400 cells). This is the deliberate tradeoff for stable chunk layout — eager
    /// mode hits ~2× the frame rate because its entities never move.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellScheduleSystem))]
    public partial struct CellUnloadSystem : ISystem
    {
        EntityQuery _singletonQuery;
        EntityQuery _refsOnlyQuery;
        ComponentTypeHandle<CellLink> _cellLinkHandle;
        ComponentTypeHandle<MaterialMeshInfo> _mmiHandle;

        public void OnCreate(ref SystemState state)
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAll<LoadedCellsMap, UnloadList, PendingCellPhysicsUnload>()
                .Build();
            state.RequireForUpdate(_singletonQuery);

            // WithPresent<MaterialMeshInfo> (not WithAll) — queries with WithAll on an
            // IEnableableComponent filter out entities whose enable bit is off. Mirrors
            // the load worker's query; see its OnCreate for the full reasoning.
            // Terrain rendering is gated by TerrainFrustumVisibilitySystem against
            // LoadedCellsMap.Active, so streaming unload only touches non-terrain refs
            // and static cell entities.
            _refsOnlyQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .WithNone<CombinedCellRenderSuppressed>()
                .WithNone<CellCoord>()
                .Build();
            _cellLinkHandle = state.GetComponentTypeHandle<CellLink>(isReadOnly: true);
            _mmiHandle = state.GetComponentTypeHandle<MaterialMeshInfo>(isReadOnly: false);
        }

        public void OnUpdate(ref SystemState state)
        {
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var loadedEntity = _singletonQuery.GetSingletonEntity();
            var unload = _singletonQuery.GetSingleton<UnloadList>();
            var pendingPhysicsUnload = _singletonQuery.GetSingleton<PendingCellPhysicsUnload>();
            if (unload.PendingEntityDestroy.Length == 0) return;

            state.EntityManager.CompleteDependencyBeforeRW<MaterialMeshInfo>();
            _cellLinkHandle.Update(ref state);
            _mmiHandle.Update(ref state);
            bool activeChanged = false;
            for (int i = 0; i < unload.PendingEntityDestroy.Length; i++)
            {
                var coord = unload.PendingEntityDestroy[i];

                new ToggleMmiJob
                {
                    Target = coord,
                    Enable = false,
                    LinkHandle = _cellLinkHandle,
                    MmiHandle = _mmiHandle,
                }.Run(_refsOnlyQuery);

                QueuePhysicsCell(ref pendingPhysicsUnload.Cells, coord);
                if (loaded.Active.Remove(coord))
                {
                    loaded.ActiveRevision++;
                    activeChanged = true;
                }
                if (loaded.SectionStates.IsCreated
                    && (!loaded.SectionStates.TryGetValue(coord, out byte rawState) || rawState != (byte)CellSectionLoadState.Failed))
                {
                    loaded.SectionStates[coord] = (byte)CellSectionLoadState.LoadedInactive;
                }
                // Managed resources (terrain Mesh/Texture/Material) stay alive across
                // enable/disable cycles — we only toggle render/physics state.
            }

            if (activeChanged)
            {
                state.EntityManager.SetComponentData(loadedEntity, loaded);
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(state.EntityManager);
            }
            unload.PendingEntityDestroy.Clear();
        }

        static void QueuePhysicsCell(ref NativeList<int2> pendingCells, int2 coord)
        {
            for (int i = 0; i < pendingCells.Length; i++)
            {
                if (pendingCells[i].Equals(coord))
                    return;
            }

            pendingCells.Add(coord);
        }

        [BurstCompile]
        private struct ToggleMmiJob : IJobChunk
        {
            public int2 Target;
            public bool Enable;
            [ReadOnly] public ComponentTypeHandle<CellLink> LinkHandle;
            public ComponentTypeHandle<MaterialMeshInfo> MmiHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var links = chunk.GetNativeArray(ref LinkHandle);
                int n = chunk.Count;
                for (int i = 0; i < n; i++)
                {
                    if (links[i].Value.Equals(Target))
                        chunk.SetComponentEnabled(ref MmiHandle, i, Enable);
                }
            }
        }

    }
}
