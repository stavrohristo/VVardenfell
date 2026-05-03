using System;

namespace VVardenfell.Core
{
    public static class MorrowindMagicEffectTextUtility
    {
        static readonly string[] s_GmstEffectIds =
        {
            "sEffectWaterBreathing",
            "sEffectSwiftSwim",
            "sEffectWaterWalking",
            "sEffectShield",
            "sEffectFireShield",
            "sEffectLightningShield",
            "sEffectFrostShield",
            "sEffectBurden",
            "sEffectFeather",
            "sEffectJump",
            "sEffectLevitate",
            "sEffectSlowFall",
            "sEffectLock",
            "sEffectOpen",
            "sEffectFireDamage",
            "sEffectShockDamage",
            "sEffectFrostDamage",
            "sEffectDrainAttribute",
            "sEffectDrainHealth",
            "sEffectDrainSpellpoints",
            "sEffectDrainFatigue",
            "sEffectDrainSkill",
            "sEffectDamageAttribute",
            "sEffectDamageHealth",
            "sEffectDamageMagicka",
            "sEffectDamageFatigue",
            "sEffectDamageSkill",
            "sEffectPoison",
            "sEffectWeaknessToFire",
            "sEffectWeaknessToFrost",
            "sEffectWeaknessToShock",
            "sEffectWeaknessToMagicka",
            "sEffectWeaknessToCommonDisease",
            "sEffectWeaknessToBlightDisease",
            "sEffectWeaknessToCorprusDisease",
            "sEffectWeaknessToPoison",
            "sEffectWeaknessToNormalWeapons",
            "sEffectDisintegrateWeapon",
            "sEffectDisintegrateArmor",
            "sEffectInvisibility",
            "sEffectChameleon",
            "sEffectLight",
            "sEffectSanctuary",
            "sEffectNightEye",
            "sEffectCharm",
            "sEffectParalyze",
            "sEffectSilence",
            "sEffectBlind",
            "sEffectSound",
            "sEffectCalmHumanoid",
            "sEffectCalmCreature",
            "sEffectFrenzyHumanoid",
            "sEffectFrenzyCreature",
            "sEffectDemoralizeHumanoid",
            "sEffectDemoralizeCreature",
            "sEffectRallyHumanoid",
            "sEffectRallyCreature",
            "sEffectDispel",
            "sEffectSoultrap",
            "sEffectTelekinesis",
            "sEffectMark",
            "sEffectRecall",
            "sEffectDivineIntervention",
            "sEffectAlmsiviIntervention",
            "sEffectDetectAnimal",
            "sEffectDetectEnchantment",
            "sEffectDetectKey",
            "sEffectSpellAbsorption",
            "sEffectReflect",
            "sEffectCureCommonDisease",
            "sEffectCureBlightDisease",
            "sEffectCureCorprusDisease",
            "sEffectCurePoison",
            "sEffectCureParalyzation",
            "sEffectRestoreAttribute",
            "sEffectRestoreHealth",
            "sEffectRestoreSpellPoints",
            "sEffectRestoreFatigue",
            "sEffectRestoreSkill",
            "sEffectFortifyAttribute",
            "sEffectFortifyHealth",
            "sEffectFortifySpellpoints",
            "sEffectFortifyFatigue",
            "sEffectFortifySkill",
            "sEffectFortifyMagickaMultiplier",
            "sEffectAbsorbAttribute",
            "sEffectAbsorbHealth",
            "sEffectAbsorbSpellPoints",
            "sEffectAbsorbFatigue",
            "sEffectAbsorbSkill",
            "sEffectResistFire",
            "sEffectResistFrost",
            "sEffectResistShock",
            "sEffectResistMagicka",
            "sEffectResistCommonDisease",
            "sEffectResistBlightDisease",
            "sEffectResistCorprusDisease",
            "sEffectResistPoison",
            "sEffectResistNormalWeapons",
            "sEffectResistParalysis",
            "sEffectRemoveCurse",
            "sEffectTurnUndead",
            "sEffectSummonScamp",
            "sEffectSummonClannfear",
            "sEffectSummonDaedroth",
            "sEffectSummonDremora",
            "sEffectSummonAncestralGhost",
            "sEffectSummonSkeletalMinion",
            "sEffectSummonLeastBonewalker",
            "sEffectSummonGreaterBonewalker",
            "sEffectSummonBonelord",
            "sEffectSummonWingedTwilight",
            "sEffectSummonHunger",
            "sEffectSummonGoldensaint",
            "sEffectSummonFlameAtronach",
            "sEffectSummonFrostAtronach",
            "sEffectSummonStormAtronach",
            "sEffectFortifyAttackBonus",
            "sEffectCommandCreatures",
            "sEffectCommandHumanoids",
            "sEffectBoundDagger",
            "sEffectBoundLongsword",
            "sEffectBoundMace",
            "sEffectBoundBattleAxe",
            "sEffectBoundSpear",
            "sEffectBoundLongbow",
            "sEffectExtraSpell",
            "sEffectBoundCuirass",
            "sEffectBoundHelm",
            "sEffectBoundBoots",
            "sEffectBoundShield",
            "sEffectBoundGloves",
            "sEffectCorpus",
            "sEffectVampirism",
            "sEffectSummonCenturionSphere",
            "sEffectSunDamage",
            "sEffectStuntedMagicka",
            "sEffectSummonFabricant",
            "sEffectSummonCreature01",
            "sEffectSummonCreature02",
            "sEffectSummonCreature03",
            "sEffectSummonCreature04",
            "sEffectSummonCreature05",
        };

        public static bool TryResolveEffectId(string gmstId, out short effectId)
        {
            effectId = -1;
            if (string.IsNullOrWhiteSpace(gmstId))
                return false;

            string normalized = Normalize(gmstId);
            for (int i = 0; i < s_GmstEffectIds.Length; i++)
            {
                if (string.Equals(Normalize(s_GmstEffectIds[i]), normalized, StringComparison.Ordinal))
                {
                    effectId = (short)i;
                    return true;
                }
            }

            return false;
        }

        static string Normalize(string value)
            => (value ?? string.Empty)
                .Trim()
                .Trim('"')
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
    }
}
