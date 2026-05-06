using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Interactions
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(DoorMotionActivationSystem))]
    public partial struct DoorMotionSystem : ISystem
    {
        EntityQuery _activeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _activeQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<DoorActivated>(),
                ComponentType.ReadWrite<DoorMotionState>(),
                ComponentType.ReadOnly<LogicalRefChild>());
            systemState.RequireForUpdate(_activeQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);
            if (deltaTime <= 0f)
                return;

            using var entities = _activeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                AdvanceDoor(ref systemState, entities[i], deltaTime);
        }

        void AdvanceDoor(ref SystemState systemState, Entity entity, float deltaTime)
        {
            if (!systemState.EntityManager.Exists(entity) || !systemState.EntityManager.IsComponentEnabled<DoorActivated>(entity))
                return;

            var state = systemState.EntityManager.GetComponentData<DoorMotionState>(entity);
            float distance = state.TargetProgress - state.Progress;
            if (math.abs(distance) <= 0.0001f || state.RangeRadians <= 0f || state.SpeedRadiansPerSecond <= 0f)
            {
                state.Progress = state.TargetProgress;
                systemState.EntityManager.SetComponentData(entity, state);
                systemState.EntityManager.SetComponentEnabled<DoorActivated>(entity, false);
                return;
            }

            float step = (state.SpeedRadiansPerSecond * deltaTime) / state.RangeRadians;
            float newProgress = state.Progress + math.sign(distance) * math.min(math.abs(distance), step);
            float deltaRadians = (newProgress - state.Progress) * state.RangeRadians;
            state.Progress = newProgress;
            systemState.EntityManager.SetComponentData(entity, state);

            quaternion delta = quaternion.AxisAngle(ResolveAxis(state.Axis), deltaRadians);
            LogicalRefRotationUtility.ApplyDelta(systemState.EntityManager, entity, delta);

            if (math.abs(state.TargetProgress - state.Progress) <= 0.0001f)
                systemState.EntityManager.SetComponentEnabled<DoorActivated>(entity, false);
        }

        static float3 ResolveAxis(byte axis)
            => LogicalRefRotationUtility.ResolveAxis(axis);
    }
}
