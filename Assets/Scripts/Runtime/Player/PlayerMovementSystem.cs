using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    [UpdateBefore(typeof(FixedTickSystem))]
    public partial struct PlayerFixedStepMovementSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        const float MinMoveEpsilon = 1e-5f;
        const float MinInputEpsilonSq = 1e-4f;
        const float StepDownSlack = 0.05f;
        const float MinGroundSnapDistance = 0.001f;
        const int GroundSlidePasses = 4;
        const int AirSlidePasses = 3;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = state.GetEntityQuery(new EntityQueryDesc
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
                    ComponentType.ReadOnly<PlayerStanceColliders>()
                }
            });
            _viewQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>());

            state.RequireForUpdate(_playerQuery);
            state.RequireForUpdate(_viewQuery);
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<FixedTickSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            float dt = SystemAPI.Time.DeltaTime;
            uint tick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            Entity playerEntity = _playerQuery.GetSingletonEntity();

            var character = _playerQuery.GetSingleton<PlayerCharacterComponent>();
            var stanceColliders = _playerQuery.GetSingleton<PlayerStanceColliders>();
            var transformRef = _playerQuery.GetSingletonRW<LocalTransform>();
            var localToWorldRef = _playerQuery.GetSingletonRW<LocalToWorld>();
            var colliderRef = _playerQuery.GetSingletonRW<PhysicsCollider>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var stateRef = _playerQuery.GetSingletonRW<PlayerCharacterState>();

            var viewRef = _viewQuery.GetSingletonRW<PlayerViewComponent>();
            var viewTransformRef = _viewQuery.GetSingletonRW<LocalTransform>();

            ref var playerTransform = ref transformRef.ValueRW;
            ref var playerLocalToWorld = ref localToWorldRef.ValueRW;
            ref var playerCollider = ref colliderRef.ValueRW;
            ref var control = ref controlRef.ValueRW;
            ref var characterState = ref stateRef.ValueRW;
            ref var view = ref viewRef.ValueRW;

            if (view.ControlledCharacter != playerEntity)
                return;

            bool wasGrounded = characterState.Grounded;
            characterState.WasGrounded = wasGrounded;

            float3 position = playerTransform.Position;
            quaternion rotation = playerTransform.Rotation;

            if (control.CrouchHeld)
            {
                if (!characterState.Crouched)
                {
                    playerCollider = new PhysicsCollider { Value = stanceColliders.Crouching };
                    characterState.Crouched = true;
                }
            }
            else if (characterState.Crouched &&
                CanStandUp(
                    physicsWorld.CollisionWorld,
                    playerCollider,
                    position,
                    character.StandingHeight - character.CrouchingHeight,
                    character.SkinWidth))
            {
                playerCollider = new PhysicsCollider { Value = stanceColliders.Standing };
                characterState.Crouched = false;
            }

            view.LocalEyeOffset = new float3(
                0f,
                characterState.Crouched ? character.CrouchingEyeHeight : character.StandingEyeHeight,
                0f);
            viewTransformRef.ValueRW = LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                view.LocalViewRotation,
                1f);

            float3 up = math.up();
            bool foundGround = ProbeGround(
                physicsWorld.CollisionWorld,
                playerCollider,
                position,
                character.MaxSlopeCosine,
                character.GroundProbeDistance,
                out float3 groundNormal,
                out float3 groundedPosition);

            if (foundGround && characterState.WorldVelocity.y <= 0f)
            {
                position = groundedPosition;
                characterState.Grounded = true;
                characterState.LastGroundedTick = tick;
                ResolveWalkableGroundVelocity(ref characterState.WorldVelocity);
            }
            else
            {
                characterState.Grounded = false;
                groundNormal = up;
            }

            float3 forward = math.normalizesafe(math.mul(rotation, new float3(0f, 0f, 1f)));
            float3 right = math.normalizesafe(math.mul(rotation, new float3(1f, 0f, 0f)));
            float3 moveVectorWorld = right * control.MoveInput.x + forward * control.MoveInput.y;
            float moveVectorLengthSq = math.lengthsq(moveVectorWorld);
            if (moveVectorLengthSq > 1f)
                moveVectorWorld *= math.rsqrt(moveVectorLengthSq);
            control.MoveVectorWorld = moveVectorWorld;

            uint jumpBufferTicks = SecondsToTicks(character.JumpBufferTime, dt);
            uint coyoteTicks = SecondsToTicks(character.CoyoteTime, dt);
            bool jumpBuffered = control.JumpPressedEvent.IsBuffered(tick, jumpBufferTicks);
            control.JumpThisFixedTick = jumpBuffered;

            bool jumpAllowed = characterState.Grounded || IsWithinTickWindow(tick, characterState.LastGroundedTick, coyoteTicks);
            bool jumpedThisTick = false;
            if (jumpBuffered && jumpAllowed)
            {
                characterState.WorldVelocity.y = character.JumpSpeed;
                characterState.Grounded = false;
                control.JumpPressedEvent.Consume();
                jumpedThisTick = true;
            }

            float inputMagnitudeSq = math.lengthsq(control.MoveInput);
            bool hasMoveInput = inputMagnitudeSq > MinInputEpsilonSq;
            characterState.Sprinting = !characterState.Crouched && characterState.Grounded && control.SprintHeld && inputMagnitudeSq > 0f;

            if (characterState.Grounded)
            {
                if (!hasMoveInput)
                {
                    characterState.WorldVelocity = float3.zero;
                }
                else
                {
                    float groundedMaxSpeed = character.GroundMaxSpeed;
                    if (characterState.Crouched)
                        groundedMaxSpeed *= character.CrouchSpeedMultiplier;
                    else if (characterState.Sprinting)
                        groundedMaxSpeed *= character.SprintSpeedMultiplier;

                    float3 targetGroundVelocity = moveVectorWorld * groundedMaxSpeed;
                    ResolveWalkableGroundVelocity(ref characterState.WorldVelocity);

                    float lerpFactor = 1f - math.exp(-character.GroundedMovementSharpness * dt);
                    characterState.WorldVelocity = math.lerp(characterState.WorldVelocity, targetGroundVelocity, lerpFactor);
                    ResolveWalkableGroundVelocity(ref characterState.WorldVelocity);
                }
            }
            else
            {
                float3 horizontalVelocity = new float3(characterState.WorldVelocity.x, 0f, characterState.WorldVelocity.z);
                horizontalVelocity += moveVectorWorld * (character.AirAcceleration * dt);
                float horizontalSpeed = math.length(horizontalVelocity);
                if (horizontalSpeed > character.AirMaxSpeed)
                    horizontalVelocity = horizontalVelocity / horizontalSpeed * character.AirMaxSpeed;

                float airDragFactor = math.saturate(1f - character.AirDrag * dt);
                horizontalVelocity *= airDragFactor;
                characterState.WorldVelocity.x = horizontalVelocity.x;
                characterState.WorldVelocity.z = horizontalVelocity.z;
            }

            if (!characterState.Grounded)
                characterState.WorldVelocity.y += character.Gravity * dt;

            if (characterState.Grounded)
            {
                float3 groundedMove = new float3(characterState.WorldVelocity.x, 0f, characterState.WorldVelocity.z) * dt;
                if (math.lengthsq(groundedMove) > MinMoveEpsilon)
                {
                    MoveGrounded(
                        physicsWorld.CollisionWorld,
                        playerCollider,
                        position,
                        groundedMove,
                        character,
                        ref position);
                }
            }
            else
            {
                float3 horizontalMove = new float3(characterState.WorldVelocity.x, 0f, characterState.WorldVelocity.z) * dt;
                if (math.lengthsq(horizontalMove) > MinMoveEpsilon)
                {
                    SweepSlide(
                        physicsWorld.CollisionWorld,
                        playerCollider,
                        horizontalMove,
                        AirSlidePasses,
                        character.SkinWidth,
                        character.MaxSlopeCosine,
                        false,
                        ref position,
                        out _);
                }

                float verticalMove = characterState.WorldVelocity.y * dt;
                if (math.abs(verticalMove) > MinMoveEpsilon)
                {
                    MoveVertical(
                        physicsWorld.CollisionWorld,
                        playerCollider,
                        ref position,
                        ref characterState.WorldVelocity.y,
                        verticalMove,
                        character.SkinWidth);
                }
            }

            bool regrounded = ProbeGround(
                physicsWorld.CollisionWorld,
                playerCollider,
                position,
                character.MaxSlopeCosine,
                character.GroundProbeDistance,
                out float3 regroundNormal,
                out float3 regroundedPosition);

            if (regrounded && characterState.WorldVelocity.y <= 0f)
            {
                position = regroundedPosition;
                characterState.Grounded = true;
                characterState.LastGroundedTick = tick;
                ResolveWalkableGroundVelocity(ref characterState.WorldVelocity);
            }
            else
            {
                characterState.Grounded = false;
            }

            if (characterState.Grounded)
            {
                characterState.GroundedTime = wasGrounded ? characterState.GroundedTime + dt : dt;
                characterState.AirborneTime = 0f;
            }
            else
            {
                characterState.AirborneTime = wasGrounded ? dt : characterState.AirborneTime + dt;
                characterState.GroundedTime = 0f;
                if (jumpedThisTick)
                    characterState.AirborneTime = dt;
            }

            playerTransform = LocalTransform.FromPositionRotationScale(position, rotation, playerTransform.Scale);
            playerLocalToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(playerTransform.Scale))
            };
        }

        [BurstCompile]
        static uint SecondsToTicks(float seconds, float fixedDeltaTime)
        {
            if (seconds <= 0f || fixedDeltaTime <= 0f)
                return 0u;

            return (uint)math.max(1, (int)math.ceil(seconds / fixedDeltaTime));
        }

        [BurstCompile]
        static bool IsWithinTickWindow(uint tick, uint eventTick, uint windowTicks)
        {
            if (windowTicks == 0u)
                return tick == eventTick;

            return tick >= eventTick && tick - eventTick <= windowTicks;
        }

        [BurstCompile]
        static void ProjectOnPlane(in float3 vector, in float3 normal, out float3 projected)
        {
            float3 normalizedNormal = math.normalizesafe(normal, math.up());
            projected = vector - normalizedNormal * math.dot(vector, normalizedNormal);
        }

        [BurstCompile]
        static void ResolveWalkableGroundVelocity(ref float3 velocity)
        {
            velocity.y = 0f;
        }

        [BurstCompile]
        static void MoveGrounded(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 start,
            in float3 move,
            in PlayerCharacterComponent character,
            ref float3 position)
        {
            float3 slidePosition = start;
            bool hit = SweepSlide(
                world,
                collider,
                move,
                GroundSlidePasses,
                character.SkinWidth,
                character.MaxSlopeCosine,
                true,
                ref slidePosition,
                out ColliderCastHit firstHit);

            float slideGroundProbeDistance = math.max(
                character.GroundProbeDistance,
                character.MaxStepHeight + math.length(move) + StepDownSlack);
            if (TrySnapToGround(
                world,
                collider,
                slidePosition,
                slideGroundProbeDistance,
                character.MaxSlopeCosine,
                out float3 snappedSlidePosition))
            {
                slidePosition = snappedSlidePosition;
            }

            float3 bestPosition = slidePosition;
            float slideDistanceSq = math.lengthsq(slidePosition - start);

            float3 planarMove = new float3(move.x, 0f, move.z);
            bool wantsStep = hit &&
                math.lengthsq(planarMove) > MinMoveEpsilon &&
                firstHit.SurfaceNormal.y < character.MaxSlopeCosine;

            if (wantsStep &&
                TryStepMove(
                    world,
                    collider,
                    start,
                    planarMove,
                    character,
                    out float3 stepPosition))
            {
                float stepDistanceSq = math.lengthsq(stepPosition - start);
                if (stepDistanceSq > slideDistanceSq)
                    bestPosition = stepPosition;
            }

            position = bestPosition;
        }

        [BurstCompile]
        static unsafe bool SweepSlide(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 move,
            int maxPasses,
            float skinWidth,
            float maxSlopeCosine,
            bool allowVerticalProjectionOnGround,
            ref float3 position,
            out ColliderCastHit firstHit)
        {
            firstHit = default;
            bool hitAnything = false;
            float3 remaining = move;

            for (int pass = 0; pass < maxPasses; pass++)
            {
                if (math.lengthsq(remaining) <= MinMoveEpsilon)
                    break;

                var castInput = new ColliderCastInput(collider.Value, position, position + remaining, quaternion.identity);
                if (!world.CastCollider(castInput, out ColliderCastHit hit))
                {
                    position += remaining;
                    break;
                }

                if (!hitAnything)
                {
                    firstHit = hit;
                    hitAnything = true;
                }

                float3 moveDelta = remaining * hit.Fraction;
                position += moveDelta;
                position += hit.SurfaceNormal * skinWidth;

                float3 residual = remaining * (1f - hit.Fraction);
                float3 projectedResidual = residual - hit.SurfaceNormal * math.dot(residual, hit.SurfaceNormal);

                if ((!allowVerticalProjectionOnGround || hit.SurfaceNormal.y < maxSlopeCosine) && projectedResidual.y > 0f)
                    projectedResidual.y = 0f;

                remaining = projectedResidual;
            }

            return hitAnything;
        }

        [BurstCompile]
        static unsafe bool TryStepMove(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 start,
            in float3 move,
            in PlayerCharacterComponent character,
            out float3 steppedPosition)
        {
            steppedPosition = start;

            float3 raiseOffset = new float3(0f, character.MaxStepHeight, 0f);
            var upCast = new ColliderCastInput(collider.Value, start, start + raiseOffset, quaternion.identity);
            float climbedHeight = character.MaxStepHeight;
            if (world.CastCollider(upCast, out ColliderCastHit upHit))
            {
                climbedHeight = character.MaxStepHeight * upHit.Fraction - character.SkinWidth;
                if (climbedHeight <= character.SkinWidth)
                    return false;
            }

            float3 raisedPosition = start + new float3(0f, climbedHeight, 0f);
            float3 forwardPosition = raisedPosition;
            SweepSlide(
                world,
                collider,
                move,
                GroundSlidePasses,
                character.SkinWidth,
                character.MaxSlopeCosine,
                false,
                ref forwardPosition,
                out _);

            if (math.lengthsq(forwardPosition - raisedPosition) <= MinMoveEpsilon)
                return false;

            float downDistance = climbedHeight + character.GroundProbeDistance + StepDownSlack;
            var downCast = new ColliderCastInput(
                collider.Value,
                forwardPosition,
                forwardPosition - new float3(0f, downDistance, 0f),
                quaternion.identity);

            if (!world.CastCollider(downCast, out ColliderCastHit downHit))
                return false;

            if (downHit.SurfaceNormal.y < character.MaxSlopeCosine)
                return false;

            steppedPosition = forwardPosition - new float3(0f, downDistance * downHit.Fraction, 0f);
            return steppedPosition.y >= start.y - StepDownSlack &&
                steppedPosition.y <= start.y + character.MaxStepHeight + character.GroundProbeDistance;
        }

        [BurstCompile]
        static unsafe void MoveVertical(
            in CollisionWorld world,
            in PhysicsCollider collider,
            ref float3 position,
            ref float verticalVelocity,
            float moveDistance,
            float skinWidth)
        {
            float3 move = new float3(0f, moveDistance, 0f);
            var castInput = new ColliderCastInput(collider.Value, position, position + move, quaternion.identity);
            if (!world.CastCollider(castInput, out ColliderCastHit hit))
            {
                position += move;
                return;
            }

            position += move * hit.Fraction;
            position += hit.SurfaceNormal * skinWidth;
            verticalVelocity = 0f;
        }

        [BurstCompile]
        static unsafe bool ProbeGround(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 position,
            float maxSlopeCosine,
            float probeDistance,
            out float3 groundNormal,
            out float3 snappedPosition)
        {
            var castInput = new ColliderCastInput(
                collider.Value,
                position,
                position - new float3(0f, probeDistance, 0f),
                quaternion.identity);

            if (world.CastCollider(castInput, out ColliderCastHit hit) && hit.SurfaceNormal.y >= maxSlopeCosine)
            {
                groundNormal = hit.SurfaceNormal;
                snappedPosition = position - new float3(0f, probeDistance * hit.Fraction, 0f);
                return true;
            }

            groundNormal = math.up();
            snappedPosition = position;
            return false;
        }

        [BurstCompile]
        static unsafe bool TrySnapToGround(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 position,
            float snapDistance,
            float maxSlopeCosine,
            out float3 snappedPosition)
        {
            if (snapDistance <= MinGroundSnapDistance)
            {
                snappedPosition = position;
                return false;
            }

            var castInput = new ColliderCastInput(
                collider.Value,
                position,
                position - new float3(0f, snapDistance, 0f),
                quaternion.identity);

            if (!world.CastCollider(castInput, out ColliderCastHit hit))
            {
                snappedPosition = position;
                return false;
            }

            if (hit.SurfaceNormal.y < maxSlopeCosine)
            {
                snappedPosition = position;
                return false;
            }

            snappedPosition = position - new float3(0f, snapDistance * hit.Fraction, 0f);
            return true;
        }

        [BurstCompile]
        static unsafe bool CanStandUp(
            in CollisionWorld world,
            in PhysicsCollider crouchedCollider,
            in float3 position,
            float standingHeightDelta,
            float skinWidth)
        {
            if (standingHeightDelta <= 0f)
                return true;

            var castInput = new ColliderCastInput(
                crouchedCollider.Value,
                position,
                position + new float3(0f, standingHeightDelta, 0f),
                quaternion.identity);

            if (!world.CastCollider(castInput, out ColliderCastHit hit))
                return true;

            return hit.Fraction * standingHeightDelta >= standingHeightDelta - skinWidth;
        }
    }
}
