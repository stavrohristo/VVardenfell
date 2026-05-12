using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;

namespace VVardenfell.Runtime.Combat
{
    public struct MorrowindCombatRuntimeState : IComponentData
    {
        public uint RandomState;
    }

    public struct PendingMeleeHitConfirmation : IBufferElementData
    {
        public uint QuerySequence;
        public uint RequestFixedTick;
        public Entity Attacker;
        public Entity Target;
        public ContentReference WeaponContent;
        public ActorWeaponAttackType AttackType;
        public float AttackStrength;
        public float Reach;
        public uint TargetPlacedRefId;
        public float3 HitPosition;
        public byte HasHitPosition;
    }

}
