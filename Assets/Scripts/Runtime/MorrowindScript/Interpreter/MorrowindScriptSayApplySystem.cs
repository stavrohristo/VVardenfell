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
    public partial struct MorrowindScriptSayApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptSayRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
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
                ApplyRequest(ref systemState, request, lookup, ref runtimeState);
                if (showSubtitles && !request.Subtitle.IsEmpty && SystemAPI.TryGetSingletonRW<RuntimeSubtitleState>(out var subtitle))
                    RuntimeShellStateUtility.ShowSubtitle(
                        ref subtitle.ValueRW,
                        request.Subtitle,
                        RuntimeShellStateUtility.EstimateSubtitleDurationSeconds(request.Subtitle));
            }

            requestCopy.Dispose();
        }

        void ApplyRequest(ref SystemState systemState, 
            in MorrowindScriptSayRequest request,
            in LogicalRefLookup lookup,
            ref MorrowindScriptRuntimeState runtimeState)
        {
            if (request.VoicePath.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] Say requires a non-empty voice path.");

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Say target ref={request.TargetPlacedRefId} is not loaded.");

            bool playLocal = systemState.EntityManager.HasComponent<PlayerTag>(target);
            if (!playLocal && !systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Say target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var requestEntity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.AddComponentData(requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = runtimeState.NextAudioRequestSequence++,
                DirectPath = request.VoicePath,
                SourceEntity = target,
                SourcePlacedRefId = request.TargetPlacedRefId,
                Position = playLocal ? default : systemState.EntityManager.GetComponentData<LocalTransform>(target).Position,
                Volume = 1f,
                Pitch = 1f,
                Kind = (byte)MorrowindScriptAudioKind.PlaySound3DVP,
                Spatial = playLocal ? (byte)0 : (byte)1,
            });

        }
    }
}
