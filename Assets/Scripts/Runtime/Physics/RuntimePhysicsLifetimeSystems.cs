using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
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
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimePhysicsLifetimeBootstrapRequest>();
        }

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
            RuntimeBootstrapRequestUtility.Consume<RuntimePhysicsLifetimeBootstrapRequest>(EntityManager);
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

    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderLast = true)]
    public partial struct RuntimeColliderBlobDisposalSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DeferredRuntimeColliderBlobDisposal>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var buffer = SystemAPI.GetSingletonBuffer<DeferredRuntimeColliderBlobDisposal>();
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

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<DeferredRuntimeColliderBlobDisposal>())
                return;

            var buffer = SystemAPI.GetSingletonBuffer<DeferredRuntimeColliderBlobDisposal>();
            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                if (entry.Value.IsCreated)
                    entry.Value.Dispose();
            }

            buffer.Clear();
        }
    }
}
