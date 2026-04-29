using Unity.Entities;
using Unity.Physics.Systems;
using Unity.Transforms;

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
        EntityQuery _pauseQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pauseQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
        }

        protected override void OnUpdate()
        {
            if (!_pauseQuery.IsEmptyIgnoreFilter)
                return;

            base.OnUpdate();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(MorrowindPreTransformSimulationSystemGroup))]
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
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindOwnedPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
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

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(MorrowindPhysicsQuerySystemGroup))]
    public partial class MorrowindPhysicsPostQueryMutationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPrePhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPostPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
    public partial class MorrowindPrePhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
    public partial class MorrowindPostPhysicsSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class MorrowindPreTransformSimulationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
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

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class MorrowindEnvironmentSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindEnvironmentSystemGroup))]
    public partial class MorrowindAudioSimulationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class MorrowindInteractionPresentationSystemGroup : MorrowindRuntimePauseGatedSystemGroup
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
