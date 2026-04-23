using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateBefore(typeof(FixedTickSystem))]
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
                    ComponentType.ReadWrite<MorrowindMovementIntent>(),
                    ComponentType.ReadWrite<MorrowindActorKinematicState>(),
                    ComponentType.ReadWrite<MorrowindMovementTuning>(),
                    ComponentType.ReadWrite<MorrowindMovementFrameTrace>(),
                }
            });
            _viewQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
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
            var intentRef = _playerQuery.GetSingletonRW<MorrowindMovementIntent>();
            var kinematicRef = _playerQuery.GetSingletonRW<MorrowindActorKinematicState>();
            var traceRef = _playerQuery.GetSingletonRW<MorrowindMovementFrameTrace>();
            var tuning = _playerQuery.GetSingleton<MorrowindMovementTuning>();

            var viewRef = _viewQuery.GetSingletonRW<PlayerViewComponent>();
            var viewTransformRef = _viewQuery.GetSingletonRW<LocalTransform>();

            ref var playerTransform = ref transformRef.ValueRW;
            ref var playerLocalToWorld = ref localToWorldRef.ValueRW;
            ref var control = ref controlRef.ValueRW;
            ref var legacyState = ref legacyStateRef.ValueRW;
            ref var intent = ref intentRef.ValueRW;
            ref var kinematic = ref kinematicRef.ValueRW;
            ref var trace = ref traceRef.ValueRW;
            ref var view = ref viewRef.ValueRW;

            if (view.ControlledCharacter != playerEntity)
                return;

            view.LocalEyeOffset = new float3(0f, characterRef.ValueRO.StandingEyeHeight, 0f);
            viewTransformRef.ValueRW = LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                view.LocalViewRotation,
                1f);

            float3 position = playerTransform.Position;
            quaternion rotation = playerTransform.Rotation;
            PhysicsCollider playerCollider = colliderRef.ValueRO;
            bool previousGrounded = legacyState.Grounded;

            var movementStats = MorrowindActorMovementStats.BuildTemporaryPlayer(RuntimeContentDatabase.Active);
            var result = MorrowindActorMovementSolver.Solve(
                physicsWorld.CollisionWorld,
                playerCollider,
                tuning,
                movementStats,
                rotation,
                ref position,
                ref intent,
                ref kinematic,
                trace,
                dt);

            intent.LocalMove.z = 0f;
            trace = result.Trace;

            legacyState.WasGrounded = previousGrounded;
            legacyState.Grounded = kinematic.Grounded;
            legacyState.WorldVelocity = result.FinalVelocity;
            legacyState.Crouched = false;
            legacyState.Sprinting = intent.RunHeld && !intent.SneakHeld && intent.SpeedFactor > 0f;
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
            control.JumpThisFixedTick = result.Trace.JumpAccepted != 0;

            playerTransform = LocalTransform.FromPositionRotationScale(position, rotation, playerTransform.Scale);
            playerLocalToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(playerTransform.Scale))
            };
        }
    }
}
