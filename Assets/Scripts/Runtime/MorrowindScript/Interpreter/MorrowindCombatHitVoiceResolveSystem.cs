using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindCombatHitVoiceSayRequestPumpSystem))]
    public partial struct MorrowindCombatHitVoiceResolveSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatHitVoiceResolveRequest>();
            systemState.RequireForUpdate<MorrowindCombatHitVoiceSayRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceResolveRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var sayRequests = systemState.EntityManager.GetBuffer<MorrowindCombatHitVoiceSayRequest>(runtimeEntity);
            for (int i = 0; i < requests.Length; i++)
                Resolve(ref systemState, ref contentBlob, requests[i], sayRequests);

            requests.Clear();
        }

        void Resolve(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            in MorrowindCombatHitVoiceResolveRequest request,
            DynamicBuffer<MorrowindCombatHitVoiceSayRequest> sayRequests)
        {
            if (!request.Actor.IsValid)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Hit voice request for ref=0x{request.TargetPlacedRefId:X8} has invalid actor handle.");
            if ((uint)request.DialogueIndex >= (uint)contentBlob.Dialogues.Length)
                throw new InvalidOperationException($"[VVardenfell][OnHit] Hit voice request for ref=0x{request.TargetPlacedRefId:X8} has invalid dialogue index {request.DialogueIndex}.");

            uint randomState = request.RandomState == 0u ? 1u : request.RandomState;
            if (!MorrowindDialogueFilterUtility.TryFindRandomMatchingVoicedInfo(
                    ref contentBlob,
                    systemState.EntityManager,
                    request.TargetEntity,
                    request.Actor,
                    request.DialogueIndex,
                    choice: 0,
                    ref randomState,
                    MorrowindVoiceAudioAvailability.IsVoiceAvailable,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    throw new InvalidOperationException($"[VVardenfell][OnHit] Hit voice dialogue for actor ref=0x{request.TargetPlacedRefId:X8} is unsupported: {unsupportedReason}");
                return;
            }

            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            FixedString512Bytes soundFile = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.SoundFile);
            if (soundFile.Length == 0)
                return;

            sayRequests.Add(new MorrowindCombatHitVoiceSayRequest
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
                VoicePath = soundFile,
                Subtitle = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response),
            });
        }
    }
}
