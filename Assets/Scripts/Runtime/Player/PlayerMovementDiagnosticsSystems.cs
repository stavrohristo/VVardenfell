using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerFixedStepMovementSystem))]
    public partial class MorrowindMovementDiagnosticsSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            EnsureDiagnosticsSingleton();
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<MorrowindMovementIntent>(),
                ComponentType.ReadOnly<MorrowindActorKinematicState>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadOnly<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<MorrowindMovementFrameTrace>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<MorrowindMovementDiagnosticsState>();
            RequireForUpdate<MorrowindMovementDiagnosticsSnapshot>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            Entity player = _playerQuery.GetSingletonEntity();
            var transform = _playerQuery.GetSingleton<LocalTransform>();
            var intent = _playerQuery.GetSingleton<MorrowindMovementIntent>();
            var kinematic = _playerQuery.GetSingleton<MorrowindActorKinematicState>();
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            var vitals = _playerQuery.GetSingleton<ActorVitalSet>();
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            var derived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var trace = _playerQuery.GetSingleton<MorrowindMovementFrameTrace>();
            var frameState = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>();
            uint fixedTick = frameState.SnapshotTick;

            ref var state = ref SystemAPI.GetSingletonRW<MorrowindMovementDiagnosticsState>().ValueRW;
            state.SnapshotSequence++;
            state.LastFixedTick = fixedTick;
            state.LastMovementSequence = trace.Sequence;
            state.LastGrounded = ToByte(kinematic.Grounded);
            state.LastOnSlope = ToByte(kinematic.OnSlope);
            state.LastJumpAccepted = trace.JumpAccepted;
            state.LastStuckFrames = kinematic.StuckFrames;

            ref var snapshot = ref SystemAPI.GetSingletonRW<MorrowindMovementDiagnosticsSnapshot>().ValueRW;
            snapshot = new MorrowindMovementDiagnosticsSnapshot
            {
                SnapshotSequence = state.SnapshotSequence,
                FixedTick = fixedTick,
                MovementSequence = trace.Sequence,
                PlayerEntity = player,
                DeltaTime = trace.DeltaTime,
                Position = transform.Position,
                LocalMove = intent.LocalMove,
                RunHeld = ToByte(intent.RunHeld),
                SneakHeld = ToByte(intent.SneakHeld),
                JumpHeld = ToByte(intent.JumpHeld),
                SpeedFactor = intent.SpeedFactor,
                IsStrafing = ToByte(intent.IsStrafing),
                ResolvedSpeed = trace.ResolvedSpeed,
                DesiredVelocity = trace.DesiredVelocity,
                FinalVelocity = trace.FinalVelocity,
                Inertia = kinematic.Inertia,
                Grounded = ToByte(kinematic.Grounded),
                OnSlope = ToByte(kinematic.OnSlope),
                WalkingOnWater = ToByte(kinematic.WalkingOnWater),
                StandingOn = kinematic.StandingOn,
                StuckFrames = kinematic.StuckFrames,
                LastStuckPosition = kinematic.LastStuckPosition,
                GroundNormal = trace.GroundNormal,
                SweepIterations = trace.SweepIterations,
                SlideCount = trace.SlideCount,
                PreviousSupportKind = trace.PreviousSupportKind,
                SupportKind = trace.SupportKind,
                SupportSnapMode = trace.SupportSnapMode,
                SupportRejectedSteep = trace.SupportRejectedSteep,
                LandingConsumedInertia = trace.LandingConsumedInertia,
                GroundProbeSnapped = trace.GroundProbeSnapped,
                StepAttempted = trace.StepAttempted,
                StepAttemptIndex = trace.StepAttemptIndex,
                StepSucceeded = trace.StepSucceeded,
                SteepSlopeRejected = trace.SteepSlopeRejected,
                UsedSeamLogic = trace.UsedSeamLogic,
                UsedGroundedWallNormalFlatten = trace.UsedGroundedWallNormalFlatten,
                JumpRequested = trace.JumpRequested,
                JumpAccepted = trace.JumpAccepted,
                LastBlocker = trace.LastBlocker,
                LastBlockerNormal = trace.LastBlockerNormal,
                LastBlockerFraction = trace.LastBlockerFraction,
                Strength = attributes.Strength,
                Willpower = attributes.Willpower,
                Agility = attributes.Agility,
                Endurance = attributes.Endurance,
                SpeedAttribute = attributes.Speed,
                Athletics = skills.Athletics,
                Acrobatics = skills.Acrobatics,
                CurrentFatigue = vitals.CurrentFatigue,
                ModifiedFatigueBase = vitals.ModifiedFatigueBase,
                JumpMagnitude = effectModifiers.JumpMagnitude,
                FeatherMagnitude = effectModifiers.FeatherMagnitude,
                BurdenMagnitude = effectModifiers.BurdenMagnitude,
                CarryCapacity = derived.CarryCapacity,
                Encumbrance = derived.Encumbrance,
                NormalizedEncumbrance = derived.NormalizedEncumbrance,
                FatigueTerm = derived.FatigueTerm,
                WalkSpeed = derived.WalkSpeed * VVardenfell.Core.WorldScale.MwUnitsToMeters,
                RunSpeed = derived.RunSpeed * VVardenfell.Core.WorldScale.MwUnitsToMeters,
                SneakWalkSpeed = derived.SneakWalkSpeed * VVardenfell.Core.WorldScale.MwUnitsToMeters,
                JumpSpeed = MorrowindPlayerSpeedResolver.Build(RuntimeContentDatabase.Active, attributes, skills, vitals, effectModifiers, derived)
                    .GetJumpSpeed(intent.RunHeld && !intent.SneakHeld),
                JumpMoveFactor = derived.JumpMoveFactor,
                StatusText = BuildStatusText(kinematic, trace),
            };
        }

        void EnsureDiagnosticsSingleton()
        {
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindMovementDiagnosticsState>());
            if (!query.IsEmptyIgnoreFilter)
                return;

            Entity entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "VVardenfell.MorrowindMovementDiagnostics");
            EntityManager.AddComponentData(entity, new MorrowindMovementDiagnosticsState());
            EntityManager.AddComponentData(entity, new MorrowindMovementDiagnosticsSnapshot());
        }

        static FixedString128Bytes BuildStatusText(
            in MorrowindActorKinematicState kinematic,
            in MorrowindMovementFrameTrace trace)
        {
            if (kinematic.StuckFrames > 0)
                return ToFixed128("stuck");
            if (trace.JumpAccepted != 0)
                return ToFixed128("jump");

            switch ((MorrowindSupportKind)trace.SupportKind)
            {
                case MorrowindSupportKind.ActorTop:
                    return ToFixed128("actor-top");
                case MorrowindSupportKind.WalkableSlope:
                    return ToFixed128("walkable-slope");
                case MorrowindSupportKind.WaterSurfaceCandidate:
                    return ToFixed128("water-surface");
                case MorrowindSupportKind.RecoveryFlat:
                    return ToFixed128("recovery");
            }

            if (!kinematic.Grounded)
                return trace.SupportRejectedSteep != 0 ? ToFixed128("steep-slope") : ToFixed128("airborne");
            if (trace.StepSucceeded != 0)
                return ToFixed128("step");
            return ToFixed128("grounded");
        }

        static FixedString128Bytes ToFixed128(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;
            if (value.Length > 127)
                value = value.Substring(0, 127);
            return new FixedString128Bytes(value);
        }

        static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
    }

    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class MorrowindMovementDiagnosticsConsoleLogSystem : SystemBase
    {
        uint _lastSnapshotSequence;
        byte _lastGrounded;
        byte _lastSneakHeld;
        byte _lastRunHeld;
        byte _lastSupportKind;
        FixedString128Bytes _lastStatusText;
        bool _initialized;

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindMovementDiagnosticsSnapshot>();
        }

        protected override void OnUpdate()
        {
            var snapshot = SystemAPI.GetSingleton<MorrowindMovementDiagnosticsSnapshot>();
            if (snapshot.SnapshotSequence == _lastSnapshotSequence)
                return;

            bool shouldLog = !_initialized
                || snapshot.Grounded != _lastGrounded
                || snapshot.SneakHeld != _lastSneakHeld
                || snapshot.RunHeld != _lastRunHeld
                || snapshot.SupportKind != _lastSupportKind
                || !snapshot.StatusText.Equals(_lastStatusText)
                || snapshot.JumpAccepted != 0
                || snapshot.StepAttempted != 0
                || snapshot.StepSucceeded != 0
                || snapshot.UsedSeamLogic != 0
                || snapshot.UsedGroundedWallNormalFlatten != 0;

            _lastSnapshotSequence = snapshot.SnapshotSequence;
            _lastGrounded = snapshot.Grounded;
            _lastSneakHeld = snapshot.SneakHeld;
            _lastRunHeld = snapshot.RunHeld;
            _lastSupportKind = snapshot.SupportKind;
            _lastStatusText = snapshot.StatusText;
            _initialized = true;

            if (!shouldLog)
                return;

        }
    }

    public static class MorrowindMovementDebug
    {
        public static bool TryGetSnapshot(out MorrowindMovementDiagnosticsSnapshot snapshot, out string error)
        {
            snapshot = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindMovementDiagnosticsSnapshot>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Movement diagnostics snapshot is not ready.";
                return false;
            }

            snapshot = query.GetSingleton<MorrowindMovementDiagnosticsSnapshot>();
            error = null;
            return true;
        }

        public static string DescribeLatest()
        {
            if (!TryGetSnapshot(out var snapshot, out string error))
                return $"Movement diagnostics unavailable: {error}";

            var builder = new StringBuilder(768);
            builder.Append("[VVardenfell][MovementDiagnostics]");
            builder.Append(" snapshot=");
            builder.Append(snapshot.SnapshotSequence);
            builder.Append(" movement=");
            builder.Append(snapshot.MovementSequence);
            builder.Append(" tick=");
            builder.Append(snapshot.FixedTick);
            builder.Append(" status=");
            builder.Append(snapshot.StatusText.ToString());
            builder.Append(" pos=");
            AppendVector(builder, snapshot.Position);
            builder.Append(" dt=");
            builder.Append(snapshot.DeltaTime.ToString("F4"));
            builder.AppendLine();

            builder.Append("  intent local=");
            AppendVector(builder, snapshot.LocalMove);
            builder.Append(" run=");
            builder.Append(snapshot.RunHeld != 0 ? "yes" : "no");
            builder.Append(" sneak=");
            builder.Append(snapshot.SneakHeld != 0 ? "yes" : "no");
            builder.Append(" jumpHeld=");
            builder.Append(snapshot.JumpHeld != 0 ? "yes" : "no");
            builder.Append(" speedFactor=");
            builder.Append(snapshot.SpeedFactor.ToString("F2"));
            builder.Append(" strafing=");
            builder.Append(snapshot.IsStrafing != 0 ? "yes" : "no");
            builder.AppendLine();

            builder.Append("  speed=");
            builder.Append(snapshot.ResolvedSpeed.ToString("F2"));
            builder.Append(" desired=");
            AppendVector(builder, snapshot.DesiredVelocity);
            builder.Append(" final=");
            AppendVector(builder, snapshot.FinalVelocity);
            builder.Append(" inertia=");
            AppendVector(builder, snapshot.Inertia);
            builder.AppendLine();

            builder.Append("  ground grounded=");
            builder.Append(snapshot.Grounded != 0 ? "yes" : "no");
            builder.Append(" slope=");
            builder.Append(snapshot.OnSlope != 0 ? "yes" : "no");
            builder.Append(" prevSupport=");
            builder.Append(DescribeSupportKind(snapshot.PreviousSupportKind));
            builder.Append(" support=");
            builder.Append(DescribeSupportKind(snapshot.SupportKind));
            builder.Append(" snapMode=");
            builder.Append(DescribeSupportSnapMode(snapshot.SupportSnapMode));
            builder.Append(" normal=");
            AppendVector(builder, snapshot.GroundNormal);
            builder.Append(" standingOn=");
            builder.Append(snapshot.StandingOn);
            builder.Append(" stuck=");
            builder.Append(snapshot.StuckFrames);
            builder.AppendLine();

            builder.Append("  solver sweeps=");
            builder.Append(snapshot.SweepIterations);
            builder.Append(" slides=");
            builder.Append(snapshot.SlideCount);
            builder.Append(" snap=");
            builder.Append(snapshot.GroundProbeSnapped != 0 ? "yes" : "no");
            builder.Append(" supportSteepReject=");
            builder.Append(snapshot.SupportRejectedSteep != 0 ? "yes" : "no");
            builder.Append(" landingConsumed=");
            builder.Append(snapshot.LandingConsumedInertia != 0 ? "yes" : "no");
            builder.Append(" step=");
            builder.Append(snapshot.StepAttempted != 0
                ? (snapshot.StepSucceeded != 0 ? "succeeded" : "failed")
                : "none");
            if (snapshot.StepAttempted != 0)
            {
                builder.Append("#");
                builder.Append(snapshot.StepAttemptIndex);
            }
            builder.Append(" steepReject=");
            builder.Append(snapshot.SteepSlopeRejected != 0 ? "yes" : "no");
            builder.Append(" seam=");
            builder.Append(snapshot.UsedSeamLogic != 0 ? "yes" : "no");
            builder.Append(" wallFlatten=");
            builder.Append(snapshot.UsedGroundedWallNormalFlatten != 0 ? "yes" : "no");
            builder.Append(" jump=");
            builder.Append(snapshot.JumpRequested != 0
                ? (snapshot.JumpAccepted != 0 ? "accepted" : "rejected")
                : "none");
            builder.AppendLine();

            builder.Append("  blocker=");
            builder.Append(snapshot.LastBlocker);
            builder.Append(" normal=");
            AppendVector(builder, snapshot.LastBlockerNormal);
            builder.Append(" fraction=");
            builder.Append(snapshot.LastBlockerFraction.ToString("F3"));
            builder.AppendLine();

            builder.Append("  stats str=");
            builder.Append(snapshot.Strength.ToString("F0"));
            builder.Append(" wil=");
            builder.Append(snapshot.Willpower.ToString("F0"));
            builder.Append(" agi=");
            builder.Append(snapshot.Agility.ToString("F0"));
            builder.Append(" end=");
            builder.Append(snapshot.Endurance.ToString("F0"));
            builder.Append(" spd=");
            builder.Append(snapshot.SpeedAttribute.ToString("F0"));
            builder.Append(" ath=");
            builder.Append(snapshot.Athletics.ToString("F0"));
            builder.Append(" acro=");
            builder.Append(snapshot.Acrobatics.ToString("F0"));
            builder.Append(" fatigue=");
            builder.Append(snapshot.CurrentFatigue.ToString("F1"));
            builder.Append("/");
            builder.Append(snapshot.ModifiedFatigueBase.ToString("F1"));
            builder.Append(" fatigueTerm=");
            builder.Append(snapshot.FatigueTerm.ToString("F2"));
            builder.AppendLine();

            builder.Append("  derived enc=");
            builder.Append(snapshot.Encumbrance.ToString("F1"));
            builder.Append("/");
            builder.Append(snapshot.CarryCapacity.ToString("F1"));
            builder.Append(" normEnc=");
            builder.Append(snapshot.NormalizedEncumbrance.ToString("F2"));
            builder.Append(" walk=");
            builder.Append(snapshot.WalkSpeed.ToString("F2"));
            builder.Append(" run=");
            builder.Append(snapshot.RunSpeed.ToString("F2"));
            builder.Append(" sneak=");
            builder.Append(snapshot.SneakWalkSpeed.ToString("F2"));
            builder.Append(" jump=");
            builder.Append(snapshot.JumpSpeed.ToString("F2"));
            builder.Append(" jumpMove=");
            builder.Append(snapshot.JumpMoveFactor.ToString("F2"));
            return builder.ToString();
        }

        public static void LogLatest()
        {
        }

        static string DescribeSupportKind(byte raw)
        {
            return ((MorrowindSupportKind)raw) switch
            {
                MorrowindSupportKind.None => "none",
                MorrowindSupportKind.FlatGround => "flat",
                MorrowindSupportKind.WalkableSlope => "walkable-slope",
                MorrowindSupportKind.ActorTop => "actor-top",
                MorrowindSupportKind.WaterSurfaceCandidate => "water",
                MorrowindSupportKind.RecoveryFlat => "recovery",
                _ => $"unknown({raw})",
            };
        }

        static string DescribeSupportSnapMode(byte raw)
        {
            return ((MorrowindSupportSnapMode)raw) switch
            {
                MorrowindSupportSnapMode.None => "none",
                MorrowindSupportSnapMode.Offset => "offset",
                MorrowindSupportSnapMode.Settle => "settle",
                _ => $"unknown({raw})",
            };
        }

        static void AppendVector(StringBuilder builder, float3 value)
        {
            builder.Append("(");
            builder.Append(value.x.ToString("F2"));
            builder.Append(",");
            builder.Append(value.y.ToString("F2"));
            builder.Append(",");
            builder.Append(value.z.ToString("F2"));
            builder.Append(")");
        }
    }
}
