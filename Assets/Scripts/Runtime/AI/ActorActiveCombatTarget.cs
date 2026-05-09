using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public struct ActorActiveCombatTarget : IComponentData, IEnableableComponent
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }
}
