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
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptMovementFlagApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptMovementFlagRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptMovementFlagRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptMovementFlagRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Movement flag target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasComponent<MorrowindMovementState>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Movement flag target ref={request.TargetPlacedRefId} has no MorrowindMovementState.");

            if (request.FlagKind != (byte)MorrowindScriptMovementFlagKind.ForceSneak)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported movement flag kind {request.FlagKind}.");

            var state = EntityManager.GetComponentData<MorrowindMovementState>(target);
            state.ForceSneak = request.Enabled != 0;
            EntityManager.SetComponentData(target, state);
        }
    }
}
