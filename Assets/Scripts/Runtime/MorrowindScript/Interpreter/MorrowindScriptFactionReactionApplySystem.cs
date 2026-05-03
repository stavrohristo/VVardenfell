using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptFactionReactionRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][MWScript] Faction reaction mutation requires active runtime content.");

            var overrides = EntityManager.GetBuffer<MorrowindFactionReactionOverride>(runtimeEntity);
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(contentDb, overrides, requests[i]);

            requests.Clear();
        }

        static void ApplyRequest(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            in MorrowindScriptFactionReactionRequest request)
        {
            bool applied = request.IsMod != 0
                ? MorrowindDialogueUtility.TryModFactionReaction(contentDb, overrides, request.SourceFactionIndex, request.TargetFactionIndex, request.Value)
                : MorrowindDialogueUtility.TrySetFactionReaction(contentDb, overrides, request.SourceFactionIndex, request.TargetFactionIndex, request.Value);

            if (!applied)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript] Faction reaction mutation references invalid factions {request.SourceFactionIndex}->{request.TargetFactionIndex}.");
            }
        }
    }
}
