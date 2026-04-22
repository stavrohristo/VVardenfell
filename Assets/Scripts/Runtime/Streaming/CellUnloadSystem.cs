using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;

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
        EntityQuery _cfgQuery;
        EntityQuery _refsOnlyQuery;  // excludes terrain (no CellCoord)
        EntityQuery _allQuery;       // refs + terrain
        EntityQuery _refPhysicsUnloadQuery;
        EntityQuery _cellPhysicsUnloadQuery;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<CellLink> _cellLinkHandle;
        ComponentTypeHandle<MaterialMeshInfo> _mmiHandle;
        static readonly ProfilerMarker k_PhysicsUnload = new("VV.Streaming.PhysicsUnload");
        static readonly ProfilerMarker k_DeactivateRefCollider = new("VV.Streaming.PhysicsUnload.DeactivateRefCollider");
        static readonly ProfilerMarker k_DeactivateCellCollider = new("VV.Streaming.PhysicsUnload.DeactivateCellCollider");

        public void OnCreate(ref SystemState state)
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAll<LoadedCellsMap, UnloadList>()
                .Build();
            _cfgQuery = SystemAPI.QueryBuilder().WithAll<StreamingConfig>().Build();
            state.RequireForUpdate(_singletonQuery);

            // WithPresent<MaterialMeshInfo> (not WithAll) — queries with WithAll on an
            // IEnableableComponent filter out entities whose enable bit is off. Mirrors
            // the load worker's query; see its OnCreate for the full reasoning.
            //
            // Two variants: _refsOnlyQuery excludes terrain (WithNone<CellCoord>) so
            // terrain stays visible when StreamingConfig.GateTerrainByRadius is false;
            // _allQuery includes terrain for when gating is on.
            _refsOnlyQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .WithNone<CellCoord>()
                .Build();
            _allQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink>()
                .WithPresent<MaterialMeshInfo>()
                .Build();
            _refPhysicsUnloadQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink, RefCollisionSource, PhysicsCollider>()
                .Build();
            _cellPhysicsUnloadQuery = SystemAPI.QueryBuilder()
                .WithAll<CellLink, StoredPhysicsColliderBlob, PhysicsCollider>()
                .Build();

            _entityHandle = state.GetEntityTypeHandle();
            _cellLinkHandle = state.GetComponentTypeHandle<CellLink>(isReadOnly: true);
            _mmiHandle      = state.GetComponentTypeHandle<MaterialMeshInfo>(isReadOnly: false);
        }

        public void OnUpdate(ref SystemState state)
        {
            var loaded = _singletonQuery.GetSingleton<LoadedCellsMap>();
            var unload = _singletonQuery.GetSingleton<UnloadList>();
            if (unload.PendingEntityDestroy.Length == 0) return;

            var cfg = _cfgQuery.GetSingleton<StreamingConfig>();
            var targetQuery = cfg.GateTerrainByRadius ? _allQuery : _refsOnlyQuery;

            _cellLinkHandle.Update(ref state);
            _mmiHandle.Update(ref state);
            _entityHandle.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            try
            {
                for (int i = 0; i < unload.PendingEntityDestroy.Length; i++)
                {
                    var coord = unload.PendingEntityDestroy[i];

                    new ToggleMmiJob
                    {
                        Target      = coord,
                        Enable      = false,
                        LinkHandle  = _cellLinkHandle,
                        MmiHandle   = _mmiHandle,
                    }.Run(targetQuery);

                    using (k_PhysicsUnload.Auto())
                    {
                        using (k_DeactivateRefCollider.Auto())
                        {
                            new RemovePhysicsColliderJob
                            {
                                Target = coord,
                                EntityHandle = _entityHandle,
                                LinkHandle = _cellLinkHandle,
                                CommandBuf = ecb.AsParallelWriter(),
                            }.Run(_refPhysicsUnloadQuery);
                        }

                        using (k_DeactivateCellCollider.Auto())
                        {
                            new RemovePhysicsColliderJob
                            {
                                Target = coord,
                                EntityHandle = _entityHandle,
                                LinkHandle = _cellLinkHandle,
                                CommandBuf = ecb.AsParallelWriter(),
                            }.Run(_cellPhysicsUnloadQuery);
                        }
                    }

                    loaded.Active.Remove(coord);
                    // Managed resources (terrain Mesh/Texture/Material) stay alive across
                    // enable/disable cycles — we only toggle render/physics state.
                }
            }
            finally
            {
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }

            unload.PendingEntityDestroy.Clear();
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

        [BurstCompile]
        private struct RemovePhysicsColliderJob : IJobChunk
        {
            public int2 Target;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<CellLink> LinkHandle;
            public EntityCommandBuffer.ParallelWriter CommandBuf;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var links = chunk.GetNativeArray(ref LinkHandle);
                int n = chunk.Count;
                for (int i = 0; i < n; i++)
                {
                    if (!links[i].Value.Equals(Target))
                        continue;

                    CommandBuf.RemoveComponent<PhysicsCollider>(unfilteredChunkIndex, entities[i]);
                }
            }
        }
    }
}
