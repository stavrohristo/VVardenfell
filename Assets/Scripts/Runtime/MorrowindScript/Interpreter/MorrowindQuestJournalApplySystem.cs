using System;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    [UpdateBefore(typeof(MorrowindScriptRefStateApplySystem))]
    public partial struct MorrowindQuestJournalApplySystem : ISystem
    {
        EntityQuery _runtimeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _runtimeQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindQuestJournalState>(),
                ComponentType.ReadWrite<MorrowindQuestJournalIndex>(),
                ComponentType.ReadWrite<MorrowindQuestJournalEntry>(),
                ComponentType.ReadWrite<MorrowindQuestJournalRequest>());
            systemState.RequireForUpdate(_runtimeQuery);
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = systemState.EntityManager.GetBuffer<MorrowindQuestJournalRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var state = systemState.EntityManager.GetComponentData<MorrowindQuestJournalState>(runtimeEntity);
            var questStates = systemState.EntityManager.GetBuffer<MorrowindQuestJournalIndex>(runtimeEntity);
            var entries = systemState.EntityManager.GetBuffer<MorrowindQuestJournalEntry>(runtimeEntity);
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (state.QuestCount != questStates.Length)
                throw new InvalidOperationException("[VVardenfell][MWScript] quest journal state count mismatch.");
            if (contentBlob.Dialogues.Length != questStates.Length)
                throw new InvalidOperationException("[VVardenfell][MWScript] quest journal content mismatch.");

            for (int i = 0; i < requests.Length; i++)
            {
                if (!MorrowindQuestJournalUtility.TryApplyRequest(ref contentBlob, ref state, time, questStates, entries, requests[i]))
                    throw new InvalidOperationException("[VVardenfell][MWScript] invalid quest journal request.");
            }

            systemState.EntityManager.SetComponentData(runtimeEntity, state);
            requests.Clear();
        }
    }
}
