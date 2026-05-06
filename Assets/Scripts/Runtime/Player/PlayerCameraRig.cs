using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
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
            var bodyTransformRef = _playerQuery.GetSingletonRW<LocalTransform>();

            var viewRef = _viewQuery.GetSingletonRW<PlayerViewComponent>();
            var viewTransformRef = _viewQuery.GetSingletonRW<LocalTransform>();

            ref var control = ref controlRef.ValueRW;
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
        }
    }

    [UpdateInGroup(typeof(MorrowindFramePhysicsQuerySystemGroup), OrderFirst = true)]
    public partial struct PlayerPhysicsViewPoseSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadWrite<PlayerPhysicsViewPose>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_viewQuery);
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            var view = _viewQuery.GetSingleton<PlayerViewComponent>();
            if (view.ControlledCharacter != playerEntity)
                return;

            quaternion worldRotation = math.normalize(math.mul(playerTransform.Rotation, view.LocalViewRotation));
            float3 worldPosition = playerTransform.Position + math.rotate(playerTransform.Rotation, view.LocalEyeOffset);
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().SnapshotTick;

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
    public partial struct PlayerCameraSyncSystem : ISystem
    {
        static readonly float3 s_FirstPersonCameraHeadOffset = new(0f, 0.105f, 0.105f);

        EntityQuery _viewQuery;
        EntityQuery _presentationQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            _presentationQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadOnly<LocalPlayerPresentationPose>());
            systemState.RequireForUpdate(_viewQuery);
            systemState.RequireForUpdate(_presentationQuery);
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<MainCameraSingleton>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Camera cam = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();

            systemState.EntityManager.CompleteDependencyBeforeRO<LocalPlayerPresentationPose>();
            var pose = _presentationQuery.GetSingleton<LocalPlayerPresentationPose>();
            if (pose.Initialized == 0)
                return;

            var presentation = _presentationQuery.GetSingleton<LocalPlayerPresentationState>();
            float3 cameraPosition = pose.ViewPosition;
            quaternion cameraRotation = pose.ViewRotation;
            if (presentation.Mode == PlayerViewMode.FirstPerson)
            {
                cameraPosition = ResolveFirstPersonCameraPosition(ref systemState, presentation, pose.BodyRotation);
            }
            else
            {
                float distance = math.max(0.25f, presentation.ThirdPersonDistance);
                float3 forward = math.rotate(cameraRotation, new float3(0f, 0f, 1f));
                cameraPosition -= forward * distance;
            }

            cam.transform.position = cameraPosition;
            cam.transform.rotation = new Quaternion(
                cameraRotation.value.x,
                cameraRotation.value.y,
                cameraRotation.value.z,
                cameraRotation.value.w);
        }

        float3 ResolveFirstPersonCameraPosition(ref SystemState systemState, 
            in LocalPlayerPresentationState presentation,
            quaternion bodyRotation)
        {
            Entity actorVisual = presentation.FirstPersonVisual;
            if (actorVisual == Entity.Null || !systemState.EntityManager.Exists(actorVisual))
                throw new System.InvalidOperationException("[VVardenfell] first-person body camera requires the local player's full actor visual.");
            if (!systemState.EntityManager.HasComponent<ActorSkeleton>(actorVisual) || !systemState.EntityManager.HasBuffer<ActorBone>(actorVisual))
                throw new System.InvalidOperationException("[VVardenfell] first-person body camera requires an animated ActorSkeleton on the local player's full actor visual.");
            if (!systemState.EntityManager.HasComponent<LocalToWorld>(actorVisual))
                throw new System.InvalidOperationException("[VVardenfell] first-person body camera requires LocalToWorld on the local player's full actor visual.");

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell] first-person body camera requires ActorAnimationBlobCatalog.");

            systemState.EntityManager.CompleteDependencyBeforeRO<ActorSkeleton>();
            systemState.EntityManager.CompleteDependencyBeforeRO<ActorBone>();
            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var skeleton = systemState.EntityManager.GetComponentData<ActorSkeleton>(actorVisual);
            var bones = systemState.EntityManager.GetBuffer<ActorBone>(actorVisual);
            ref var catalog = ref catalogRef.Value;
            int boneIndex = ActorSkeletonUtility.ResolveBoneIndex(ref catalog, skeleton, ActorSkeletonNameHash.Bip01Head);
            if ((uint)boneIndex >= (uint)bones.Length)
                throw new System.InvalidOperationException("[VVardenfell] first-person body camera could not resolve Bip01 Head on the local player's full actor visual.");

            float4x4 actorWorld = systemState.EntityManager.GetComponentData<LocalToWorld>(actorVisual).Value;
            float3 headPosition = math.mul(actorWorld, bones[boneIndex].LocalToRoot).c3.xyz;
            return headPosition + math.rotate(bodyRotation, s_FirstPersonCameraHeadOffset);
        }
    }
}
