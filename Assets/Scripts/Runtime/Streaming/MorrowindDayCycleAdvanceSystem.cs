using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup), OrderFirst = true)]
    public partial struct MorrowindTimeAdvanceSystem : ISystem
    {
        EntityQuery _timeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _timeQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<MorrowindTimeState>(), ComponentType.ReadWrite<MorrowindTimeAdvanceRequest>());
            systemState.RequireForUpdate(_timeQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
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
            if (time.Paused == 0 && time.TimeScale > 0f)
            {
                float realSeconds = SystemAPI.Time.DeltaTime;
                advancedHours += realSeconds * (time.TimeScale / 3600f);
            }

            time.FastForwarding = fastForwarding;
            MorrowindDayCycleUtility.AdvanceHours(ref time, advancedHours);
        }
    }
}
