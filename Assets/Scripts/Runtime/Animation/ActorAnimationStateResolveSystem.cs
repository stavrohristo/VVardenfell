#if VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial struct ActorAnimationStateResolveSystem : ISystem
    {
        static readonly ProfilerMarker k_ResolveState = new("VV.ActorAnimationState.Resolve");

        public void OnUpdate(ref SystemState state)
        {
            using (k_ResolveState.Auto())
            {
                state.Dependency = new ResolveAnimationStateJob().ScheduleParallel(state.Dependency);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct ResolveAnimationStateJob : IJobEntity
        {
            void Execute(
                ref ActorAnimationState animState,
                ref ActorAnimationController controller,
                in MorrowindMovementState movementState)
            {
                float2 localMove = movementState.LocalMove;
                byte isMoving = (byte)(math.lengthsq(localMove) > 0.0001f ? 1 : 0);

                animState.LocalMove = localMove;
                animState.IsMoving = isMoving;
                animState.IsRunning = (byte)(movementState.RunHeld ? 1 : 0);
                animState.IsSneaking = (byte)(movementState.SneakHeld ? 1 : 0);
                animState.IsSwimming = 0;
                animState.IsJumping = (byte)(movementState.JumpAccepted ? 1 : 0);

                FixedString64Bytes locomotionGroup = ResolveLocomotionGroup(localMove, animState);
                if (!locomotionGroup.Equals(controller.RequestedGroup))
                    controller.RequestedGroup = locomotionGroup;
            }
        }

        static FixedString64Bytes ResolveLocomotionGroup(float2 localMove, ActorAnimationState state)
        {
            if (state.IsMoving == 0)
            {
                if (state.IsSwimming != 0)
                    return IdleSwimGroup();
                return state.IsSneaking != 0 ? IdleSneakGroup() : IdleGroup();
            }

            byte family = 0;
            if (state.IsSwimming != 0)
                family = state.IsRunning != 0 ? (byte)4 : (byte)3;
            else if (state.IsSneaking != 0)
                family = 2;
            else if (state.IsRunning != 0)
                family = 1;

            return ResolveMovementGroup(localMove, family);
        }

        static FixedString64Bytes ResolveMovementGroup(float2 localMove, byte family)
        {
            bool lateral = math.abs(localMove.x) > math.abs(localMove.y);
            byte direction;
            if (lateral)
                direction = localMove.x >= 0f ? (byte)3 : (byte)2;
            else
                direction = localMove.y >= 0f ? (byte)0 : (byte)1;

            FixedString64Bytes value = default;
            AppendFamily(ref value, family);
            AppendDirection(ref value, direction);
            return value;
        }

        static void AppendFamily(ref FixedString64Bytes value, byte family)
        {
            if (family == 4)
            {
                AppendSwim(ref value);
                AppendRun(ref value);
                return;
            }

            if (family == 3)
            {
                AppendSwim(ref value);
                AppendWalk(ref value);
                return;
            }

            if (family == 2)
            {
                AppendSneak(ref value);
                return;
            }

            if (family == 1)
            {
                AppendRun(ref value);
                return;
            }

            AppendWalk(ref value);
        }

        static void AppendDirection(ref FixedString64Bytes value, byte direction)
        {
            if (direction == 1)
            {
                value.Append((byte)'b');
                value.Append((byte)'a');
                value.Append((byte)'c');
                value.Append((byte)'k');
                return;
            }

            if (direction == 2)
            {
                value.Append((byte)'l');
                value.Append((byte)'e');
                value.Append((byte)'f');
                value.Append((byte)'t');
                return;
            }

            if (direction == 3)
            {
                value.Append((byte)'r');
                value.Append((byte)'i');
                value.Append((byte)'g');
                value.Append((byte)'h');
                value.Append((byte)'t');
                return;
            }

            value.Append((byte)'f');
            value.Append((byte)'o');
            value.Append((byte)'r');
            value.Append((byte)'w');
            value.Append((byte)'a');
            value.Append((byte)'r');
            value.Append((byte)'d');
        }

        static void AppendWalk(ref FixedString64Bytes value)
        {
            value.Append((byte)'w');
            value.Append((byte)'a');
            value.Append((byte)'l');
            value.Append((byte)'k');
        }

        static void AppendRun(ref FixedString64Bytes value)
        {
            value.Append((byte)'r');
            value.Append((byte)'u');
            value.Append((byte)'n');
        }

        static void AppendSneak(ref FixedString64Bytes value)
        {
            value.Append((byte)'s');
            value.Append((byte)'n');
            value.Append((byte)'e');
            value.Append((byte)'a');
            value.Append((byte)'k');
        }

        static void AppendSwim(ref FixedString64Bytes value)
        {
            value.Append((byte)'s');
            value.Append((byte)'w');
            value.Append((byte)'i');
            value.Append((byte)'m');
        }

        static FixedString64Bytes IdleGroup()
        {
            FixedString64Bytes value = default;
            value.Append((byte)'i');
            value.Append((byte)'d');
            value.Append((byte)'l');
            value.Append((byte)'e');
            return value;
        }

        static FixedString64Bytes IdleSwimGroup()
        {
            FixedString64Bytes value = IdleGroup();
            AppendSwim(ref value);
            return value;
        }

        static FixedString64Bytes IdleSneakGroup()
        {
            FixedString64Bytes value = IdleGroup();
            AppendSneak(ref value);
            return value;
        }
    }
}
#endif
