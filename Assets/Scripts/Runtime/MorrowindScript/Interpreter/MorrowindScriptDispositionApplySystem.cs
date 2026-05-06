using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptDispositionApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptDispositionRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptDispositionRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                TryApplyRequest(ref systemState, requests[i], lookup);

            requests.Clear();
        }

        void TryApplyRequest(ref SystemState systemState, in MorrowindScriptDispositionRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.HasComponent<ActorDispositionState>(target))
                return;

            var disposition = systemState.EntityManager.GetComponentData<ActorDispositionState>(target);
            disposition.BaseDisposition = request.IsMod != 0
                ? disposition.BaseDisposition + request.Value
                : request.Value;
            systemState.EntityManager.SetComponentData(target, disposition);
        }
    }
}
