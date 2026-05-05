using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Drains <see cref="LoadQueue"/> and flips <see cref="MaterialMeshInfo"/> on for every
    /// non-terrain entity whose <see cref="CellLink.Value"/> equals the dequeued coord.
    ///
    /// Every entity was Instantiated once at bootstrap by <see cref="WorldSpawner.SpawnAll"/>
    /// in a disabled state, so "loading" a cell is a pure bit-flip scan — no structural
    /// changes, no chunk-layout scrambling, same cost profile as eager mode.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellUnloadSystem))]
    public partial struct CellLoadWorkerSystem : ISystem
    {
        EntityQuery _singletonQuery;
        EntityQuery _refsOnlyQuery;
        ComponentTypeHandle<CellLink> _cellLinkHandle;
        ComponentTypeHandle<MaterialMeshInfo> _mmiHandle;

        public void OnCreate(ref SystemState state)
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAll<StreamingConfig, LoadQueue, LoadedCellsMap, LogicalRefLookup, PendingCellPhysicsLoad>()
                .Build();
            state.RequireForUpdate(_singletonQuery);

            // WithPresent<MaterialMeshInfo> (not WithAll) — by default queries built with
            // WithAll<IEnableableComponent> *exclude* entities whose enable bit is off.
            // Every ref is spawned disabled at bootstrap, so without WithPresent the query
            // finds zero entities and the re-enable loop is a no-op.
            // Terrain rendering is gated by TerrainFrustumVisibilitySystem against
            // LoadedCellsMap.Active, so cell loads only re-enable non-terrain refs and
            // static cell entities.
            _refsOnlyQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .WithNone<CellCoord>()
                .Build();
            _cellLinkHandle = state.GetComponentTypeHandle<CellLink>(isReadOnly: true);
            _mmiHandle = state.GetComponentTypeHandle<MaterialMeshInfo>(isReadOnly: false);
        }

        public void OnUpdate(ref SystemState state)
        {
            var queue = _singletonQuery.GetSingleton<LoadQueue>();
            if (queue.Queue.Count == 0) return;

            var cfg = _singletonQuery.GetSingleton<StreamingConfig>();
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var loadedEntity = _singletonQuery.GetSingletonEntity();
            uint startActiveRevision = loaded.ActiveRevision;
            var logicalRefs = _singletonQuery.GetSingleton<LogicalRefLookup>();
            var pendingPhysicsLoad = _singletonQuery.GetSingleton<PendingCellPhysicsLoad>();

            _cellLinkHandle.Update(ref state);
            state.EntityManager.CompleteDependencyBeforeRW<MaterialMeshInfo>();
            _mmiHandle.Update(ref state);

            int budget = cfg.MaxLoadsPerFrame;
            bool spawnedCellThisFrame = false;
            while (budget-- > 0 && queue.Queue.TryDequeue(out var coord))
            {
                if (loaded.Active.Contains(coord))
                    continue;

                if (!loaded.Streamed.Contains(coord))
                {
                    if (WorldSpawner.TrySpawnExteriorCellByCoord(
                            World.DefaultGameObjectInjectionWorld,
                            coord,
                            ref loaded,
                            ref logicalRefs,
                            active: true,
                            gateTerrainByRadius: cfg.GateTerrainByRadius))
                    {
                        QueuePhysicsCell(ref pendingPhysicsLoad.Cells, coord);
                        spawnedCellThisFrame = true;
                    }

                    if (spawnedCellThisFrame)
                        break;

                    continue;
                }

                new ToggleCellMmiJob
                {
                    Target = coord,
                    Enable = true,
                    LinkHandle = _cellLinkHandle,
                    MmiHandle = _mmiHandle,
                }.Run(_refsOnlyQuery);

                QueuePhysicsCell(ref pendingPhysicsLoad.Cells, coord);
                if (loaded.Active.Add(coord))
                    loaded.ActiveRevision++;
            }

            if (loaded.ActiveRevision != startActiveRevision)
            {
                state.EntityManager.SetComponentData(loadedEntity, loaded);
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(state.EntityManager);
            }
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

        /// <summary>
        /// Burst-compiled chunk scan that flips the <see cref="MaterialMeshInfo"/> enable
        /// bit on every entity in a given cell. Mirror of <c>CellUnloadSystem.ToggleMmiJob</c> —
        /// kept local here so both systems can hold their own handles.
        /// </summary>
        [BurstCompile]
        internal struct ToggleCellMmiJob : IJobChunk
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
