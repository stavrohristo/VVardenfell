using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindScriptSayApplySystem))]
    public partial class ActorAiPassiveGreetingSayRequestSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorAiPassiveGreetingSayRequest>();
            RequireForUpdate<MorrowindScriptSayRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorAiPassiveGreetingSayRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var sayRequests = EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                sayRequests.Add(new MorrowindScriptSayRequest
                {
                    TargetEntity = request.TargetEntity,
                    TargetPlacedRefId = request.TargetPlacedRefId,
                    VoicePath = request.VoicePath,
                    Subtitle = request.Subtitle,
                });
            }

            requests.Clear();
        }
    }
}
