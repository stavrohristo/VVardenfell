using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;

namespace VVardenfell.Runtime.Movement
{
    public readonly struct MorrowindActorMovementResult
    {
        public readonly float2 PlanarInput;
        public readonly float3 LocalMoveWorld;
        public readonly float3 FinalVelocity;
        public readonly MorrowindMovementFrameTrace Trace;

        public MorrowindActorMovementResult(
            float2 planarInput,
            float3 localMoveWorld,
            float3 finalVelocity,
            in MorrowindMovementFrameTrace trace)
        {
            PlanarInput = planarInput;
            LocalMoveWorld = localMoveWorld;
            FinalVelocity = finalVelocity;
            Trace = trace;
        }
    }

    public static class MorrowindActorMovementSolver
    {
        const float MinMoveEpsilon = 1e-5f;
        const float MinInputEpsilonSq = 1e-4f;
        const float JumpDiagonalScale = 0.707f;

        public static MorrowindActorMovementResult Solve(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            in MorrowindActorMovementStats.Context stats,
            quaternion rotation,
            ref float3 position,
            ref MorrowindMovementIntent intent,
            ref MorrowindActorKinematicState kinematic,
            in MorrowindMovementFrameTrace previousTrace,
            float dt)
        {
            var trace = new MorrowindMovementFrameTrace
            {
                Sequence = previousTrace.Sequence + 1,
                DeltaTime = dt,
                StartPosition = position,
                EndPosition = position,
                GroundNormal = math.up(),
            };

            bool wasGrounded = kinematic.Grounded;
            float3 groundNormal = math.up();
            float3 groundPosition = position;
            Entity standingOn = Entity.Null;
            bool shouldSampleGroundBeforeMove = kinematic.Inertia.y <= 0f;
            bool groundedBeforeMove = shouldSampleGroundBeforeMove && ProbeGround(
                world,
                collider,
                position,
                tuning.MaxSlopeCosine,
                tuning.StepSizeDown + tuning.GroundOffset * 2f,
                tuning.GroundOffset,
                out groundNormal,
                out groundPosition,
                out standingOn);

            if (groundedBeforeMove)
            {
                kinematic.Grounded = true;
                kinematic.OnSlope = groundNormal.y < tuning.MaxSlopeCosine;
                kinematic.StandingOn = standingOn;
                trace.GroundNormal = groundNormal;
                trace.StandingOn = standingOn;
            }
            else
            {
                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = Entity.Null;
            }

            float2 planarInput = intent.LocalMove.xy;
            float inputLengthSq = math.lengthsq(planarInput);
            if (inputLengthSq > 1f)
                planarInput *= math.rsqrt(inputLengthSq);

            intent.SpeedFactor = math.saturate(math.length(planarInput));
            intent.IsStrafing = math.abs(planarInput.x) > math.abs(planarInput.y) * 2f;

            float3 forward = math.normalizesafe(math.mul(rotation, new float3(0f, 0f, 1f)));
            float3 right = math.normalizesafe(math.mul(rotation, new float3(1f, 0f, 0f)));
            float3 localMoveWorld = right * planarInput.x + forward * planarInput.y;
            localMoveWorld.y = 0f;
            float moveLengthSq = math.lengthsq(localMoveWorld);
            if (moveLengthSq > 1f)
                localMoveWorld *= math.rsqrt(moveLengthSq);

            float3 desiredVelocity = float3.zero;
            if (moveLengthSq > MinInputEpsilonSq)
            {
                float speed = stats.GetCurrentSpeed(intent.RunHeld, intent.SneakHeld, kinematic.Grounded, intent.SpeedFactor, intent.IsStrafing);
                trace.ResolvedSpeed = speed;
                desiredVelocity = localMoveWorld * speed;
            }
            trace.DesiredVelocity = desiredVelocity;

            bool jumpRequested = intent.LocalMove.z > 0f;
            trace.JumpRequested = ToByte(jumpRequested);
            bool canJump = jumpRequested && kinematic.Grounded && !intent.SneakHeld;
            if (canJump)
            {
                trace.JumpAccepted = 1;
                float jumpSpeed = stats.GetJumpSpeed(intent.RunHeld);
                if (math.lengthsq(desiredVelocity.xz) <= MinMoveEpsilon)
                {
                    kinematic.Inertia = new float3(0f, jumpSpeed, 0f);
                }
                else
                {
                    float3 horizontal = math.normalizesafe(new float3(desiredVelocity.x, 0f, desiredVelocity.z));
                    kinematic.Inertia = new float3(horizontal.x, 1f, horizontal.z) * jumpSpeed * JumpDiagonalScale;
                }

                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = Entity.Null;
            }

            float airControlFactor = 1f;
            if (!kinematic.Grounded)
                airControlFactor = stats.GetJumpMoveFactor();

            float3 velocity = desiredVelocity * airControlFactor;
            if (!kinematic.Grounded || kinematic.OnSlope)
                velocity += kinematic.Inertia;

            if (math.lengthsq(velocity) > MinMoveEpsilon)
                MoveKinematic(world, collider, tuning, ref position, ref velocity, dt, kinematic.Grounded, ref trace);

            bool forceGroundProbe = trace.StepSucceeded != 0;
            bool shouldProbeGroundAfterMove = forceGroundProbe || kinematic.Inertia.y <= 0f || kinematic.StuckFrames > 0;
            bool groundedAfterMove = false;
            float3 regroundNormal = math.up();
            float3 regroundPosition = position;
            Entity regroundStandingOn = Entity.Null;
            if (shouldProbeGroundAfterMove)
            {
                groundedAfterMove = ProbeGround(
                    world,
                    collider,
                    position,
                    tuning.MaxSlopeCosine,
                    2f * tuning.GroundOffset + (wasGrounded ? tuning.StepSizeDown : 0f),
                    tuning.GroundOffset,
                    out regroundNormal,
                    out regroundPosition,
                    out regroundStandingOn);
            }

            if (groundedAfterMove)
            {
                kinematic.Grounded = true;
                kinematic.OnSlope = regroundNormal.y < tuning.MaxSlopeCosine;
                kinematic.StandingOn = regroundStandingOn;
                trace.GroundNormal = regroundNormal;
                trace.StandingOn = regroundStandingOn;
                if (!kinematic.OnSlope)
                {
                    position = regroundPosition;
                    trace.GroundProbeSnapped = 1;
                    kinematic.Inertia = float3.zero;
                }
            }
            else
            {
                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = Entity.Null;
            }

            if (!kinematic.Grounded || kinematic.OnSlope)
                kinematic.Inertia.y -= tuning.Gravity * dt;

            trace.FinalVelocity = velocity;
            trace.EndPosition = position;
            UpdateStuckState(ref kinematic, trace.StartPosition, trace.EndPosition, velocity, dt);
            return new MorrowindActorMovementResult(planarInput, localMoveWorld, velocity, trace);
        }

