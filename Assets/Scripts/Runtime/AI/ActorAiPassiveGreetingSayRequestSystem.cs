using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindScriptSayApplySystem))]
    public partial struct ActorAiPassiveGreetingSayRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorAiPassiveGreetingSayRequest>();
            systemState.RequireForUpdate<MorrowindScriptSayRequest>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ActorAiPassiveGreetingSayRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var sayRequests = systemState.EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                sayRequests.Add(new MorrowindScriptSayRequest
                {
                    TargetEntity = request.TargetEntity,
                    TargetPlacedRefId = request.TargetPlacedRefId,
                    VoicePath = request.VoicePath,
                    Subtitle = request.Subtitle,
                    AllowMissingVoicePath = 1,
                });
            }

            requests.Clear();
        }
    }
}
