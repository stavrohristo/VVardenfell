using System;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Magic
{
    static class ActorMagicStatUtility
    {
        public static ActorRuntimeStatSeed InitializeAuthoritativeState(in ActorRuntimeStatSeed seed)
        {
            var result = seed;
            result.AttributeBase = IsZero(result.AttributeBase) ? result.Attributes : result.AttributeBase;
            result.SkillBase = IsZero(result.SkillBase) ? result.Skills : result.SkillBase;
            if (result.VitalBase.Health <= 0f)
                result.VitalBase.Health = result.Vitals.ModifiedHealthBase;
            if (result.VitalBase.Magicka <= 0f)
                result.VitalBase.Magicka = result.Vitals.ModifiedMagickaBase;
            if (result.VitalBase.Fatigue <= 0f)
                result.VitalBase.Fatigue = result.Vitals.ModifiedFatigueBase;
            RecomputeReadModel(ref result);
            return result;
        }

        public static void RecomputeReadModel(ref ActorRuntimeStatSeed seed)
        {
            seed.Attributes = Combine(seed.AttributeBase, seed.AttributeDamage, seed.AttributeModifiers);
            seed.Skills = Combine(seed.SkillBase, seed.SkillDamage, seed.SkillModifiers);
            seed.Vitals.ModifiedHealthBase = math.max(0f, seed.VitalBase.Health + seed.VitalModifiers.Health);
            seed.Vitals.ModifiedMagickaBase = math.max(0f, seed.VitalBase.Magicka + seed.VitalModifiers.Magicka);
            seed.Vitals.ModifiedFatigueBase = math.max(0f, seed.VitalBase.Fatigue + seed.VitalModifiers.Fatigue);
            seed.Vitals.CurrentHealth = math.min(seed.Vitals.CurrentHealth, seed.Vitals.ModifiedHealthBase);
            seed.Vitals.CurrentMagicka = math.min(seed.Vitals.CurrentMagicka, seed.Vitals.ModifiedMagickaBase);
            seed.Vitals.CurrentFatigue = math.min(seed.Vitals.CurrentFatigue, seed.Vitals.ModifiedFatigueBase);
        }

        public static ActorAttributeSet Combine(
            in ActorAttributeSet bases,
            in ActorAttributeSet damage,
            in ActorAttributeSet modifiers)
        {
            return new ActorAttributeSet
            {
                Strength = math.max(0f, bases.Strength - damage.Strength + modifiers.Strength),
                Intelligence = math.max(0f, bases.Intelligence - damage.Intelligence + modifiers.Intelligence),
                Willpower = math.max(0f, bases.Willpower - damage.Willpower + modifiers.Willpower),
                Agility = math.max(0f, bases.Agility - damage.Agility + modifiers.Agility),
                Speed = math.max(0f, bases.Speed - damage.Speed + modifiers.Speed),
                Endurance = math.max(0f, bases.Endurance - damage.Endurance + modifiers.Endurance),
                Personality = math.max(0f, bases.Personality - damage.Personality + modifiers.Personality),
                Luck = math.max(0f, bases.Luck - damage.Luck + modifiers.Luck),
            };
        }

        public static ActorSkillSet Combine(
            in ActorSkillSet bases,
            in ActorSkillSet damage,
            in ActorSkillSet modifiers)
        {
            return new ActorSkillSet
            {
                Block = math.max(0f, bases.Block - damage.Block + modifiers.Block),
                Armorer = math.max(0f, bases.Armorer - damage.Armorer + modifiers.Armorer),
                MediumArmor = math.max(0f, bases.MediumArmor - damage.MediumArmor + modifiers.MediumArmor),
                HeavyArmor = math.max(0f, bases.HeavyArmor - damage.HeavyArmor + modifiers.HeavyArmor),
                BluntWeapon = math.max(0f, bases.BluntWeapon - damage.BluntWeapon + modifiers.BluntWeapon),
                LongBlade = math.max(0f, bases.LongBlade - damage.LongBlade + modifiers.LongBlade),
                Axe = math.max(0f, bases.Axe - damage.Axe + modifiers.Axe),
                Spear = math.max(0f, bases.Spear - damage.Spear + modifiers.Spear),
                Athletics = math.max(0f, bases.Athletics - damage.Athletics + modifiers.Athletics),
                Enchant = math.max(0f, bases.Enchant - damage.Enchant + modifiers.Enchant),
                Destruction = math.max(0f, bases.Destruction - damage.Destruction + modifiers.Destruction),
                Alteration = math.max(0f, bases.Alteration - damage.Alteration + modifiers.Alteration),
                Illusion = math.max(0f, bases.Illusion - damage.Illusion + modifiers.Illusion),
                Conjuration = math.max(0f, bases.Conjuration - damage.Conjuration + modifiers.Conjuration),
                Mysticism = math.max(0f, bases.Mysticism - damage.Mysticism + modifiers.Mysticism),
                Restoration = math.max(0f, bases.Restoration - damage.Restoration + modifiers.Restoration),
                Alchemy = math.max(0f, bases.Alchemy - damage.Alchemy + modifiers.Alchemy),
                Unarmored = math.max(0f, bases.Unarmored - damage.Unarmored + modifiers.Unarmored),
                Security = math.max(0f, bases.Security - damage.Security + modifiers.Security),
                Sneak = math.max(0f, bases.Sneak - damage.Sneak + modifiers.Sneak),
                Acrobatics = math.max(0f, bases.Acrobatics - damage.Acrobatics + modifiers.Acrobatics),
                LightArmor = math.max(0f, bases.LightArmor - damage.LightArmor + modifiers.LightArmor),
                ShortBlade = math.max(0f, bases.ShortBlade - damage.ShortBlade + modifiers.ShortBlade),
                Marksman = math.max(0f, bases.Marksman - damage.Marksman + modifiers.Marksman),
                Mercantile = math.max(0f, bases.Mercantile - damage.Mercantile + modifiers.Mercantile),
                Speechcraft = math.max(0f, bases.Speechcraft - damage.Speechcraft + modifiers.Speechcraft),
                HandToHand = math.max(0f, bases.HandToHand - damage.HandToHand + modifiers.HandToHand),
            };
        }

        public static float GetAttribute(in ActorAttributeSet attributes, ActorAttributeKind attribute)
            => attribute switch
            {
                ActorAttributeKind.Strength => attributes.Strength,
                ActorAttributeKind.Intelligence => attributes.Intelligence,
                ActorAttributeKind.Willpower => attributes.Willpower,
                ActorAttributeKind.Agility => attributes.Agility,
                ActorAttributeKind.Speed => attributes.Speed,
                ActorAttributeKind.Endurance => attributes.Endurance,
                ActorAttributeKind.Personality => attributes.Personality,
                ActorAttributeKind.Luck => attributes.Luck,
                _ => throw new InvalidOperationException("[VVardenfell][Magic] Unknown actor attribute kind."),
            };

        public static void SetAttribute(ref ActorAttributeSet attributes, ActorAttributeKind attribute, float value)
        {
            switch (attribute)
            {
                case ActorAttributeKind.Strength: attributes.Strength = value; break;
                case ActorAttributeKind.Intelligence: attributes.Intelligence = value; break;
                case ActorAttributeKind.Willpower: attributes.Willpower = value; break;
                case ActorAttributeKind.Agility: attributes.Agility = value; break;
                case ActorAttributeKind.Speed: attributes.Speed = value; break;
                case ActorAttributeKind.Endurance: attributes.Endurance = value; break;
                case ActorAttributeKind.Personality: attributes.Personality = value; break;
                case ActorAttributeKind.Luck: attributes.Luck = value; break;
                default: throw new InvalidOperationException("[VVardenfell][Magic] Unknown actor attribute kind.");
            }
        }

        public static void AddAttribute(ref ActorAttributeSet attributes, sbyte attribute, float value)
        {
            var kind = ToAttributeKind(attribute);
            SetAttribute(ref attributes, kind, GetAttribute(attributes, kind) + value);
        }

        public static void AddSkill(ref ActorSkillSet skills, sbyte skill, float value)
        {
            var kind = ToSkillKind(skill);
            SetSkill(ref skills, kind, PlayerSkillMutationApplySystem.GetSkill(skills, kind) + value);
        }

        static ActorAttributeKind ToAttributeKind(sbyte attribute)
        {
            if ((uint)attribute >= 8u)
                throw new InvalidOperationException("[VVardenfell][Magic] Attribute-targeted effect has invalid attribute argument.");

            return (ActorAttributeKind)(attribute + 1);
        }

        static ActorSkillKind ToSkillKind(sbyte skill)
        {
            if ((uint)skill >= 27u)
                throw new InvalidOperationException("[VVardenfell][Magic] Skill-targeted effect has invalid skill argument.");

            return (ActorSkillKind)(skill + 1);
        }

        public static void SetSkill(ref ActorSkillSet skills, ActorSkillKind skill, float value)
        {
            switch (skill)
            {
                case ActorSkillKind.Block: skills.Block = value; break;
                case ActorSkillKind.Armorer: skills.Armorer = value; break;
                case ActorSkillKind.MediumArmor: skills.MediumArmor = value; break;
                case ActorSkillKind.HeavyArmor: skills.HeavyArmor = value; break;
                case ActorSkillKind.BluntWeapon: skills.BluntWeapon = value; break;
                case ActorSkillKind.LongBlade: skills.LongBlade = value; break;
                case ActorSkillKind.Axe: skills.Axe = value; break;
                case ActorSkillKind.Spear: skills.Spear = value; break;
                case ActorSkillKind.Athletics: skills.Athletics = value; break;
                case ActorSkillKind.Enchant: skills.Enchant = value; break;
                case ActorSkillKind.Destruction: skills.Destruction = value; break;
                case ActorSkillKind.Alteration: skills.Alteration = value; break;
                case ActorSkillKind.Illusion: skills.Illusion = value; break;
                case ActorSkillKind.Conjuration: skills.Conjuration = value; break;
                case ActorSkillKind.Mysticism: skills.Mysticism = value; break;
                case ActorSkillKind.Restoration: skills.Restoration = value; break;
                case ActorSkillKind.Alchemy: skills.Alchemy = value; break;
                case ActorSkillKind.Unarmored: skills.Unarmored = value; break;
                case ActorSkillKind.Security: skills.Security = value; break;
                case ActorSkillKind.Sneak: skills.Sneak = value; break;
                case ActorSkillKind.Acrobatics: skills.Acrobatics = value; break;
                case ActorSkillKind.LightArmor: skills.LightArmor = value; break;
                case ActorSkillKind.ShortBlade: skills.ShortBlade = value; break;
                case ActorSkillKind.Marksman: skills.Marksman = value; break;
                case ActorSkillKind.Mercantile: skills.Mercantile = value; break;
                case ActorSkillKind.Speechcraft: skills.Speechcraft = value; break;
                case ActorSkillKind.HandToHand: skills.HandToHand = value; break;
                default: throw new InvalidOperationException("[VVardenfell][Magic] Unknown actor skill kind.");
            }
        }

        static bool IsZero(in ActorAttributeSet value)
            => value.Strength == 0f
               && value.Intelligence == 0f
               && value.Willpower == 0f
               && value.Agility == 0f
               && value.Speed == 0f
               && value.Endurance == 0f
               && value.Personality == 0f
               && value.Luck == 0f;

        static bool IsZero(in ActorSkillSet value)
            => value.Block == 0f
               && value.Armorer == 0f
               && value.MediumArmor == 0f
               && value.HeavyArmor == 0f
               && value.BluntWeapon == 0f
               && value.LongBlade == 0f
               && value.Axe == 0f
               && value.Spear == 0f
               && value.Athletics == 0f
               && value.Enchant == 0f
               && value.Destruction == 0f
               && value.Alteration == 0f
               && value.Illusion == 0f
               && value.Conjuration == 0f
               && value.Mysticism == 0f
               && value.Restoration == 0f
               && value.Alchemy == 0f
               && value.Unarmored == 0f
               && value.Security == 0f
               && value.Sneak == 0f
               && value.Acrobatics == 0f
               && value.LightArmor == 0f
               && value.ShortBlade == 0f
               && value.Marksman == 0f
               && value.Mercantile == 0f
               && value.Speechcraft == 0f
               && value.HandToHand == 0f;
    }
}
