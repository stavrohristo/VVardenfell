using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public enum PlayerFactionMutationKind : byte
    {
        ModReputation = 1,
        RaiseRank = 2,
        Join = 3,
        Expel = 4,
        ClearExpelled = 5,
    }

    public struct PlayerFactionMutationRequest : IBufferElementData
    {
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public int FactionIndex;
        public int Value;
        public byte Kind;
    }

    public struct ActorFactionRankMutationRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }
}