        static void MoveKinematic(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref float3 velocity,
            float time,
            bool startedGrounded,
            ref MorrowindMovementFrameTrace trace)
        {
            float remainingTime = time;
            float3 originalVelocity = velocity;
            float3 lastSlideNormal = math.up();
            float3 lastSlideNormalFallback = math.up();
            int slideCount = 0;

            for (int iteration = 0; iteration < tuning.MaxIterations && remainingTime > 0.0001f; iteration++)
            {
                trace.SweepIterations++;
                float3 move = velocity * remainingTime;
                if (math.lengthsq(move) <= MinMoveEpsilon)
                    break;

                var castInput = new ColliderCastInput(collider.Value, position, position + move, quaternion.identity);
                if (!world.CastCollider(castInput, out ColliderCastHit hit))
                {
                    position += move;
                    break;
                }

                trace.LastBlocker = hit.Entity;
                trace.LastBlockerNormal = hit.SurfaceNormal;
                trace.LastBlockerFraction = hit.Fraction;

                bool seenGround = startedGrounded || IsWalkableSlope(hit.SurfaceNormal, tuning.MaxSlopeCosine);
                float3 oldPosition = position;
                bool stepped = false;
                if (hit.SurfaceNormal.y < tuning.MaxSlopeCosine)
                {
                    trace.SteepSlopeRejected = 1;
                    trace.StepAttempted = 1;
                    stepped = TryStep(world, collider, tuning, ref position, ref velocity, ref remainingTime, startedGrounded, iteration == 0);
                }

                if (stepped)
                {
                    trace.StepSucceeded = 1;
                    continue;
                }

                position = oldPosition;
                remainingTime *= 1f - hit.Fraction;

                float3 planeNormal = hit.SurfaceNormal;
                float3 originalPlaneNormal = planeNormal;
                if (seenGround && !IsWalkableSlope(planeNormal, tuning.MaxSlopeCosine) && math.abs(planeNormal.y) > 0.0001f)
                    planeNormal = math.normalizesafe(new float3(planeNormal.x, 0f, planeNormal.z), planeNormal);

                float3 direction = math.normalizesafe(velocity);
                position += move * hit.Fraction;
                position -= direction * tuning.CollisionMargin;

                float3 newVelocity = math.dot(velocity, planeNormal) <= 0f
                    ? Reject(velocity, planeNormal)
                    : velocity;

                bool usedSeamLogic = false;
                if (slideCount > 0)
                {
                    float dotA = math.dot(lastSlideNormal, originalPlaneNormal);
                    float dotB = slideCount <= 1 ? 1f : math.dot(lastSlideNormalFallback, originalPlaneNormal);
                    if (dotA <= 0f || dotB <= 0f)
                    {
                        float3 bestNormal = dotB < dotA ? lastSlideNormalFallback : lastSlideNormal;
                        float3 constraint = math.cross(bestNormal, originalPlaneNormal);
                        if (math.lengthsq(constraint) > 0f)
                        {
                            constraint = math.normalize(constraint);
                            newVelocity = Project(velocity, constraint);
                            usedSeamLogic = true;
                        }
                    }
                }

                if (!usedSeamLogic)
                    position += planeNormal * (tuning.CollisionMargin * 2f);

                if (seenGround && math.dot(newVelocity, originalVelocity) <= 0f)
                {
                    float3 perpendicular = math.cross(newVelocity, originalVelocity);
                    if (math.lengthsq(perpendicular) > 0f && math.abs(math.normalize(perpendicular).y) > 0.7071f)
                        break;
                }

                if (!IsWalkableSlope(planeNormal, tuning.MaxSlopeCosine) && !usedSeamLogic)
                    newVelocity.y = math.min(newVelocity.y, velocity.y);

                slideCount++;
                trace.SlideCount = slideCount;
                lastSlideNormalFallback = lastSlideNormal;
                lastSlideNormal = originalPlaneNormal;
                velocity = newVelocity;
            }
        }

