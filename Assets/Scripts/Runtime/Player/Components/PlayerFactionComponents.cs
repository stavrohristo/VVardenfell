using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct PlayerFactionMembership : IBufferElementData
    {
        public int FactionIndex;
        public int Rank;
        public int Reputation;
        public byte Joined;
        public byte Expelled;
    }
}
