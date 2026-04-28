using System;
using System.Collections.Generic;
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
        const int MagicEffectFlagTargetSkill = 0x1;
        const int MagicEffectFlagTargetAttribute = 0x2;
        const int MagicEffectFlagNoMagnitude = 0x8;

        SpellWindowViewModel BuildSpellModel(RuntimeContentDatabase contentDb, in SpellWindowState state, in PlayerPresentationStats playerStats)
        {
            int spellCount = contentDb?.SpellCount ?? 0;
            var model = new SpellWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Magic",
                FilterText = state.FilterText.ToString(),
                FooterButtonText = "Delete",
                EmptyStateText = "No known spells",
                SpellSummaryText = $"Known spells: 0   Cached definitions: {spellCount}",
                EffectSummaryText = "No selected spell",
                ActiveEffects = BuildActiveEffectIcons(contentDb, playerStats),
            };

            if (!playerStats.HasPlayer || contentDb == null || !EntityManager.Exists(playerStats.PlayerEntity) || !EntityManager.HasBuffer<PlayerKnownSpell>(playerStats.PlayerEntity))
                return model;

            var knownSpells = EntityManager.GetBuffer<PlayerKnownSpell>(playerStats.PlayerEntity);
            var entries = new List<SpellWindowEntryViewModel>(knownSpells.Length);
            int selectedIndex = knownSpells.Length == 0 ? -1 : Math.Clamp(state.SelectedSpellIndex, 0, knownSpells.Length - 1);
            string filter = state.FilterText.ToString();
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentDb.Data.Spells.Length)
                    continue;

                ref readonly var spell = ref contentDb.Get(spellHandle);
                if (!MatchesSpellFilter(spell, filter))
                    continue;

                entries.Add(new SpellWindowEntryViewModel
                {
                    SpellIndex = i,
                    Name = RuntimeContentMetadataResolver.ResolveSpellName(spell),
                    CostText = spell.Cost.ToString(),
                    TypeText = RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType),
                    EffectTooltipText = BuildSpellEffectTooltip(contentDb, spell),
                    SpellTooltip = BuildSpellTooltip(contentDb, spell),
                    Selected = i == selectedIndex,
                });
            }

            model.Entries = entries.ToArray();
            model.SpellSummaryText = $"Known spells: {entries.Count}   Cached definitions: {spellCount}";
            model.EmptyStateText = entries.Count == 0 && !string.IsNullOrWhiteSpace(filter)
                ? $"No spells match \"{filter.Trim()}\""
                : "No known spells";
            if (selectedIndex >= 0 && selectedIndex < knownSpells.Length)
            {
                var spellHandle = knownSpells[selectedIndex].Spell;
                if (spellHandle.IsValid && spellHandle.Index >= 0 && spellHandle.Index < contentDb.Data.Spells.Length)
                {
                    ref readonly var spell = ref contentDb.Get(spellHandle);
                    model.EffectSummaryText = $"{RuntimeContentMetadataResolver.ResolveSpellName(spell)}   Cost {spell.Cost}   {RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType)}";
                    model.Effects = BuildSpellEffectRows(contentDb, spell);
                }
            }

            return model;
        }

        static bool MatchesSpellFilter(in SpellDef spell, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            if (!string.IsNullOrWhiteSpace(spell.Name) && spell.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrWhiteSpace(spell.Id) && spell.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static SpellWindowEffectRow[] BuildSpellEffectRows(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return Array.Empty<SpellWindowEffectRow>();

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new SpellWindowEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new SpellWindowEffectRow
                {
                    EffectId = effect.EffectId,
                    Name = RuntimeContentMetadataResolver.ResolveMagicEffectName(contentDb, effect.EffectId),
                    DetailText = BuildEffectDetail(contentDb, effect),
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(contentDb, effect.EffectId),
                };
            }

            return rows;
        }

        RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(RuntimeContentDatabase contentDb, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || contentDb == null
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                return Array.Empty<RuntimeMagicEffectIconViewModel>();
            }

            return BuildActiveEffectIcons(contentDb, EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true));
        }

        static RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            if (contentDb == null || activeEffects.Length == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            var ordered = new List<short>();
            var groups = new Dictionary<short, ActiveEffectIconGroup>();
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var active = activeEffects[i];
                if (active.Applied == 0)
                    continue;
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                    continue;

                if (!groups.TryGetValue(active.EffectId, out var group))
                {
                    group = new ActiveEffectIconGroup(contentDb, active.EffectId);
                    groups.Add(active.EffectId, group);
                    ordered.Add(active.EffectId);
                }

                group.Add(active);
            }

            if (ordered.Count == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            float fadeTime = 1f;
            if (contentDb.TryGetGameSettingFloat("fMagicStartIconBlink", out float gmstFadeTime) && gmstFadeTime > 0f)
                fadeTime = gmstFadeTime;

            var result = new RuntimeMagicEffectIconViewModel[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                short effectId = ordered[i];
                var group = groups[effectId];
                string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(contentDb, effectId);
                var descriptionLines = group.BuildDescriptionLines(displayName);
                string iconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(contentDb, effectId);
                result[i] = new RuntimeMagicEffectIconViewModel
                {
                    EffectId = effectId,
                    IconPath = iconPath,
                    DisplayName = displayName,
                    TooltipText = BuildActiveEffectPlainTooltip(displayName, descriptionLines),
                    Tooltip = new RuntimeMagicEffectTooltipViewModel
                    {
                        IconPath = iconPath,
                        DisplayName = displayName,
                        DescriptionLines = descriptionLines,
                    },
                    Alpha = group.ComputeAlpha(fadeTime),
                    SourceLines = descriptionLines,
                };
            }

            return result;
        }

        sealed class ActiveEffectIconGroup
        {
            readonly RuntimeContentDatabase _contentDb;
            readonly short _effectId;
            readonly List<string> _sourceLines = new();
            float _lowestFadeTimeLeft = float.PositiveInfinity;

            public ActiveEffectIconGroup(RuntimeContentDatabase contentDb, short effectId)
            {
                _contentDb = contentDb;
                _effectId = effectId;
            }

            public void Add(in ActorActiveMagicEffect active)
            {
                string source = active.SourceName.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = active.SourceId.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = $"Effect {_effectId}";

                _sourceLines.Add(BuildActiveEffectSourceLine(_contentDb, _effectId, active, source));
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                    _lowestFadeTimeLeft = Math.Min(_lowestFadeTimeLeft, active.TimeLeftSeconds);
            }

            public float ComputeAlpha(float fadeTime)
            {
                if (fadeTime <= 0f || float.IsPositiveInfinity(_lowestFadeTimeLeft))
                    return 1f;

                return Math.Clamp(_lowestFadeTimeLeft / fadeTime, 0f, 1f);
            }

            public string[] BuildDescriptionLines(string displayName)
                => CollapseRedundantDescriptionLines(_sourceLines, displayName);
        }

        static RuntimeSpellTooltipViewModel BuildSpellTooltip(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            string title = RuntimeContentMetadataResolver.ResolveSpellName(spell);
            var effects = BuildSpellTooltipEffects(contentDb, spell);
            return new RuntimeSpellTooltipViewModel
            {
                Title = title,
                SchoolText = spell.SpellType == 0 ? BuildSpellSchoolText(contentDb, spell) : null,
                Effects = effects,
            };
        }

        static RuntimeSpellTooltipEffectRow[] BuildSpellTooltipEffects(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return Array.Empty<RuntimeSpellTooltipEffectRow>();

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new RuntimeSpellTooltipEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new RuntimeSpellTooltipEffectRow
                {
                    EffectId = effect.EffectId,
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(contentDb, effect.EffectId),
                    Text = BuildSpellTooltipEffectText(contentDb, effect),
                };
            }

            return rows;
        }

        static string BuildSpellSchoolText(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return null;

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            if (available <= 0)
                return null;

            short effectId = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex].EffectId;
            int school = -1;
            if (contentDb.TryGetMagicEffectHandle(effectId, out var handle))
            {
                ref readonly var def = ref contentDb.Get(handle);
                school = def.School;
            }

            string schoolName = RuntimeContentMetadataResolver.ResolveSchoolName(contentDb, school);
            string schoolLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, "sSchool", "School");
            return string.IsNullOrWhiteSpace(schoolName) ? null : $"{schoolLabel}: {schoolName}";
        }

        static string BuildEffectDetail(RuntimeContentDatabase contentDb, in MagicEffectInstanceDef effect)
        {
            var parts = new List<string>(4);
            if (effect.MagnitudeMin != 0 || effect.MagnitudeMax != 0)
                parts.Add(effect.MagnitudeMin == effect.MagnitudeMax
                    ? $"{effect.MagnitudeMin} {Pluralize(effect.MagnitudeMin, "pt", "pts")}"
                    : $"{effect.MagnitudeMin} to {effect.MagnitudeMax} pts");
            if (effect.Duration > 0)
                parts.Add($"for {effect.Duration} {Pluralize(effect.Duration, "sec", "secs")}");
            if (effect.Area > 0)
                parts.Add($"in {effect.Area} ft");
            parts.Add(effect.Range switch
            {
                0 => "on Self",
                1 => "on Touch",
                2 => "on Target",
                _ => "range ?",
            });
            return string.Join(" ", parts);
        }

        static string BuildSpellTooltipEffectText(RuntimeContentDatabase contentDb, in MagicEffectInstanceDef effect)
        {
            string name = RuntimeContentMetadataResolver.ResolveMagicEffectName(contentDb, effect.EffectId);
            string argument = ResolveEffectArgumentName(effect);
            if (!string.IsNullOrWhiteSpace(argument))
                name = $"{name} {argument}";

            string detail = BuildEffectDetail(contentDb, effect);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}";
        }

        static string BuildSpellEffectTooltip(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            var effects = BuildSpellEffectRows(contentDb, spell);
            if (effects.Length == 0)
                return string.Empty;

            var lines = new List<string>(effects.Length);
            for (int i = 0; i < effects.Length; i++)
            {
                string name = string.IsNullOrWhiteSpace(effects[i].Name) ? "Effect" : effects[i].Name.Trim();
                string detail = string.IsNullOrWhiteSpace(effects[i].DetailText) ? string.Empty : effects[i].DetailText.Trim();
                lines.Add(string.IsNullOrEmpty(detail) ? name : $"{name} {detail}");
            }

            return string.Join("\n", lines);
        }

        static string ResolveEffectArgumentName(in MagicEffectInstanceDef effect)
        {
            if (effect.Attribute >= 0)
                return RuntimeContentMetadataResolver.ResolveAttributeName(effect.Attribute);
            if (effect.Skill >= 0)
                return RuntimeContentMetadataResolver.ResolveSkillName(effect.Skill);
            return string.Empty;
        }

        static string Pluralize(int value, string singular, string plural)
            => Math.Abs(value) == 1 ? singular : plural;

        static string BuildActiveEffectSourceLine(
            RuntimeContentDatabase contentDb,
            short effectId,
            in ActorActiveMagicEffect active,
            string source)
        {
            string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(contentDb, effectId);
            string line = string.IsNullOrWhiteSpace(source)
                ? displayName
                : source.Trim();
            string sourceLabel = line;
            string detail = string.Empty;

            if (RuntimeContentMetadataResolver.TryGetMagicEffectDef(contentDb, effectId, out var def))
            {
                if ((def.Flags & MagicEffectFlagTargetSkill) != 0 && active.Skill >= 0)
                {
                    string skillName = RuntimeContentMetadataResolver.ResolveSkillName(active.Skill);
                    if (!string.IsNullOrWhiteSpace(skillName))
                        line += $" ({skillName})";
                }

                if ((def.Flags & MagicEffectFlagTargetAttribute) != 0 && active.Attribute >= 0)
                {
                    string attributeName = RuntimeContentMetadataResolver.ResolveAttributeName(active.Attribute);
                    if (!string.IsNullOrWhiteSpace(attributeName))
                        line += $" ({attributeName})";
                }

                sourceLabel = line;
                detail = FormatActiveEffectMagnitude(contentDb, effectId, active.Magnitude, def.Flags).TrimStart(':', ' ');
            }
            else if (Math.Abs(active.Magnitude) > 0.0001f)
            {
                detail = $"{(int)active.Magnitude} {RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, Math.Abs((int)active.Magnitude) == 1 ? "sPoint" : "sPoints", "pts")}";
            }

            if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                detail = string.IsNullOrWhiteSpace(detail)
                    ? BuildActiveEffectDuration(contentDb, active.TimeLeftSeconds).Trim()
                    : $"{detail} {BuildActiveEffectDuration(contentDb, active.TimeLeftSeconds).Trim()}";

            if (string.Equals(sourceLabel, displayName, StringComparison.OrdinalIgnoreCase))
                return detail;
            return string.IsNullOrWhiteSpace(detail) ? sourceLabel : $"{sourceLabel}: {detail}";
        }

        static string[] CollapseRedundantDescriptionLines(List<string> lines, string displayName)
        {
            if (lines == null || lines.Count == 0)
                return Array.Empty<string>();

            var result = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i]?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (string.Equals(line, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(line);
            }

            return result.ToArray();
        }

        static string BuildActiveEffectPlainTooltip(string displayName, string[] descriptionLines)
        {
            var lines = new List<string>(1 + (descriptionLines?.Length ?? 0));
            if (!string.IsNullOrWhiteSpace(displayName))
                lines.Add(displayName.Trim());
            if (descriptionLines != null)
            {
                for (int i = 0; i < descriptionLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(descriptionLines[i]))
                        lines.Add(descriptionLines[i].Trim());
                }
            }

            return string.Join("\n", lines);
        }

        static string FormatActiveEffectMagnitude(RuntimeContentDatabase contentDb, short effectId, float magnitude, int flags)
        {
            var displayType = ResolveMagnitudeDisplayType(effectId, flags);
            if (displayType == ActiveEffectMagnitudeDisplayType.None)
                return string.Empty;

            int integerMagnitude = (int)magnitude;
            if (displayType == ActiveEffectMagnitudeDisplayType.TimesInt)
            {
                string unit = RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, "sXTimesINT", "x INT");
                return $" {(integerMagnitude / 10f):0.0}{unit}";
            }

            string result = $": {integerMagnitude}";
            if (displayType != ActiveEffectMagnitudeDisplayType.Percentage)
                result += " ";

            result += displayType switch
            {
                ActiveEffectMagnitudeDisplayType.Feet => RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, "sFeet", "ft"),
                ActiveEffectMagnitudeDisplayType.Level => RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, Math.Abs(integerMagnitude) == 1 ? "sLevel" : "sLevels", "levels"),
                ActiveEffectMagnitudeDisplayType.Percentage => RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, "sPercent", "%"),
                _ => RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, Math.Abs(integerMagnitude) == 1 ? "sPoint" : "sPoints", "pts"),
            };

            return result;
        }

        static string BuildActiveEffectDuration(RuntimeContentDatabase contentDb, float timeLeftSeconds)
        {
            int seconds = Math.Max(0, (int)Math.Ceiling(timeLeftSeconds));
            string durationLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(contentDb, "sDuration", "Duration");
            return $" {durationLabel}: {seconds}";
        }

        enum ActiveEffectMagnitudeDisplayType
        {
            None,
            Feet,
            Level,
            Percentage,
            Points,
            TimesInt,
        }

        static ActiveEffectMagnitudeDisplayType ResolveMagnitudeDisplayType(short effectId, int flags)
        {
            if ((flags & MagicEffectFlagNoMagnitude) != 0)
                return ActiveEffectMagnitudeDisplayType.None;
            if (effectId == 84)
                return ActiveEffectMagnitudeDisplayType.TimesInt;
            if (effectId == 59 || effectId is >= 64 and <= 66)
                return ActiveEffectMagnitudeDisplayType.Feet;
            if (effectId == 118 || effectId == 119)
                return ActiveEffectMagnitudeDisplayType.Level;
            if (effectId is >= 28 and <= 36
                || effectId is >= 90 and <= 99
                || effectId == 40
                || effectId == 47
                || effectId == 57
                || effectId == 68)
            {
                return ActiveEffectMagnitudeDisplayType.Percentage;
            }

            return ActiveEffectMagnitudeDisplayType.Points;
        }
    }
}