        static bool TryStep(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref float3 velocity,
            ref float remainingTime,
            bool onGround,
            bool firstIteration)
        {
            if (math.lengthsq(new float2(velocity.x, velocity.z)) <= MinMoveEpsilon)
                return false;

            float3 up = new float3(0f, tuning.StepSizeUp, 0f);
            var upCast = new ColliderCastInput(collider.Value, position, position + up, quaternion.identity);
            float upDistance;
            if (!world.CastCollider(upCast, out ColliderCastHit upHit))
                upDistance = tuning.StepSizeUp;
            else if (upHit.Fraction * tuning.StepSizeUp > tuning.CollisionMargin)
                upDistance = upHit.Fraction * tuning.StepSizeUp - tuning.CollisionMargin;
            else
                return false;

            float3 toMove = velocity * remainingTime;
            float3 horizontal = new float3(toMove.x, 0f, toMove.z);
            float moveDistance = math.length(horizontal);
            if (moveDistance <= MinMoveEpsilon)
                return false;

            float3 direction = horizontal / moveDistance;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1 && !firstIteration)
                    return false;

                float attemptDistance = attempt == 1
                    ? moveDistance
                    : (attempt == 2 ? 10f * WorldScale.MwUnitsToMeters : 20f * WorldScale.MwUnitsToMeters);
                if (attempt == 3)
                    upDistance = math.min(upDistance, tuning.StepSizeUp);

