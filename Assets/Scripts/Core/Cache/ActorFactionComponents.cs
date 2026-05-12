using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct ActorFactionMembership : IBufferElementData
    {
        public int FactionIndex;
        public int Rank;
        public byte Joined;
    }
}
