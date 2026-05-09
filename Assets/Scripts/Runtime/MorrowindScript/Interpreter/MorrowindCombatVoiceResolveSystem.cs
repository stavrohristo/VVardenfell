using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindScriptSayApplySystem))]
    public partial struct MorrowindCombatVoiceResolveSystem : ISystem
    {
        NativeReference<int> _uniqueRequestCount;
        NativeReference<int> _invalidRequestIndex;
        NativeReference<byte> _invalidRequestReason;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatVoiceResolveRequest>();
            systemState.RequireForUpdate<MorrowindScriptSayRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();

            _uniqueRequestCount = new NativeReference<int>(Allocator.Persistent);
            _invalidRequestIndex = new NativeReference<int>(Allocator.Persistent);
            _invalidRequestReason = new NativeReference<byte>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_uniqueRequestCount.IsCreated)
                _uniqueRequestCount.Dispose();
            if (_invalidRequestIndex.IsCreated)
                _invalidRequestIndex.Dispose();
            if (_invalidRequestReason.IsCreated)
                _invalidRequestReason.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob content = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindCombatVoiceResolveRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            _uniqueRequestCount.Value = 0;
            _invalidRequestIndex.Value = -1;
            _invalidRequestReason.Value = 0;
            new StageUniqueRequestsJob
            {
                Requests = requests.AsNativeArray(),
                UniqueRequestCount = _uniqueRequestCount,
                InvalidRequestIndex = _invalidRequestIndex,
                InvalidRequestReason = _invalidRequestReason,
            }.Schedule(systemState.Dependency).Complete();
            systemState.Dependency = default;

            if (_invalidRequestReason.Value != 0)
                ThrowInvalidRequest(requests[_invalidRequestIndex.Value], _invalidRequestReason.Value);
            int uniqueCount = _uniqueRequestCount.Value;

            var sayRequests = systemState.EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
            for (int i = 0; i < uniqueCount; i++)
                Resolve(ref systemState, ref content, requests[i], sayRequests);

            requests.Clear();
        }

        static void ThrowInvalidRequest(in MorrowindCombatVoiceResolveRequest request, byte reason)
        {
            if (reason == 1)
                throw new InvalidOperationException($"[VVardenfell][CombatVoice] Voice request for ref=0x{request.TargetPlacedRefId:X8} has invalid actor handle.");
            if (reason == 2)
                throw new InvalidOperationException($"[VVardenfell][CombatVoice] Voice request for ref=0x{request.TargetPlacedRefId:X8} has no dialogue topic hash.");

            throw new InvalidOperationException($"[VVardenfell][CombatVoice] Voice request for ref=0x{request.TargetPlacedRefId:X8} is invalid.");
        }

        [BurstCompile]
        struct StageUniqueRequestsJob : IJob
        {
            public NativeArray<MorrowindCombatVoiceResolveRequest> Requests;
            public NativeReference<int> UniqueRequestCount;
            public NativeReference<int> InvalidRequestIndex;
            public NativeReference<byte> InvalidRequestReason;

            public void Execute()
            {
                int write = 0;
                for (int read = 0; read < Requests.Length; read++)
                {
                    var request = Requests[read];
                    if (!request.Actor.IsValid)
                    {
                        InvalidRequestIndex.Value = read;
                        InvalidRequestReason.Value = 1;
                        return;
                    }
                    if (request.DialogueIdHash == 0UL)
                    {
                        InvalidRequestIndex.Value = read;
                        InvalidRequestReason.Value = 2;
                        return;
                    }
                    if (ContainsRequest(Requests, write, request))
                        continue;

                    if (write != read)
                        Requests[write] = request;
                    write++;
                }

                UniqueRequestCount.Value = write;
            }
        }

        static bool ContainsRequest(NativeArray<MorrowindCombatVoiceResolveRequest> requests, int length, in MorrowindCombatVoiceResolveRequest request)
        {
            for (int i = 0; i < length; i++)
            {
                var existing = requests[i];
                if (existing.TargetEntity == request.TargetEntity
                    && existing.TargetPlacedRefId == request.TargetPlacedRefId
                    && existing.DialogueIdHash == request.DialogueIdHash)
                {
                    return true;
                }
            }

            return false;
        }

        void Resolve(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            in MorrowindCombatVoiceResolveRequest request,
            DynamicBuffer<MorrowindScriptSayRequest> sayRequests)
        {
            if (!RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref content, request.DialogueIdHash, out var dialogueHandle) || !dialogueHandle.IsValid)
                return;

            ref RuntimeDialogueDefBlob dialogue = ref RuntimeContentBlobUtility.Get(ref content, dialogueHandle);
            if (dialogue.Type != DialogueDefType.Voice)
                throw new InvalidOperationException($"[VVardenfell][CombatVoice] Dialogue hash 0x{request.DialogueIdHash:X16} is not a voice dialogue.");

            uint randomState = request.RandomState == 0u ? 1u : request.RandomState;
            if (!MorrowindDialogueFilterUtility.TryFindRandomMatchingVoicedInfoNoAlloc(
                    ref content,
                    systemState.EntityManager,
                    request.TargetEntity,
                    request.Actor,
                    dialogueHandle.Index,
                    choice: 0,
                    ref randomState,
                    out int infoIndex,
                    out byte unsupportedFunction))
            {
                if (unsupportedFunction != 0)
                    throw new InvalidOperationException($"[VVardenfell][CombatVoice] Voice dialogue for actor ref=0x{request.TargetPlacedRefId:X8} has unsupported select function {(DialogueConditionFunction)unsupportedFunction}.");
                return;
            }

            ref RuntimeDialogueInfoDefBlob info = ref content.DialogueInfos[infoIndex];
            FixedString512Bytes soundFile = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.SoundFile);
            if (soundFile.Length == 0)
                return;

            sayRequests.Add(new MorrowindScriptSayRequest
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
                VoicePath = soundFile,
                Subtitle = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response),
                AllowMissingVoicePath = 1,
            });
        }
    }
}
