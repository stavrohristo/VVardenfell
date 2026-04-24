using System;
using Unity.Collections;
using UnityEngine;
using VVardenfell.Core.Cache;
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
            BuildSkillRows(contentDb, playerStats, out var majorSkills, out var minorSkills, out var miscSkills);

            return new StatsWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                CharacterName = playerStats.HasPlayer ? ToDisplay(playerStats.Identity.CharacterName, "Player") : "--",
                HealthFillNormalized = playerStats.HasPlayer ? playerStats.HealthFill : 0f,
                HealthText = playerStats.HasPlayer
                    ? $"{playerStats.Vitals.CurrentHealth:0}/{playerStats.Vitals.ModifiedHealthBase:0}"
                    : "0/0",
                MagickaFillNormalized = playerStats.HasPlayer ? playerStats.MagickaFill : 0f,
                MagickaText = playerStats.HasPlayer
                    ? $"{playerStats.Vitals.CurrentMagicka:0}/{playerStats.Vitals.ModifiedMagickaBase:0}"
                    : "0/0",
                FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f,
                FatigueText = playerStats.HasPlayer
                    ? $"{playerStats.Vitals.CurrentFatigue:0}/{playerStats.Vitals.ModifiedFatigueBase:0}"
                    : "0/0",
                LevelText = playerStats.HasPlayer ? Math.Max(1, playerStats.Identity.Level).ToString() : "--",
                RaceText = playerStats.HasPlayer ? ResolveRaceDisplayName(contentDb, playerStats.Identity.RaceName) : "--",
                ClassText = playerStats.HasPlayer ? ResolveClassDisplayName(contentDb, playerStats.Identity.ClassName) : "--",
                Attributes = attributes,
                MajorSkills = majorSkills,
                MinorSkills = minorSkills,
                MiscSkills = miscSkills,
                Factions = BuildFactionRows(contentDb, playerStats),
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

        static void BuildSkillRows(
            RuntimeContentDatabase contentDb,
            in PlayerPresentationStats playerStats,
            out StatsWindowSkillRow[] majorSkills,
            out StatsWindowSkillRow[] minorSkills,
            out StatsWindowSkillRow[] miscSkills)
        {
            majorSkills = Array.Empty<StatsWindowSkillRow>();
            minorSkills = Array.Empty<StatsWindowSkillRow>();

            var assigned = new bool[s_SkillDisplayNames.Length];
            if (playerStats.HasPlayer
                && TryResolveClass(contentDb, playerStats.Identity.ClassName, out var classDef))
            {
                majorSkills = BuildSkillRows(classDef.MajorSkills, playerStats, assigned);
                minorSkills = BuildSkillRows(classDef.MinorSkills, playerStats, assigned);
            }

            int miscCount = 0;
            for (int i = 0; i < assigned.Length; i++)
            {
                if (!assigned[i])
                    miscCount++;
            }

            miscSkills = new StatsWindowSkillRow[miscCount];
            int write = 0;
            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i])
                    continue;

                miscSkills[write++] = BuildSkillRow(i, playerStats);
            }
        }

        static StatsWindowSkillRow[] BuildSkillRows(int[] skillIndices, in PlayerPresentationStats playerStats, bool[] assigned)
        {
            if (skillIndices == null || skillIndices.Length == 0)
                return Array.Empty<StatsWindowSkillRow>();

            var rows = new StatsWindowSkillRow[Math.Min(skillIndices.Length, s_SkillDisplayNames.Length)];
            int write = 0;
            for (int i = 0; i < skillIndices.Length; i++)
            {
                int skillIndex = skillIndices[i];
                if (skillIndex < 0 || skillIndex >= s_SkillDisplayNames.Length || assigned[skillIndex])
                    continue;

                assigned[skillIndex] = true;
                rows[write++] = BuildSkillRow(skillIndex, playerStats);
            }

            if (write == rows.Length)
                return rows;

            Array.Resize(ref rows, write);
            return rows;
        }

        static StatsWindowSkillRow BuildSkillRow(int skillIndex, in PlayerPresentationStats playerStats)
        {
            return new StatsWindowSkillRow
            {
                Name = skillIndex >= 0 && skillIndex < s_SkillDisplayNames.Length ? s_SkillDisplayNames[skillIndex] : "--",
                Value = ResolveSkillValue(skillIndex, playerStats),
            };
        }

        static bool TryResolveClass(RuntimeContentDatabase contentDb, FixedString64Bytes classId, out ClassDef classDef)
        {
            classDef = default;
            string id = classId.ToString();
            if (contentDb == null || string.IsNullOrWhiteSpace(id) || !contentDb.TryGetClassHandle(id, out var handle) || !handle.IsValid)
                return false;

            classDef = contentDb.GetClass(handle);
            return true;
        }

        static StatsWindowFactionRow[] BuildFactionRows(RuntimeContentDatabase contentDb, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || contentDb == null
                || !contentDb.TryGetActorHandle("player", out var actorHandle)
                || !actorHandle.IsValid)
            {
                return Array.Empty<StatsWindowFactionRow>();
            }

            ref readonly var actor = ref contentDb.Get(actorHandle);
            if (string.IsNullOrWhiteSpace(actor.FactionId)
                || !contentDb.TryGetFactionHandle(actor.FactionId, out var factionHandle)
                || !factionHandle.IsValid)
            {
                return Array.Empty<StatsWindowFactionRow>();
            }

            ref readonly var faction = ref contentDb.GetFaction(factionHandle);
            if (faction.Hidden != 0)
                return Array.Empty<StatsWindowFactionRow>();

            return new[]
            {
                new StatsWindowFactionRow
                {
                    Name = string.IsNullOrWhiteSpace(faction.Name) ? actor.FactionId : faction.Name.Trim(),
                    Rank = ResolveFactionRankName(faction, actor.Rank),
                },
            };
        }

        static string ResolveFactionRankName(in FactionDef faction, int rank)
        {
            var rankNames = faction.RankNames ?? Array.Empty<string>();
            if (rank >= 0 && rank < rankNames.Length && !string.IsNullOrWhiteSpace(rankNames[rank]))
                return rankNames[rank].Trim();

            return rank >= 0 ? rank.ToString() : "--";
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

        static string ResolveSkillValue(int skillIndex, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer)
                return "--";

            var skills = playerStats.Skills;
            return skillIndex switch
            {
                0 => skills.Block.ToString("0"),
                1 => skills.Armorer.ToString("0"),
                2 => skills.MediumArmor.ToString("0"),
                3 => skills.HeavyArmor.ToString("0"),
                4 => skills.BluntWeapon.ToString("0"),
                5 => skills.LongBlade.ToString("0"),
                6 => skills.Axe.ToString("0"),
                7 => skills.Spear.ToString("0"),
                8 => skills.Athletics.ToString("0"),
                9 => skills.Enchant.ToString("0"),
                10 => skills.Destruction.ToString("0"),
                11 => skills.Alteration.ToString("0"),
                12 => skills.Illusion.ToString("0"),
                13 => skills.Conjuration.ToString("0"),
                14 => skills.Mysticism.ToString("0"),
                15 => skills.Restoration.ToString("0"),
                16 => skills.Alchemy.ToString("0"),
                17 => skills.Unarmored.ToString("0"),
                18 => skills.Security.ToString("0"),
                19 => skills.Sneak.ToString("0"),
                20 => skills.Acrobatics.ToString("0"),
                21 => skills.LightArmor.ToString("0"),
                22 => skills.ShortBlade.ToString("0"),
                23 => skills.Marksman.ToString("0"),
                24 => skills.Mercantile.ToString("0"),
                25 => skills.Speechcraft.ToString("0"),
                26 => skills.HandToHand.ToString("0"),
                _ => "--",
            };
        }

        static string ResolveClassDisplayName(RuntimeContentDatabase contentDb, FixedString64Bytes classId)
        {
            string id = classId.ToString();
            if (!string.IsNullOrWhiteSpace(id)
                && contentDb != null
                && contentDb.TryGetClassHandle(id, out var handle)
                && handle.IsValid)
            {
                ref readonly var classDef = ref contentDb.GetClass(handle);
                if (!string.IsNullOrWhiteSpace(classDef.Name))
                    return classDef.Name.Trim();
            }

            return ToDisplay(classId, "--");
        }

        static string ResolveRaceDisplayName(RuntimeContentDatabase contentDb, FixedString64Bytes raceId)
        {
            string id = raceId.ToString();
            if (!string.IsNullOrWhiteSpace(id)
                && contentDb != null
                && contentDb.TryGetRaceHandle(id, out var handle)
                && handle.IsValid)
            {
                ref readonly var raceDef = ref contentDb.GetRace(handle);
                if (!string.IsNullOrWhiteSpace(raceDef.Name))
                    return raceDef.Name.Trim();
            }

            return ToDisplay(raceId, "--");
        }

        static string ToDisplay(FixedString64Bytes value, string fallback)
        {
            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }
    }
}

