using System;
using UnityEngine;

namespace VVardenfell.Runtime.UI.Shell
{
    public enum JournalWindowBookMode : byte
    {
        Journal = 0,
        Quest = 1,
    }

    public sealed class JournalWindowViewModel
    {
        public Rect NormalizedRect;
        public bool ShowAll;
        public JournalWindowBookMode Mode;
        public bool OverlayOpen;
        public int SelectedDialogueIndex;
        public int Page = -1;
        public float QuestScrollY = 1f;
        public float EntryScrollY = 1f;
        public string Title;
        public string EmptyStateText;
        public string ToggleButtonText;
        public string SelectedQuestTitle;
        public string SelectedQuestStageText;
        public string SelectedQuestStatusText;
        public JournalQuestRowViewModel[] Quests = Array.Empty<JournalQuestRowViewModel>();
        public JournalEntryRowViewModel[] JournalEntries = Array.Empty<JournalEntryRowViewModel>();
        public JournalEntryRowViewModel[] TopicEntries = Array.Empty<JournalEntryRowViewModel>();
        public JournalEntryRowViewModel[] Entries = Array.Empty<JournalEntryRowViewModel>();
    }

    public sealed class JournalQuestRowViewModel
    {
        public int DialogueIndex;
        public int[] DialogueIndices = Array.Empty<int>();
        public uint FirstSequence;
        public string Title;
        public string StageText;
        public string StatusText;
        public bool Selected;
        public bool Finished;
    }

    public sealed class JournalEntryRowViewModel
    {
        public uint Sequence;
        public int JournalIndex;
        public string TimestampText;
        public string StageText;
        public string BodyText;
    }
}
