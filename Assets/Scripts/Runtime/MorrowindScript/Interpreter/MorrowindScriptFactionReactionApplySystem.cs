using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptFactionReactionApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptFactionReactionRequest>();
            RequireForUpdate<MorrowindFactionReactionOverride>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptFactionReactionRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            var overrides = EntityManager.GetBuffer<MorrowindFactionReactionOverride>(runtimeEntity);
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref contentBlob, overrides, requests[i]);

            requests.Clear();
        }

        static void ApplyRequest(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            in MorrowindScriptFactionReactionRequest request)
        {
            bool applied = request.IsMod != 0
                ? MorrowindDialogueUtility.TryModFactionReaction(ref contentBlob, overrides, request.SourceFactionIndex, request.TargetFactionIndex, request.Value)
                : MorrowindDialogueUtility.TrySetFactionReaction(ref contentBlob, overrides, request.SourceFactionIndex, request.TargetFactionIndex, request.Value);

            if (!applied)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript] Faction reaction mutation references invalid factions {request.SourceFactionIndex}->{request.TargetFactionIndex}.");
            }
        }
    }
}
