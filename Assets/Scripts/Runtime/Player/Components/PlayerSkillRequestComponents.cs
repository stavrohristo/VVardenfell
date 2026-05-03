using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public enum ActorSkillKind : byte
    {
        None = 0,
        Block = 1,
        Armorer = 2,
        MediumArmor = 3,
        HeavyArmor = 4,
        BluntWeapon = 5,
        LongBlade = 6,
        Axe = 7,
        Spear = 8,
        Athletics = 9,
        Enchant = 10,
        Destruction = 11,
        Alteration = 12,
        Illusion = 13,
        Conjuration = 14,
        Mysticism = 15,
        Restoration = 16,
        Alchemy = 17,
        Unarmored = 18,
        Security = 19,
        Sneak = 20,
        Acrobatics = 21,
        LightArmor = 22,
        ShortBlade = 23,
        Marksman = 24,
        Mercantile = 25,
        Speechcraft = 26,
        HandToHand = 27,
    }

    public enum PlayerSkillMutationKind : byte
    {
        Set = 1,
        Mod = 2,
    }

    public struct PlayerSkillMutationRequest : IBufferElementData
    {
        public float Value;
        public byte Skill;
        public byte Kind;
    }
}
