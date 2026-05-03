using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public enum ActorAiGreetingPhase : byte
    {
        None = 0,
        InProgress = 1,
        Done = 2,
    }

    public struct ActorAiGreetingState : IComponentData
    {
        public float Timer;
        public byte Phase;
    }

    public struct ActorAiPassiveGreetingSayRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public FixedString512Bytes VoicePath;
        public FixedString512Bytes Subtitle;
    }
}
