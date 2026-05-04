using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct ActorBlockState : IComponentData
    {
        public byte Active;
        public uint Sequence;
        public uint AnimatedSequence;
    }
}
