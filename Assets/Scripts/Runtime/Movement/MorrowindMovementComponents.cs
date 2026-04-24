using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;

namespace VVardenfell.Runtime.Movement
{
    public enum MorrowindSupportKind : byte
    {
        None = 0,
        FlatGround = 1,
        WalkableSlope = 2,
        ActorTop = 3,
        WaterSurfaceCandidate = 4,
        RecoveryFlat = 5,
    }

    public enum MorrowindSupportSnapMode : byte
    {
        None = 0,
        Offset = 1,
        Settle = 2,
    }

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
        public byte PreviousSupportKind;
        public byte SupportKind;
        public byte SupportSnapMode;
        public byte SupportRejectedSteep;
        public byte LandingConsumedInertia;
        public byte GroundProbeSnapped;
        public byte StepAttempted;
        public byte StepAttemptIndex;
        public byte StepSucceeded;
        public byte SteepSlopeRejected;
        public byte UsedSeamLogic;
        public byte UsedGroundedWallNormalFlatten;
        public byte JumpRequested;
        public byte JumpAccepted;
    }
}
