using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Systems
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorWeaponAnimationSystem))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class MorrowindDamageSystemGroup : MorrowindRuntimePauseGatedSystemGroup
    {
    }
}
