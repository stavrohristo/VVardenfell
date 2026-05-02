using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public enum MorrowindScriptInstanceStatus : byte
    {
        None = 0,
        Running = 1,
        Disabled = 2,
        Faulted = 3,
    }

    public struct MorrowindScriptRuntimeState : IComponentData
    {
        public uint NextAudioRequestSequence;
    }

    public struct MorrowindScriptInstance : IComponentData
    {
        public MorrowindScriptProgramDefHandle Program;
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public FixedString128Bytes DisabledReason;
    }

    public struct MorrowindGlobalScriptInstance : IComponentData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public struct MorrowindScriptStartRequest : IBufferElementData
    {
        public MorrowindScriptProgramDefHandle Program;
        public int ProgramIndex;
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public struct MorrowindScriptLocalValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct MorrowindScriptStackValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct MorrowindScriptGlobalValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct MorrowindActorDeathCount : IBufferElementData
    {
        public int Count;
    }

    public struct MorrowindActorDeathCounted : IComponentData
    {
    }

    public struct MorrowindQuestJournalState : IComponentData
    {
        public int QuestCount;
        public uint NextEntrySequence;
    }

    public struct MorrowindQuestJournalIndex : IBufferElementData
    {
        public int Index;
        public byte Started;
        public byte Finished;
    }

    public struct MorrowindQuestJournalEntry : IBufferElementData
    {
        public uint Sequence;
        public int DialogueIndex;
        public int InfoIndex;
        public int JournalIndex;
        public int Day;
        public int Month;
        public int DayOfMonth;
        public byte QuestStatus;
    }

    public enum MorrowindQuestJournalRequestOperation : byte
    {
        Journal = 0,
        SetIndex = 1,
    }

    public struct MorrowindQuestJournalRequest : IBufferElementData
    {
        public int DialogueIndex;
        public int InfoIndex;
        public int JournalIndex;
        public byte QuestStatus;
        public byte Operation;
    }

    public struct MorrowindScriptActiveSource : IBufferElementData
    {
        public ulong LoopSourceKey;
    }

    public struct MorrowindScriptPlayingSound : IBufferElementData
    {
        public ulong LoopKey;
    }

    public struct MorrowindScriptRefStateRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Disabled;
    }

    public struct MorrowindScriptTransformRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public float Radians;
        public float3 Position;
        public ulong InteriorCellHash;
        public byte Axis;
        public byte Operation;
    }

    public enum MorrowindScriptAiPackageRequestType : byte
    {
        Wander = 1,
        Travel = 2,
        Follow = 3,
    }

    public struct MorrowindScriptAiPackageRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public Entity FollowTargetEntity;
        public uint FollowTargetPlacedRefId;
        public float3 TargetPosition;
        public float WanderRadius;
        public float IdleSeconds;
        public float FollowDistance;
        public ulong DestinationInteriorCellHash;
        public byte PackageType;
        public byte ShouldRepeat;
        public byte AllowPartial;
    }

    public struct MorrowindScriptAudioRequest : IComponentData
    {
        public uint Sequence;
        public SoundDefHandle Sound;
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public float3 Position;
        public float Volume;
        public float Pitch;
        public byte Kind;
        public byte Spatial;
        public byte Looping;
    }
}
