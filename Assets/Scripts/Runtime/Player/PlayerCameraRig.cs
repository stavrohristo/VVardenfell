using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindFixedPrePhysicsSystemGroup))]
    public partial struct PlayerVariableLookSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<MorrowindMovementIntent>(),
                ComponentType.ReadWrite<LocalTransform>());
            _viewQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>());
            state.RequireForUpdate(_playerQuery);
            state.RequireForUpdate(_viewQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var character = _playerQuery.GetSingleton<PlayerCharacterComponent>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var intentRef = _playerQuery.GetSingletonRW<MorrowindMovementIntent>();
            var bodyTransformRef = _playerQuery.GetSingletonRW<LocalTransform>();

            var viewRef = _viewQuery.GetSingletonRW<PlayerViewComponent>();
            var viewTransformRef = _viewQuery.GetSingletonRW<LocalTransform>();

            ref var control = ref controlRef.ValueRW;
            ref var intent = ref intentRef.ValueRW;
            ref var view = ref viewRef.ValueRW;
            if (view.ControlledCharacter != playerEntity)
                return;

            float2 lookDelta = control.LookDeltaDegrees;
            if (!math.all(lookDelta == float2.zero))
            {
                ref var bodyTransform = ref bodyTransformRef.ValueRW;
                bodyTransform.Rotation = math.normalize(math.mul(bodyTransform.Rotation, quaternion.RotateY(math.radians(lookDelta.x))));
                view.LocalPitchDegrees = math.clamp(
                    view.LocalPitchDegrees - lookDelta.y,
                    character.MinPitch,
                    character.MaxPitch);
                view.LocalViewRotation = quaternion.RotateX(math.radians(view.LocalPitchDegrees));
            }

            viewTransformRef.ValueRW = LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                view.LocalViewRotation,
                1f);

            control.LookDeltaDegrees = float2.zero;
            intent.LookDeltaDegrees = float2.zero;
        }
    }

    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateAfter(typeof(PlayerFixedStepMovementSystem))]
    [UpdateBefore(typeof(PlayerInteractionRaycastSystem))]
    public partial class PlayerPhysicsViewPoseSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadWrite<PlayerPhysicsViewPose>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<FixedTickSystem.Singleton>();
        }

        protected override void OnUpdate()
        {
            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            var view = _viewQuery.GetSingleton<PlayerViewComponent>();
            if (view.ControlledCharacter != playerEntity)
                return;

            quaternion worldRotation = math.normalize(math.mul(playerTransform.Rotation, view.LocalViewRotation));
            float3 worldPosition = playerTransform.Position + math.rotate(playerTransform.Rotation, view.LocalEyeOffset);
            uint fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick + 1u;

            var poseRef = _viewQuery.GetSingletonRW<PlayerPhysicsViewPose>();
            poseRef.ValueRW = new PlayerPhysicsViewPose
            {
                Position = worldPosition,
                Rotation = worldRotation,
                FixedTick = fixedTick,
            };
        }
    }

    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class PlayerCameraSyncSystem : SystemBase
    {
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            RequireForUpdate(_viewQuery);
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _viewQuery.IsEmptyIgnoreFilter)
                return;

            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            var localToWorld = _viewQuery.GetSingleton<LocalToWorld>();
            float4x4 viewMatrix = localToWorld.Value;
            cam.transform.position = viewMatrix.c3.xyz;
            quaternion viewRotation = quaternion.LookRotationSafe(
                math.normalizesafe(viewMatrix.c2.xyz, new float3(0f, 0f, 1f)),
                math.normalizesafe(viewMatrix.c1.xyz, math.up()));
            cam.transform.rotation = new Quaternion(
                viewRotation.value.x,
                viewRotation.value.y,
                viewRotation.value.z,
                viewRotation.value.w);
        }
    }
}
