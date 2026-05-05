using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindScriptRefStateApplySystem))]
    public partial class MorrowindQuestJournalApplySystem : SystemBase
    {
        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadWrite<MorrowindQuestJournalState>(),
                ComponentType.ReadWrite<MorrowindQuestJournalIndex>(),
                ComponentType.ReadWrite<MorrowindQuestJournalEntry>(),
                ComponentType.ReadWrite<MorrowindQuestJournalRequest>());
            RequireForUpdate(_runtimeQuery);
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindQuestJournalRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var state = EntityManager.GetComponentData<MorrowindQuestJournalState>(runtimeEntity);
            var questStates = EntityManager.GetBuffer<MorrowindQuestJournalIndex>(runtimeEntity);
            var entries = EntityManager.GetBuffer<MorrowindQuestJournalEntry>(runtimeEntity);
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (state.QuestCount != questStates.Length)
            {
                Debug.LogError($"[VVardenfell][MWScript] quest journal state count mismatch: state={state.QuestCount} buffer={questStates.Length}.");
                requests.Clear();
                return;
            }
            if (contentBlob.Dialogues.Length != questStates.Length)
            {
                Debug.LogError($"[VVardenfell][MWScript] quest journal content mismatch: content={contentBlob.Dialogues.Length} buffer={questStates.Length}.");
                requests.Clear();
                return;
            }

            for (int i = 0; i < requests.Length; i++)
            {
                if (!MorrowindQuestJournalUtility.TryApplyRequest(ref contentBlob, ref state, time, questStates, entries, requests[i]))
                    Debug.LogError($"[VVardenfell][MWScript] invalid quest journal request dialogueIndex={requests[i].DialogueIndex} stage={requests[i].JournalIndex}.");
            }

            EntityManager.SetComponentData(runtimeEntity, state);
            requests.Clear();
        }
    }
}
