using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActorEventConsumeApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptActorEventConsumeRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptActorEventConsumeRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptActorEventConsumeRequest request)
        {
            if (request.TargetEntity == Entity.Null || !systemState.EntityManager.Exists(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} is not loaded.");

            if (request.TargetPlacedRefId != 0u)
            {
                if (!systemState.EntityManager.HasComponent<PlacedRefIdentity>(request.TargetEntity))
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} has no placed ref identity.");

                var identity = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(request.TargetEntity);
                if (identity.Value != request.TargetPlacedRefId)
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target mismatch requested={request.TargetPlacedRefId} actual={identity.Value}.");
            }

            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} has no actor script event state.");

            var state = systemState.EntityManager.GetComponentData<ActorScriptEventState>(request.TargetEntity);
            switch ((MorrowindScriptActorEventConsumeKind)request.Kind)
            {
                case MorrowindScriptActorEventConsumeKind.Murdered:
                    state.Murdered = 0;
                    break;
                case MorrowindScriptActorEventConsumeKind.LastHitObject:
                    state.LastHitObject = default;
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Unknown actor event consume kind {request.Kind}.");
            }

            systemState.EntityManager.SetComponentData(request.TargetEntity, state);
        }
    }
}
