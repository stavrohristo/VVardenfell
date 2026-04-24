using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

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

    public static partial class MorrowindActorMovementSolver
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
    }
}