                float3 start = position + new float3(0f, upDistance, 0f);
                float3 dest = start + direction * attemptDistance;
                var forwardCast = new ColliderCastInput(collider.Value, start, dest, quaternion.identity);
                float3 forwardDest = dest;
                float effectiveDistance = attemptDistance;
                if (world.CastCollider(forwardCast, out ColliderCastHit forwardHit))
                {
                    effectiveDistance *= forwardHit.Fraction;
                    if (effectiveDistance <= tuning.CollisionMargin)
                        return false;

                    effectiveDistance -= tuning.CollisionMargin;
                    forwardDest = start + direction * effectiveDistance;
                    forwardDest += forwardHit.SurfaceNormal * tuning.CollisionMargin;
                }

                float downStepSize = attempt > 2
                    ? upDistance
                    : effectiveDistance + upDistance + tuning.StepSizeDown;
                var downCast = new ColliderCastInput(
                    collider.Value,
                    forwardDest,
                    forwardDest - new float3(0f, downStepSize, 0f),
                    quaternion.identity);

                if (!world.CastCollider(downCast, out ColliderCastHit downHit) ||
                    !IsWalkableSlope(downHit.SurfaceNormal, tuning.MaxSlopeCosine))
                    continue;

                float downDistance = downHit.Fraction * downStepSize > tuning.CollisionMargin
                    ? downHit.Fraction * downStepSize - tuning.CollisionMargin
                    : 0f;

                if (downDistance - tuning.CollisionMargin - tuning.GroundOffset > upDistance && !onGround)
                    return false;

                float3 newPosition = forwardDest - new float3(0f, downDistance, 0f);
                if (math.lengthsq(position - newPosition) < tuning.CollisionMargin * tuning.CollisionMargin)
                    return false;

                velocity = Reject(velocity, downHit.SurfaceNormal);
                position = newPosition;
                remainingTime *= 1f - math.saturate(effectiveDistance / math.max(moveDistance, MinMoveEpsilon));
                return true;
            }

            return false;
        }

        static bool ProbeGround(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in float3 position,
            float maxSlopeCosine,
            float probeDistance,
            float groundOffset,
            out float3 groundNormal,
            out float3 snappedPosition,
            out Entity standingOn)
        {
            var castInput = new ColliderCastInput(
                collider.Value,
                position,
                position - new float3(0f, probeDistance, 0f),
                quaternion.identity);

            if (probeDistance > 0f && world.CastCollider(castInput, out ColliderCastHit hit))
            {
                groundNormal = hit.SurfaceNormal;
                snappedPosition = position - new float3(0f, probeDistance * hit.Fraction, 0f);
                if (IsWalkableSlope(hit.SurfaceNormal, maxSlopeCosine))
                    snappedPosition += new float3(0f, groundOffset, 0f);
                standingOn = hit.Entity;
                return true;
            }

            groundNormal = math.up();
            snappedPosition = position;
            standingOn = Entity.Null;
            return false;
        }

        static bool IsWalkableSlope(float3 normal, float maxSlopeCosine) => normal.y > maxSlopeCosine;

        static float3 Reject(float3 direction, float3 planeNormal)
        {
            float3 normal = math.normalizesafe(planeNormal, math.up());
            return direction - normal * math.dot(direction, normal);
        }

        static float3 Project(float3 direction, float3 axis)
        {
            float3 normal = math.normalizesafe(axis);
            return normal * math.dot(direction, normal);
        }

        static void UpdateStuckState(
            ref MorrowindActorKinematicState kinematic,
            float3 startPosition,
            float3 endPosition,
            float3 attemptedVelocity,
            float dt)
        {
            bool attemptedMeaningfulMove = math.lengthsq(attemptedVelocity) * dt * dt > 0.000001f;
            bool barelyMoved = math.lengthsq(endPosition - startPosition) < 0.000001f;
            if (attemptedMeaningfulMove && barelyMoved)
            {
                kinematic.StuckFrames++;
                kinematic.LastStuckPosition = endPosition;
            }
            else
            {
                kinematic.StuckFrames = 0;
            }
        }

        static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
    }
}
