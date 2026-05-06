using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptMovementFlagApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptMovementFlagRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptMovementFlagRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptMovementFlagRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Movement flag target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasComponent<MorrowindMovementState>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Movement flag target ref={request.TargetPlacedRefId} has no MorrowindMovementState.");

            if (request.FlagKind != (byte)MorrowindScriptMovementFlagKind.ForceSneak)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported movement flag kind {request.FlagKind}.");

            var state = systemState.EntityManager.GetComponentData<MorrowindMovementState>(target);
            state.ForceSneak = request.Enabled != 0;
            systemState.EntityManager.SetComponentData(target, state);
        }
    }
}
