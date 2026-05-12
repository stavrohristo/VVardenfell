using Unity.Entities;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public static class RuntimeColliderAttachmentUtility
    {
        public static bool AttachSource(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.AttachSource(
                entityManager,
                entity,
                collider,
                kind,
                active,
                temporary);
        }

        public static bool QueueAttachSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.QueueAttachSource(
                entityManager,
                ref ecb,
                entity,
                collider,
                kind,
                active,
                temporary);
        }

        public static bool BindExistingSource(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool temporary = false)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.BindExistingSource(
                entityManager,
                entity,
                collider,
                kind,
                temporary);
        }

        public static void QueueAttachNewSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.QueueAttachNewSource(
                entityManager,
                ref ecb,
                entity,
                collider,
                kind,
                active,
                temporary);
        }

        public static void QueueAttachInstantiatedSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity prefab,
            Entity instance,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.QueueAttachInstantiatedSource(
                entityManager,
                ref ecb,
                prefab,
                instance,
                collider,
                kind,
                active,
                temporary);
        }

        public static bool EnablePhysics(EntityManager entityManager, Entity entity)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(entityManager, entity);
        }

        public static bool QueueEnablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.QueueEnablePhysics(entityManager, ref ecb, entity);
        }

        public static void DisablePhysics(EntityManager entityManager, Entity entity)
        {
            VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(entityManager, entity);
        }

        public static void QueueDisablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.QueueDisablePhysics(entityManager, ref ecb, entity);
        }
    }
}
