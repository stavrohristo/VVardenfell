using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(DoorMotionActivationSystem))]
    public partial class DoorMotionSystem : SystemBase
    {
        EntityQuery _activeQuery;

        protected override void OnCreate()
        {
            _activeQuery = GetEntityQuery(
                ComponentType.ReadWrite<DoorActivated>(),
                ComponentType.ReadWrite<DoorMotionState>(),
                ComponentType.ReadOnly<LogicalRefChild>());
            RequireForUpdate(_activeQuery);
        }

        protected override void OnUpdate()
        {
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);
            if (deltaTime <= 0f)
                return;

            using var entities = _activeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                AdvanceDoor(entities[i], deltaTime);
        }

        void AdvanceDoor(Entity entity, float deltaTime)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.IsComponentEnabled<DoorActivated>(entity))
                return;

            var state = EntityManager.GetComponentData<DoorMotionState>(entity);
            float distance = state.TargetProgress - state.Progress;
            if (math.abs(distance) <= 0.0001f || state.RangeRadians <= 0f || state.SpeedRadiansPerSecond <= 0f)
            {
                state.Progress = state.TargetProgress;
                EntityManager.SetComponentData(entity, state);
                EntityManager.SetComponentEnabled<DoorActivated>(entity, false);
                return;
            }

            float step = (state.SpeedRadiansPerSecond * deltaTime) / state.RangeRadians;
            float newProgress = state.Progress + math.sign(distance) * math.min(math.abs(distance), step);
            float deltaRadians = (newProgress - state.Progress) * state.RangeRadians;
            state.Progress = newProgress;
            EntityManager.SetComponentData(entity, state);

            quaternion delta = quaternion.AxisAngle(ResolveAxis(state.Axis), deltaRadians);
            ApplyRotation(entity, delta);

            if (math.abs(state.TargetProgress - state.Progress) <= 0.0001f)
                EntityManager.SetComponentEnabled<DoorActivated>(entity, false);
        }

        void ApplyRotation(Entity entity, quaternion delta)
        {
            RemoveStaticIfPresent(entity);
            RotateEntity(entity, delta);

            if (!EntityManager.HasBuffer<LogicalRefChild>(entity))
                return;

            var children = EntityManager.GetBuffer<LogicalRefChild>(entity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null
                    || child == entity
                    || !EntityManager.Exists(child)
                    || EntityManager.HasComponent<InteractionActivationProxyTag>(child)
                    || EntityManager.HasComponent<Parent>(child)
                    || !EntityManager.HasComponent<LocalTransform>(child))
                {
                    continue;
                }

                RemoveStaticIfPresent(child);
                RotateEntity(child, delta);
            }
        }

        void RotateEntity(Entity entity, quaternion delta)
        {
            if (!EntityManager.HasComponent<LocalTransform>(entity))
                return;

            var transform = EntityManager.GetComponentData<LocalTransform>(entity);
            transform.Rotation = math.normalize(math.mul(delta, transform.Rotation));
            EntityManager.SetComponentData(entity, transform);

            if (EntityManager.HasComponent<LocalToWorld>(entity))
            {
                EntityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                });
            }
        }

        void RemoveStaticIfPresent(Entity entity)
        {
            if (EntityManager.HasComponent<Unity.Transforms.Static>(entity))
                EntityManager.RemoveComponent<Unity.Transforms.Static>(entity);
        }

        static float3 ResolveAxis(byte axis)
        {
            return axis switch
            {
                1 => new float3(0f, 0f, 1f),
                2 => new float3(0f, 1f, 0f),
                _ => new float3(1f, 0f, 0f),
            };
        }
    }
}
