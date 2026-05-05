using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Combat
{
    static partial class MorrowindMeleeCombatMechanics
    {
        const uint WeaponFlagMagical = 0x01u;
        const uint WeaponFlagSilver = 0x02u;

        static readonly short SanctuaryEffectId = RequireEffectId("sEffectSanctuary");
        static readonly short ChameleonEffectId = RequireEffectId("sEffectChameleon");
        static readonly short InvisibilityEffectId = RequireEffectId("sEffectInvisibility");
        static readonly short BlindEffectId = RequireEffectId("sEffectBlind");
        static readonly short FortifyAttackEffectId = RequireEffectId("sEffectFortifyAttackBonus");
        static readonly short ResistNormalWeaponsEffectId = RequireEffectId("sEffectResistNormalWeapons");
        static readonly short WeaknessToNormalWeaponsEffectId = RequireEffectId("sEffectWeaknessToNormalWeapons");

        public static void ResolveWeaponEquipment(
            ref RuntimeContentBlob content,
            in ContentReference weaponContent,
            out bool hasWeapon,
            out ItemDefHandle weaponHandle,
            out ItemEquipmentDef weapon,
            out bool weaponHasEnchantment)
        {
            hasWeapon = false;
            weaponHandle = default;
            weapon = default;
            weaponHasEnchantment = false;

            if (!weaponContent.IsValid)
                return;

            if (weaponContent.Kind != ContentReferenceKind.Item)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee hit weapon content kind {weaponContent.Kind} is not an item.");

            weaponHandle = new ItemDefHandle { Value = weaponContent.HandleValue };
            if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref content, weaponHandle, out weapon) || weapon.Kind != ItemEquipmentKind.Weapon)
                throw new InvalidOperationException($"[VVardenfell][Combat] Equipped weapon handle {weaponHandle.Value} does not resolve to WEAP equipment.");

            ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref content, weaponHandle);
            weaponHasEnchantment = item.EnchantIdHash != 0UL;
            hasWeapon = true;
        }

        public static float ComputeMeleeReach(ref RuntimeContentBlob content, bool hasWeapon, in ItemEquipmentDef weapon)
        {
            float combatDistance = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatDistance);
            if (hasWeapon)
                return combatDistance * math.max(0f, weapon.WeaponReach) * WorldScale.MwUnitsToMeters;

            return combatDistance * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fHandToHandReach) * WorldScale.MwUnitsToMeters;
        }

        public static float ComputeFatigueTerm(ref RuntimeContentBlob content, in ActorVitalSet vitals)
        {
            float max = vitals.ModifiedFatigueBase;
            float current = vitals.CurrentFatigue;
            float normalized = math.floor(max) == 0f ? 1f : math.max(0f, current / max);
            return RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueBase)
                   - RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueMult) * (1f - normalized);
        }

        public static float ComputeHitChance(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attackerAttributes,
            in ActorSkillSet attackerSkills,
            in ActorVitalSet attackerVitals,
            DynamicBuffer<ActorActiveMagicEffect> attackerEffects,
            bool hasWeapon,
            in ItemEquipmentDef weapon,
            in ActorAttributeSet targetAttributes,
            in ActorVitalSet targetVitals,
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            bool targetInCombat)
        {
            float defenseTerm = 0f;
            if (targetVitals.CurrentFatigue >= 0f)
            {
                if (targetInCombat)
                    defenseTerm = ComputeEvasion(ref content, targetAttributes, targetVitals, targetEffects);

                float invisibilityMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatInvisoMult);
                defenseTerm += math.min(100f, invisibilityMult * SumEffectMagnitude(targetEffects, ChameleonEffectId));
                defenseTerm += math.min(100f, invisibilityMult * SumEffectMagnitude(targetEffects, InvisibilityEffectId));
            }

            float skill = ResolveWeaponSkill(attackerSkills, hasWeapon ? weapon.Type : -1);
            float attackTerm = skill
                               + attackerAttributes.Agility / 5f
                               + attackerAttributes.Luck / 10f;
            attackTerm *= ComputeFatigueTerm(ref content, attackerVitals);
            attackTerm += SumEffectMagnitude(attackerEffects, FortifyAttackEffectId)
                          - SumEffectMagnitude(attackerEffects, BlindEffectId);

            return math.round(attackTerm - defenseTerm);
        }

        public static float ComputeAttackFatigueLoss(
            ref RuntimeContentBlob content,
            in ActorDerivedMovementStats derived,
            bool hasWeapon,
            in ItemEquipmentDef weapon,
            float attackStrength)
        {
            float fatigueLoss = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueAttackBase)
                                + derived.NormalizedEncumbrance * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueAttackMult);
            if (hasWeapon)
                fatigueLoss += weapon.Weight * attackStrength * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fWeaponFatigueMult);

            return fatigueLoss;
        }

        public static float ComputeWeaponDamage(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attackerAttributes,
            in ItemEquipmentDef weapon,
            ActorWeaponAttackType attackType,
            bool fullDamage)
        {
            GetWeaponAttackRange(weapon, attackType, out int min, out int max);
            float damage = fullDamage ? max : min;
            damage *= RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDamageStrengthBase)
                      + attackerAttributes.Strength * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDamageStrengthMult) * 0.1f;
            return damage;
        }

        public static float ComputeHandToHandDamage(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attackerAttributes,
            in ActorSkillSet attackerSkills,
            bool fullDamage,
            bool healthDamage)
        {
            float minStrike = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fMinHandToHandMult);
            float maxStrike = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fMaxHandToHandMult);
            float damage = attackerSkills.HandToHand * (fullDamage ? maxStrike : minStrike);
            if (healthDamage)
                damage *= RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fHandtoHandHealthPer);
            return damage;
        }

        public static float ApplyNormalWeaponResistance(
            ref RuntimeContentBlob content,
            in ItemEquipmentDef weapon,
            bool hasEnchantment,
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            float damage)
        {
            if (!IsNormalWeaponDamage(weapon, hasEnchantment))
                return damage;

            return ApplyNormalWeaponResistanceEffects(ref content, targetEffects, damage);
        }

        public static float ApplyNormalWeaponResistanceEffects(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            float damage)
        {
            float resistance = SumEffectMagnitude(targetEffects, ResistNormalWeaponsEffectId) / 100f;
            float weakness = SumEffectMagnitude(targetEffects, WeaknessToNormalWeaponsEffectId) / 100f;
            return damage * (1f - math.min(1f, resistance - weakness));
        }

        public static bool IsFullDamageHit(uint roll0To99, float hitChance)
            => roll0To99 < hitChance;

        public static float ResolveWeaponSkill(in ActorSkillSet skills, int weaponType)
        {
            return weaponType switch
            {
                -1 => skills.HandToHand,
                0 => skills.ShortBlade,
                1 => skills.LongBlade,
                2 => skills.LongBlade,
                3 => skills.BluntWeapon,
                4 => skills.BluntWeapon,
                5 => skills.Spear,
                6 => skills.Axe,
                7 => skills.BluntWeapon,
                8 => skills.BluntWeapon,
                _ => throw new InvalidOperationException($"[VVardenfell][Combat] Unsupported melee weapon type {weaponType}."),
            };
        }

        public static float SumEffectMagnitude(DynamicBuffer<ActorActiveMagicEffect> effects, short effectId)
        {
            float total = 0f;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.EffectId != effectId || effect.Applied == 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;

                total += math.max(0f, effect.Magnitude);
            }

            return total;
        }

        static float ComputeEvasion(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorVitalSet vitals,
            DynamicBuffer<ActorActiveMagicEffect> effects)
        {
            float evasion = attributes.Agility / 5f + attributes.Luck / 10f;
            evasion *= ComputeFatigueTerm(ref content, vitals);
            evasion += math.min(100f, SumEffectMagnitude(effects, SanctuaryEffectId));
            return evasion;
        }

        static void GetWeaponAttackRange(in ItemEquipmentDef weapon, ActorWeaponAttackType attackType, out int min, out int max)
        {
            switch (attackType)
            {
                case ActorWeaponAttackType.Chop:
                    min = weapon.ChopMin;
                    max = weapon.ChopMax;
                    return;
                case ActorWeaponAttackType.Slash:
                    min = weapon.SlashMin;
                    max = weapon.SlashMax;
                    return;
                case ActorWeaponAttackType.Thrust:
                    min = weapon.ThrustMin;
                    max = weapon.ThrustMax;
                    return;
                default:
                    throw new InvalidOperationException($"[VVardenfell][Combat] Unknown attack type {attackType}.");
            }
        }

        public static bool IsNormalWeaponDamage(in ItemEquipmentDef weapon, bool hasEnchantment)
            => (weapon.WeaponFlags & WeaponFlagSilver) == 0
               && (weapon.WeaponFlags & WeaponFlagMagical) == 0
               && !hasEnchantment;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Combat] Unknown magic effect GMST id '{gmstId}'.");

            return effectId;
        }
    }
}




