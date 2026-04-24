using Unity.Entities;
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
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.AttachSource(
                entityManager,
                entity,
                collider,
                kind,
                active,
                temporary);
        }

        public static bool EnablePhysics(EntityManager entityManager, Entity entity)
        {
            return VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.EnablePhysics(entityManager, entity);
        }

        public static void DisablePhysics(EntityManager entityManager, Entity entity)
        {
            VVardenfell.Runtime.Physics.RuntimeColliderPhysicsUtility.DisablePhysics(entityManager, entity);
        }
    }
}
