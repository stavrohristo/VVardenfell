using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using System;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    public partial class PlayerFixedStepMovementSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<PlayerTag>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<LocalToWorld>(),
                    ComponentType.ReadWrite<PhysicsCollider>(),
                    ComponentType.ReadWrite<PlayerCharacterComponent>(),
                    ComponentType.ReadWrite<PlayerCharacterControl>(),
                    ComponentType.ReadWrite<PlayerCharacterState>(),
                    ComponentType.ReadWrite<MorrowindMovementInput>(),
                    ComponentType.ReadWrite<MorrowindMovementState>(),
                    ComponentType.ReadOnly<PlayerStanceColliders>(),
                    ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                }
            });
            _viewQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<MorrowindMovementSettings>();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            Entity playerEntity = _playerQuery.GetSingletonEntity();

            var transformRef = _playerQuery.GetSingletonRW<LocalTransform>();
            var localToWorldRef = _playerQuery.GetSingletonRW<LocalToWorld>();
            var colliderRef = _playerQuery.GetSingletonRW<PhysicsCollider>();
            var characterRef = _playerQuery.GetSingletonRW<PlayerCharacterComponent>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var legacyStateRef = _playerQuery.GetSingletonRW<PlayerCharacterState>();
            var inputRef = _playerQuery.GetSingletonRW<MorrowindMovementInput>();
            var movementStateRef = _playerQuery.GetSingletonRW<MorrowindMovementState>();
            var stanceColliders = _playerQuery.GetSingleton<PlayerStanceColliders>();
            var movementSpeed = _playerQuery.GetSingleton<MorrowindMovementSpeed>();
            var settings = SystemAPI.GetSingleton<MorrowindMovementSettings>();

            var viewRef = _viewQuery.GetSingletonRW<PlayerViewComponent>();
            var viewTransformRef = _viewQuery.GetSingletonRW<LocalTransform>();

            ref var playerTransform = ref transformRef.ValueRW;
            ref var playerLocalToWorld = ref localToWorldRef.ValueRW;
            ref var control = ref controlRef.ValueRW;
            ref var legacyState = ref legacyStateRef.ValueRW;
            ref var movementInput = ref inputRef.ValueRW;
            ref var movementState = ref movementStateRef.ValueRW;
            ref var view = ref viewRef.ValueRW;

            if (view.ControlledCharacter != playerEntity)
                return;

            bool crouched = ResolveCrouchedState(
                physicsWorld.CollisionWorld,
                stanceColliders,
                characterRef.ValueRO,
                playerTransform.Position,
                playerTransform.Rotation,
                legacyState.Crouched,
                movementInput.SneakHeld);
            movementInput.SneakHeld = crouched;
            control.CrouchHeld = crouched;
            PhysicsCollider activeCollider = new PhysicsCollider
            {
                Value = crouched ? stanceColliders.Crouching : stanceColliders.Standing,
            };
            colliderRef.ValueRW = activeCollider;

            float eyeHeight = crouched
                ? characterRef.ValueRO.CrouchingEyeHeight
                : characterRef.ValueRO.StandingEyeHeight;
            view.LocalEyeOffset = new float3(0f, eyeHeight, 0f);
            viewTransformRef.ValueRW = LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                view.LocalViewRotation,
                1f);

            float3 position = playerTransform.Position;
            quaternion rotation = playerTransform.Rotation;
            PhysicsCollider playerCollider = activeCollider;
            bool previousGrounded = legacyState.Grounded;

            var result = MorrowindActorMovementSolver.Solve(
                EntityManager,
                physicsWorld.CollisionWorld,
                playerCollider,
                settings,
                movementSpeed,
                rotation,
                ref position,
                ref movementInput,
                ref movementState,
                dt);

            movementInput.JumpPressed = false;

            legacyState.WasGrounded = previousGrounded;
            legacyState.Grounded = movementState.Grounded;
            legacyState.WorldVelocity = movementState.LastVelocity;
            legacyState.Crouched = crouched;
            legacyState.Sprinting = movementInput.RunHeld && !movementInput.SneakHeld && movementState.SpeedFactor > 0f;
            if (legacyState.Grounded)
            {
                legacyState.GroundedTime = legacyState.WasGrounded ? legacyState.GroundedTime + dt : dt;
                legacyState.AirborneTime = 0f;
            }
            else
            {
                legacyState.AirborneTime = legacyState.WasGrounded ? dt : legacyState.AirborneTime + dt;
                legacyState.GroundedTime = 0f;
            }

            control.MoveInput = result.PlanarInput;
            control.MoveVectorWorld = result.LocalMoveWorld;
            control.JumpThisFixedTick = movementState.JumpAccepted;

            playerTransform = LocalTransform.FromPositionRotationScale(position, rotation, playerTransform.Scale);
            playerLocalToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(playerTransform.Scale))
            };
        }

        static bool ResolveCrouchedState(
            in CollisionWorld world,
            in PlayerStanceColliders stanceColliders,
            in PlayerCharacterComponent character,
            float3 position,
            quaternion rotation,
            bool currentlyCrouched,
            bool crouchRequested)
        {
            if (!stanceColliders.Standing.IsCreated || !stanceColliders.Crouching.IsCreated)
                throw new InvalidOperationException("[VVardenfell] player crouch requires both standing and crouching stance colliders.");

            if (crouchRequested)
                return true;

            if (!currentlyCrouched)
                return false;

            return !CanStand(world, stanceColliders, character, position, rotation);
        }

        static bool CanStand(
            in CollisionWorld world,
            in PlayerStanceColliders stanceColliders,
            in PlayerCharacterComponent character,
            float3 position,
            quaternion rotation)
        {
            float standDelta = character.StandingHeight - character.CrouchingHeight;
            if (standDelta <= 0f)
                return true;

            var input = new ColliderCastInput(
                stanceColliders.Crouching,
                position,
                position + new float3(0f, standDelta, 0f),
                rotation);
            return !world.CastCollider(input);
        }
    }
}
