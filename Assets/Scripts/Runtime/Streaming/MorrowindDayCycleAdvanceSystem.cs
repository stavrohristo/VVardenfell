using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateBefore(typeof(LightingEnvironmentResolveSystem))]
    public partial class MorrowindDayCycleAdvanceSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDayCycleState>();
        }

        protected override void OnUpdate()
        {
            ref var dayCycle = ref SystemAPI.GetSingletonRW<MorrowindDayCycleState>().ValueRW;
            if (dayCycle.GameHoursPerSecond <= 0f)
                return;

            MorrowindDayCycleUtility.AdvanceHours(
                ref dayCycle,
                SystemAPI.Time.DeltaTime * dayCycle.GameHoursPerSecond);
        }
    }
}
