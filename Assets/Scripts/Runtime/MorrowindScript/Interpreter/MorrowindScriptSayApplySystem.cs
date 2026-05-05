using System;
using Unity.Collections;
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

            var requestCopy = new NativeArray<MorrowindScriptSayRequest>(requests.Length, Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
                requestCopy[i] = requests[i];
            requests.Clear();

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref var runtimeState = ref SystemAPI.GetSingletonRW<MorrowindScriptRuntimeState>().ValueRW;
            bool showSubtitles = HudUserPreferences.ShowSubtitles;

            for (int i = 0; i < requestCopy.Length; i++)
            {
                var request = requestCopy[i];
                ApplyRequest(request, lookup, ref runtimeState);
                if (showSubtitles && !request.Subtitle.IsEmpty && SystemAPI.TryGetSingletonRW<RuntimeSubtitleState>(out var subtitle))
                    RuntimeShellStateUtility.ShowSubtitle(
                        ref subtitle.ValueRW,
                        request.Subtitle,
                        RuntimeShellStateUtility.EstimateSubtitleDurationSeconds(request.Subtitle));
            }

            requestCopy.Dispose();
        }

        void ApplyRequest(
            in MorrowindScriptSayRequest request,
            in LogicalRefLookup lookup,
            ref MorrowindScriptRuntimeState runtimeState)
        {
            if (request.VoicePath.IsEmpty)
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
