using System;
using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    public static class RuntimePhysicsMutationQueueUtility
    {
        static World s_QueryWorld;
        static EntityQuery s_QueueQuery;
        static bool s_QueueQueryCreated;

        public static Entity RequireQueueEntity(EntityManager entityManager)
        {
            EntityQuery query = GetQueueQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][Physics] Runtime physics mutation queue has not been created.");

            return query.GetSingletonEntity();
        }

        static EntityQuery GetQueueQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_QueueQueryCreated && s_QueryWorld == world)
                return s_QueueQuery;

            if (s_QueueQueryCreated)
                s_QueueQuery.Dispose();

            s_QueryWorld = world;
            s_QueueQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                ComponentType.ReadWrite<RuntimePhysicsMutationRequest>());
            s_QueueQueryCreated = true;
            return s_QueueQuery;
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
