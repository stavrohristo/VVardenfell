using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public struct ActorCombatTarget : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public uint Sequence;
    }
}
