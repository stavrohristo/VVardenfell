using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Movement
{
    public struct MorrowindMovementIntent : IComponentData
    {
        public float3 LocalMove;
        public float2 LookDeltaDegrees;
        public bool RunHeld;
        public bool SneakHeld;
        public bool JumpHeld;
        public bool InteractPressed;
        public float SpeedFactor;
        public bool IsStrafing;
    }

    public struct MorrowindActorKinematicState : IComponentData
    {
        public float3 Inertia;
        public bool Grounded;
        public bool OnSlope;
        public bool WalkingOnWater;
        public Entity StandingOn;
        public uint StuckFrames;
        public float3 LastStuckPosition;
    }

    public struct MorrowindMovementTuning : IComponentData
    {
        public float StepSizeUp;
        public float StepSizeDown;
        public float GroundOffset;
        public float CollisionMargin;
        public float MaxSlopeCosine;
        public float Gravity;
        public int MaxIterations;

        public static MorrowindMovementTuning OpenMwDefaults()
        {
            return new MorrowindMovementTuning
            {
                StepSizeUp = 34f * WorldScale.MwUnitsToMeters,
                StepSizeDown = 62f * WorldScale.MwUnitsToMeters,
                GroundOffset = 1f * WorldScale.MwUnitsToMeters,
                CollisionMargin = 0.2f * WorldScale.MwUnitsToMeters,
                MaxSlopeCosine = math.cos(math.radians(46f)),
                Gravity = 8.96f,
                MaxIterations = 8,
            };
        }
    }

    public struct MorrowindMovementFrameTrace : IComponentData
    {
        public uint Sequence;
        public float DeltaTime;
        public float3 StartPosition;
        public float3 EndPosition;
        public float3 DesiredVelocity;
        public float3 FinalVelocity;
        public float ResolvedSpeed;
        public float3 GroundNormal;
        public Entity StandingOn;
        public Entity LastBlocker;
        public float3 LastBlockerNormal;
        public float LastBlockerFraction;
        public int SweepIterations;
        public int SlideCount;
        public byte GroundProbeSnapped;
        public byte StepAttempted;
        public byte StepSucceeded;
        public byte SteepSlopeRejected;
        public byte JumpRequested;
        public byte JumpAccepted;
    }

    public struct MorrowindMovementDiagnosticsState : IComponentData
    {
        public uint SnapshotSequence;
        public uint LastFixedTick;
        public uint LastMovementSequence;
        public byte LastGrounded;
        public byte LastOnSlope;
        public byte LastJumpAccepted;
        public uint LastStuckFrames;
    }

    public struct MorrowindMovementDiagnosticsSnapshot : IComponentData
    {
        public uint SnapshotSequence;
        public uint FixedTick;
        public uint MovementSequence;
        public Entity PlayerEntity;
        public float DeltaTime;
        public float3 Position;
        public float3 LocalMove;
        public byte RunHeld;
        public byte SneakHeld;
        public byte JumpHeld;
        public float SpeedFactor;
        public byte IsStrafing;
        public float ResolvedSpeed;
        public float3 DesiredVelocity;
        public float3 FinalVelocity;
        public float3 Inertia;
        public byte Grounded;
        public byte OnSlope;
        public byte WalkingOnWater;
        public Entity StandingOn;
        public uint StuckFrames;
        public float3 LastStuckPosition;
        public float3 GroundNormal;
        public int SweepIterations;
        public int SlideCount;
        public byte GroundProbeSnapped;
        public byte StepAttempted;
        public byte StepSucceeded;
        public byte SteepSlopeRejected;
        public byte JumpRequested;
        public byte JumpAccepted;
        public Entity LastBlocker;
        public float3 LastBlockerNormal;
        public float LastBlockerFraction;
        public FixedString128Bytes StatusText;
    }

    public static class MorrowindActorMovementStats
    {
        const float DefaultSpeedAttribute = 40f;
        const float DefaultAthletics = 30f;
        const float DefaultAcrobatics = 30f;
        const float DefaultNormalizedEncumbrance = 0f;
        const float DefaultFatigueTerm = 1f;

        public readonly struct Context
        {
            readonly RuntimeContentDatabase _contentDb;

            public Context(RuntimeContentDatabase contentDb)
            {
                _contentDb = contentDb;
            }

            public float GetCurrentSpeed(bool running, bool sneaking, bool inAir, float speedFactor, bool strafing)
            {
                float walkSpeed = Gmst("fMinWalkSpeed", 100f)
                    + 0.01f * DefaultSpeedAttribute * (Gmst("fMaxWalkSpeed", 200f) - Gmst("fMinWalkSpeed", 100f));
                walkSpeed *= 1f - Gmst("fEncumberedMoveEffect", 0.5f) * DefaultNormalizedEncumbrance;
                walkSpeed = math.max(0f, walkSpeed);

                if (sneaking)
                    walkSpeed *= Gmst("fSneakSpeedMultiplier", 0.5f);

                float speed = running && !sneaking
                    ? walkSpeed * (0.01f * DefaultAthletics * Gmst("fAthleticsRunBonus", 1f) + Gmst("fBaseRunMultiplier", 1.75f))
                    : walkSpeed;

                speed *= math.saturate(speedFactor);
                if (strafing)
                    speed *= 0.75f;

                return speed * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpSpeed(bool running)
            {
                float encumbranceTerm = Gmst("fJumpEncumbranceBase", 0f)
                    + Gmst("fJumpEncumbranceMultiplier", 1f) * (1f - DefaultNormalizedEncumbrance);
                float a = DefaultAcrobatics;
                float b = 0f;
                if (a > 50f)
                {
                    b = a - 50f;
                    a = 50f;
                }

                float jump = Gmst("fJumpAcrobaticsBase", 128f)
                    + math.pow(a / 15f, Gmst("fJumpAcroMultiplier", 1f));
                jump += 3f * b * Gmst("fJumpAcroMultiplier", 1f);
                jump *= encumbranceTerm;
                if (running)
                    jump *= Gmst("fJumpRunMultiplier", 1f);
                jump *= DefaultFatigueTerm;
                jump += 8.96f * 69.99125109f;
                jump /= 3f;
                return jump * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpMoveFactor()
            {
                float factor = Gmst("fJumpMoveBase", 0f) + Gmst("fJumpMoveMult", 1f) * DefaultAcrobatics / 100f;
                return math.min(1f, factor);
            }

            float Gmst(string id, float fallback)
            {
                if (_contentDb != null && _contentDb.TryGetGameSettingFloat(id, out float value))
                    return value;
                return fallback;
            }
        }

        public static Context BuildTemporaryPlayer(RuntimeContentDatabase contentDb)
        {
            // TODO: Replace these temporary player defaults with actor stats, active effects,
            // encumbrance, fatigue, water-walking, and levitation once those records exist in ECS.
            return new Context(contentDb);
        }

        public static string DescribeTemporaryPlayerDefaults(RuntimeContentDatabase contentDb)
        {
            var context = BuildTemporaryPlayer(contentDb);
            var builder = new StringBuilder(256);
            builder.Append("defaults speed=");
            builder.Append(DefaultSpeedAttribute.ToString("F0"));
            builder.Append(" athletics=");
            builder.Append(DefaultAthletics.ToString("F0"));
            builder.Append(" acrobatics=");
            builder.Append(DefaultAcrobatics.ToString("F0"));
            builder.Append(" encumbrance=");
            builder.Append(DefaultNormalizedEncumbrance.ToString("F2"));
            builder.Append(" fatigue=");
            builder.Append(DefaultFatigueTerm.ToString("F2"));
            builder.Append(" walk=");
            builder.Append(context.GetCurrentSpeed(false, false, true, 1f, false).ToString("F2"));
            builder.Append(" run=");
            builder.Append(context.GetCurrentSpeed(true, false, true, 1f, false).ToString("F2"));
            builder.Append(" sneak=");
            builder.Append(context.GetCurrentSpeed(false, true, true, 1f, false).ToString("F2"));
            builder.Append(" strafe=");
            builder.Append(context.GetCurrentSpeed(false, false, true, 1f, true).ToString("F2"));
            builder.Append(" jump=");
            builder.Append(context.GetJumpSpeed(true).ToString("F2"));
            builder.Append(" jumpMove=");
            builder.Append(context.GetJumpMoveFactor().ToString("F2"));
            builder.Append(" gmst=");
            builder.Append(contentDb != null ? "runtime" : "fallback");
            return builder.ToString();
        }
    }

    public static class MorrowindPlayerSpeedResolver
    {
        public static MorrowindActorMovementStats.Context Build(RuntimeContentDatabase contentDb)
            => MorrowindActorMovementStats.BuildTemporaryPlayer(contentDb);

        public static string DescribeDefaults(RuntimeContentDatabase contentDb)
            => MorrowindActorMovementStats.DescribeTemporaryPlayerDefaults(contentDb);
    }
}
