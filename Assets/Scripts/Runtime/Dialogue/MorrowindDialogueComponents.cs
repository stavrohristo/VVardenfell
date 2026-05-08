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
        public byte ServiceOpen;
        public uint Sequence;
        public Entity SpeakerEntity;
        public uint SpeakerPlacedRefId;
        public ActorDefHandle SpeakerActor;
        public int SelectedTopicDialogueIndex;
        public int ChoiceDialogueIndex;
        public int LastInfoIndex;
        public int TemporaryDispositionDelta;
        public int PermanentDispositionDelta;
        public FixedString128Bytes SpeakerId;
    }

    public struct MorrowindDialogueSessionLine : IBufferElementData
    {
        public int DialogueIndex;
        public int InfoIndex;
        public byte ShowTitle;
        public byte Style;
        public FixedString512Bytes BodyOverride;
    }

    public enum MorrowindDialogueSessionLineStyle : byte
    {
        Normal = 0,
        Notification = 1,
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
        OpenService = 4,
        ServiceAction = 5,
    }

    public enum MorrowindDialogueServiceKind : byte
    {
        None = 0,
        Persuasion = 1,
        Barter = 2,
        Travel = 3,
    }

    public enum MorrowindDialogueServiceAction : byte
    {
        None = 0,
        Close = 1,
        Persuade = 2,
        Travel = 3,
        StageMerchantItem = 4,
        StagePlayerItem = 5,
        ResetBarter = 6,
        OfferBarter = 7,
        AdjustBarterOffer = 8,
    }

    public enum MorrowindPersuasionAction : byte
    {
        Admire = 0,
        Intimidate = 1,
        Taunt = 2,
        Bribe10 = 3,
        Bribe100 = 4,
        Bribe1000 = 5,
    }

    public struct MorrowindDialogueResponseRequest : IComponentData
    {
        public byte Pending;
        public byte Action;
        public int DialogueIndex;
        public int ChoiceValue;
        public byte ServiceKind;
        public byte ServiceAction;
        public int Int0;
        public int Int1;
    }

    public struct MorrowindDialogueServiceWindowState : IComponentData
    {
        public byte Visible;
        public byte Mode;
        public Entity SpeakerEntity;
        public uint SpeakerPlacedRefId;
        public ActorDefHandle SpeakerActor;
        public int BarterOffer;
    }

    public struct MorrowindDialogueBarterStagedItem : IBufferElementData
    {
        public byte Owner;
        public int SourceIndex;
        public ContentReference Content;
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
        public int Count;
        public int Condition;
        public float EnchantmentCharge;
    }

    public struct ActorBarterState : IComponentData
    {
        public int Gold;
        public float RestockDueTotalHours;
    }

    public struct PlayerTravelingState : IComponentData
    {
        public byte Active;
    }
}
