using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionProxyBoundsUtility
    {
        static readonly float3 MinExtents = new(0.08f, 0.08f, 0.08f);

        public static bool TryBuildAggregateWorldBounds(EntityManager entityManager, Entity logicalEntity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.Exists(logicalEntity))
                return false;

            bool hasBounds = false;
            if (TryGetWorldBounds(entityManager, logicalEntity, out AABB logicalBounds))
            {
                worldBounds = logicalBounds;
                hasBounds = true;
            }

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return hasBounds;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (!entityManager.Exists(child) || entityManager.HasComponent<InteractionActivationProxyTag>(child))
                    continue;

                if (!TryGetWorldBounds(entityManager, child, out AABB childBounds))
                    continue;

                worldBounds = hasBounds ? Encapsulate(worldBounds, childBounds) : childBounds;
                hasBounds = true;
            }

            if (!hasBounds)
                return false;

            worldBounds.Extents = math.max(worldBounds.Extents, MinExtents);
            return true;
        }

        static bool TryGetWorldBounds(EntityManager entityManager, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (TryGetRenderWorldBounds(entityManager, entity, out worldBounds))
                return true;

            return TryGetPhysicsWorldBounds(entityManager, entity, out worldBounds);
        }

        static bool TryGetRenderWorldBounds(EntityManager entityManager, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.HasComponent<RenderBounds>(entity) || !entityManager.HasComponent<LocalToWorld>(entity))
                return false;

            var localBounds = entityManager.GetComponentData<RenderBounds>(entity).Value;
            float4x4 localToWorld = entityManager.GetComponentData<LocalToWorld>(entity).Value;
            float3 center = math.transform(localToWorld, localBounds.Center);
            float3x3 rotationScale = new(localToWorld.c0.xyz, localToWorld.c1.xyz, localToWorld.c2.xyz);
            float3 extents = math.abs(rotationScale.c0) * localBounds.Extents.x
                + math.abs(rotationScale.c1) * localBounds.Extents.y
                + math.abs(rotationScale.c2) * localBounds.Extents.z;

            worldBounds = new AABB
            {
                Center = center,
                Extents = extents,
            };
            return true;
        }

        static bool TryGetPhysicsWorldBounds(EntityManager entityManager, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.HasComponent<PhysicsCollider>(entity) || !TryBuildRigidBody(entityManager, entity, out RigidBody body))
                return false;

            Aabb aabb = body.CalculateAabb();
            worldBounds = new AABB
            {
                Center = (aabb.Min + aabb.Max) * 0.5f,
                Extents = (aabb.Max - aabb.Min) * 0.5f,
            };
            return true;
        }

        static bool TryBuildRigidBody(EntityManager entityManager, Entity entity, out RigidBody body)
        {
            body = default;
            var collider = entityManager.GetComponentData<PhysicsCollider>(entity);
            if (!collider.Value.IsCreated)
                return false;

            if (entityManager.HasComponent<LocalToWorld>(entity))
            {
                var matrix = entityManager.GetComponentData<LocalToWorld>(entity).Value;
                float3 c0 = matrix.c0.xyz;
                float3 c1 = matrix.c1.xyz;
                float3 c2 = matrix.c2.xyz;
                float scale = math.length(c0);
                if (scale <= 1e-5f)
                    return false;

                var rotation = new quaternion(new float3x3(c0 / scale, c1 / scale, c2 / scale));
                body = new RigidBody
                {
                    Collider = collider.Value,
                    Entity = entity,
                    WorldFromBody = new RigidTransform(rotation, matrix.c3.xyz),
                    Scale = scale,
                };
                return true;
            }

            if (!entityManager.HasComponent<LocalTransform>(entity))
                return false;

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            body = new RigidBody
            {
                Collider = collider.Value,
                Entity = entity,
                WorldFromBody = new RigidTransform(transform.Rotation, transform.Position),
                Scale = transform.Scale,
            };
            return true;
        }

        static AABB Encapsulate(AABB a, AABB b)
        {
            float3 min = math.min(a.Min, b.Min);
            float3 max = math.max(a.Max, b.Max);
            return new AABB
            {
                Center = (min + max) * 0.5f,
                Extents = (max - min) * 0.5f,
            };
        }
    }
}
