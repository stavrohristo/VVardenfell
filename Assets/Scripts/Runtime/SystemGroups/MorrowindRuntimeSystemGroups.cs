using Unity.Entities;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

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

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial class MorrowindInputSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindOwnedPhysicsSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup), OrderFirst = true)]
    public partial class MorrowindPhysicsPreBuildSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial class MorrowindPhysicsQuerySystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(MorrowindPhysicsQuerySystemGroup))]
    public partial class MorrowindPhysicsPostQueryMutationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPrePhysicsSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial class MorrowindFixedPostPhysicsSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
    public partial class MorrowindPrePhysicsSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
    public partial class MorrowindPostPhysicsSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class MorrowindPreTransformSimulationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class MorrowindPostTransformSimulationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    public partial class MorrowindInteractionSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindInteractionSystemGroup))]
    public partial class MorrowindWorldMutationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class MorrowindEnvironmentSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindEnvironmentSystemGroup))]
    public partial class MorrowindAudioSimulationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(MorrowindPostTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class MorrowindInteractionPresentationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MorrowindPresentationSystemGroup : ComponentSystemGroup
    {
    }

    public static class MorrowindSystemGroupDiagnostics
    {
        public static bool LogScheduleOnBoot;

        public static string DescribeSchedule()
        {
            return "Morrowind runtime schedule: initialization -> Morrowind input -> owned physics pre-build "
                + "-> manual Unity physics build/sim/export -> owned physics query -> owned physics post-query mutation "
                + "-> Morrowind pre-transform -> Unity transforms "
                + "-> Morrowind post-transform (interaction, world mutation, streaming, environment, audio) "
                + "-> Morrowind presentation.";
        }
    }

    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class MorrowindSystemGroupDiagnosticsSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (MorrowindSystemGroupDiagnostics.LogScheduleOnBoot)

            Enabled = false;
        }
    }
}
