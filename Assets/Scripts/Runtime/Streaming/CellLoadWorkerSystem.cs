using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Drains <see cref="LoadQueue"/> and flips <see cref="MaterialMeshInfo"/> on for every
    /// ref + terrain entity whose <see cref="CellLink.Value"/> equals the dequeued coord.
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
        EntityQuery _refsOnlyQuery;  // excludes terrain (no CellCoord)
        EntityQuery _allQuery;       // refs + terrain
        EntityQuery _physicsLoadQuery;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<CellLink> _cellLinkHandle;
        ComponentTypeHandle<MaterialMeshInfo> _mmiHandle;
        ComponentTypeHandle<RuntimeColliderSource> _colliderSourceHandle;

        static readonly ProfilerMarker k_PhysicsLoad = new("VV.Streaming.PhysicsLoad");
        static readonly ProfilerMarker k_ActivateCollider = new("VV.Streaming.PhysicsLoad.ActivateCollider");

        public void OnCreate(ref SystemState state)
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAll<StreamingConfig, LoadQueue, LoadedCellsMap, LogicalRefLookup>()
                .Build();
            state.RequireForUpdate(_singletonQuery);

            // WithPresent<MaterialMeshInfo> (not WithAll) — by default queries built with
            // WithAll<IEnableableComponent> *exclude* entities whose enable bit is off.
            // Every ref is spawned disabled at bootstrap, so without WithPresent the query
            // finds zero entities and the re-enable loop is a no-op.
            //
            // Two query variants:
            //   _refsOnlyQuery — WithNone<CellCoord> leaves terrain alone (terrain is the
            //   only thing with CellCoord). Used when StreamingConfig.GateTerrainByRadius
            //   is false (default) — terrain stays permanently enabled.
            //   _allQuery — includes terrain; used when gating is enabled.
            _refsOnlyQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .WithNone<CellCoord>()
                .Build();
            _allQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .Build();
            _physicsLoadQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink, RuntimeColliderSource>()
                .WithNone<PhysicsCollider>()
                .Build();

            _entityHandle = state.GetEntityTypeHandle();
            _cellLinkHandle = state.GetComponentTypeHandle<CellLink>(isReadOnly: true);
            _mmiHandle      = state.GetComponentTypeHandle<MaterialMeshInfo>(isReadOnly: false);
            _colliderSourceHandle = state.GetComponentTypeHandle<RuntimeColliderSource>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var queue = _singletonQuery.GetSingleton<LoadQueue>();
            if (queue.Queue.Count == 0) return;

            var cfg    = _singletonQuery.GetSingleton<StreamingConfig>();
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var logicalRefs = _singletonQuery.GetSingleton<LogicalRefLookup>();

            _cellLinkHandle.Update(ref state);
            _mmiHandle.Update(ref state);
            _entityHandle.Update(ref state);
            _colliderSourceHandle.Update(ref state);


            var targetQuery = cfg.GateTerrainByRadius ? _allQuery : _refsOnlyQuery;
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            bool physicsChanged = false;

            try
            {
                int budget = cfg.MaxLoadsPerFrame;
                bool spawnedCellThisFrame = false;
                while (budget-- > 0 && queue.Queue.TryDequeue(out var coord))
                {
                    if (loaded.Active.Contains(coord))
                        continue;

                    if (!loaded.Map.ContainsKey(coord))
                    {
                        if (WorldResources.Cells.TryGetValue(coord, out var cellData) && cellData != null)
                        {
                            WorldSpawner.SpawnExteriorCell(
                                World.DefaultGameObjectInjectionWorld,
                                coord,
                                cellData,
                                ref loaded,
                                ref logicalRefs,
                                active: true,
                                gateTerrainByRadius: cfg.GateTerrainByRadius);
                            physicsChanged = true;
                            spawnedCellThisFrame = true;
                        }

                        if (spawnedCellThisFrame)
                            break;

                        continue;
                    }

                    new ToggleCellMmiJob
                    {
                        Target     = coord,
                        Enable     = true,
                        LinkHandle = _cellLinkHandle,
                        MmiHandle  = _mmiHandle,
                    }.Run(targetQuery);

                    using (k_PhysicsLoad.Auto())
                    {
                        using (k_ActivateCollider.Auto())
                        {
                            new ActivatePhysicsJob
                            {
                                Target = coord,
                                EntityHandle = _entityHandle,
                                LinkHandle = _cellLinkHandle,
                                SourceHandle = _colliderSourceHandle,
                                CommandBuf = ecb.AsParallelWriter(),
                            }.Run(_physicsLoadQuery);
                        }
                    }

                    physicsChanged = true;
                    loaded.Active.Add(coord);
                }
            }
            finally
            {
                if (physicsChanged)
                    ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
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

        [BurstCompile]
        internal struct ActivatePhysicsJob : IJobChunk
        {
            public int2 Target;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<CellLink> LinkHandle;
            [ReadOnly] public ComponentTypeHandle<RuntimeColliderSource> SourceHandle;
            public EntityCommandBuffer.ParallelWriter CommandBuf;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var links = chunk.GetNativeArray(ref LinkHandle);
                var sources = chunk.GetNativeArray(ref SourceHandle);
                int n = chunk.Count;
                for (int i = 0; i < n; i++)
                {
                    if (!links[i].Value.Equals(Target))
                        continue;

                    if (!sources[i].Value.IsCreated)
                        continue;

                    CommandBuf.AddComponent(unfilteredChunkIndex, entities[i], new PhysicsCollider
                    {
                        Value = sources[i].Value
                    });
                }
            }
        }
    }

}
