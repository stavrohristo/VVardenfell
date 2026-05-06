using System;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptFactionReactionApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptFactionReactionRequest>();
            systemState.RequireForUpdate<MorrowindFactionReactionOverride>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptFactionReactionRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            var overrides = systemState.EntityManager.GetBuffer<MorrowindFactionReactionOverride>(runtimeEntity);
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
                    "[VVardenfell][MWScript] Faction reaction mutation references invalid factions.");
            }
        }
    }
}
