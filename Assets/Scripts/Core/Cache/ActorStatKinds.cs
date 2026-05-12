namespace VVardenfell.Runtime.Components
{
    public enum ActorAttributeKind : byte
    {
        None = 0,
        Strength = 1,
        Intelligence = 2,
        Willpower = 3,
        Agility = 4,
        Speed = 5,
        Endurance = 6,
        Personality = 7,
        Luck = 8,
    }

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
}
