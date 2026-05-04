using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;

namespace VVardenfell.Runtime.Combat
{
    public struct MorrowindMeleeHitEvent : IComponentData
    {
        public Entity Attacker;
        public Entity Target;
        public ContentReference WeaponContent;
        public ActorWeaponAttackType AttackType;
        public float AttackStrength;
        public float3 HitPosition;
        public byte HasHitPosition;
    }
}
