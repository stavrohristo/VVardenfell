using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptOnDeathConsumeApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptOnDeathConsumeRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptOnDeathConsumeRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptOnDeathConsumeRequest request)
        {
            if (request.TargetEntity == Entity.Null || !systemState.EntityManager.Exists(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasComponent<PlacedRefIdentity>(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target ref={request.TargetPlacedRefId} has no placed ref identity.");

            var identity = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(request.TargetEntity);
            if (identity.Value != request.TargetPlacedRefId)
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target mismatch requested={request.TargetPlacedRefId} actual={identity.Value}.");

            if (!systemState.EntityManager.HasComponent<MorrowindActorOnDeathConsumed>(request.TargetEntity))
                systemState.EntityManager.AddComponent<MorrowindActorOnDeathConsumed>(request.TargetEntity);
        }
    }
}
