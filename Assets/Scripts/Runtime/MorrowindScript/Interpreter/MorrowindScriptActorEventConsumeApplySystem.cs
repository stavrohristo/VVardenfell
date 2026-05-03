using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptActorEventConsumeApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptActorEventConsumeRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptActorEventConsumeRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i]);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptActorEventConsumeRequest request)
        {
            if (request.TargetEntity == Entity.Null || !EntityManager.Exists(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} is not loaded.");

            if (request.TargetPlacedRefId != 0u)
            {
                if (!EntityManager.HasComponent<PlacedRefIdentity>(request.TargetEntity))
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} has no placed ref identity.");

                var identity = EntityManager.GetComponentData<PlacedRefIdentity>(request.TargetEntity);
                if (identity.Value != request.TargetPlacedRefId)
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target mismatch requested={request.TargetPlacedRefId} actual={identity.Value}.");
            }

            if (!EntityManager.HasComponent<ActorScriptEventState>(request.TargetEntity))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor event target ref={request.TargetPlacedRefId} has no actor script event state.");

            var state = EntityManager.GetComponentData<ActorScriptEventState>(request.TargetEntity);
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

            EntityManager.SetComponentData(request.TargetEntity, state);
        }
    }
}
