using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindQuestJournalUtility
    {
        const byte QuestStatusName = 1;
        const byte QuestStatusFinished = 2;
        const byte QuestStatusRestart = 3;

        public static bool TryApplyRequest(
            RuntimeContentDatabase contentDb,
            ref MorrowindQuestJournalState state,
            in MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> entries,
            in MorrowindQuestJournalRequest request)
        {
            if (contentDb == null || (uint)request.DialogueIndex >= (uint)questStates.Length)
                return false;

            var quest = questStates[request.DialogueIndex];
            quest.Started = 1;

            if (request.Operation == (byte)MorrowindQuestJournalRequestOperation.SetIndex)
            {
                quest.Index = request.JournalIndex;
                questStates[request.DialogueIndex] = quest;
                return true;
            }

            if (quest.Index < request.JournalIndex)
                quest.Index = request.JournalIndex;

            if (request.InfoIndex >= 0)
            {
                if (!ContainsEntry(entries, request.DialogueIndex, request.InfoIndex))
                {
                    uint sequence = math.max(1u, state.NextEntrySequence);
                    entries.Add(new MorrowindQuestJournalEntry
                    {
                        Sequence = sequence,
                        DialogueIndex = request.DialogueIndex,
                        InfoIndex = request.InfoIndex,
                        JournalIndex = request.JournalIndex,
                        Day = time.DaysPassed,
                        Month = time.Month,
                        DayOfMonth = time.Day,
                        QuestStatus = request.QuestStatus,
                    });
                    state.NextEntrySequence = sequence + 1u;
                }

                if (request.QuestStatus == QuestStatusFinished)
                    quest.Finished = 1;
                else if (request.QuestStatus == QuestStatusRestart)
                {
                    quest.Finished = 0;
                    RestartFinishedQuestSiblings(contentDb, questStates, request.DialogueIndex);
                }
            }

            questStates[request.DialogueIndex] = quest;
            return true;
        }

        static void RestartFinishedQuestSiblings(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            int sourceDialogueIndex)
        {
            string sourceName = ResolveJournalQuestName(contentDb, sourceDialogueIndex);
            if (string.IsNullOrWhiteSpace(sourceName))
                return;

            int count = math.min(questStates.Length, contentDb.DialogueCount);
            for (int i = 0; i < count; i++)
            {
                if (i == sourceDialogueIndex)
                    continue;

                var quest = questStates[i];
                if (quest.Finished == 0)
                    continue;

                string questName = ResolveJournalQuestName(contentDb, i);
                if (!string.Equals(sourceName, questName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                quest.Finished = 0;
                questStates[i] = quest;
            }
        }

        static string ResolveJournalQuestName(RuntimeContentDatabase contentDb, int dialogueIndex)
        {
            if (contentDb == null || (uint)dialogueIndex >= (uint)contentDb.DialogueCount)
                return string.Empty;

            ref readonly DialogueDef dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Journal)
                return string.Empty;

            int start = math.max(0, dialogue.FirstInfoIndex);
            int end = math.min(contentDb.DialogueInfoCount, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = start; i < end; i++)
            {
                ref readonly DialogueInfoDef info = ref contentDb.Data.DialogueInfos[i];
                if (info.QuestStatus != QuestStatusName)
                    continue;

                return string.IsNullOrWhiteSpace(info.Response) ? string.Empty : info.Response.Trim();
            }

            return string.Empty;
        }

        static bool ContainsEntry(
            DynamicBuffer<MorrowindQuestJournalEntry> entries,
            int dialogueIndex,
            int infoIndex)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.DialogueIndex == dialogueIndex && entry.InfoIndex == infoIndex)
                    return true;
            }

            return false;
        }
    }
}
