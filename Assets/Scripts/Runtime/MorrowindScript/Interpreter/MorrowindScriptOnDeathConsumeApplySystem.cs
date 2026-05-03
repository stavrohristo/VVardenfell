using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptOnDeathConsumeApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptOnDeathConsumeRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptOnDeathConsumeRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i]);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptOnDeathConsumeRequest request)
        {
            if (request.TargetEntity == Entity.Null || !EntityManager.Exists(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasComponent<PlacedRefIdentity>(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target ref={request.TargetPlacedRefId} has no placed ref identity.");

            var identity = EntityManager.GetComponentData<PlacedRefIdentity>(request.TargetEntity);
            if (identity.Value != request.TargetPlacedRefId)
                throw new InvalidOperationException($"[VVardenfell][MWScript] OnDeath target mismatch requested={request.TargetPlacedRefId} actual={identity.Value}.");

            if (!EntityManager.HasComponent<MorrowindActorOnDeathConsumed>(request.TargetEntity))
                EntityManager.AddComponent<MorrowindActorOnDeathConsumed>(request.TargetEntity);
        }
    }
}
