using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueUtility
    {
        public static bool TryAddTopic(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            int dialogueIndex)
        {
            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length
                || (uint)dialogueIndex >= (uint)knownTopics.Length)
                return false;

            ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return false;

            knownTopics[dialogueIndex] = new MorrowindKnownDialogueTopic { Known = 1 };
            return true;
        }

        public static bool TryRecordTopicEntry(
            ref RuntimeContentBlob contentBlob,
            ref MorrowindDialogueState state,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindTopicJournalEntry> entries,
            int dialogueIndex,
            int infoIndex,
            uint actorPlacedRefId,
            FixedString128Bytes actorId)
        {
            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length
                || (uint)infoIndex >= (uint)contentBlob.DialogueInfos.Length)
                return false;

            ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
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
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindTopicJournalEntry> entries,
            int dialogueIndex,
            FixedString128Bytes actorName)
        {
            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
                return false;

            ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
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
            ref RuntimeContentBlob contentBlob,
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
            if (knownTopics.Length != contentBlob.Dialogues.Length
                || questStates.Length != contentBlob.Dialogues.Length)
                return false;

            for (int dialogueIndex = 0; dialogueIndex < contentBlob.Dialogues.Length; dialogueIndex++)
            {
                ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
                int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
                if (dialogue.Type == DialogueDefType.Journal)
                {
                    for (int infoIndex = dialogue.FirstInfoIndex; infoIndex < end; infoIndex++)
                    {
                        ref RuntimeDialogueInfoDefBlob info = ref contentBlob.DialogueInfos[infoIndex];
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
                        if (!MorrowindQuestJournalUtility.TryApplyRequest(ref contentBlob, ref questState, time, questStates, questEntries, request))
                            return false;
                    }
                }
                else if (dialogue.Type == DialogueDefType.Topic)
                {
                    knownTopics[dialogueIndex] = new MorrowindKnownDialogueTopic { Known = 1 };
                    for (int infoIndex = dialogue.FirstInfoIndex; infoIndex < end; infoIndex++)
                    {
                        if (!TryRecordTopicEntry(ref contentBlob, ref dialogueState, time, topicEntries, dialogueIndex, infoIndex, actorPlacedRefId, actorId))
                            return false;
                    }
                }
            }

            return true;
        }

        public static void AddTopicsFromResponse(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            string response,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle)
        {
            if (string.IsNullOrWhiteSpace(response))
                return;

            for (int i = 0; i < contentBlob.Dialogues.Length && i < knownTopics.Length; i++)
            {
                if (knownTopics[i].Known != 0)
                    continue;

                ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[i];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                string stringId = dialogue.StringId.ToString();
                string id = string.IsNullOrWhiteSpace(stringId) ? dialogue.Id.ToString() : stringId;
                if (ContainsTopicReference(response, id))
                {
                    if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                            ref contentBlob,
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

        public static void AddTopicsFromResponseBurst(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            FixedString512Bytes response,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle,
            ref MorrowindDialogueFilterUtility.QueryContext queryContext)
        {
            if (IsWhiteSpace(response))
                return;

            for (int i = 0; i < contentBlob.Dialogues.Length && i < knownTopics.Length; i++)
            {
                if (knownTopics[i].Known != 0)
                    continue;

                ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[i];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                FixedString128Bytes topic = ResolveDialogueTopicId(ref dialogue);
                if (!ContainsTopicReference(response, topic))
                    continue;

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                        ref contentBlob,
                        ref worldCells,
                        entityManager,
                        speakerEntity,
                        speakerHandle,
                        i,
                        -1,
                        ref queryContext,
                        out _,
                        out _))
                {
                    continue;
                }

                knownTopics[i] = new MorrowindKnownDialogueTopic { Known = 1 };
            }
        }

        public static bool ContainsTopicEntry(DynamicBuffer<MorrowindTopicJournalEntry> entries, int dialogueIndex, int infoIndex)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DialogueIndex == dialogueIndex && entries[i].InfoIndex == infoIndex)
                    return true;
            }

            return false;
        }

        public static bool ResponseContainsTopicReference(string response, string topic)
            => !string.IsNullOrWhiteSpace(response) && ContainsTopicReference(response, topic);

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

        static bool ContainsTopicReference(FixedString512Bytes response, FixedString128Bytes topic)
        {
            int topicStart = 0;
            int topicEnd = topic.Length;
            TrimNormalizedRange(topic, ref topicStart, ref topicEnd);
            if (topicStart >= topicEnd)
                return false;

            int explicitIndex = IndexOf(response, (byte)'@', 0);
            while (explicitIndex >= 0)
            {
                int end = IndexOf(response, (byte)'#', explicitIndex + 1);
                if (end > explicitIndex)
                {
                    int valueStart = explicitIndex + 1;
                    int valueEnd = end;
                    TrimNormalizedRange(response, ref valueStart, ref valueEnd);
                    if (NormalizedEquals(response, valueStart, valueEnd, topic, topicStart, topicEnd))
                        return true;

                    explicitIndex = IndexOf(response, (byte)'@', end + 1);
                }
                else
                {
                    break;
                }
            }

            int index = IndexOfTopic(response, topic, topicStart, topicEnd, 0);
            while (index >= 0)
            {
                int before = index - 1;
                if (before < 0 || IsOpenMwTopicSeparator((char)response[before]))
                    return true;

                index = IndexOfTopic(response, topic, topicStart, topicEnd, index + 1);
            }

            return false;
        }

        static int IndexOfTopic(string response, string topic, int startIndex)
            => response.IndexOf(topic, startIndex, StringComparison.OrdinalIgnoreCase);

        static int IndexOfTopic(FixedString512Bytes response, FixedString128Bytes topic, int topicStart, int topicEnd, int startIndex)
        {
            int topicLength = topicEnd - topicStart;
            if (topicLength <= 0 || response.Length < topicLength)
                return -1;

            int max = response.Length - topicLength;
            for (int i = Math.Max(0, startIndex); i <= max; i++)
            {
                bool matched = true;
                for (int j = 0; j < topicLength; j++)
                {
                    if (ToLowerAscii(response[i + j]) != ToLowerAscii(topic[topicStart + j]))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                    return i;
            }

            return -1;
        }

        static int IndexOf(FixedString512Bytes value, byte needle, int startIndex)
        {
            for (int i = Math.Max(0, startIndex); i < value.Length; i++)
            {
                if (value[i] == needle)
                    return i;
            }

            return -1;
        }

        static bool IsOpenMwTopicSeparator(char ch)
            => ch == '\n'
               || ch == '\r'
               || ch == ' '
               || ch == '\t'
               || ch == '\''
               || ch == '"'
               || ch == '('
               || ch == '[';

        static FixedString128Bytes ResolveDialogueTopicId(ref RuntimeDialogueDefBlob dialogue)
        {
            FixedString128Bytes stringId = RuntimeFixedStringUtility.ToFixed128OrDefault(ref dialogue.StringId);
            if (!IsWhiteSpace(stringId))
                return stringId;

            return RuntimeFixedStringUtility.ToFixed128OrDefault(ref dialogue.Id);
        }

        static bool IsWhiteSpace(FixedString512Bytes value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (!IsAsciiWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }

        static bool IsWhiteSpace(FixedString128Bytes value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (!IsAsciiWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }

        static void TrimNormalizedRange(FixedString512Bytes value, ref int start, ref int end)
        {
            while (start < end && IsAsciiWhiteSpace(value[start]))
                start++;

            while (end > start && (IsAsciiWhiteSpace(value[end - 1]) || value[end - 1] == 0x7Fu))
                end--;
        }

        static void TrimNormalizedRange(FixedString128Bytes value, ref int start, ref int end)
        {
            while (start < end && IsAsciiWhiteSpace(value[start]))
                start++;

            while (end > start && (IsAsciiWhiteSpace(value[end - 1]) || value[end - 1] == 0x7Fu))
                end--;
        }

        static bool NormalizedEquals(
            FixedString512Bytes left,
            int leftStart,
            int leftEnd,
            FixedString128Bytes right,
            int rightStart,
            int rightEnd)
        {
            int leftLength = leftEnd - leftStart;
            int rightLength = rightEnd - rightStart;
            if (leftLength != rightLength)
                return false;

            for (int i = 0; i < leftLength; i++)
            {
                if (ToLowerAscii(left[leftStart + i]) != ToLowerAscii(right[rightStart + i]))
                    return false;
            }

            return true;
        }

        static byte ToLowerAscii(byte value)
            => value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;

        static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v';

        public static bool TryModFactionReaction(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex,
            int diff)
        {
            if (!IsValidFactionIndex(ref contentBlob, sourceFactionIndex)
                || !IsValidFactionIndex(ref contentBlob, targetFactionIndex))
                return false;

            int value = GetFactionReaction(ref contentBlob, overrides, sourceFactionIndex, targetFactionIndex) + diff;
            SetFactionReactionOverride(overrides, sourceFactionIndex, targetFactionIndex, value);
            return true;
        }

        public static bool TrySetFactionReaction(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindFactionReactionOverride> overrides,
            int sourceFactionIndex,
            int targetFactionIndex,
            int value)
        {
            if (!IsValidFactionIndex(ref contentBlob, sourceFactionIndex)
                || !IsValidFactionIndex(ref contentBlob, targetFactionIndex))
                return false;

            SetFactionReactionOverride(overrides, sourceFactionIndex, targetFactionIndex, value);
            return true;
        }

        public static int GetFactionReaction(
            ref RuntimeContentBlob contentBlob,
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

            if (!IsValidFactionIndex(ref contentBlob, sourceFactionIndex)
                || !IsValidFactionIndex(ref contentBlob, targetFactionIndex))
                return 0;

            ref RuntimeFactionDefBlob source = ref contentBlob.Factions[sourceFactionIndex];
            ulong targetIdHash = contentBlob.Factions[targetFactionIndex].IdHash;
            if (targetIdHash == 0UL)
                return 0;

            RuntimeContentBlobUtility.RequireRange(source.FirstReactionIndex, source.ReactionCount, contentBlob.FactionReactions.Length, "faction reaction");
            for (int i = 0; i < source.ReactionCount; i++)
            {
                ref RuntimeFactionReactionDefBlob reaction = ref contentBlob.FactionReactions[source.FirstReactionIndex + i];
                if (reaction.FactionIdHash == targetIdHash)
                    return reaction.Reaction;
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

        static bool IsValidFactionIndex(ref RuntimeContentBlob contentBlob, int index)
            => (uint)index < (uint)contentBlob.Factions.Length;
    }
}
