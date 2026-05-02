using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueUtility
    {
        public static bool TryAddTopic(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            int dialogueIndex)
        {
            if (contentDb == null
                || (uint)dialogueIndex >= (uint)contentDb.DialogueCount
                || (uint)dialogueIndex >= (uint)knownTopics.Length)
                return false;

            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return false;

            knownTopics[dialogueIndex] = new MorrowindKnownDialogueTopic { Known = 1 };
            return true;
        }

        public static bool TryRecordTopicEntry(
            RuntimeContentDatabase contentDb,
            ref MorrowindDialogueState state,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindTopicJournalEntry> entries,
            int dialogueIndex,
            int infoIndex,
            uint actorPlacedRefId,
            FixedString128Bytes actorId)
        {
            if (contentDb == null
                || (uint)dialogueIndex >= (uint)contentDb.DialogueCount
                || (uint)infoIndex >= (uint)contentDb.DialogueInfoCount)
                return false;

            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return false;

            if (ContainsTopicEntry(entries, dialogueIndex, infoIndex))
                return true;

            entries.Add(new MorrowindTopicJournalEntry
            {
                Sequence = state.NextTopicEntrySequence++,
                DialogueIndex = dialogueIndex,
                InfoIndex = infoIndex,
                ActorPlacedRefId = actorPlacedRefId,
                ActorId = actorId,
                Day = time.DaysPassed,
                Month = time.Month,
                DayOfMonth = time.Day,
            });
            return true;
        }

        public static bool TryRemoveLastAddedTopicResponse(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindTopicJournalEntry> entries,
            int dialogueIndex,
            FixedString128Bytes actorName)
        {
            if (contentDb == null
                || (uint)dialogueIndex >= (uint)contentDb.DialogueCount)
                return false;

            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return true;

            string actor = actorName.ToString();
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].DialogueIndex == dialogueIndex
                    && string.Equals(entries[i].ActorId.ToString(), actor, StringComparison.Ordinal))
                {
                    entries.RemoveAt(i);
                    return true;
                }
            }

            return true;
        }

        public static bool TryFillJournal(
            RuntimeContentDatabase contentDb,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            uint actorPlacedRefId,
            FixedString128Bytes actorId)
        {
            if (contentDb == null
                || knownTopics.Length != contentDb.DialogueCount
                || questStates.Length != contentDb.DialogueCount)
                return false;

            for (int dialogueIndex = 0; dialogueIndex < contentDb.DialogueCount; dialogueIndex++)
            {
                ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
                int end = Math.Min(contentDb.DialogueInfoCount, dialogue.FirstInfoIndex + dialogue.InfoCount);
                if (dialogue.Type == DialogueDefType.Journal)
                {
                    for (int infoIndex = dialogue.FirstInfoIndex; infoIndex < end; infoIndex++)
                    {
                        ref readonly var info = ref contentDb.Data.DialogueInfos[infoIndex];
                        if (info.QuestStatus == 1)
                            continue;

                        var request = new MorrowindQuestJournalRequest
                        {
                            DialogueIndex = dialogueIndex,
                            InfoIndex = infoIndex,
                            JournalIndex = info.DispositionOrJournalIndex,
                            QuestStatus = info.QuestStatus,
                            Operation = (byte)MorrowindQuestJournalRequestOperation.Journal,
                        };
                        if (!MorrowindQuestJournalUtility.TryApplyRequest(contentDb, ref questState, time, questStates, questEntries, request))
                            return false;
                    }
                }
                else if (dialogue.Type == DialogueDefType.Topic)
                {
                    knownTopics[dialogueIndex] = new MorrowindKnownDialogueTopic { Known = 1 };
                    for (int infoIndex = dialogue.FirstInfoIndex; infoIndex < end; infoIndex++)
                    {
                        if (!TryRecordTopicEntry(contentDb, ref dialogueState, time, topicEntries, dialogueIndex, infoIndex, actorPlacedRefId, actorId))
                            return false;
                    }
                }
            }

            return true;
        }

        public static void AddTopicsFromResponse(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            string response,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle)
        {
            if (contentDb == null || string.IsNullOrWhiteSpace(response))
                return;

            for (int i = 0; i < contentDb.DialogueCount && i < knownTopics.Length; i++)
            {
                if (knownTopics[i].Known != 0)
                    continue;

                ref readonly var dialogue = ref contentDb.Data.Dialogues[i];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                string id = dialogue.StringId ?? dialogue.Id;
                if (ContainsTopicReference(response, id))
                {
                    if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                            contentDb,
                            entityManager,
                            speakerEntity,
                            speakerHandle,
                            i,
                            -1,
                            out _,
                            out _))
                    {
                        continue;
                    }

                    knownTopics[i] = new MorrowindKnownDialogueTopic { Known = 1 };
                }
            }
        }

        static bool ContainsTopicEntry(DynamicBuffer<MorrowindTopicJournalEntry> entries, int dialogueIndex, int infoIndex)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DialogueIndex == dialogueIndex && entries[i].InfoIndex == infoIndex)
                    return true;
            }

            return false;
        }

        static bool ContainsTopicReference(string response, string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return false;

            int explicitIndex = response.IndexOf('@');
            while (explicitIndex >= 0)
            {
                int end = response.IndexOf('#', explicitIndex + 1);
                if (end > explicitIndex)
                {
                    string linkedTopic = response.Substring(explicitIndex + 1, end - explicitIndex - 1).TrimEnd('\x7F');
                    if (string.Equals(ContentId.NormalizeId(linkedTopic), ContentId.NormalizeId(topic), StringComparison.Ordinal))
                        return true;
                    explicitIndex = response.IndexOf('@', end + 1);
                }
                else
                {
                    break;
                }
            }

            int index = IndexOfTopic(response, topic, 0);
            while (index >= 0)
            {
                int before = index - 1;
                if (before < 0 || IsOpenMwTopicSeparator(response[before]))
                    return true;

                index = IndexOfTopic(response, topic, index + 1);
            }

            return false;
        }

        static int IndexOfTopic(string response, string topic, int startIndex)
            => response.IndexOf(topic, startIndex, StringComparison.OrdinalIgnoreCase);

        static bool IsOpenMwTopicSeparator(char ch)
            => ch == '\n'
               || ch == '\r'
               || ch == ' '
               || ch == '\t'
               || ch == '\''
               || ch == '"'
               || ch == '('
               || ch == '[';

        public static bool TryModFactionReaction(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex,
            int diff)
        {
            if (!IsValidFactionIndex(contentDb, sourceFactionIndex)
                || !IsValidFactionIndex(contentDb, targetFactionIndex))
                return false;

            int value = GetFactionReaction(contentDb, overrides, sourceFactionIndex, targetFactionIndex) + diff;
            SetFactionReactionOverride(overrides, sourceFactionIndex, targetFactionIndex, value);
            return true;
        }

        public static bool TrySetFactionReaction(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex,
            int value)
        {
            if (!IsValidFactionIndex(contentDb, sourceFactionIndex)
                || !IsValidFactionIndex(contentDb, targetFactionIndex))
                return false;

            SetFactionReactionOverride(overrides, sourceFactionIndex, targetFactionIndex, value);
            return true;
        }

        public static int GetFactionReaction(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex)
        {
            for (int i = 0; i < overrides.Length; i++)
            {
                if (overrides[i].SourceFactionIndex == sourceFactionIndex
                    && overrides[i].TargetFactionIndex == targetFactionIndex)
                    return overrides[i].Reaction;
            }

            if (!IsValidFactionIndex(contentDb, sourceFactionIndex)
                || !IsValidFactionIndex(contentDb, targetFactionIndex))
                return 0;

            ref readonly var source = ref contentDb.Data.Factions[sourceFactionIndex];
            string targetId = contentDb.Data.Factions[targetFactionIndex].Id;
            if (source.Reactions == null || string.IsNullOrWhiteSpace(targetId))
                return 0;

            for (int i = 0; i < source.Reactions.Length; i++)
            {
                if (string.Equals(ContentId.NormalizeId(source.Reactions[i].FactionId), ContentId.NormalizeId(targetId), StringComparison.Ordinal))
                    return source.Reactions[i].Reaction;
            }

            return 0;
        }

        static void SetFactionReactionOverride(
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex,
            int value)
        {
            for (int i = 0; i < overrides.Length; i++)
            {
                if (overrides[i].SourceFactionIndex == sourceFactionIndex
                    && overrides[i].TargetFactionIndex == targetFactionIndex)
                {
                    overrides[i] = new MorrowindFactionReactionOverride
                    {
                        SourceFactionIndex = sourceFactionIndex,
                        TargetFactionIndex = targetFactionIndex,
                        Reaction = value,
                    };
                    return;
                }
            }

            overrides.Add(new MorrowindFactionReactionOverride
            {
                SourceFactionIndex = sourceFactionIndex,
                TargetFactionIndex = targetFactionIndex,
                Reaction = value,
            });
        }

        static bool IsValidFactionIndex(RuntimeContentDatabase contentDb, int index)
            => contentDb?.Data.Factions != null && (uint)index < (uint)contentDb.Data.Factions.Length;
    }
}
