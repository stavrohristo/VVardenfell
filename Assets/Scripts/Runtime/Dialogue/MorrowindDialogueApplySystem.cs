using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindQuestJournalApplySystem))]
    public partial class MorrowindDialogueApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDialogueState>();
            RequireForUpdate<MorrowindDialogueRequest>();
            RequireForUpdate<MorrowindQuestJournalState>();
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindDialogueState>();
            var requests = EntityManager.GetBuffer<MorrowindDialogueRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var knownTopics = EntityManager.GetBuffer<MorrowindKnownDialogueTopic>(runtimeEntity);
            var topicEntries = EntityManager.GetBuffer<MorrowindTopicJournalEntry>(runtimeEntity);
            ref var dialogueState = ref SystemAPI.GetSingletonRW<MorrowindDialogueState>().ValueRW;
            ref var questState = ref SystemAPI.GetSingletonRW<MorrowindQuestJournalState>().ValueRW;
            var questStates = EntityManager.GetBuffer<MorrowindQuestJournalIndex>(runtimeEntity);
            var questEntries = EntityManager.GetBuffer<MorrowindQuestJournalEntry>(runtimeEntity);
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.Operation == (byte)MorrowindDialogueRequestOperation.AddTopic)
                {
                    if (!MorrowindDialogueUtility.TryAddTopic(ref contentBlob, knownTopics, request.DialogueIndex))
                        Debug.LogError($"[VVardenfell][Dialogue] invalid AddTopic request dialogueIndex={request.DialogueIndex}.");
                }
                else if (request.Operation == (byte)MorrowindDialogueRequestOperation.FillJournal)
                {
                    if (!MorrowindDialogueUtility.TryFillJournal(
                            ref contentBlob,
                            ref dialogueState,
                            ref questState,
                            time,
                            knownTopics,
                            topicEntries,
                            questStates,
                            questEntries,
                            0u,
                            default))
                    {
                        Debug.LogError("[VVardenfell][Dialogue] invalid FillJournal request.");
                    }
                }
            }

            requests.Clear();
        }
    }
}
