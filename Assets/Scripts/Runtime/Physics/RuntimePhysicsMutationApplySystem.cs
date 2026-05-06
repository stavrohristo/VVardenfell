using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup), OrderLast = true)]
    public partial struct RuntimePhysicsMutationApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimePhysicsMutationQueueTag>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            systemState.Dependency.Complete();

            Entity queueEntity = SystemAPI.GetSingletonEntity<RuntimePhysicsMutationQueueTag>();
            var mutations = systemState.EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            var flush = systemState.EntityManager.GetComponentData<PhysicsFlushRequested>(queueEntity);
            if (mutations.Length == 0 && flush.Pending == 0)
                return;

            using var snapshot = mutations.ToNativeArray(Allocator.Temp);
            mutations.Clear();
            systemState.EntityManager.SetComponentData(queueEntity, new PhysicsFlushRequested());

            for (int i = 0; i < snapshot.Length; i++)
                Apply(ref systemState, snapshot[i]);

        }

        void Apply(ref SystemState systemState, in RuntimePhysicsMutationRequest request)
        {
            switch (request.Kind)
            {
                case RuntimePhysicsMutationKind.Enable:
                    RuntimeColliderPhysicsUtility.EnablePhysics(systemState.EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.Disable:
                    RuntimeColliderPhysicsUtility.DisablePhysics(systemState.EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.AttachSource:
                    RuntimeColliderPhysicsUtility.AttachSource(
                        systemState.EntityManager,
                        request.Entity,
                        request.Collider,
                        request.ColliderKind,
                        request.Active != 0,
                        request.Temporary != 0);
                    break;
                case RuntimePhysicsMutationKind.SetPhysicsCollider:
                    ApplyPhysicsColliderSwap(ref systemState, request.Entity, request.Collider);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][Physics] Unsupported runtime physics mutation kind {request.Kind}.");
            }
        }

        void ApplyPhysicsColliderSwap(ref SystemState systemState, Entity entity, BlobAssetReference<Collider> collider)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot apply an empty PhysicsCollider swap.");

            var physicsCollider = new PhysicsCollider { Value = collider };
            if (systemState.EntityManager.HasComponent<PhysicsCollider>(entity))
            {
                systemState.EntityManager.SetComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: true);
            }
            else
            {
                systemState.EntityManager.AddComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: true);
            }

            if (!systemState.EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                systemState.EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: false);
            if (!systemState.EntityManager.HasComponent<PhysicsVelocity>(entity))
                systemState.EntityManager.AddComponentData(entity, new PhysicsVelocity());
        }

    }
}
