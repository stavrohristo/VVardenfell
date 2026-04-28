#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class LocalPlayerVisualMovementSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<MorrowindMovementState>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
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
    public partial class LocalPlayerPresentationPoseSmoothSystem : SystemBase
    {
        const float PositionResponsiveness = 30f;
        const float SnapDistanceSq = 4f;

        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<LocalPlayerPresentationPose>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
        }

        protected override void OnUpdate()
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

            float dt = SystemAPI.Time.DeltaTime;
            float alpha = dt > 0f
                ? 1f - math.exp(-PositionResponsiveness * dt)
                : 1f;

            var poseRef = _playerQuery.GetSingletonRW<LocalPlayerPresentationPose>();
            ref var pose = ref poseRef.ValueRW;
            bool snap = pose.Initialized == 0
                || math.lengthsq(pose.BodyPosition - targetBodyPosition) > SnapDistanceSq
                || math.lengthsq(pose.ViewPosition - targetViewPosition) > SnapDistanceSq;

            if (snap)
            {
                pose.BodyPosition = targetBodyPosition;
                pose.ViewPosition = targetViewPosition;
                pose.Initialized = 1;
            }
            else
            {
                pose.BodyPosition = math.lerp(pose.BodyPosition, targetBodyPosition, alpha);
                pose.ViewPosition = math.lerp(pose.ViewPosition, targetViewPosition, alpha);
            }

            pose.BodyRotation = targetBodyRotation;
            pose.ViewRotation = targetViewRotation;
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class LocalPlayerVisualTransformSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<LocalPlayerPresentationPose>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
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
