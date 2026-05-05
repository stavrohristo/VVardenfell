using System;
using System.Collections.Generic;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        const byte JournalQuestStatusName = 1;
        static readonly string[] k_MonthGameSettings =
        {
            "sMonthMorningstar",
            "sMonthSunsdawn",
            "sMonthFirstseed",
            "sMonthRainshand",
            "sMonthSecondseed",
            "sMonthMidyear",
            "sMonthSunsheight",
            "sMonthLastseed",
            "sMonthHeartfire",
            "sMonthFrostfall",
            "sMonthSunsdusk",
            "sMonthEveningstar",
        };

        static readonly string[] k_DefaultMonthNames =
        {
            "Morning Star",
            "Sun's Dawn",
            "First Seed",
            "Rain's Hand",
            "Second Seed",
            "Mid Year",
            "Sun's Height",
            "Last Seed",
            "Hearthfire",
            "Frost Fall",
            "Sun's Dusk",
            "Evening Star",
        };

        sealed class JournalQuestGroup
        {
            public string Name;
            public readonly List<int> DialogueIndices = new();
            public uint FirstSequence = uint.MaxValue;
            public int RepresentativeDialogueIndex = -1;
            public int Index;
            public bool Finished;
        }

        static JournalWindowViewModel BuildJournalModel(
            ref RuntimeContentBlob contentBlob,
            in JournalWindowState state,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> entries,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries)
        {
            var rows = BuildJournalQuestRows(ref contentBlob, state, questStates, entries);
            int selectedDialogueIndex = ResolveSelectedJournalQuest(rows, state.SelectedDialogueIndex);
            for (int i = 0; i < rows.Length; i++)
                rows[i].Selected = rows[i].DialogueIndex == selectedDialogueIndex;

            JournalQuestRowViewModel selected = FindJournalRow(rows, selectedDialogueIndex);
            JournalEntryRowViewModel[] selectedEntries = selected != null
                ? BuildJournalEntryRows(ref contentBlob, entries, selected.DialogueIndices)
                : Array.Empty<JournalEntryRowViewModel>();

            return new JournalWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
                ShowAll = state.ShowAll != 0,
                Mode = state.Mode == 1 ? JournalWindowBookMode.Quest : JournalWindowBookMode.Journal,
                OverlayOpen = state.OverlayOpen != 0,
                SelectedDialogueIndex = selectedDialogueIndex,
                Page = state.Page,
                QuestScrollY = ClampJournalScroll(state.QuestScrollY),
                EntryScrollY = ClampJournalScroll(state.EntryScrollY),
                Title = "Journal",
                EmptyStateText = "You have no journal entries!",
                ToggleButtonText = state.ShowAll == 0 ? "All" : "Active",
                SelectedQuestTitle = selected?.Title ?? string.Empty,
                SelectedQuestStageText = selected?.StageText ?? string.Empty,
                SelectedQuestStatusText = selected?.StatusText ?? string.Empty,
                Quests = rows,
                JournalEntries = BuildJournalEntryRows(ref contentBlob, entries, null),
                TopicEntries = BuildTopicJournalEntryRows(ref contentBlob, topicEntries),
                Entries = selectedEntries,
            };
        }

        static JournalQuestRowViewModel[] BuildJournalQuestRows(
            ref RuntimeContentBlob contentBlob,
            in JournalWindowState state,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> entries)
        {
            if (questStates.Length == 0)
                return Array.Empty<JournalQuestRowViewModel>();

            var groups = new List<JournalQuestGroup>();
            int questCount = Math.Min(questStates.Length, contentBlob.Dialogues.Length);
            for (int dialogueIndex = 0; dialogueIndex < questCount; dialogueIndex++)
            {
                var questState = questStates[dialogueIndex];
                if (questState.Started == 0 && questState.Index == 0 && !HasJournalEntries(entries, dialogueIndex))
                    continue;

                ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Journal)
                    continue;

                string questName = ResolveJournalQuestName(ref contentBlob, dialogueIndex);
                if (string.IsNullOrWhiteSpace(questName))
                    continue;

                var group = FindOrCreateQuestGroup(groups, questName);
                uint firstSequence = ResolveFirstJournalEntrySequence(entries, dialogueIndex);
                group.DialogueIndices.Add(dialogueIndex);
                group.FirstSequence = Math.Min(group.FirstSequence, firstSequence);
                if (group.RepresentativeDialogueIndex < 0)
                    group.RepresentativeDialogueIndex = dialogueIndex;
                group.Index = Math.Max(group.Index, questState.Index);
                group.Finished |= questState.Finished != 0;
            }

            var rows = new List<JournalQuestRowViewModel>(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (state.ShowAll == 0 && group.Finished)
                    continue;

                rows.Add(new JournalQuestRowViewModel
                {
                    DialogueIndex = group.RepresentativeDialogueIndex,
                    DialogueIndices = group.DialogueIndices.ToArray(),
                    FirstSequence = group.FirstSequence,
                    Title = group.Name,
                    StageText = group.Index == 0 ? string.Empty : $"Stage {group.Index}",
                    StatusText = group.Finished ? "Completed" : "Active",
                    Finished = group.Finished,
                });
            }

            rows.Sort(CompareJournalQuestRows);
            return rows.ToArray();
        }

        static int CompareJournalQuestRows(JournalQuestRowViewModel left, JournalQuestRowViewModel right)
        {
            return string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
        }

        static JournalQuestGroup FindOrCreateQuestGroup(List<JournalQuestGroup> groups, string questName)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (string.Equals(groups[i].Name, questName, StringComparison.OrdinalIgnoreCase))
                    return groups[i];
            }

            var group = new JournalQuestGroup
            {
                Name = questName,
            };
            groups.Add(group);
            return group;
        }

        static int ResolveSelectedJournalQuest(JournalQuestRowViewModel[] rows, int requestedDialogueIndex)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].DialogueIndex == requestedDialogueIndex)
                    return requestedDialogueIndex;
            }

            return rows.Length == 0 ? -1 : rows[0].DialogueIndex;
        }

        static JournalQuestRowViewModel FindJournalRow(JournalQuestRowViewModel[] rows, int dialogueIndex)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].DialogueIndex == dialogueIndex)
                    return rows[i];
            }

            return null;
        }

        static JournalEntryRowViewModel[] BuildJournalEntryRows(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindQuestJournalEntry> entries,
            int[] selectedDialogueIndices)
        {
            var rows = new List<JournalEntryRowViewModel>();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (selectedDialogueIndices != null
                    && selectedDialogueIndices.Length > 0
                    && !ContainsDialogueIndex(selectedDialogueIndices, entry.DialogueIndex))
                    continue;

                string body = ResolveJournalEntryText(ref contentBlob, entry.InfoIndex);
                if (string.IsNullOrWhiteSpace(body))
                    continue;

                rows.Add(new JournalEntryRowViewModel
                {
                    Sequence = entry.Sequence,
                    JournalIndex = entry.JournalIndex,
                    TimestampText = ResolveJournalTimestamp(ref contentBlob, entry),
                    StageText = $"Stage {entry.JournalIndex}",
                    BodyText = body ?? string.Empty,
                });
            }

            rows.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            return rows.ToArray();
        }

        static JournalEntryRowViewModel[] BuildTopicJournalEntryRows(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindTopicJournalEntry> entries)
        {
            var rows = new List<JournalEntryRowViewModel>();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if ((uint)entry.DialogueIndex >= (uint)contentBlob.Dialogues.Length
                    || (uint)entry.InfoIndex >= (uint)contentBlob.DialogueInfos.Length)
                    continue;

                string body = ResolveJournalEntryText(ref contentBlob, entry.InfoIndex);
                if (string.IsNullOrWhiteSpace(body))
                    continue;

                rows.Add(new JournalEntryRowViewModel
                {
                    Sequence = entry.Sequence,
                    TimestampText = ResolveTopicTimestamp(ref contentBlob, entry),
                    StageText = ResolveDialogueTitle(ref contentBlob, entry.DialogueIndex),
                    BodyText = body,
                });
            }

            rows.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            return rows.ToArray();
        }

        static bool ContainsDialogueIndex(int[] dialogueIndices, int dialogueIndex)
        {
            for (int i = 0; i < dialogueIndices.Length; i++)
            {
                if (dialogueIndices[i] == dialogueIndex)
                    return true;
            }

            return false;
        }

        static bool HasJournalEntries(DynamicBuffer<MorrowindQuestJournalEntry> entries, int dialogueIndex)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DialogueIndex == dialogueIndex)
                    return true;
            }

            return false;
        }

        static uint ResolveFirstJournalEntrySequence(DynamicBuffer<MorrowindQuestJournalEntry> entries, int dialogueIndex)
        {
            uint result = uint.MaxValue;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DialogueIndex == dialogueIndex && entries[i].Sequence < result)
                    result = entries[i].Sequence;
            }

            return result;
        }

        static string ResolveJournalQuestName(ref RuntimeContentBlob contentBlob, int dialogueIndex)
        {
            ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
            int start = Math.Max(0, dialogue.FirstInfoIndex);
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = start; i < end; i++)
            {
                ref RuntimeDialogueInfoDefBlob info = ref contentBlob.DialogueInfos[i];
                if (info.QuestStatus != JournalQuestStatusName)
                    continue;

                string response = info.Response.ToString();
                return string.IsNullOrWhiteSpace(response) ? string.Empty : response.Trim();
            }

            return string.Empty;
        }

        static string ResolveJournalEntryText(ref RuntimeContentBlob contentBlob, int infoIndex)
        {
            if ((uint)infoIndex >= (uint)contentBlob.DialogueInfos.Length)
                return string.Empty;

            string response = contentBlob.DialogueInfos[infoIndex].Response.ToString();
            return string.IsNullOrWhiteSpace(response) ? string.Empty : response.Trim();
        }

        static string ResolveDialogueTitle(ref RuntimeContentBlob contentBlob, int dialogueIndex)
        {
            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
                return string.Empty;

            ref RuntimeDialogueDefBlob dialogue = ref contentBlob.Dialogues[dialogueIndex];
            string stringId = dialogue.StringId.ToString();
            if (!string.IsNullOrWhiteSpace(stringId))
                return stringId.Trim();

            string id = dialogue.Id.ToString();
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        static string ResolveJournalTimestamp(ref RuntimeContentBlob contentBlob, in MorrowindQuestJournalEntry entry)
        {
            if (entry.DayOfMonth <= 0)
                return string.Empty;

            int month = Math.Clamp(entry.Month, 0, k_DefaultMonthNames.Length - 1);
            string monthName = ResolveMonthName(ref contentBlob, month);
            string dayLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sDay", "Day");
            return $"{entry.DayOfMonth} {monthName} ({dayLabel} {entry.Day})";
        }

        static string ResolveTopicTimestamp(ref RuntimeContentBlob contentBlob, in MorrowindTopicJournalEntry entry)
        {
            if (entry.DayOfMonth <= 0)
                return string.Empty;

            int month = Math.Clamp(entry.Month, 0, k_DefaultMonthNames.Length - 1);
            string monthName = ResolveMonthName(ref contentBlob, month);
            string dayLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sDay", "Day");
            return $"{entry.DayOfMonth} {monthName} ({dayLabel} {entry.Day})";
        }

        static string ResolveMonthName(ref RuntimeContentBlob contentBlob, int month)
        {
            if (month >= 0 && month < k_MonthGameSettings.Length)
            {
                string value = RuntimeContentBlobUtility.RequireGameSettingStringAllowEmptyByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(k_MonthGameSettings[month]));
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return k_DefaultMonthNames[Math.Clamp(month, 0, k_DefaultMonthNames.Length - 1)];
        }

        static float ClampJournalScroll(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
