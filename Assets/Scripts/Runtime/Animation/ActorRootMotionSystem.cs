#if VVARDENFELL_ACTOR_ROOT_MOTION
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorPoseSamplingSystem))]
    public partial struct ActorRootMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Root-motion extraction and accumulation stripping now happen inside
            // ActorPoseSamplingSystem so we do not traverse the animated subtree twice.
        }
    }
}
#endif
