using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Movement
{
    public readonly struct MorrowindActorMovementResult
    {
        public readonly float2 PlanarInput;
        public readonly float3 LocalMoveWorld;
        public readonly float3 FinalVelocity;

        public MorrowindActorMovementResult(
            float2 planarInput,
            float3 localMoveWorld,
            float3 finalVelocity)
        {
            PlanarInput = planarInput;
            LocalMoveWorld = localMoveWorld;
            FinalVelocity = finalVelocity;
        }
    }

    [BurstCompile]
    public static partial class MorrowindActorMovementKernel
    {
        struct MovementSolveScratch
        {
            public byte StepSucceeded;
            public MorrowindSupportKind SupportKind;
            public float3 GroundNormal;
            public Entity StandingOn;
        }

        const float MinMoveEpsilon = 1e-5f;
        const float MinInputEpsilonSq = 1e-4f;
        const float JumpDiagonalScale = 0.707f;
        const float SupportSlopeReportingThreshold = 0.97f;
        const float FlatGroundSnapToleranceScale = 0.5f;
        const float ExtraStairHackStep = 10f;
        const float ExtraStairHackStep2 = 20f;

        [BurstCompile]
        public static void Solve(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementSettings settings,
            in MorrowindMovementSpeed speed,
            in ComponentLookup<PlayerTag> playerLookup,
            in ComponentLookup<PassiveActorPresence> passiveActorLookup,
            in quaternion rotation,
            ref float3 position,
            ref MorrowindMovementInput input,
            ref MorrowindMovementState movementState,
            float dt, out MorrowindActorMovementResult mamr)
        {
            var scratch = new MovementSolveScratch { GroundNormal = math.up() };

            movementState.JumpAccepted = false;
            bool wasGrounded = movementState.Grounded;
            bool wasOnSlope = movementState.OnSlope;
            bool hadSolidGroundBeforeMove = wasGrounded && !wasOnSlope;

            float2 planarInput = input.LocalMove;
            float inputLengthSq = math.lengthsq(planarInput);
            if (inputLengthSq > 1f)
                planarInput *= math.rsqrt(inputLengthSq);

            movementState.LocalMove = planarInput;
            movementState.SpeedFactor = math.saturate(math.length(planarInput));
            movementState.IsStrafing = math.abs(planarInput.x) > math.abs(planarInput.y) * 2f;
            movementState.RunHeld = input.RunHeld;
            bool effectiveSneak = input.SneakHeld || movementState.ForceSneak;
            movementState.SneakHeld = effectiveSneak;

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
                float resolvedSpeed = speed.GetCurrentSpeed(input.RunHeld, effectiveSneak, movementState.SpeedFactor, movementState.IsStrafing);
                desiredVelocity = localMoveWorld * resolvedSpeed;
            }

            bool jumpRequested = input.JumpPressed;
            bool canJump = jumpRequested && hadSolidGroundBeforeMove && !effectiveSneak;
            if (canJump)
            {
                movementState.JumpAccepted = true;
                float jumpSpeed = speed.GetJumpSpeed(input.RunHeld);
                if (math.lengthsq(desiredVelocity.xz) <= MinMoveEpsilon)
                {
                    movementState.Inertia = new float3(0f, jumpSpeed, 0f);
                }
                else
                {
                    float3 horizontal = math.normalizesafe(new float3(desiredVelocity.x, 0f, desiredVelocity.z));
                    movementState.Inertia = new float3(horizontal.x, 1f, horizontal.z) * jumpSpeed * JumpDiagonalScale;
                }

                hadSolidGroundBeforeMove = false;
            }

            float airControlFactor = 1f;
            if (!hadSolidGroundBeforeMove)
                airControlFactor = speed.JumpMoveFactor;

            float3 velocity = desiredVelocity * airControlFactor;
            if (!hadSolidGroundBeforeMove)
                velocity += movementState.Inertia;

            if (math.lengthsq(velocity) > MinMoveEpsilon)
                MoveKinematic(world, collider, settings, ref position, ref velocity, dt, hadSolidGroundBeforeMove, ref scratch);

            bool allowGroundedRecoveryFallback = false;
            bool shouldResolveSupport = scratch.StepSucceeded != 0 || movementState.Inertia.y <= 0f;

            GroundSupportResult support = GroundSupportResult.None(position);
            
            if(shouldResolveSupport)FindGroundSupport(
                    world,
                    collider,
                    position,
                    settings,
                    playerLookup,
                    passiveActorLookup,
                    wasGrounded,
                    allowGroundedRecoveryFallback, out support);

            ApplySupportResult(
                world,
                collider,
                settings,
                ref position,
                ref movementState,
                ref scratch,
                support,
                hadSolidGroundBeforeMove);

            if (movementState.Grounded && !movementState.OnSlope)
                velocity.y = 0f;

            if (!(movementState.Grounded && !movementState.OnSlope))
                movementState.Inertia.y -= settings.Gravity * dt;

            movementState.LastVelocity = velocity;
            movementState.GroundNormal = scratch.GroundNormal;
            movementState.StandingOn = scratch.StandingOn;
            movementState.SupportKind = (byte)scratch.SupportKind;
            mamr = new MorrowindActorMovementResult(planarInput, localMoveWorld, velocity);
        }
    }
}
