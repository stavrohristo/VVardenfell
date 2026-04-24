using System;
using Unity.Collections;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        // Vanilla TES3 attribute order. Display names come from GMSTs (sStrength, sIntelligence, ...)
        // in real MW - hardcoded here while the actor data pillar is pending so the Stats window
        // matches the vanilla layout today. Replace with GMST lookup once localization lands.
        static readonly string[] s_AttributeDisplayNames =
        {
            "Strength", "Intelligence", "Willpower", "Agility",
            "Speed", "Endurance", "Personality", "Luck",
        };

        // Vanilla TES3 skill order (27 skills). Grouping into Major/Minor/Misc requires the
        // player's class, which doesn't exist pre-character-creation. For now every skill sits
        // in Misc so the window shows the full skill list the way vanilla would.
        static readonly string[] s_SkillDisplayNames =
        {
            "Block", "Armorer", "Medium Armor", "Heavy Armor", "Blunt Weapon",
            "Long Blade", "Axe", "Spear", "Athletics", "Enchant",
            "Destruction", "Alteration", "Illusion", "Conjuration", "Mysticism",
            "Restoration", "Alchemy", "Unarmored", "Security", "Sneak",
            "Acrobatics", "Light Armor", "Short Blade", "Marksman",
            "Mercantile", "Speechcraft", "Hand-to-hand",
        };

        static StatsWindowViewModel BuildStatsModel(RuntimeContentDatabase contentDb, in StatsWindowState state, in PlayerPresentationStats playerStats)
        {
            var attributes = BuildAttributeRows(playerStats);

            var miscSkills = new StatsWindowSkillRow[s_SkillDisplayNames.Length];
            for (int i = 0; i < miscSkills.Length; i++)
                miscSkills[i] = new StatsWindowSkillRow
                {
                    Name = s_SkillDisplayNames[i],
                    Value = ResolveSkillValue(s_SkillDisplayNames[i], playerStats),
                };

            return new StatsWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                CharacterName = playerStats.HasPlayer ? ToDisplay(playerStats.Identity.CharacterName, "Player") : "--",
                HealthFillNormalized = 0f,
                HealthText = "0/0",
                MagickaFillNormalized = 0f,
                MagickaText = "0/0",
                FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f,
                FatigueText = playerStats.HasPlayer
                    ? $"{playerStats.Vitals.CurrentFatigue:0}/{playerStats.Vitals.ModifiedFatigueBase:0}"
                    : "0/0",
                LevelText = playerStats.HasPlayer ? Math.Max(1, playerStats.Identity.Level).ToString() : "--",
                RaceText = playerStats.HasPlayer ? ToDisplay(playerStats.Identity.RaceName, "--") : "--",
                ClassText = playerStats.HasPlayer ? ToDisplay(playerStats.Identity.ClassName, "--") : "--",
                Attributes = attributes,
                MajorSkills = Array.Empty<StatsWindowSkillRow>(),
                MinorSkills = Array.Empty<StatsWindowSkillRow>(),
                MiscSkills = miscSkills,
                Factions = Array.Empty<StatsWindowFactionRow>(),
                BirthSignName = playerStats.HasPlayer ? ToDisplay(playerStats.Identity.BirthSignName, string.Empty) : string.Empty,
                ReputationText = playerStats.HasPlayer ? playerStats.Identity.Reputation.ToString() : string.Empty,
            };
        }

        static StatsWindowAttributeRow[] BuildAttributeRows(in PlayerPresentationStats playerStats)
        {
            var rows = new StatsWindowAttributeRow[s_AttributeDisplayNames.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                string name = s_AttributeDisplayNames[i];
                rows[i] = new StatsWindowAttributeRow
                {
                    Name = name,
                    Value = ResolveAttributeValue(name, playerStats),
                };
            }

            return rows;
        }

        static string ResolveAttributeValue(string name, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer)
                return "--";

            var attributes = playerStats.Attributes;
            return name switch
            {
                "Strength" => attributes.Strength.ToString("0"),
                "Intelligence" => attributes.Intelligence.ToString("0"),
                "Willpower" => attributes.Willpower.ToString("0"),
                "Agility" => attributes.Agility.ToString("0"),
                "Speed" => attributes.Speed.ToString("0"),
                "Endurance" => attributes.Endurance.ToString("0"),
                "Personality" => attributes.Personality.ToString("0"),
                "Luck" => attributes.Luck.ToString("0"),
                _ => "--",
            };
        }

        static string ResolveSkillValue(string name, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer)
                return "--";

            var skills = playerStats.Skills;
            return name switch
            {
                "Block" => skills.Block.ToString("0"),
                "Armorer" => skills.Armorer.ToString("0"),
                "Medium Armor" => skills.MediumArmor.ToString("0"),
                "Heavy Armor" => skills.HeavyArmor.ToString("0"),
                "Blunt Weapon" => skills.BluntWeapon.ToString("0"),
                "Long Blade" => skills.LongBlade.ToString("0"),
                "Axe" => skills.Axe.ToString("0"),
                "Spear" => skills.Spear.ToString("0"),
                "Athletics" => skills.Athletics.ToString("0"),
                "Enchant" => skills.Enchant.ToString("0"),
                "Destruction" => skills.Destruction.ToString("0"),
                "Alteration" => skills.Alteration.ToString("0"),
                "Illusion" => skills.Illusion.ToString("0"),
                "Conjuration" => skills.Conjuration.ToString("0"),
                "Mysticism" => skills.Mysticism.ToString("0"),
                "Restoration" => skills.Restoration.ToString("0"),
                "Alchemy" => skills.Alchemy.ToString("0"),
                "Unarmored" => skills.Unarmored.ToString("0"),
                "Security" => skills.Security.ToString("0"),
                "Sneak" => skills.Sneak.ToString("0"),
                "Acrobatics" => skills.Acrobatics.ToString("0"),
                "Light Armor" => skills.LightArmor.ToString("0"),
                "Short Blade" => skills.ShortBlade.ToString("0"),
                "Marksman" => skills.Marksman.ToString("0"),
                "Mercantile" => skills.Mercantile.ToString("0"),
                "Speechcraft" => skills.Speechcraft.ToString("0"),
                "Hand-to-hand" => skills.HandToHand.ToString("0"),
                _ => "--",
            };
        }

        static string ToDisplay(FixedString64Bytes value, string fallback)
        {
            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }
    }
}

