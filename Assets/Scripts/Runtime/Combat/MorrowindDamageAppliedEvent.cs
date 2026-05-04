using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Combat
{
    public struct MorrowindDamageAppliedEvent : IComponentData
    {
        public Entity Attacker;
        public Entity Target;
        public ContentReference SourceContent;
        public float Amount;
        public float AttackStrength;
        public MorrowindDamageTargetVital TargetVital;
        public MorrowindDamageSourceKind SourceKind;
        public byte FullDamage;
        public MorrowindBlockImpact BlockImpact;
        public MorrowindArmorImpact ArmorImpact;
        public float3 HitPosition;
        public byte HasHitPosition;
    }
}
