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
    public partial class RuntimePhysicsMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimePhysicsMutationQueueTag>();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            Entity queueEntity = SystemAPI.GetSingletonEntity<RuntimePhysicsMutationQueueTag>();
            var mutations = EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            var flush = EntityManager.GetComponentData<PhysicsFlushRequested>(queueEntity);
            if (mutations.Length == 0 && flush.Pending == 0)
                return;

            using var snapshot = mutations.ToNativeArray(Allocator.Temp);
            mutations.Clear();
            EntityManager.SetComponentData(queueEntity, new PhysicsFlushRequested());

            for (int i = 0; i < snapshot.Length; i++)
                Apply(snapshot[i]);

        }

        void Apply(in RuntimePhysicsMutationRequest request)
        {
            switch (request.Kind)
            {
                case RuntimePhysicsMutationKind.Enable:
                    RuntimeColliderPhysicsUtility.EnablePhysics(EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.Disable:
                    RuntimeColliderPhysicsUtility.DisablePhysics(EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.AttachSource:
                    RuntimeColliderPhysicsUtility.AttachSource(
                        EntityManager,
                        request.Entity,
                        request.Collider,
                        request.ColliderKind,
                        request.Active != 0,
                        request.Temporary != 0);
                    break;
                case RuntimePhysicsMutationKind.SetPhysicsCollider:
                    ApplyPhysicsColliderSwap(request.Entity, request.Collider);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][Physics] Unsupported runtime physics mutation kind {request.Kind}.");
            }
        }

        void ApplyPhysicsColliderSwap(Entity entity, BlobAssetReference<Collider> collider)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot apply an empty PhysicsCollider swap.");

            var physicsCollider = new PhysicsCollider { Value = collider };
            if (EntityManager.HasComponent<PhysicsCollider>(entity))
            {
                EntityManager.SetComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: true);
            }
            else
            {
                EntityManager.AddComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: true);
            }

            if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: false);
            if (!EntityManager.HasComponent<PhysicsVelocity>(entity))
                EntityManager.AddComponentData(entity, new PhysicsVelocity());
        }

    }
}
