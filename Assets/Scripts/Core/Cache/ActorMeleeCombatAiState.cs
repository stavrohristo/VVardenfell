using Unity.Entities;
using VVardenfell.Runtime.Animation;

namespace VVardenfell.Runtime.Combat
{
    public struct ActorMeleeCombatAiState : IComponentData
    {
        public float CooldownSeconds;
        public float DesiredAttackStrength;
        public ActorWeaponAttackType DesiredAttackType;
        public byte AttackInProgress;
    }
}
