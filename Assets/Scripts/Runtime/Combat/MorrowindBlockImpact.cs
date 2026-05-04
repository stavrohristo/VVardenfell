using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Combat
{
    public struct MorrowindBlockImpact
    {
        public Entity Target;
        public ContentReference ShieldContent;
        public ActorSkillKind ShieldSkill;
        public float IncomingDamage;
        public int Chance;
        public int Roll;
        public byte Blocked;
    }
}
