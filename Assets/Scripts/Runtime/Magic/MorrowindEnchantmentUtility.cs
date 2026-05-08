using System;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Magic
{
    static class MorrowindEnchantmentUtility
    {
        public const int CastOnce = 0;
        public const int WhenStrikes = 1;
        public const int WhenUsed = 2;
        public const int ConstantEffect = 3;
        public const int EnchantmentFlagAutocalc = 1;

        public static bool IsUsableMagicItemType(int type)
            => type == CastOnce || type == WhenUsed;

        public static int CalculateCastCost(ref RuntimeContentBlob content, ref RuntimeEnchantmentDefBlob enchantment, in ActorSkillSet skills)
        {
            float baseCost = CalculateBaseCastCost(ref content, ref enchantment);
            float result = baseCost - ((baseCost / 100f) * (skills.Enchant - 10f));
            return (int)math.max(1f, result);
        }

        public static int CalculateCharge(ref RuntimeContentBlob content, ref RuntimeEnchantmentDefBlob enchantment)
        {
            if ((enchantment.Flags & EnchantmentFlagAutocalc) == 0)
                return math.max(0, enchantment.Charge);

            int charge = (int)math.round(CalculateAutocalcCost(ref content, enchantment.EffectStartIndex, enchantment.EffectCount, enchantment.ContentId.Value));
            return enchantment.EnchantmentType switch
            {
                CastOnce => charge * RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iMagicItemChargeOnce),
                WhenStrikes => charge * RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iMagicItemChargeStrike),
                WhenUsed => charge * RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iMagicItemChargeUse),
                ConstantEffect => charge * RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iMagicItemChargeConst),
                _ => throw new InvalidOperationException($"[VVardenfell][Magic] Unknown enchantment type {enchantment.EnchantmentType}."),
            };
        }

        public static float ResolveCurrentCharge(ref RuntimeContentBlob content, ref RuntimeEnchantmentDefBlob enchantment, float savedCharge)
            => savedCharge < 0f ? CalculateCharge(ref content, ref enchantment) : savedCharge;

        public static float NormalizeInitialCharge(float charge)
            => charge == 0f ? -1f : charge;

        static float CalculateBaseCastCost(ref RuntimeContentBlob content, ref RuntimeEnchantmentDefBlob enchantment)
        {
            if ((enchantment.Flags & EnchantmentFlagAutocalc) == 0)
                return math.max(0, enchantment.Cost);

            return CalculateAutocalcCost(ref content, enchantment.EffectStartIndex, enchantment.EffectCount, enchantment.ContentId.Value);
        }

        static float CalculateAutocalcCost(ref RuntimeContentBlob content, int effectStartIndex, int effectCount, ulong sourceContentId)
        {
            if (effectStartIndex < 0 || effectCount <= 0)
                throw new InvalidOperationException($"[VVardenfell][Magic] Enchantment contentId=0x{sourceContentId:X16} has no effects.");

            float costMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fEffectCostMult);
            float total = 0f;
            for (int i = 0; i < effectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[effectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref MorrowindSpellCostUtility.RequireMagicEffect(ref content, instance.EffectId, sourceContentId);
                float duration = (effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoDuration) != 0 ? 1f : math.max(1f, instance.Duration);
                if ((effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagAppliedOnce) != 0)
                    duration = math.max(0f, instance.Duration);
                float magnitude = (effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoMagnitude) != 0
                    ? 1f
                    : 0.5f * (math.max(0, instance.MagnitudeMin) + math.max(0, instance.MagnitudeMax));
                float area = math.max(0, instance.Area);
                float effectCost = ((magnitude * duration) + area) * effect.BaseCost * costMult * 0.05f;
                if (instance.Range == MorrowindMagicRange.Target)
                    effectCost *= 1.5f;
                total += effectCost;
            }

            return math.round(total);
        }
    }
}
