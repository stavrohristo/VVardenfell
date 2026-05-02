using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public struct ActorCombatTargetState : IComponentData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Active;
    }
}
