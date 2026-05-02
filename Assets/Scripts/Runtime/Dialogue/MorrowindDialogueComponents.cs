using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public enum MorrowindDialogueRequestOperation : byte
    {
        AddTopic = 0,
        FillJournal = 1,
    }

    public struct MorrowindDialogueState : IComponentData
    {
        public int DialogueCount;
        public uint NextTopicEntrySequence;
        public uint NextSessionSequence;
    }

    public struct MorrowindKnownDialogueTopic : IBufferElementData
    {
        public byte Known;
    }

    public struct MorrowindTopicJournalEntry : IBufferElementData
    {
        public uint Sequence;
        public int DialogueIndex;
        public int InfoIndex;
        public uint ActorPlacedRefId;
        public FixedString128Bytes ActorId;
        public int Day;
        public int Month;
        public int DayOfMonth;
    }

    public struct MorrowindFactionReactionOverride : IBufferElementData
    {
        public int SourceFactionIndex;
        public int TargetFactionIndex;
        public int Reaction;
    }

    public struct MorrowindDialogueRequest : IBufferElementData
    {
        public int DialogueIndex;
        public byte Operation;
    }

    public struct MorrowindDialogueSession : IComponentData
    {
        public byte Active;
        public byte NeedsGreeting;
        public byte Goodbye;
        public byte ChoiceActive;
        public uint Sequence;
        public Entity SpeakerEntity;
        public uint SpeakerPlacedRefId;
        public ActorDefHandle SpeakerActor;
        public int SelectedTopicDialogueIndex;
        public int ChoiceDialogueIndex;
        public int LastInfoIndex;
        public FixedString128Bytes SpeakerId;
    }

    public struct MorrowindDialogueSessionLine : IBufferElementData
    {
        public int DialogueIndex;
        public int InfoIndex;
        public byte ShowTitle;
    }

    public struct MorrowindDialogueChoice : IBufferElementData
    {
        public int Value;
        public FixedString512Bytes Text;
    }

    public enum MorrowindDialogueResponseAction : byte
    {
        None = 0,
        SelectTopic = 1,
        Goodbye = 2,
        AnswerChoice = 3,
    }

    public struct MorrowindDialogueResponseRequest : IComponentData
    {
        public byte Pending;
        public byte Action;
        public int DialogueIndex;
        public int ChoiceValue;
    }
}
