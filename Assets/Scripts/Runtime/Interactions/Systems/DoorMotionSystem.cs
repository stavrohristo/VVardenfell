using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

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
            LogicalRefRotationUtility.ApplyDelta(EntityManager, entity, delta);

            if (math.abs(state.TargetProgress - state.Progress) <= 0.0001f)
                EntityManager.SetComponentEnabled<DoorActivated>(entity, false);
        }

        static float3 ResolveAxis(byte axis)
            => LogicalRefRotationUtility.ResolveAxis(axis);
    }
}
