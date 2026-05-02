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

    public struct MorrowindMovementInput : IComponentData
    {
        public const byte RunFlag = 1 << 0;
        public const byte SneakFlag = 1 << 1;
        public const byte JumpPressedFlag = 1 << 2;

        public float2 LocalMove;
        public byte Flags;

        public bool RunHeld
        {
            readonly get => (Flags & RunFlag) != 0;
            set => SetFlag(RunFlag, value);
        }

        public bool SneakHeld
        {
            readonly get => (Flags & SneakFlag) != 0;
            set => SetFlag(SneakFlag, value);
        }

        public bool JumpPressed
        {
            readonly get => (Flags & JumpPressedFlag) != 0;
            set => SetFlag(JumpPressedFlag, value);
        }

        void SetFlag(byte flag, bool value)
        {
            Flags = value ? (byte)(Flags | flag) : (byte)(Flags & ~flag);
        }
    }

    public struct MorrowindMovementState : IComponentData
    {
        public const byte GroundedFlag = 1 << 0;
        public const byte OnSlopeFlag = 1 << 1;
        public const byte WalkingOnWaterFlag = 1 << 2;
        public const byte StrafingFlag = 1 << 3;
        public const byte JumpAcceptedFlag = 1 << 4;
        public const byte RunFlag = 1 << 5;
        public const byte SneakFlag = 1 << 6;
        public const byte ForceSneakFlag = 1 << 7;

        public float3 Inertia;
        public float3 LastVelocity;
        public float3 GroundNormal;
        public Entity StandingOn;
        public float2 LocalMove;
        public float SpeedFactor;
        public byte Flags;
        public byte SupportKind;

        public bool Grounded
        {
            readonly get => (Flags & GroundedFlag) != 0;
            set => SetFlag(GroundedFlag, value);
        }

        public bool OnSlope
        {
            readonly get => (Flags & OnSlopeFlag) != 0;
            set => SetFlag(OnSlopeFlag, value);
        }

        public bool WalkingOnWater
        {
            readonly get => (Flags & WalkingOnWaterFlag) != 0;
            set => SetFlag(WalkingOnWaterFlag, value);
        }

        public bool IsStrafing
        {
            readonly get => (Flags & StrafingFlag) != 0;
            set => SetFlag(StrafingFlag, value);
        }

        public bool JumpAccepted
        {
            readonly get => (Flags & JumpAcceptedFlag) != 0;
            set => SetFlag(JumpAcceptedFlag, value);
        }

        public bool RunHeld
        {
            readonly get => (Flags & RunFlag) != 0;
            set => SetFlag(RunFlag, value);
        }

        public bool SneakHeld
        {
            readonly get => (Flags & SneakFlag) != 0;
            set => SetFlag(SneakFlag, value);
        }

        public bool ForceSneak
        {
            readonly get => (Flags & ForceSneakFlag) != 0;
            set => SetFlag(ForceSneakFlag, value);
        }

        void SetFlag(byte flag, bool value)
        {
            Flags = value ? (byte)(Flags | flag) : (byte)(Flags & ~flag);
        }
    }

    public struct MorrowindMovementSettings : IComponentData
    {
        public float StepSizeUp;
        public float StepSizeDown;
        public float GroundOffset;
        public float CollisionMargin;
        public float MaxSlopeCosine;
        public float Gravity;
        public int MaxIterations;

        public static MorrowindMovementSettings OpenMwDefaults()
        {
            return new MorrowindMovementSettings
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

    public struct MorrowindMovementSpeed : IComponentData
    {
        public float WalkSpeed;
        public float RunSpeed;
        public float SneakWalkSpeed;
        public float JumpSpeed;
        public float JumpRunMultiplier;
        public float JumpMoveFactor;

        public readonly float GetCurrentSpeed(bool running, bool sneaking, float speedFactor, bool strafing)
        {
            float speed = running && !sneaking
                ? RunSpeed
                : (sneaking ? SneakWalkSpeed : WalkSpeed);

            speed *= math.saturate(speedFactor);
            if (strafing)
                speed *= 0.75f;

            return speed * WorldScale.MwUnitsToMeters;
        }

        public readonly float GetJumpSpeed(bool running)
            => JumpSpeed * (running ? math.max(0f, JumpRunMultiplier) : 1f) * WorldScale.MwUnitsToMeters;
    }
}
