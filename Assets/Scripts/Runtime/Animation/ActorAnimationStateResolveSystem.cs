using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
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
            void Execute(ref ActorAnimationState animState, ref ActorAnimationController controller)
            {
                if (!IsDefault(animState))
                    animState = default;

                FixedString64Bytes idleGroup = IdleGroup();
                if (!idleGroup.Equals(controller.RequestedGroup))
                    controller.RequestedGroup = idleGroup;
            }
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

        static bool IsDefault(in ActorAnimationState state)
        {
            return math.lengthsq(state.LocalMove) <= 0f
                   && state.IsMoving == 0
                   && state.IsRunning == 0
                   && state.IsSneaking == 0
                   && state.IsSwimming == 0
                   && state.IsJumping == 0
                   && state.IsDead == 0;
        }
    }
}
