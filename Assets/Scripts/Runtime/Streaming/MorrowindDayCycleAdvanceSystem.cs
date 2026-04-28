using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup), OrderFirst = true)]
    public partial class MorrowindTimeAdvanceSystem : SystemBase
    {
        EntityQuery _timeQuery;

        protected override void OnCreate()
        {
            _timeQuery = GetEntityQuery(ComponentType.ReadWrite<MorrowindTimeState>(), ComponentType.ReadWrite<MorrowindTimeAdvanceRequest>());
            RequireForUpdate(_timeQuery);
        }

        protected override void OnUpdate()
        {
            Entity entity = _timeQuery.GetSingletonEntity();
            ref var time = ref SystemAPI.GetSingletonRW<MorrowindTimeState>().ValueRW;
            var requests = SystemAPI.GetSingletonBuffer<MorrowindTimeAdvanceRequest>();

            float requestedHours = 0f;
            byte fastForwarding = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].Hours > 0f)
                {
                    requestedHours += requests[i].Hours;
                    if (requests[i].Kind != (byte)MorrowindTimeAdvanceKind.Normal)
                        fastForwarding = 1;
                }
            }
            requests.Clear();

            float advancedHours = requestedHours;
            if (time.Paused == 0 && time.TimeScale > 0f && time.SimulationTimeScale > 0f)
            {
                float realSeconds = SystemAPI.Time.DeltaTime;
                advancedHours += realSeconds * (time.TimeScale / 3600f) * math.max(0f, time.SimulationTimeScale);
            }

            time.FastForwarding = fastForwarding;
            MorrowindDayCycleUtility.AdvanceHours(ref time, advancedHours);
        }
    }
}
