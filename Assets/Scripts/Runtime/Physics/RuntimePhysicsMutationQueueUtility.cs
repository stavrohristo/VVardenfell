using System;
using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    public static class RuntimePhysicsMutationQueueUtility
    {
        public static Entity RequireQueueEntity(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                ComponentType.ReadWrite<RuntimePhysicsMutationRequest>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation queue has not been created.");

            return query.GetSingletonEntity();
        }

        public static void EnqueueEnable(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;

            Entity queueEntity = RequireQueueEntity(entityManager);
            var buffer = entityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            EnqueueEnable(ref buffer, entity);
            MarkFlushRequested(entityManager, queueEntity);
        }

        public static void EnqueueDisable(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;

            Entity queueEntity = RequireQueueEntity(entityManager);
            var buffer = entityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            EnqueueDisable(ref buffer, entity);
            MarkFlushRequested(entityManager, queueEntity);
        }

        public static void EnqueueSetPhysicsCollider(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot queue an empty PhysicsCollider swap.");

            Entity queueEntity = RequireQueueEntity(entityManager);
            var buffer = entityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            EnqueueSetPhysicsCollider(ref buffer, entity, collider);
            MarkFlushRequested(entityManager, queueEntity);
        }

        public static bool QueueEnablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null)
                return false;

            Entity queueEntity = RequireQueueEntity(entityManager);
            ecb.AppendToBuffer(queueEntity, new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.Enable,
                Entity = entity,
            });
            QueueFlushRequested(entityManager, ref ecb, queueEntity);
            return true;
        }

        public static void QueueDisablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null)
                return;

            Entity queueEntity = RequireQueueEntity(entityManager);
            ecb.AppendToBuffer(queueEntity, new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.Disable,
                Entity = entity,
            });
            QueueFlushRequested(entityManager, ref ecb, queueEntity);
        }

        public static void QueueAttachSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary)
        {
            if (entity == Entity.Null || !collider.IsCreated)
                return;

            Entity queueEntity = RequireQueueEntity(entityManager);
            ecb.AppendToBuffer(queueEntity, new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.AttachSource,
                Entity = entity,
                Collider = collider,
                ColliderKind = kind,
                Active = active ? (byte)1 : (byte)0,
                Temporary = temporary ? (byte)1 : (byte)0,
            });
            QueueFlushRequested(entityManager, ref ecb, queueEntity);
        }

        public static void EnqueueEnable(ref DynamicBuffer<RuntimePhysicsMutationRequest> buffer, Entity entity)
        {
            if (entity == Entity.Null)
                return;

            buffer.Add(new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.Enable,
                Entity = entity,
            });
        }

        public static void EnqueueDisable(ref DynamicBuffer<RuntimePhysicsMutationRequest> buffer, Entity entity)
        {
            if (entity == Entity.Null)
                return;

            buffer.Add(new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.Disable,
                Entity = entity,
            });
        }

        public static void EnqueueSetPhysicsCollider(
            ref DynamicBuffer<RuntimePhysicsMutationRequest> buffer,
            Entity entity,
            BlobAssetReference<Collider> collider)
        {
            if (entity == Entity.Null)
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot queue an empty PhysicsCollider swap.");

            buffer.Add(new RuntimePhysicsMutationRequest
            {
                Kind = RuntimePhysicsMutationKind.SetPhysicsCollider,
                Entity = entity,
                Collider = collider,
            });
        }

        public static void MarkFlushRequested(EntityManager entityManager, Entity queueEntity)
        {
            if (!entityManager.HasComponent<PhysicsFlushRequested>(queueEntity))
                throw new InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation queue is missing PhysicsFlushRequested.");

            entityManager.SetComponentData(queueEntity, new PhysicsFlushRequested { Pending = 1 });
        }

        static void QueueFlushRequested(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity queueEntity)
        {
            if (!entityManager.HasComponent<PhysicsFlushRequested>(queueEntity))
                throw new InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation queue is missing PhysicsFlushRequested.");

            ecb.SetComponent(queueEntity, new PhysicsFlushRequested { Pending = 1 });
        }
    }
}
