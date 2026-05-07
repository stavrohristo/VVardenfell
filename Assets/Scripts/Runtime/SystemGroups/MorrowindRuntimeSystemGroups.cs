using Unity.Entities;
using Unity.Core;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Systems
{
    /// <summary>
    /// VVardenfell-owned runtime phases. Unity still owns physics, transform, and
    /// presentation execution; these groups make our side of those boundaries explicit.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class MorrowindInitializationSystemGroup : ComponentSystemGroup
    {
    }

    public abstract partial class MorrowindRuntimeGatedSystemGroup : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<MorrowindRuntimeActive>();
        }
    }

    public abstract partial class MorrowindRuntimePauseGatedSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
        static int s_RestSimulationDepth;

        EntityQuery _pauseQuery;
        EntityQuery _shellQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pauseQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            _shellQuery = GetEntityQuery(ComponentType.ReadOnly<RuntimeShellState>());
        }

        protected override void OnUpdate()
        {
            bool paused = !_pauseQuery.IsEmptyIgnoreFilter;
            if (!paused)
            {
                base.OnUpdate();
                return;
            }

            if (!ShouldUpdateDuringRestAdvancing || !IsRestAdvancing())
                return;

            if (s_RestSimulationDepth > 0)
            {
                base.OnUpdate();
                return;
            }

            UpdateForRestSimulation();
        }

        protected virtual bool ShouldUpdateDuringRestAdvancing => true;
        protected virtual float RestSimulationSecondsPerRealSecond => 3600f;
        protected virtual float MaxRestSimulationStepSeconds => 10f;
        protected virtual int MaxRestSimulationStepsPerUpdate => 16;

        bool IsRestAdvancing()
        {
            if (_shellQuery.IsEmptyIgnoreFilter)
                return false;

            return _shellQuery.GetSingleton<RuntimeShellState>().RestMenuAdvancing != 0;
        }

        void UpdateForRestSimulation()
        {
            TimeData sourceTime = World.Time;
            float sourceDelta = math.max(0f, sourceTime.DeltaTime);
            float requestedSimulatedSeconds = sourceDelta * math.max(0f, RestSimulationSecondsPerRealSecond);
            float maxStepSeconds = math.max(0.0001f, MaxRestSimulationStepSeconds);
            int maxSteps = math.max(1, MaxRestSimulationStepsPerUpdate);
            float simulatedSeconds = math.min(requestedSimulatedSeconds, maxStepSeconds * maxSteps);
            if (simulatedSeconds <= 0f)
                return;

            int stepCount = math.clamp((int)math.ceil(simulatedSeconds / maxStepSeconds), 1, maxSteps);
            float stepSeconds = simulatedSeconds / stepCount;

            s_RestSimulationDepth++;
            try
            {
                for (int i = 0; i < stepCount; i++)
                {
                    World.PushTime(new TimeData(
                        elapsedTime: sourceTime.ElapsedTime + stepSeconds * (i + 1),
                        deltaTime: stepSeconds));

                    try
                    {
                        base.OnUpdate();
                    }
                    finally
                    {
                        World.PopTime();
                    }
                }
            }
            finally
            {
                s_RestSimulationDepth--;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(MorrowindFramePhysicsQuerySystemGroup))]
    public partial class MorrowindAudioMenuSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial class MorrowindInputSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(VVardenfell.Runtime.Shell.RuntimeShellPauseSyncSystem))]
    public partial class MorrowindGameplayInputSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override bool ShouldUpdateDuringRestAdvancing => false;
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateBefore(typeof(MorrowindMenuMutationSystemGroup))]
    public partial class MorrowindGameplayMutationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(VVardenfell.Runtime.Shell.RuntimeShellPauseSyncSystem))]
    public partial class MorrowindMenuMutationSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindOwnedPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup), OrderFirst = true)]
    public partial class MorrowindPhysicsPreBuildSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial class MorrowindPhysicsQuerySystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderLast = true)]
    public partial class MorrowindProjectileSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(MorrowindPhysicsQuerySystemGroup))]
    public partial class MorrowindPhysicsPostQueryMutationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPrePhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPostPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial class MorrowindFramePhysicsQuerySystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
    public partial class MorrowindPrePhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
    public partial class MorrowindPostPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override float RestSimulationSecondsPerRealSecond => 12f;
        protected override float MaxRestSimulationStepSeconds => 0.05f;
        protected override int MaxRestSimulationStepsPerUpdate => 8;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class MorrowindPreTransformSimulationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class MorrowindPresentationBuildSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class MorrowindPostTransformSimulationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    public partial class MorrowindInteractionSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindInteractionSystemGroup))]
    public partial class MorrowindWorldMutationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(MorrowindInteractionPresentationSystemGroup))]
    public partial class MorrowindEnvironmentSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateBefore(typeof(MorrowindInteractionPresentationSystemGroup))]
    public partial class MorrowindAudioSimulationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
        protected override bool ShouldUpdateDuringRestAdvancing => false;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindPostTransformSimulationSystemGroup))]
    public partial class MorrowindInteractionPresentationSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MorrowindAudioPresentationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MorrowindPresentationSystemGroup : MorrowindRuntimeGatedSystemGroup
    {
    }
}
