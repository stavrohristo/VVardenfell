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
        public FixedString128Bytes DisabledReason;
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
