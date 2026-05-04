using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Combat
{
    public struct MorrowindArmorImpact
    {
        public Entity Target;
        public ItemEquipmentSlot Slot;
        public ContentReference Content;
        public ActorSkillKind Skill;
        public int ConditionDamage;
        public byte HasEquippedArmor;
    }
}
