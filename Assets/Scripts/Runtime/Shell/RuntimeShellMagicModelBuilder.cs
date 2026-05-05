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

        SpellWindowViewModel BuildSpellModel(ref RuntimeContentBlob contentBlob, in SpellWindowState state, in PlayerPresentationStats playerStats)
        {
            int spellCount = contentBlob.Spells.Length;
            var model = new SpellWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
                Title = "Magic",
                FilterText = state.FilterText.ToString(),
                FooterButtonText = "Delete",
                EmptyStateText = "No known spells",
                SpellSummaryText = $"Known spells: 0   Cached definitions: {spellCount}",
                EffectSummaryText = "No selected spell",
                ActiveEffects = BuildActiveEffectIcons(ref contentBlob, playerStats),
            };

            if (!playerStats.HasPlayer || !EntityManager.Exists(playerStats.PlayerEntity) || !EntityManager.HasBuffer<ActorKnownSpell>(playerStats.PlayerEntity))
                return model;

            var knownSpells = EntityManager.GetBuffer<ActorKnownSpell>(playerStats.PlayerEntity);
            var entries = new List<SpellWindowEntryViewModel>(knownSpells.Length);
            int selectedIndex = knownSpells.Length == 0 ? -1 : Math.Clamp(state.SelectedSpellIndex, 0, knownSpells.Length - 1);
            string filter = state.FilterText.ToString();
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentBlob.Spells.Length)
                    continue;

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, spellHandle);
                if (!MatchesSpellFilter(ref spell, filter))
                    continue;

                entries.Add(new SpellWindowEntryViewModel
                {
                    SpellIndex = i,
                    Name = RuntimeContentMetadataResolver.ResolveSpellName(ref spell),
                    CostText = spell.Cost.ToString(),
                    TypeText = RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType),
                    EffectTooltipText = BuildSpellEffectTooltip(ref contentBlob, ref spell),
                    SpellTooltip = BuildSpellTooltip(ref contentBlob, ref spell),
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
                if (spellHandle.IsValid && spellHandle.Index >= 0 && spellHandle.Index < contentBlob.Spells.Length)
                {
                    ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, spellHandle);
                    model.EffectSummaryText = $"{RuntimeContentMetadataResolver.ResolveSpellName(ref spell)}   Cost {spell.Cost}   {RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType)}";
                    model.Effects = BuildSpellEffectRows(ref contentBlob, ref spell);
                }
            }

            return model;
        }

        static bool MatchesSpellFilter(ref RuntimeSpellDefBlob spell, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            string name = spell.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string id = spell.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id) && id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static SpellWindowEffectRow[] BuildSpellEffectRows(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return Array.Empty<SpellWindowEffectRow>();

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new SpellWindowEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new SpellWindowEffectRow
                {
                    EffectId = effect.EffectId,
                    Name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effect.EffectId),
                    DetailText = BuildEffectDetail(ref contentBlob, effect),
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId),
                };
            }

            return rows;
        }

        RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                return Array.Empty<RuntimeMagicEffectIconViewModel>();
            }

            return BuildActiveEffectIcons(ref contentBlob, EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true));
        }

        RuntimeMagicEffectIconViewModel[] BuildHudActiveEffectIcons(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                _hasCachedHudActiveEffectSignature = false;
                _cachedHudActiveEffects = Array.Empty<RuntimeMagicEffectIconViewModel>();
                return _cachedHudActiveEffects;
            }

            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true);
            ulong signature = ComputeActiveEffectSignature(activeEffects);
            if (signature == 0UL)
            {
                _hasCachedHudActiveEffectSignature = false;
                _cachedHudActiveEffects = Array.Empty<RuntimeMagicEffectIconViewModel>();
                return _cachedHudActiveEffects;
            }

            if (!_hasCachedHudActiveEffectSignature || signature != _cachedHudActiveEffectSignature)
            {
                _cachedHudActiveEffects = BuildActiveEffectIcons(ref contentBlob, activeEffects);
                _cachedHudActiveEffectSignature = signature;
                _hasCachedHudActiveEffectSignature = true;
                return _cachedHudActiveEffects;
            }

            UpdateCachedActiveEffectAlpha(ref contentBlob, activeEffects, _cachedHudActiveEffects);
            return _cachedHudActiveEffects;
        }

        static ulong ComputeActiveEffectSignature(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            int count = 0;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                var active = activeEffects[i];
                if (active.Applied == 0)
                    continue;
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                    continue;

                count++;
                hash = (hash ^ (ushort)active.EffectId) * prime;
                hash = (hash ^ (byte)active.Skill) * prime;
                hash = (hash ^ (byte)active.Attribute) * prime;
                hash = (hash ^ (byte)active.SourceKind) * prime;
                hash = (hash ^ (uint)active.SourceName.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.SourceId.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.Magnitude.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.DurationSeconds.GetHashCode()) * prime;
            }

            return count == 0 ? 0UL : (hash ^ (uint)count) * prime;
        }

        static void UpdateCachedActiveEffectAlpha(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            RuntimeMagicEffectIconViewModel[] cachedEffects)
        {
            if (cachedEffects == null || cachedEffects.Length == 0)
                return;

            float fadeTime = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fMagicStartIconBlink);

            for (int i = 0; i < cachedEffects.Length; i++)
            {
                var cached = cachedEffects[i];
                if (cached == null)
                    continue;

                float lowestFadeTimeLeft = float.PositiveInfinity;
                for (int j = 0; j < activeEffects.Length; j++)
                {
                    var active = activeEffects[j];
                    if (active.Applied == 0 || active.EffectId != cached.EffectId)
                        continue;
                    if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                        continue;
                    if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                        lowestFadeTimeLeft = Math.Min(lowestFadeTimeLeft, active.TimeLeftSeconds);
                }

                cached.Alpha = fadeTime <= 0f || float.IsPositiveInfinity(lowestFadeTimeLeft)
                    ? 1f
                    : Math.Clamp(lowestFadeTimeLeft / fadeTime, 0f, 1f);
            }
        }

        static RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            if (activeEffects.Length == 0)
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
                    group = new ActiveEffectIconGroup(active.EffectId);
                    groups.Add(active.EffectId, group);
                    ordered.Add(active.EffectId);
                }

                group.Add(ref contentBlob, active);
            }

            if (ordered.Count == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            float fadeTime = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fMagicStartIconBlink);

            var result = new RuntimeMagicEffectIconViewModel[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                short effectId = ordered[i];
                var group = groups[effectId];
                string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effectId);
                var descriptionLines = group.BuildDescriptionLines(displayName);
                string iconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effectId);
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
            readonly short _effectId;
            readonly List<string> _sourceLines = new();
            float _lowestFadeTimeLeft = float.PositiveInfinity;

            public ActiveEffectIconGroup(short effectId)
            {
                _effectId = effectId;
            }

            public void Add(ref RuntimeContentBlob contentBlob, in ActorActiveMagicEffect active)
            {
                string source = active.SourceName.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = active.SourceId.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = $"Effect {_effectId}";

                _sourceLines.Add(BuildActiveEffectSourceLine(ref contentBlob, _effectId, active, source));
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

        static RuntimeSpellTooltipViewModel BuildSpellTooltip(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            string title = RuntimeContentMetadataResolver.ResolveSpellName(ref spell);
            var effects = BuildSpellTooltipEffects(ref contentBlob, ref spell);
            return new RuntimeSpellTooltipViewModel
            {
                Title = title,
                SchoolText = spell.SpellType == 0 ? BuildSpellSchoolText(ref contentBlob, ref spell) : null,
                Effects = effects,
            };
        }

        static RuntimeSpellTooltipEffectRow[] BuildSpellTooltipEffects(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return Array.Empty<RuntimeSpellTooltipEffectRow>();

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new RuntimeSpellTooltipEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new RuntimeSpellTooltipEffectRow
                {
                    EffectId = effect.EffectId,
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId),
                    Text = BuildSpellTooltipEffectText(ref contentBlob, effect),
                };
            }

            return rows;
        }

        static string BuildSpellSchoolText(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return null;

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            if (available <= 0)
                return null;

            short effectId = contentBlob.MagicEffectInstances[spell.EffectStartIndex].EffectId;
            int school = -1;
            if (RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref contentBlob, effectId, out var handle))
            {
                ref RuntimeMagicEffectDefBlob def = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
                school = def.School;
            }

            string schoolName = RuntimeContentMetadataResolver.ResolveSchoolName(ref contentBlob, school);
            string schoolLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sSchool", "School");
            return string.IsNullOrWhiteSpace(schoolName) ? null : $"{schoolLabel}: {schoolName}";
        }

        static string BuildEffectDetail(ref RuntimeContentBlob contentBlob, in MagicEffectInstanceDef effect)
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

        static string BuildSpellTooltipEffectText(ref RuntimeContentBlob contentBlob, in MagicEffectInstanceDef effect)
        {
            string name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effect.EffectId);
            string argument = ResolveEffectArgumentName(effect);
            if (!string.IsNullOrWhiteSpace(argument))
                name = $"{name} {argument}";

            string detail = BuildEffectDetail(ref contentBlob, effect);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}";
        }

        static string BuildSpellEffectTooltip(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            var effects = BuildSpellEffectRows(ref contentBlob, ref spell);
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
            ref RuntimeContentBlob contentBlob,
            short effectId,
            in ActorActiveMagicEffect active,
            string source)
        {
            string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effectId);
            string line = string.IsNullOrWhiteSpace(source)
                ? displayName
                : source.Trim();
            string sourceLabel = line;
            string detail = string.Empty;

            if (RuntimeContentMetadataResolver.TryGetMagicEffectFlags(ref contentBlob, effectId, out int flags))
            {
                if ((flags & MagicEffectFlagTargetSkill) != 0 && active.Skill >= 0)
                {
                    string skillName = RuntimeContentMetadataResolver.ResolveSkillName(active.Skill);
                    if (!string.IsNullOrWhiteSpace(skillName))
                        line += $" ({skillName})";
                }

                if ((flags & MagicEffectFlagTargetAttribute) != 0 && active.Attribute >= 0)
                {
                    string attributeName = RuntimeContentMetadataResolver.ResolveAttributeName(active.Attribute);
                    if (!string.IsNullOrWhiteSpace(attributeName))
                        line += $" ({attributeName})";
                }

                sourceLabel = line;
                detail = FormatActiveEffectMagnitude(ref contentBlob, effectId, active.Magnitude, flags).TrimStart(':', ' ');
            }
            else if (Math.Abs(active.Magnitude) > 0.0001f)
            {
                detail = $"{(int)active.Magnitude} {RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs((int)active.Magnitude) == 1 ? "sPoint" : "sPoints", "pts")}";
            }

            if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                detail = string.IsNullOrWhiteSpace(detail)
                    ? BuildActiveEffectDuration(ref contentBlob, active.TimeLeftSeconds).Trim()
                    : $"{detail} {BuildActiveEffectDuration(ref contentBlob, active.TimeLeftSeconds).Trim()}";

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

        static string FormatActiveEffectMagnitude(ref RuntimeContentBlob contentBlob, short effectId, float magnitude, int flags)
        {
            var displayType = ResolveMagnitudeDisplayType(effectId, flags);
            if (displayType == ActiveEffectMagnitudeDisplayType.None)
                return string.Empty;

            int integerMagnitude = (int)magnitude;
            if (displayType == ActiveEffectMagnitudeDisplayType.TimesInt)
            {
                string unit = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sXTimesINT", "x INT");
                return $" {(integerMagnitude / 10f):0.0}{unit}";
            }

            string result = $": {integerMagnitude}";
            if (displayType != ActiveEffectMagnitudeDisplayType.Percentage)
                result += " ";

            result += displayType switch
            {
                ActiveEffectMagnitudeDisplayType.Feet => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sFeet", "ft"),
                ActiveEffectMagnitudeDisplayType.Level => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs(integerMagnitude) == 1 ? "sLevel" : "sLevels", "levels"),
                ActiveEffectMagnitudeDisplayType.Percentage => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sPercent", "%"),
                _ => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs(integerMagnitude) == 1 ? "sPoint" : "sPoints", "pts"),
            };

            return result;
        }

        static string BuildActiveEffectDuration(ref RuntimeContentBlob contentBlob, float timeLeftSeconds)
        {
            int seconds = Math.Max(0, (int)Math.Ceiling(timeLeftSeconds));
            string durationLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sDuration", "Duration");
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

