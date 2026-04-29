using Unity.Collections;
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

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(RuntimeColliderBlobDisposalSystem))]
    public partial class RuntimeGeneratedColliderBlobCleanupSystem : SystemBase
    {
        EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<RuntimeGeneratedColliderBlobCleanup>() },
                None = new[] { ComponentType.ReadOnly<RuntimeColliderSource>() },
            });
            RequireForUpdate(_query);
        }

        protected override void OnUpdate()
        {
            using var entities = _query.ToEntityArray(Allocator.Temp);
            using var cleanups = _query.ToComponentDataArray<RuntimeGeneratedColliderBlobCleanup>(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (cleanups[i].Value.IsCreated)
                    RuntimeColliderBlobLifetime.DeferGeneratedBlobDisposal(EntityManager, cleanups[i].Value);
                ecb.RemoveComponent<RuntimeGeneratedColliderBlobCleanup>(entities[i]);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeGeneratedColliderBlobCleanup>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var cleanups = query.ToComponentDataArray<RuntimeGeneratedColliderBlobCleanup>(Allocator.Temp);
            for (int i = 0; i < cleanups.Length; i++)
            {
                if (cleanups[i].Value.IsCreated)
                    cleanups[i].Value.Dispose();
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderLast = true)]
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

        protected override void OnDestroy()
        {
            if (_query.IsEmptyIgnoreFilter)
                return;

            var buffer = _query.GetSingletonBuffer<DeferredRuntimeColliderBlobDisposal>();
            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                if (entry.Value.IsCreated)
                    entry.Value.Dispose();
            }

            buffer.Clear();
        }
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup), OrderLast = true)]
    public partial class RuntimePhysicsLifetimeValidationSystem : SystemBase
    {
        int _lastInvalidSources = -1;

        protected override void OnCreate()
        {
            RequireForUpdate<RuntimePhysicsLifetimeState>();
        }

        protected override void OnUpdate()
        {
            int invalidSources = CountInvalidRuntimeColliderSources();
            bool invalidCountsChanged = _lastInvalidSources != invalidSources;
            _lastInvalidSources = invalidSources;

            if (invalidCountsChanged && invalidSources > 0)
            {
                Debug.LogWarning($"[VVardenfell][Physics] invalid collider sources detected: {invalidSources}.");
            }
        }

        int CountInvalidRuntimeColliderSources()
        {
            int invalidSources = 0;
            foreach (var source in SystemAPI.Query<RefRO<RuntimeColliderSource>>())
            {
                if (!source.ValueRO.Value.IsCreated)
                    invalidSources++;
            }

            return invalidSources;
        }
    }
}
