using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static StatsWindowViewModel BuildStatsModel(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in StatsWindowState state,
            in PlayerPresentationStats playerStats)
        {
            var attributes = BuildAttributeRows(ref contentBlob, playerStats);
            BuildSkillRows(ref contentBlob, playerStats, out var majorSkills, out var minorSkills, out var miscSkills);

            return new StatsWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
                CharacterName = playerStats.HasPlayer ? RuntimeContentMetadataResolver.ToDisplay(playerStats.Identity.CharacterName, "Player") : "--",
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
                RaceText = playerStats.HasPlayer ? RuntimeContentMetadataResolver.ResolveRaceDisplayName(ref contentBlob, playerStats.Identity.RaceName) : "--",
                ClassText = playerStats.HasPlayer ? RuntimeContentMetadataResolver.ResolveClassDisplayName(ref contentBlob, playerStats.Identity.ClassName) : "--",
                Attributes = attributes,
                MajorSkills = majorSkills,
                MinorSkills = minorSkills,
                MiscSkills = miscSkills,
                Factions = BuildFactionRows(ref contentBlob, entityManager, playerStats),
                BirthSignName = playerStats.HasPlayer ? RuntimeContentMetadataResolver.ToDisplay(playerStats.Identity.BirthSignName, string.Empty) : string.Empty,
                ReputationText = playerStats.HasPlayer ? playerStats.Identity.Reputation.ToString() : string.Empty,
            };
        }

        static StatsWindowAttributeRow[] BuildAttributeRows(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats)
        {
            var rows = new StatsWindowAttributeRow[RuntimeContentMetadataResolver.AttributeCount];
            for (int i = 0; i < rows.Length; i++)
            {
                string name = RuntimeContentMetadataResolver.ResolveAttributeName(i);
                rows[i] = new StatsWindowAttributeRow
                {
                    Name = name,
                    Value = ResolveAttributeValue(i, playerStats),
                    AttributeIndex = i,
                    TooltipText = BuildAttributeTooltip(ref contentBlob, i),
                };
            }

            return rows;
        }

        static void BuildSkillRows(
            ref RuntimeContentBlob contentBlob,
            in PlayerPresentationStats playerStats,
            out StatsWindowSkillRow[] majorSkills,
            out StatsWindowSkillRow[] minorSkills,
            out StatsWindowSkillRow[] miscSkills)
        {
            majorSkills = Array.Empty<StatsWindowSkillRow>();
            minorSkills = Array.Empty<StatsWindowSkillRow>();

            var assigned = new bool[RuntimeContentMetadataResolver.SkillCount];
            if (playerStats.HasPlayer
                && RuntimeContentMetadataResolver.TryResolveClassHandle(ref contentBlob, playerStats.Identity.ClassName, out var classHandle))
            {
                ref RuntimeClassDefBlob classDef = ref RuntimeContentBlobUtility.GetClass(ref contentBlob, classHandle);
                majorSkills = BuildSkillRows(ref contentBlob, ref contentBlob.ClassMajorSkills, classDef.FirstMajorSkillIndex, classDef.MajorSkillCount, playerStats, assigned);
                minorSkills = BuildSkillRows(ref contentBlob, ref contentBlob.ClassMinorSkills, classDef.FirstMinorSkillIndex, classDef.MinorSkillCount, playerStats, assigned);
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

                miscSkills[write++] = BuildSkillRow(ref contentBlob, i, playerStats);
            }
        }

        static StatsWindowSkillRow[] BuildSkillRows(
            ref RuntimeContentBlob contentBlob,
            ref BlobArray<int> skillIndices,
            int first,
            int count,
            in PlayerPresentationStats playerStats,
            bool[] assigned)
        {
            RuntimeContentBlobUtility.RequireRange(first, count, skillIndices.Length, "class skill");
            if (count == 0)
                return Array.Empty<StatsWindowSkillRow>();

            var rows = new StatsWindowSkillRow[Math.Min(count, RuntimeContentMetadataResolver.SkillCount)];
            int write = 0;
            for (int i = 0; i < count; i++)
            {
                int skillIndex = skillIndices[first + i];
                if (skillIndex < 0 || skillIndex >= RuntimeContentMetadataResolver.SkillCount || assigned[skillIndex])
                    continue;

                assigned[skillIndex] = true;
                rows[write++] = BuildSkillRow(ref contentBlob, skillIndex, playerStats);
            }

            if (write == rows.Length)
                return rows;

            Array.Resize(ref rows, write);
            return rows;
        }

        static StatsWindowSkillRow BuildSkillRow(ref RuntimeContentBlob contentBlob, int skillIndex, in PlayerPresentationStats playerStats)
        {
            string name = RuntimeContentMetadataResolver.ResolveSkillName(skillIndex);
            return new StatsWindowSkillRow
            {
                Name = string.IsNullOrWhiteSpace(name) ? "--" : name,
                Value = ResolveSkillValue(skillIndex, playerStats),
                SkillIndex = skillIndex,
                TooltipText = BuildSkillTooltip(ref contentBlob, skillIndex),
            };
        }

        static string BuildAttributeTooltip(ref RuntimeContentBlob contentBlob, int attributeIndex)
        {
            string name = RuntimeContentMetadataResolver.ResolveAttributeName(attributeIndex);
            return JoinTooltip(name);
        }

        static string BuildSkillTooltip(ref RuntimeContentBlob contentBlob, int skillIndex)
        {
            string name = RuntimeContentMetadataResolver.ResolveSkillName(skillIndex);
            string description = string.Empty;
            string governingAttribute = string.Empty;
            string icon = string.Empty;
            for (int i = 0; i < contentBlob.Skills.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob skill = ref contentBlob.Skills[i];
                if (skill.Int0 != skillIndex)
                    continue;

                string recordName = skill.Name.ToString();
                if (!string.IsNullOrWhiteSpace(recordName))
                    name = recordName.Trim();
                description = skill.Text.ToString();
                governingAttribute = RuntimeContentMetadataResolver.ResolveAttributeName(skill.Int1);
                icon = skill.Icon.ToString();
                break;
            }

            string attributeLine = string.IsNullOrWhiteSpace(governingAttribute) ? null : $"Governing Attribute: {governingAttribute}";
            return JoinTooltip(name, description, attributeLine, string.IsNullOrWhiteSpace(icon) ? null : icon);
        }

        static string JoinTooltip(params string[] lines)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(lines[i].Trim());
            }

            return builder.ToString();
        }

        static StatsWindowFactionRow[] BuildFactionRows(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || playerStats.PlayerEntity == Entity.Null
                || !entityManager.Exists(playerStats.PlayerEntity)
                || !entityManager.HasBuffer<PlayerFactionMembership>(playerStats.PlayerEntity))
            {
                return Array.Empty<StatsWindowFactionRow>();
            }

            var memberships = entityManager.GetBuffer<PlayerFactionMembership>(playerStats.PlayerEntity, true);
            int count = 0;
            for (int i = 0; i < memberships.Length; i++)
            {
                if (IsVisibleFactionMembership(ref contentBlob, memberships[i]))
                    count++;
            }

            if (count == 0)
                return Array.Empty<StatsWindowFactionRow>();

            var rows = new StatsWindowFactionRow[count];
            int write = 0;
            for (int i = 0; i < memberships.Length; i++)
            {
                var membership = memberships[i];
                if (!IsVisibleFactionMembership(ref contentBlob, membership))
                    continue;

                ref RuntimeFactionDefBlob faction = ref contentBlob.Factions[membership.FactionIndex];
                string fallback = faction.Id.ToString();
                rows[write++] = new StatsWindowFactionRow
                {
                    Name = RuntimeContentMetadataResolver.ResolveFactionDisplayName(ref faction, fallback),
                    Rank = RuntimeContentMetadataResolver.ResolveFactionRankName(ref contentBlob, ref faction, membership.Rank),
                };
            }

            return rows;
        }

        static bool IsVisibleFactionMembership(ref RuntimeContentBlob contentBlob, in PlayerFactionMembership membership)
        {
            if (membership.Joined == 0
                || membership.Expelled != 0
                || (uint)membership.FactionIndex >= (uint)contentBlob.Factions.Length)
            {
                return false;
            }

            ref RuntimeFactionDefBlob faction = ref contentBlob.Factions[membership.FactionIndex];
            return faction.Hidden == 0;
        }

        static string ResolveAttributeValue(int attributeIndex, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer)
                return "--";

            var attributes = playerStats.Attributes;
            return attributeIndex switch
            {
                0 => attributes.Strength.ToString("0"),
                1 => attributes.Intelligence.ToString("0"),
                2 => attributes.Willpower.ToString("0"),
                3 => attributes.Agility.ToString("0"),
                4 => attributes.Speed.ToString("0"),
                5 => attributes.Endurance.ToString("0"),
                6 => attributes.Personality.ToString("0"),
                7 => attributes.Luck.ToString("0"),
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

    }
}

