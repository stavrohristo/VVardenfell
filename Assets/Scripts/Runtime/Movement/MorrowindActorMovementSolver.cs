using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

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
        const float SupportSlopeReportingThreshold = 0.97f;
        const float FlatGroundSnapToleranceScale = 0.5f;
        const float ExtraStairHackStep = 10f;
        const float ExtraStairHackStep2 = 20f;

        public static MorrowindActorMovementResult Solve(
            EntityManager entityManager,
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
            bool wasOnSlope = kinematic.OnSlope;
            bool hadSolidGroundBeforeMove = wasGrounded && !wasOnSlope;
            trace.PreviousSupportKind = (byte)ResolvePreviousSupportKind(previousTrace, kinematic, entityManager);
            trace.SupportKind = (byte)MorrowindSupportKind.None;
            trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.None;

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
            bool canJump = jumpRequested && hadSolidGroundBeforeMove && !intent.SneakHeld;
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

                hadSolidGroundBeforeMove = false;
            }

            float airControlFactor = 1f;
            if (!hadSolidGroundBeforeMove)
                airControlFactor = stats.GetJumpMoveFactor();

            float3 velocity = desiredVelocity * airControlFactor;
            if (!hadSolidGroundBeforeMove)
                velocity += kinematic.Inertia;

            if (math.lengthsq(velocity) > MinMoveEpsilon)
                MoveKinematic(world, collider, tuning, ref position, ref velocity, dt, hadSolidGroundBeforeMove, ref trace);

            bool allowGroundedRecoveryFallback = wasGrounded && kinematic.StuckFrames > 0;
            bool shouldResolveSupport = trace.StepSucceeded != 0 || kinematic.Inertia.y <= 0f || kinematic.StuckFrames > 0;
            var support = shouldResolveSupport
                ? FindGroundSupport(
                    entityManager,
                    world,
                    collider,
                    position,
                    tuning,
                    wasGrounded,
                    allowGroundedRecoveryFallback)
                : GroundSupportResult.None(position);

            ApplySupportResult(
                world,
                collider,
                tuning,
                ref position,
                ref kinematic,
                ref trace,
                support,
                hadSolidGroundBeforeMove);

            if (kinematic.Grounded && !kinematic.OnSlope)
                velocity.y = 0f;

            if (!(kinematic.Grounded && !kinematic.OnSlope))
                kinematic.Inertia.y -= tuning.Gravity * dt;

            trace.FinalVelocity = velocity;
            trace.EndPosition = position;
            UpdateStuckState(ref kinematic, trace.StartPosition, trace.EndPosition, velocity, dt);
            return new MorrowindActorMovementResult(planarInput, localMoveWorld, velocity, trace);
        }

        readonly struct GroundSupportResult
        {
            public readonly MorrowindSupportKind Kind;
            public readonly float3 Normal;
            public readonly float3 HitPosition;
            public readonly float3 SupportedPosition;
            public readonly Entity StandingOn;
            public readonly float Fraction;
            public readonly float ProbeDistance;
            public readonly bool RejectedSteep;

            GroundSupportResult(
                MorrowindSupportKind kind,
                float3 normal,
                float3 hitPosition,
                float3 supportedPosition,
                Entity standingOn,
                float fraction,
                float probeDistance,
                bool rejectedSteep)
            {
                Kind = kind;
                Normal = normal;
                HitPosition = hitPosition;
                SupportedPosition = supportedPosition;
                StandingOn = standingOn;
                Fraction = fraction;
                ProbeDistance = probeDistance;
                RejectedSteep = rejectedSteep;
            }

            public static GroundSupportResult None(float3 position, bool rejectedSteep = false)
                => new(MorrowindSupportKind.None, math.up(), position, position, Entity.Null, 1f, 0f, rejectedSteep);

            public static GroundSupportResult Create(
                MorrowindSupportKind kind,
                float3 normal,
                float3 hitPosition,
                float3 supportedPosition,
                Entity standingOn,
                float fraction,
                float probeDistance,
                bool rejectedSteep = false)
                => new(kind, normal, hitPosition, supportedPosition, standingOn, fraction, probeDistance, rejectedSteep);

            public bool HasSolidSupport =>
                Kind == MorrowindSupportKind.FlatGround
                || Kind == MorrowindSupportKind.WalkableSlope
                || Kind == MorrowindSupportKind.RecoveryFlat;
        }

        static MorrowindSupportKind ResolvePreviousSupportKind(
            in MorrowindMovementFrameTrace previousTrace,
            in MorrowindActorKinematicState kinematic,
            EntityManager entityManager)
        {
            if (previousTrace.SupportKind != 0)
                return (MorrowindSupportKind)previousTrace.SupportKind;

            if (kinematic.WalkingOnWater)
                return MorrowindSupportKind.WaterSurfaceCandidate;

            if (!kinematic.Grounded)
                return MorrowindSupportKind.None;

            if (IsActorSupport(entityManager, kinematic.StandingOn))
                return MorrowindSupportKind.ActorTop;

            return MorrowindSupportKind.FlatGround;
        }

        static GroundSupportResult FindGroundSupport(
            EntityManager entityManager,
            in CollisionWorld world,
            in PhysicsCollider collider,
            float3 position,
            in MorrowindMovementTuning tuning,
            bool wasGrounded,
            bool allowRecoveryFallback)
        {
            if (allowRecoveryFallback)
            {
                return GroundSupportResult.Create(
                    MorrowindSupportKind.RecoveryFlat,
                    math.up(),
                    position,
                    position,
                    Entity.Null,
                    0f,
                    0f);
            }

            float dropDistance = 2f * tuning.GroundOffset + (wasGrounded ? tuning.StepSizeDown : 0f);
            var castInput = new ColliderCastInput(
                collider.Value,
                position,
                position - new float3(0f, dropDistance, 0f),
                quaternion.identity);

            if (!world.CastCollider(castInput, out ColliderCastHit hit))
                return GroundSupportResult.None(position);

            float3 hitPosition = position - new float3(0f, dropDistance * hit.Fraction, 0f);
            float3 supportedPosition = hitPosition + new float3(0f, tuning.GroundOffset, 0f);
            bool walkable = IsWalkableSlope(hit.SurfaceNormal, tuning.MaxSlopeCosine);
            bool actorTop = IsActorSupport(entityManager, hit.Entity);

            if (actorTop)
            {
                return walkable
                    ? GroundSupportResult.Create(
                        MorrowindSupportKind.ActorTop,
                        hit.SurfaceNormal,
                        hitPosition,
                        supportedPosition,
                        hit.Entity,
                        hit.Fraction,
                        dropDistance)
                    : GroundSupportResult.None(position, rejectedSteep: true);
            }

            if (!walkable)
                return GroundSupportResult.None(position, rejectedSteep: true);

            MorrowindSupportKind kind = hit.SurfaceNormal.y >= SupportSlopeReportingThreshold
                ? MorrowindSupportKind.FlatGround
                : MorrowindSupportKind.WalkableSlope;

            return GroundSupportResult.Create(
                kind,
                hit.SurfaceNormal,
                hitPosition,
                supportedPosition,
                hit.Entity,
                hit.Fraction,
                dropDistance);
        }

        static void ApplySupportResult(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref MorrowindActorKinematicState kinematic,
            ref MorrowindMovementFrameTrace trace,
            in GroundSupportResult support,
            bool hadSolidGroundBeforeMove)
        {
            trace.SupportKind = (byte)support.Kind;
            trace.SupportRejectedSteep = ToByte(support.RejectedSteep);
            trace.GroundNormal = support.Normal;
            trace.StandingOn = support.StandingOn;
            kinematic.WalkingOnWater = support.Kind == MorrowindSupportKind.WaterSurfaceCandidate;

            if (support.Kind == MorrowindSupportKind.ActorTop)
            {
                if (support.SupportedPosition.y <= position.y)
                    position.y = support.SupportedPosition.y;

                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = support.StandingOn;
                return;
            }

            if (support.Kind != MorrowindSupportKind.RecoveryFlat)
            {
                kinematic.StuckFrames = 0;
                kinematic.LastStuckPosition = position;
            }

            if (!support.HasSolidSupport)
            {
                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = Entity.Null;
                return;
            }

            kinematic.Grounded = true;
            kinematic.OnSlope = false;
            kinematic.StandingOn = support.StandingOn;

            if (!hadSolidGroundBeforeMove && kinematic.Inertia.y <= 0f)
                trace.LandingConsumedInertia = 1;

            ApplyLandingSnap(world, collider, tuning, ref position, ref trace, support, hadSolidGroundBeforeMove);
            kinematic.Inertia = float3.zero;
        }

        static void ApplyLandingSnap(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref MorrowindMovementFrameTrace trace,
            in GroundSupportResult support,
            bool hadSolidGroundBeforeMove)
        {
            float hitDistance = support.Fraction * support.ProbeDistance;
            float flatGroundSnapTolerance = math.max(tuning.CollisionMargin, tuning.GroundOffset * FlatGroundSnapToleranceScale);
            bool withinFlatGroundTolerance = hitDistance <= tuning.GroundOffset + flatGroundSnapTolerance;
            bool routineSupportedSurface = hadSolidGroundBeforeMove
                && (support.Kind == MorrowindSupportKind.FlatGround || support.Kind == MorrowindSupportKind.WalkableSlope);

            if (support.Kind == MorrowindSupportKind.RecoveryFlat || hitDistance > tuning.GroundOffset)
            {
                if (routineSupportedSurface
                    && withinFlatGroundTolerance)
                {
                    trace.GroundProbeSnapped = 0;
                    trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.None;
                    return;
                }

                trace.GroundProbeSnapped = 1;
                position = support.SupportedPosition;
                trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.Offset;
                return;
            }

            if (routineSupportedSurface)
            {
                trace.GroundProbeSnapped = 0;
                trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.None;
                return;
            }

            float3 start = support.HitPosition;
            float3 settleEnd = start + new float3(0f, 2f * tuning.GroundOffset, 0f);
            var settleCast = new ColliderCastInput(collider.Value, start, settleEnd, quaternion.identity);
            float3 settleHitPosition = settleEnd;
            if (world.CastCollider(settleCast, out ColliderCastHit settleHit))
                settleHitPosition = start + new float3(0f, 2f * tuning.GroundOffset * settleHit.Fraction, 0f);

            trace.GroundProbeSnapped = 1;
            position = (start + settleHitPosition) * 0.5f;
            trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.Settle;
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
                    stepped = TryStep(world, collider, tuning, ref position, ref velocity, ref remainingTime, startedGrounded, iteration == 0, ref trace);
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
                {
                    planeNormal = math.normalizesafe(new float3(planeNormal.x, 0f, planeNormal.z), planeNormal);
                    trace.UsedGroundedWallNormalFlatten = 1;
                }

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
                            trace.UsedSeamLogic = 1;
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
            bool firstIteration,
            ref MorrowindMovementFrameTrace trace)
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
                trace.StepAttempted = 1;
                trace.StepAttemptIndex = (byte)attempt;

                if (attempt > 1 && !firstIteration)
                    return false;

                float attemptDistance = attempt == 1
                    ? moveDistance
                    : (attempt == 2
                        ? ExtraStairHackStep * WorldScale.MwUnitsToMeters
                        : ExtraStairHackStep2 * WorldScale.MwUnitsToMeters);
                if (attempt == 3)
                {
                    upDistance = math.min(upDistance, tuning.StepSizeUp);
                }

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

        static bool IsWalkableSlope(float3 normal, float maxSlopeCosine) => normal.y > maxSlopeCosine;

        static bool IsActorSupport(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null
                && entityManager.Exists(entity)
                && (entityManager.HasComponent<PlayerTag>(entity) || entityManager.HasComponent<PassiveActorPresence>(entity));
        }

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
