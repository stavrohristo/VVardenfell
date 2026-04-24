using System.Text;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Movement
{
    public static class MorrowindActorMovementStats
    {
        public readonly struct Context
        {
            readonly RuntimeContentDatabase _contentDb;
            readonly ActorAttributeSet _attributes;
            readonly ActorSkillSet _skills;
            readonly ActorVitalSet _vitals;
            readonly ActorEffectStatModifiers _effectModifiers;
            readonly ActorDerivedMovementStats _derived;

            public Context(
                RuntimeContentDatabase contentDb,
                in ActorAttributeSet attributes,
                in ActorSkillSet skills,
                in ActorVitalSet vitals,
                in ActorEffectStatModifiers effectModifiers,
                in ActorDerivedMovementStats derived)
            {
                _contentDb = contentDb;
                _attributes = attributes;
                _skills = skills;
                _vitals = vitals;
                _effectModifiers = effectModifiers;
                _derived = derived;
            }

            public float GetCurrentSpeed(bool running, bool sneaking, bool inAir, float speedFactor, bool strafing)
            {
                float speed = running && !sneaking
                    ? _derived.RunSpeed
                    : (sneaking ? _derived.SneakWalkSpeed : _derived.WalkSpeed);

                speed *= math.saturate(speedFactor);
                if (strafing)
                    speed *= 0.75f;

                return speed * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpSpeed(bool running)
            {
                if (running)
                    return _derived.JumpSpeed * Gmst("fJumpRunMultiplier", 1f) * WorldScale.MwUnitsToMeters;

                return _derived.JumpSpeed * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpMoveFactor() => _derived.JumpMoveFactor;

            public float GetMovementFatigueLossPerSecond(bool running, bool sneaking, float speedFactor)
            {
                if (speedFactor <= 0f || _derived.Encumbrance > _derived.CarryCapacity)
                    return 0f;

                float fatigueLoss = 0f;
                if (sneaking)
                {
                    fatigueLoss = Gmst("fFatigueSneakBase", 0f)
                        + _derived.NormalizedEncumbrance * Gmst("fFatigueSneakMult", 0f);
                }
                else if (running)
                {
                    fatigueLoss = Gmst("fFatigueRunBase", 0f)
                        + _derived.NormalizedEncumbrance * Gmst("fFatigueRunMult", 0f);
                }

                return fatigueLoss * math.saturate(speedFactor);
            }

            public float GetJumpFatigueLoss()
            {
                float normalizedEncumbrance = math.min(1f, _derived.NormalizedEncumbrance);
                return Gmst("fFatigueJumpBase", 0f)
                    + normalizedEncumbrance * Gmst("fFatigueJumpMult", 0f);
            }

            public float GetFatigueRestorePerSecond()
            {
                if (_vitals.CurrentFatigue >= _vitals.ModifiedFatigueBase)
                    return 0f;

                float normalizedEncumbrance = _derived.NormalizedEncumbrance;
                if (normalizedEncumbrance > 1f)
                    normalizedEncumbrance = 1f;

                return (Gmst("fFatigueReturnBase", 0f) + Gmst("fFatigueReturnMult", 0f) * (1f - normalizedEncumbrance))
                    * (Gmst("fEndFatigueMult", 1f) * _attributes.Endurance);
            }

            float Gmst(string id, float fallback)
            {
                if (_contentDb != null && _contentDb.TryGetGameSettingFloat(id, out float value))
                    return value;
                return fallback;
            }
        }

        public static ActorRuntimeStatSeed CreateDefaultPlayerSeed()
        {
            var attributes = new ActorAttributeSet
            {
                Strength = 40f,
                Intelligence = 40f,
                Willpower = 40f,
                Agility = 40f,
                Speed = 40f,
                Endurance = 40f,
                Personality = 40f,
                Luck = 40f,
            };

            var vitals = new ActorVitalSet();
            ApplyVitalBases(null, attributes, ref vitals, initializeMissingCurrents: true);
            return new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = new ActorSkillSet
                {
                    Block = 30f,
                    Armorer = 30f,
                    MediumArmor = 30f,
                    HeavyArmor = 30f,
                    BluntWeapon = 30f,
                    LongBlade = 30f,
                    Axe = 30f,
                    Spear = 30f,
                    Athletics = 30f,
                    Enchant = 30f,
                    Destruction = 30f,
                    Alteration = 30f,
                    Illusion = 30f,
                    Conjuration = 30f,
                    Mysticism = 30f,
                    Restoration = 30f,
                    Alchemy = 30f,
                    Unarmored = 30f,
                    Security = 30f,
                    Sneak = 30f,
                    Acrobatics = 30f,
                    LightArmor = 30f,
                    ShortBlade = 30f,
                    Marksman = 30f,
                    Mercantile = 30f,
                    Speechcraft = 30f,
                    HandToHand = 30f,
                },
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers
                {
                    JumpMagnitude = 0f,
                    FeatherMagnitude = 0f,
                    BurdenMagnitude = 0f,
                },
            };
        }

        public static ActorDerivedMovementStats BuildDerived(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            float inventoryWeight)
        {
            var derived = new ActorDerivedMovementStats
            {
                CarryCapacity = ComputeCarryCapacity(contentDb, attributes),
                Encumbrance = ComputeEncumbrance(effectModifiers, inventoryWeight),
            };
            derived.NormalizedEncumbrance = ComputeNormalizedEncumbrance(derived.Encumbrance, derived.CarryCapacity);
            ApplyMovementDerived(contentDb, attributes, skills, vitals, effectModifiers, ref derived);
            return derived;
        }

        public static float ComputeModifiedFatigueBase(in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength + attributes.Willpower + attributes.Agility + attributes.Endurance);

        public static float ComputeModifiedHealthBase(in ActorAttributeSet attributes)
            => math.max(1f, (attributes.Strength + attributes.Endurance) * 0.5f);

        public static float ComputeModifiedMagickaBase(RuntimeContentDatabase contentDb, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Intelligence * Gmst(contentDb, "fPCbaseMagickaMult", 1f));

        public static void ApplyVitalBases(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            ref ActorVitalSet vitals,
            bool initializeMissingCurrents)
        {
            vitals.ModifiedHealthBase = ComputeModifiedHealthBase(attributes);
            vitals.ModifiedMagickaBase = ComputeModifiedMagickaBase(contentDb, attributes);
            vitals.ModifiedFatigueBase = ComputeModifiedFatigueBase(attributes);

            if (initializeMissingCurrents)
            {
                if (vitals.CurrentHealth <= 0f)
                    vitals.CurrentHealth = vitals.ModifiedHealthBase;
                if (vitals.CurrentMagicka <= 0f)
                    vitals.CurrentMagicka = vitals.ModifiedMagickaBase;
                if (vitals.CurrentFatigue <= 0f)
                    vitals.CurrentFatigue = vitals.ModifiedFatigueBase;
            }

            vitals.CurrentHealth = ClampVital(vitals.CurrentHealth, vitals.ModifiedHealthBase);
            vitals.CurrentMagicka = ClampVital(vitals.CurrentMagicka, vitals.ModifiedMagickaBase);
            vitals.CurrentFatigue = ClampVital(vitals.CurrentFatigue, vitals.ModifiedFatigueBase);
        }

        public static float ComputeCarryCapacity(RuntimeContentDatabase contentDb, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength * Gmst(contentDb, "fEncumbranceStrMult", 5f));

        public static float ComputeEncumbrance(in ActorEffectStatModifiers effectModifiers, float inventoryWeight)
            => math.max(0f, inventoryWeight - effectModifiers.FeatherMagnitude + effectModifiers.BurdenMagnitude);

        public static float ComputeNormalizedEncumbrance(float encumbrance, float carryCapacity)
        {
            if (encumbrance == 0f)
                return 0f;
            if (carryCapacity == 0f)
                return 1f;
            return encumbrance / carryCapacity;
        }

        public static void ApplyMovementDerived(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            ref ActorDerivedMovementStats derived)
        {
            float modifiedFatigueBase = math.max(0f, vitals.ModifiedFatigueBase);
            float normalizedFatigue = modifiedFatigueBase <= 0f
                ? 1f
                : math.max(0f, vitals.CurrentFatigue / modifiedFatigueBase);
            derived.FatigueTerm = Gmst(contentDb, "fFatigueBase", 1f)
                - Gmst(contentDb, "fFatigueMult", 1f) * (1f - normalizedFatigue);
            float walkSpeed = Gmst(contentDb, "fMinWalkSpeed", 100f)
                + 0.01f * attributes.Speed * (Gmst(contentDb, "fMaxWalkSpeed", 200f) - Gmst(contentDb, "fMinWalkSpeed", 100f));
            walkSpeed *= 1f - Gmst(contentDb, "fEncumberedMoveEffect", 0.5f) * derived.NormalizedEncumbrance;
            walkSpeed = math.max(0f, walkSpeed);

            float runSpeed = walkSpeed
                * (0.01f * skills.Athletics * Gmst(contentDb, "fAthleticsRunBonus", 1f)
                    + Gmst(contentDb, "fBaseRunMultiplier", 1.75f));
            float sneakWalkSpeed = walkSpeed * Gmst(contentDb, "fSneakSpeedMultiplier", 0.5f);

            float a = skills.Acrobatics;
            float b = 0f;
            if (a > 50f)
            {
                b = a - 50f;
                a = 50f;
            }

            float jump = Gmst(contentDb, "fJumpAcrobaticsBase", 128f)
                + math.pow(a / 15f, Gmst(contentDb, "fJumpAcroMultiplier", 1f));
            jump += 3f * b * Gmst(contentDb, "fJumpAcroMultiplier", 1f);
            jump += effectModifiers.JumpMagnitude * 64f;
            jump *= Gmst(contentDb, "fJumpEncumbranceBase", 0f)
                + Gmst(contentDb, "fJumpEncumbranceMultiplier", 1f) * (1f - derived.NormalizedEncumbrance);
            jump *= derived.FatigueTerm;
            jump += 8.96f * (1f / WorldScale.MwUnitsToMeters);
            jump /= 3f;

            float jumpMoveFactor = math.min(1f,
                Gmst(contentDb, "fJumpMoveBase", 0f)
                + Gmst(contentDb, "fJumpMoveMult", 1f) * skills.Acrobatics / 100f);

            derived.WalkSpeed = walkSpeed;
            derived.RunSpeed = runSpeed;
            derived.SneakWalkSpeed = sneakWalkSpeed;
            derived.JumpSpeed = jump;
            derived.JumpMoveFactor = jumpMoveFactor;
        }

        public static Context Build(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            return new Context(contentDb, attributes, skills, vitals, effectModifiers, derived);
        }

        public static string DescribeRuntimeState(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            var context = Build(contentDb, attributes, skills, vitals, effectModifiers, derived);
            var builder = new StringBuilder(384);
            builder.Append("stats str=");
            builder.Append(attributes.Strength.ToString("F0"));
            builder.Append(" wil=");
            builder.Append(attributes.Willpower.ToString("F0"));
            builder.Append(" agi=");
            builder.Append(attributes.Agility.ToString("F0"));
            builder.Append(" end=");
            builder.Append(attributes.Endurance.ToString("F0"));
            builder.Append(" spd=");
            builder.Append(attributes.Speed.ToString("F0"));
            builder.Append(" ath=");
            builder.Append(skills.Athletics.ToString("F0"));
            builder.Append(" acro=");
            builder.Append(skills.Acrobatics.ToString("F0"));
            builder.Append(" health=");
            builder.Append(vitals.CurrentHealth.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedHealthBase.ToString("F1"));
            builder.Append(" magicka=");
            builder.Append(vitals.CurrentMagicka.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedMagickaBase.ToString("F1"));
            builder.Append(" fatigue=");
            builder.Append(vitals.CurrentFatigue.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedFatigueBase.ToString("F1"));
            builder.Append(" fatigueTerm=");
            builder.Append(derived.FatigueTerm.ToString("F2"));
            builder.Append(" enc=");
            builder.Append(derived.Encumbrance.ToString("F1"));
            builder.Append("/");
            builder.Append(derived.CarryCapacity.ToString("F1"));
            builder.Append(" normEnc=");
            builder.Append(derived.NormalizedEncumbrance.ToString("F2"));
            builder.Append(" walk=");
            builder.Append(context.GetCurrentSpeed(false, false, true, 1f, false).ToString("F2"));
            builder.Append(" run=");
            builder.Append(context.GetCurrentSpeed(true, false, true, 1f, false).ToString("F2"));
            builder.Append(" sneak=");
            builder.Append(context.GetCurrentSpeed(false, true, true, 1f, false).ToString("F2"));
            builder.Append(" jump=");
            builder.Append(context.GetJumpSpeed(true).ToString("F2"));
            builder.Append(" jumpMove=");
            builder.Append(context.GetJumpMoveFactor().ToString("F2"));
            builder.Append(" gmst=");
            builder.Append(contentDb != null ? "runtime" : "fallback");
            return builder.ToString();
        }

        static float Gmst(RuntimeContentDatabase contentDb, string id, float fallback)
        {
            if (contentDb != null && contentDb.TryGetGameSettingFloat(id, out float value))
                return value;
            return fallback;
        }

        static float ClampVital(float current, float max)
        {
            if (max <= 0f)
                return 0f;

            if (float.IsNaN(current) || float.IsInfinity(current))
                return max;

            return math.clamp(current, 0f, max);
        }
    }

    public static class MorrowindPlayerSpeedResolver
    {
        public static MorrowindActorMovementStats.Context Build(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => MorrowindActorMovementStats.Build(contentDb, attributes, skills, vitals, effectModifiers, derived);

        public static string DescribeRuntimeState(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => MorrowindActorMovementStats.DescribeRuntimeState(contentDb, attributes, skills, vitals, effectModifiers, derived);
    }
}
