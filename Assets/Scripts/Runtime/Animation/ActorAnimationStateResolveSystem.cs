using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial struct ActorAnimationStateResolveSystem : ISystem
    {
        static readonly FixedString64Bytes IdleGroup = new("idle");

        public void OnUpdate(ref SystemState state)
        {
            foreach (var (animState, controller) in
                     SystemAPI.Query<RefRW<ActorAnimationState>, RefRW<ActorAnimationController>>()
                         .WithAll<ActorPresentation>())
            {
                animState.ValueRW = default;

                if (!IdleGroup.Equals(controller.ValueRO.RequestedGroup))
                    controller.ValueRW.RequestedGroup = IdleGroup;
            }
        }
    }
}
