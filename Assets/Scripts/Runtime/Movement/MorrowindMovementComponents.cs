using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Movement
{
    public enum MorrowindSupportKind : byte
    {
        None = 0,
        FlatGround = 1,
        WalkableSlope = 2,
        ActorTop = 3,
        WaterSurfaceCandidate = 4,
        RecoveryFlat = 5,
    }

    public enum MorrowindSupportSnapMode : byte
    {
        None = 0,
        Offset = 1,
        Settle = 2,
    }

    public struct MorrowindMovementIntent : IComponentData
    {
        public float3 LocalMove;
        public float2 LookDeltaDegrees;
        public bool RunHeld;
        public bool SneakHeld;
        public bool JumpHeld;
        public bool InteractPressed;
        public float SpeedFactor;
        public bool IsStrafing;
    }

    public struct MorrowindActorKinematicState : IComponentData
    {
        public float3 Inertia;
        public bool Grounded;
        public bool OnSlope;
        public bool WalkingOnWater;
        public Entity StandingOn;
        public uint StuckFrames;
        public float3 LastStuckPosition;
    }

    public struct MorrowindMovementTuning : IComponentData
    {
        public float StepSizeUp;
        public float StepSizeDown;
        public float GroundOffset;
        public float CollisionMargin;
        public float MaxSlopeCosine;
        public float Gravity;
        public int MaxIterations;

        public static MorrowindMovementTuning OpenMwDefaults()
        {
            return new MorrowindMovementTuning
            {
                StepSizeUp = 34f * WorldScale.MwUnitsToMeters,
                StepSizeDown = 62f * WorldScale.MwUnitsToMeters,
                GroundOffset = 1f * WorldScale.MwUnitsToMeters,
                CollisionMargin = 0.2f * WorldScale.MwUnitsToMeters,
                MaxSlopeCosine = math.cos(math.radians(46f)),
                Gravity = 8.96f,
                MaxIterations = 8,
            };
        }
    }

    public struct MorrowindMovementFrameTrace : IComponentData
    {
        public uint Sequence;
        public float DeltaTime;
        public float3 StartPosition;
        public float3 EndPosition;
        public float3 DesiredVelocity;
        public float3 FinalVelocity;
        public float ResolvedSpeed;
        public float3 GroundNormal;
        public Entity StandingOn;
        public Entity LastBlocker;
        public float3 LastBlockerNormal;
        public float LastBlockerFraction;
        public int SweepIterations;
        public int SlideCount;
        public byte PreviousSupportKind;
        public byte SupportKind;
        public byte SupportSnapMode;
        public byte SupportRejectedSteep;
        public byte LandingConsumedInertia;
        public byte GroundProbeSnapped;
        public byte StepAttempted;
        public byte StepAttemptIndex;
        public byte StepSucceeded;
        public byte SteepSlopeRejected;
        public byte UsedSeamLogic;
        public byte UsedGroundedWallNormalFlatten;
        public byte JumpRequested;
        public byte JumpAccepted;
    }

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
            float fatigueBase = 40f + 40f + 40f + 40f;
            return new ActorRuntimeStatSeed
            {
                Attributes = new ActorAttributeSet
                {
                    Strength = 40f,
                    Intelligence = 40f,
                    Willpower = 40f,
                    Agility = 40f,
                    Speed = 40f,
                    Endurance = 40f,
                    Personality = 40f,
                    Luck = 40f,
                },
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
                Vitals = new ActorVitalSet
                {
                    CurrentFatigue = fatigueBase,
                    ModifiedFatigueBase = fatigueBase,
                },
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
