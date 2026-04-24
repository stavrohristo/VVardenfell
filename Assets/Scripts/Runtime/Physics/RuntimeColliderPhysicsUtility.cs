using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    public static class RuntimeColliderPhysicsUtility
    {
        public static bool AttachSource(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !collider.IsCreated)
                return false;

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                entityManager.SetComponentData(entity, source);
            else
                entityManager.AddComponentData(entity, source);

            if (active)
                return EnablePhysics(entityManager, entity);

            DisablePhysics(entityManager, entity);
            return true;
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
            if (entity == Entity.Null || !entityManager.Exists(entity) || !collider.IsCreated)
                return false;

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                ecb.SetComponent(entity, source);
            else
                ecb.AddComponent(entity, source);

            if (active)
                return QueueEnablePhysics(entityManager, ref ecb, entity);

            QueueDisablePhysics(entityManager, ref ecb, entity);
            return true;
        }

        public static void QueueAttachNewSource(
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (entity == Entity.Null || !collider.IsCreated)
                return;

            ecb.AddComponent(entity, new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            });

            if (active)
            {
                ecb.AddComponent(entity, new PhysicsCollider { Value = collider });
                ecb.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }
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
            if (instance == Entity.Null || !collider.IsCreated)
                return;

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(prefab))
                ecb.SetComponent(instance, source);
            else
                ecb.AddComponent(instance, source);

            var physicsCollider = new PhysicsCollider { Value = collider };
            if (active)
            {
                if (entityManager.HasComponent<PhysicsCollider>(prefab))
                    ecb.SetComponent(instance, physicsCollider);
                else
                    ecb.AddComponent(instance, physicsCollider);

                if (entityManager.HasComponent<PhysicsWorldIndex>(prefab))
                    ecb.SetSharedComponent(instance, new PhysicsWorldIndex { Value = 0 });
                else
                    ecb.AddSharedComponent(instance, new PhysicsWorldIndex { Value = 0 });
            }
            else if (entityManager.HasComponent<PhysicsCollider>(prefab))
            {
                ecb.RemoveComponent<PhysicsCollider>(instance);
            }
        }

        public static bool EnablePhysics(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return false;
            if (!entityManager.HasComponent<RuntimeColliderSource>(entity))
                return false;

            var source = entityManager.GetComponentData<RuntimeColliderSource>(entity);
            if (!source.Value.IsCreated)
                return false;

            var collider = new PhysicsCollider { Value = source.Value };
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                entityManager.SetComponentData(entity, collider);
            else
                entityManager.AddComponentData(entity, collider);

            if (!entityManager.HasComponent<PhysicsWorldIndex>(entity))
                entityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });

            return true;
        }

        public static bool QueueEnablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return false;
            if (!entityManager.HasComponent<RuntimeColliderSource>(entity))
                return false;

            var source = entityManager.GetComponentData<RuntimeColliderSource>(entity);
            if (!source.Value.IsCreated)
                return false;

            var collider = new PhysicsCollider { Value = source.Value };
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                ecb.SetComponent(entity, collider);
            else
                ecb.AddComponent(entity, collider);

            if (!entityManager.HasComponent<PhysicsWorldIndex>(entity))
                ecb.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });

            return true;
        }

        public static void DisablePhysics(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                entityManager.RemoveComponent<PhysicsCollider>(entity);
        }

        public static void QueueDisablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                ecb.RemoveComponent<PhysicsCollider>(entity);
        }
    }
}
