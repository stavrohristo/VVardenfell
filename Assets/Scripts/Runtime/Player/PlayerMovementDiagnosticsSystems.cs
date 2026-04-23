using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateAfter(typeof(PlayerFixedStepMovementSystem))]
    [UpdateBefore(typeof(FixedTickSystem))]
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
                ComponentType.ReadOnly<MorrowindMovementFrameTrace>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<MorrowindMovementDiagnosticsState>();
            RequireForUpdate<MorrowindMovementDiagnosticsSnapshot>();
            RequireForUpdate<FixedTickSystem.Singleton>();
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            Entity player = _playerQuery.GetSingletonEntity();
            var transform = _playerQuery.GetSingleton<LocalTransform>();
            var intent = _playerQuery.GetSingleton<MorrowindMovementIntent>();
            var kinematic = _playerQuery.GetSingleton<MorrowindActorKinematicState>();
            var trace = _playerQuery.GetSingleton<MorrowindMovementFrameTrace>();
            uint fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick + 1u;

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
                GroundProbeSnapped = trace.GroundProbeSnapped,
                StepAttempted = trace.StepAttempted,
                StepSucceeded = trace.StepSucceeded,
                SteepSlopeRejected = trace.SteepSlopeRejected,
                JumpRequested = trace.JumpRequested,
                JumpAccepted = trace.JumpAccepted,
                LastBlocker = trace.LastBlocker,
                LastBlockerNormal = trace.LastBlockerNormal,
                LastBlockerFraction = trace.LastBlockerFraction,
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
            if (!kinematic.Grounded)
                return ToFixed128("airborne");
            if (kinematic.OnSlope)
                return ToFixed128("slope");
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

    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(MorrowindMovementDiagnosticsSystem))]
    [UpdateBefore(typeof(FixedTickSystem))]
    public partial class MorrowindMovementDiagnosticsHotkeySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (WasHotkeyPressed())
            {
                Debug.Log("[VVardenfell][MovementDiagnostics] F10 pressed; dumping OpenMW-style movement state.");
                Debug.Log(MorrowindMovementDebug.DescribeLatest());
            }
        }

        static bool WasHotkeyPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f10Key.wasPressedThisFrame)
                return true;

            try
            {
                return Input.GetKeyDown(KeyCode.F10);
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
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
            builder.Append(" step=");
            builder.Append(snapshot.StepAttempted != 0
                ? (snapshot.StepSucceeded != 0 ? "succeeded" : "failed")
                : "none");
            builder.Append(" steepReject=");
            builder.Append(snapshot.SteepSlopeRejected != 0 ? "yes" : "no");
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

            builder.Append("  ");
            builder.Append(MorrowindPlayerSpeedResolver.DescribeDefaults(RuntimeContentDatabase.Active));
            return builder.ToString();
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
