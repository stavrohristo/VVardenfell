using Unity.Entities;

namespace VVardenfell.Runtime.Combat
{
    public struct ActorCombatMovementState : IComponentData
    {
        public float LateralMoveUntilTime;
        public float NextLateralMoveTime;
        public sbyte LateralDirection;
    }
}
