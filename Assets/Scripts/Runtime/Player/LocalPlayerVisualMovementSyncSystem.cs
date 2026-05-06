#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial struct LocalPlayerVisualMovementSyncSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<MorrowindMovementState>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<LocalPlayerVisual>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerMovementState = _playerQuery.GetSingleton<MorrowindMovementState>();

            foreach (var (visual, movementState) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, RefRW<MorrowindMovementState>>())
            {
                if (visual.ValueRO.Player == player)
                    movementState.ValueRW = playerMovementState;
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    [UpdateBefore(typeof(LocalPlayerVisualTransformSyncSystem))]
    public partial struct LocalPlayerPresentationPoseSyncSystem : ISystem
    {
        const float TeleportResetDistanceSq = 4f;

        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<LocalPlayerPresentationPose>());
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_viewQuery);
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            LocalTransform playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            PlayerViewComponent view = _viewQuery.GetSingleton<PlayerViewComponent>();
            if (view.ControlledCharacter != player)
                return;

            float3 targetBodyPosition = playerTransform.Position;
            quaternion targetBodyRotation = playerTransform.Rotation;
            float3 targetViewPosition = targetBodyPosition + math.rotate(targetBodyRotation, view.LocalEyeOffset);
            quaternion targetViewRotation = math.normalize(math.mul(targetBodyRotation, view.LocalViewRotation));
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;

            var poseRef = _playerQuery.GetSingletonRW<LocalPlayerPresentationPose>();
            ref var pose = ref poseRef.ValueRW;
            bool newPhysicsPose = pose.Initialized == 0 || pose.LastFixedTick != fixedTick;
            if (newPhysicsPose)
            {
                bool reset = pose.Initialized == 0
                             || math.lengthsq(pose.TargetBodyPosition - targetBodyPosition) > TeleportResetDistanceSq;
                if (reset)
                {
                    pose.PreviousBodyPosition = targetBodyPosition;
                    pose.TargetBodyPosition = targetBodyPosition;
                    pose.PreviousViewPosition = targetViewPosition;
                    pose.TargetViewPosition = targetViewPosition;
                    pose.InterpolationTime = UnityEngine.Time.fixedDeltaTime;
                }
                else
                {
                    pose.PreviousBodyPosition = pose.TargetBodyPosition;
                    pose.TargetBodyPosition = targetBodyPosition;
                    pose.PreviousViewPosition = pose.TargetViewPosition;
                    pose.TargetViewPosition = targetViewPosition;
                    pose.InterpolationTime = 0f;
                }

                pose.LastFixedTick = fixedTick;
                pose.Initialized = 1;
            }

            float fixedDelta = math.max(0.0001f, UnityEngine.Time.fixedDeltaTime);
            pose.InterpolationTime = math.min(fixedDelta, pose.InterpolationTime + SystemAPI.Time.DeltaTime);
            float alpha = math.saturate(pose.InterpolationTime / fixedDelta);
            pose.BodyPosition = math.lerp(pose.PreviousBodyPosition, pose.TargetBodyPosition, alpha);
            pose.BodyRotation = targetBodyRotation;
            pose.ViewPosition = math.lerp(pose.PreviousViewPosition, pose.TargetViewPosition, alpha);
            pose.ViewRotation = targetViewRotation;
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial struct LocalPlayerVisualTransformSyncSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<LocalPlayerPresentationPose>());
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_viewQuery);
            systemState.RequireForUpdate<LocalPlayerVisual>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            LocalTransform playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            LocalPlayerPresentationPose pose = _playerQuery.GetSingleton<LocalPlayerPresentationPose>();
            PlayerViewComponent view = _viewQuery.GetSingleton<PlayerViewComponent>();
            if (view.ControlledCharacter != player || pose.Initialized == 0)
                return;

            foreach (var (visual, transform, localToWorld) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, RefRW<LocalTransform>, RefRW<LocalToWorld>>())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                transform.ValueRW = LocalTransform.FromPositionRotationScale(pose.BodyPosition, pose.BodyRotation, playerTransform.Scale);
                localToWorld.ValueRW = new LocalToWorld
                {
                    Value = float4x4.TRS(pose.BodyPosition, pose.BodyRotation, new float3(playerTransform.Scale)),
                };
            }
        }
    }
}
#endif
