using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Magic
{
    static class MorrowindSpellCostUtility
    {
        public const int SpellTypeSpell = 0;
        public const int SpellTypePower = 5;
        public const int SpellFlagAutocalc = 1;
        public const int SpellFlagAlways = 4;
        public const int MagicEffectFlagNoDuration = 0x4;
        public const int MagicEffectFlagNoMagnitude = 0x8;
        public const int MagicEffectFlagHarmful = 0x10;
        public const int MagicEffectFlagAppliedOnce = 0x1000;
        public const int MagicEffectFlagNonRecastable = 0x4000;
        public const int MagicEffectFlagUnreflectable = 0x10000;

        public static int CalculateSpellCost(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            if ((spell.Flags & SpellFlagAutocalc) == 0)
                return math.max(0, spell.Cost);

            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
            float costMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fEffectCostMult);
            float total = 0f;
            ulong spellContentId = spell.ContentId.Value;
            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref RequireMagicEffect(ref content, instance.EffectId, spellContentId);
                float duration = (effect.Flags & MagicEffectFlagNoDuration) != 0 ? 1f : math.max(1f, instance.Duration);
                if ((effect.Flags & MagicEffectFlagAppliedOnce) != 0)
                    duration = math.max(0f, instance.Duration);
                float magnitude = (effect.Flags & MagicEffectFlagNoMagnitude) != 0
                    ? 1f
                    : 0.5f * (math.max(0, instance.MagnitudeMin) + math.max(0, instance.MagnitudeMax));
                float area = math.max(0, instance.Area);
                float effectCost = ((magnitude * duration) + area) * effect.BaseCost * costMult * 0.05f;
                if (instance.Range == MorrowindMagicRange.Target)
                    effectCost *= 1.5f;
                total += effectCost;
            }

            return (int)math.round(total);
        }

        public static float CalculateSuccessChance(
            ref RuntimeContentBlob content,
            ref RuntimeSpellDefBlob spell,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorDerivedMovementStats derived,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            bool checkMagicka,
            out ActorSkillKind effectiveSchool)
        {
            effectiveSchool = ActorSkillKind.None;
            if (spell.SpellType == SpellTypePower)
                return 100f;
            if (spell.SpellType != SpellTypeSpell)
                return 100f;
            if ((spell.Flags & SpellFlagAlways) != 0)
                return 100f;
            if (MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(activeEffects, MorrowindMagicEffectIds.Silence) > 0f)
                return 0f;

            int spellCost = CalculateSpellCost(ref content, ref spell);
            if (checkMagicka && spellCost > 0 && vitals.CurrentMagicka < spellCost)
                return 0f;

            float baseChance = CalculateBaseChance(ref content, ref spell, attributes, skills, out effectiveSchool);
            float soundPenalty = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(activeEffects, MorrowindMagicEffectIds.Sound);
            return (baseChance - soundPenalty) * derived.FatigueTerm;
        }

        static float CalculateBaseChance(
            ref RuntimeContentBlob content,
            ref RuntimeSpellDefBlob spell,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            out ActorSkillKind effectiveSchool)
        {
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
            ulong spellContentId = spell.ContentId.Value;
            float costMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fEffectCostMult);
            float lowest = float.PositiveInfinity;
            effectiveSchool = ActorSkillKind.None;
            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref RequireMagicEffect(ref content, instance.EffectId, spellContentId);
                float duration = instance.Duration;
                if ((effect.Flags & MagicEffectFlagAppliedOnce) == 0)
                    duration = math.max(1f, duration);
                float x = duration;
                x *= 0.1f * effect.BaseCost;
                x *= 0.5f * (instance.MagnitudeMin + instance.MagnitudeMax);
                x += instance.Area * 0.05f * effect.BaseCost;
                if (instance.Range == MorrowindMagicRange.Target)
                    x *= 1.5f;
                x *= costMult;

                ActorSkillKind school = ResolveSchool(effect.School);
                float skillValue = PlayerSkillMutationApplySystem.GetSkill(skills, school);
                float chance = (2f * skillValue) - x;
                if (chance < lowest)
                {
                    lowest = chance;
                    effectiveSchool = school;
                }
            }

            if (!math.isfinite(lowest))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spellContentId:X16} produced no effective school.");

            return lowest - CalculateSpellCost(ref content, ref spell) + (0.2f * attributes.Willpower) + (0.1f * attributes.Luck);
        }

        public static ActorSkillKind ResolveSchool(int school)
            => school switch
            {
                0 => ActorSkillKind.Alteration,
                1 => ActorSkillKind.Conjuration,
                2 => ActorSkillKind.Destruction,
                3 => ActorSkillKind.Illusion,
                4 => ActorSkillKind.Mysticism,
                5 => ActorSkillKind.Restoration,
                _ => throw new InvalidOperationException($"[VVardenfell][Magic] Unknown magic school {school}."),
            };

        public static ref RuntimeMagicEffectDefBlob RequireMagicEffect(ref RuntimeContentBlob content, short effectId, ulong spellContentId)
        {
            if (!RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref content, effectId, out MagicEffectDefHandle handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spellContentId:X16} references missing magic effect {effectId}.");
            return ref RuntimeContentBlobUtility.Get(ref content, handle);
        }
    }

    static class MorrowindMagicRange
    {
        public const int Self = 0;
        public const int Touch = 1;
        public const int Target = 2;
    }
}
