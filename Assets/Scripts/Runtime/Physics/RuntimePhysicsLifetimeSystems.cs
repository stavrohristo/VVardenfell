using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    /// <summary>
    /// Runtime physics ownership rules:
    /// - World/cache collider blobs are owned by WorldResources and disposed only on world teardown.
    /// - Player stance collider blobs are owned by player teardown.
    /// - Generated temporary blobs, such as activation proxy colliders, must be queued here
    ///   for deferred disposal after Unity Physics has rebuilt without entities that used them.
    /// </summary>
    public struct DeferredRuntimeColliderBlobDisposal : IBufferElementData
    {
        public BlobAssetReference<Collider> Value;
        public int PhysicsTicksRemaining;
    }

    public struct RuntimePhysicsLifetimeState : IComponentData
    {
        public int PlayerColliders;
        public int TerrainColliders;
        public int StaticCellColliders;
        public int PlacedRefColliders;
        public int ActivationProxyColliders;
        public int RuntimeSpawnColliders;
        public int InvalidColliderSources;
        public int PendingTemporaryBlobDisposals;
        public uint SampleSequence;
    }

    public static class RuntimePhysicsLifetimeDiagnostics
    {
        public static bool LogNextSample;

        public static string Describe(in RuntimePhysicsLifetimeState state)
        {
            return $"physics lifetime: seq={state.SampleSequence}, player={state.PlayerColliders}, "
                + $"placedRefs={state.PlacedRefColliders}, staticCells={state.StaticCellColliders}, "
                + $"terrain={state.TerrainColliders}, proxies={state.ActivationProxyColliders}, "
                + $"runtimeSpawns={state.RuntimeSpawnColliders}, invalidSources={state.InvalidColliderSources}, "
                + $"pendingTempBlobDisposals={state.PendingTemporaryBlobDisposals}";
        }
    }

    public static class RuntimeColliderBlobLifetime
    {
        const int DefaultDeferredPhysicsTicks = 2;

        public static void DeferGeneratedBlobDisposal(EntityManager entityManager, BlobAssetReference<Collider> collider)
        {
            if (!collider.IsCreated)
                return;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<DeferredRuntimeColliderBlobDisposal>());
            if (query.IsEmptyIgnoreFilter)
            {
                Debug.LogWarning("[VVardenfell][Physics] generated collider blob disposal requested before runtime physics lifetime state was ready.");
                return;
            }

            var buffer = entityManager.GetBuffer<DeferredRuntimeColliderBlobDisposal>(query.GetSingletonEntity());
            buffer.Add(new DeferredRuntimeColliderBlobDisposal
            {
                Value = collider,
                PhysicsTicksRemaining = DefaultDeferredPhysicsTicks,
            });
        }
    }

    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class RuntimePhysicsLifetimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity entity;
            if (SystemAPI.HasSingleton<RuntimePhysicsLifetimeState>())
            {
                entity = SystemAPI.GetSingletonEntity<RuntimePhysicsLifetimeState>();
            }
            else
            {
                entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.RuntimePhysicsLifetime");
                EntityManager.AddComponentData(entity, new RuntimePhysicsLifetimeState());
            }

            if (!EntityManager.HasBuffer<DeferredRuntimeColliderBlobDisposal>(entity))
                EntityManager.AddBuffer<DeferredRuntimeColliderBlobDisposal>(entity);
        }
    }

    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup), OrderLast = true)]
    public partial class RuntimeColliderBlobDisposalSystem : SystemBase
    {
        EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(ComponentType.ReadWrite<DeferredRuntimeColliderBlobDisposal>());
            RequireForUpdate(_query);
        }

        protected override void OnUpdate()
        {
            var buffer = _query.GetSingletonBuffer<DeferredRuntimeColliderBlobDisposal>();
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                var entry = buffer[i];
                entry.PhysicsTicksRemaining--;
                if (entry.PhysicsTicksRemaining > 0)
                {
                    buffer[i] = entry;
                    continue;
                }

                if (entry.Value.IsCreated)
                    entry.Value.Dispose();
                buffer.RemoveAt(i);
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup), OrderLast = true)]
    public partial class RuntimePhysicsLifetimeDiagnosticsSystem : SystemBase
    {
        int _lastInvalidSources = -1;
        EntityQuery _stateQuery;
        EntityQuery _pendingDisposalQuery;
        EntityQuery _playerQuery;
        EntityQuery _sourceQuery;

        protected override void OnCreate()
        {
            _stateQuery = GetEntityQuery(ComponentType.ReadWrite<RuntimePhysicsLifetimeState>());
            _pendingDisposalQuery = GetEntityQuery(ComponentType.ReadOnly<DeferredRuntimeColliderBlobDisposal>());
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PhysicsCollider>());
            _sourceQuery = GetEntityQuery(ComponentType.ReadOnly<RuntimeColliderSource>());

            RequireForUpdate(_stateQuery);
        }

        protected override void OnUpdate()
        {
            var state = _stateQuery.GetSingletonRW<RuntimePhysicsLifetimeState>();
            ref var value = ref state.ValueRW;
            value.SampleSequence++;
            value.PlayerColliders = _playerQuery.CalculateEntityCount();
            CountRuntimeColliderSources(
                out value.TerrainColliders,
                out value.StaticCellColliders,
                out value.PlacedRefColliders,
                out value.ActivationProxyColliders,
                out value.RuntimeSpawnColliders,
                out value.InvalidColliderSources);
            value.PendingTemporaryBlobDisposals = _pendingDisposalQuery.IsEmptyIgnoreFilter
                ? 0
                : _pendingDisposalQuery.GetSingletonBuffer<DeferredRuntimeColliderBlobDisposal>(true).Length;

            if (RuntimePhysicsLifetimeDiagnostics.LogNextSample)
            {
                RuntimePhysicsLifetimeDiagnostics.LogNextSample = false;
                Debug.Log($"[VVardenfell][Physics] {RuntimePhysicsLifetimeDiagnostics.Describe(value)}");
            }

            bool invalidCountsChanged = _lastInvalidSources != value.InvalidColliderSources;
            _lastInvalidSources = value.InvalidColliderSources;

            if (invalidCountsChanged && value.InvalidColliderSources > 0)
            {
                Debug.LogWarning($"[VVardenfell][Physics] invalid collider sources detected: {value.InvalidColliderSources}.");
            }
        }

        void CountRuntimeColliderSources(
            out int terrain,
            out int staticCells,
            out int placedRefs,
            out int activationProxies,
            out int runtimeSpawns,
            out int invalidSources)
        {
            terrain = 0;
            staticCells = 0;
            placedRefs = 0;
            activationProxies = 0;
            runtimeSpawns = 0;
            invalidSources = 0;
            using var entities = _sourceQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var sources = _sourceQuery.ToComponentDataArray<RuntimeColliderSource>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < sources.Length; i++)
            {
                if (!sources[i].Value.IsCreated)
                {
                    invalidSources++;
                    continue;
                }

                if (!EntityManager.HasComponent<PhysicsCollider>(entities[i]))
                    continue;

                switch (sources[i].Kind)
                {
                    case RuntimeColliderKind.TerrainCell:
                        terrain++;
                        break;
                    case RuntimeColliderKind.StaticCell:
                        staticCells++;
                        break;
                    case RuntimeColliderKind.PlacedRef:
                        placedRefs++;
                        break;
                    case RuntimeColliderKind.ActivationProxy:
                        activationProxies++;
                        break;
                    case RuntimeColliderKind.RuntimeSpawn:
                        runtimeSpawns++;
                        break;
                }
            }
        }
    }
}
