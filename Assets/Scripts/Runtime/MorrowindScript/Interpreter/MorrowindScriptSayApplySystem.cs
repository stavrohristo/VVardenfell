using System;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptSayApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptSayRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref var runtimeState = ref SystemAPI.GetSingletonRW<MorrowindScriptRuntimeState>().ValueRW;
            bool showSubtitles = HudUserPreferences.ShowSubtitles;

            for (int i = 0; i < requests.Length; i++)
            {
                ApplyRequest(requests[i], lookup, ref runtimeState);
                if (showSubtitles && !requests[i].Subtitle.IsEmpty && SystemAPI.TryGetSingletonRW<RuntimeShellState>(out var shell))
                    RuntimeShellStateUtility.ShowMessageBox(ref shell.ValueRW, requests[i].Subtitle);
            }

            requests.Clear();
        }

        void ApplyRequest(
            in MorrowindScriptSayRequest request,
            in LogicalRefLookup lookup,
            ref MorrowindScriptRuntimeState runtimeState)
        {
            if (string.IsNullOrWhiteSpace(request.VoicePath.ToString()))
                throw new InvalidOperationException("[VVardenfell][MWScript] Say requires a non-empty voice path.");

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Say target ref={request.TargetPlacedRefId} is not loaded.");

            bool playLocal = EntityManager.HasComponent<PlayerTag>(target);
            if (!playLocal && !EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Say target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var requestEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = runtimeState.NextAudioRequestSequence++,
                DirectPath = request.VoicePath,
                SourceEntity = target,
                SourcePlacedRefId = request.TargetPlacedRefId,
                Position = playLocal ? default : EntityManager.GetComponentData<LocalTransform>(target).Position,
                Volume = 1f,
                Pitch = 1f,
                Kind = (byte)MorrowindScriptAudioKind.PlaySound3DVP,
                Spatial = playLocal ? (byte)0 : (byte)1,
            });

        }
    }
}
