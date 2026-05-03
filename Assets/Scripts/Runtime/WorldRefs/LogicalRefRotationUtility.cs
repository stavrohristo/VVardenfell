using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class LogicalRefRotationUtility
    {
        public static void ApplyDelta(EntityManager entityManager, Entity logicalEntity, quaternion delta)
        {
            RemoveStaticIfPresent(entityManager, logicalEntity);
            RotateEntity(entityManager, logicalEntity, delta);

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null
                    || child == logicalEntity
                    || !entityManager.Exists(child)
                    || entityManager.HasComponent<InteractionActivationProxyTag>(child)
                    || entityManager.HasComponent<Parent>(child)
                    || !entityManager.HasComponent<LocalTransform>(child))
                {
                    continue;
                }

                RemoveStaticIfPresent(entityManager, child);
                RotateEntity(entityManager, child, delta);
            }
        }

        public static void ApplyWorldDelta(EntityManager entityManager, Entity logicalEntity, quaternion delta)
        {
            RemoveStaticIfPresent(entityManager, logicalEntity);
            RotateEntityWorld(entityManager, logicalEntity, delta);

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null
                    || child == logicalEntity
                    || !entityManager.Exists(child)
                    || entityManager.HasComponent<InteractionActivationProxyTag>(child)
                    || entityManager.HasComponent<Parent>(child)
                    || !entityManager.HasComponent<LocalTransform>(child))
                {
                    continue;
                }

                RemoveStaticIfPresent(entityManager, child);
                RotateEntityWorld(entityManager, child, delta);
            }
        }

        public static void SetAngle(EntityManager entityManager, Entity logicalEntity, byte axis, float radians)
        {
            if (!entityManager.HasComponent<LocalTransform>(logicalEntity))
                return;

            var transform = entityManager.GetComponentData<LocalTransform>(logicalEntity);
            quaternion current = SafeNormalize(transform.Rotation);
            float3 sourceAngles = ExtractSourceAngles(current);
            switch (axis)
            {
                case 1:
                    sourceAngles.y = radians;
                    break;
                case 2:
                    sourceAngles.z = radians;
                    break;
                default:
                    sourceAngles.x = radians;
                    break;
            }

            quaternion target = ComposeSourceAngles(sourceAngles);
            quaternion delta = math.normalize(math.mul(target, math.inverse(current)));
            ApplyDelta(entityManager, logicalEntity, delta);
        }

        public static float GetAngle(quaternion rotation, byte axis)
        {
            float3 sourceAngles = ExtractSourceAngles(SafeNormalize(rotation));
            return axis switch
            {
                1 => sourceAngles.y,
                2 => sourceAngles.z,
                _ => sourceAngles.x,
            };
        }

        public static float3 ResolveAxis(byte axis)
        {
            return axis switch
            {
                1 => new float3(0f, 0f, 1f),
                2 => new float3(0f, 1f, 0f),
                _ => new float3(1f, 0f, 0f),
            };
        }

        static void RotateEntity(EntityManager entityManager, Entity entity, quaternion delta)
        {
            if (!entityManager.HasComponent<LocalTransform>(entity))
                return;

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            transform.Rotation = math.normalize(math.mul(delta, transform.Rotation));
            entityManager.SetComponentData(entity, transform);

            if (entityManager.HasComponent<LocalToWorld>(entity))
            {
                entityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                });
            }
        }

        static void RotateEntityWorld(EntityManager entityManager, Entity entity, quaternion delta)
        {
            if (!entityManager.HasComponent<LocalTransform>(entity))
                return;

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            transform.Rotation = math.normalize(math.mul(transform.Rotation, delta));
            entityManager.SetComponentData(entity, transform);

            if (entityManager.HasComponent<LocalToWorld>(entity))
            {
                entityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                });
            }
        }

        static float3 ExtractSourceAngles(quaternion rotation)
        {
            float3x3 matrix = new float3x3(rotation);
            float sourceY = math.asin(math.clamp(-matrix.c1.x, -1f, 1f));
            float cosY = math.cos(sourceY);
            if (math.abs(cosY) <= 0.00001f)
            {
                return new float3(
                    0f,
                    sourceY,
                    math.atan2(-matrix.c2.y, matrix.c2.z));
            }

            return new float3(
                math.atan2(matrix.c1.z, matrix.c1.y),
                sourceY,
                math.atan2(matrix.c2.x, matrix.c0.x));
        }

        static quaternion ComposeSourceAngles(float3 sourceAngles)
        {
            quaternion x = quaternion.AxisAngle(new float3(1f, 0f, 0f), sourceAngles.x);
            quaternion z = quaternion.AxisAngle(new float3(0f, 0f, 1f), sourceAngles.y);
            quaternion y = quaternion.AxisAngle(new float3(0f, 1f, 0f), sourceAngles.z);
            return SafeNormalize(math.mul(math.mul(x, z), y));
        }

        static quaternion SafeNormalize(quaternion value)
        {
            float lengthSq = math.lengthsq(value.value);
            return lengthSq > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;
        }

        static void RemoveStaticIfPresent(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<Static>(entity))
                entityManager.RemoveComponent<Static>(entity);
        }
    }
}
