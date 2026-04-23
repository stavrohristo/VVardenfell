using Unity.Entities;
using Unity.Physics;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public enum RuntimeColliderKind : byte
    {
        None = 0,
        TerrainCell = 1,
        StaticCell = 2,
        PlacedRef = 3,
        ActivationProxy = 4,
        RuntimeSpawn = 5,
        Player = 6,
    }

    public struct RuntimeColliderSource : IComponentData
    {
        public BlobAssetReference<Collider> Value;
        public RuntimeColliderKind Kind;
        public byte Temporary;
    }

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

        public static void DisablePhysics(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                entityManager.RemoveComponent<PhysicsCollider>(entity);
        }
    }
}
