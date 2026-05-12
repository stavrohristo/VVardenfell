using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public struct ActorAiPassiveGreetingSayRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public FixedString512Bytes VoicePath;
        public FixedString512Bytes Subtitle;
    }
}
