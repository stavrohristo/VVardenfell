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
        SpellWindowViewModel BuildSpellModel(RuntimeContentDatabase contentDb, in SpellWindowState state, in PlayerPresentationStats playerStats)
        {
            int spellCount = contentDb?.SpellCount ?? 0;
            var model = new SpellWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Magic",
                FilterText = string.Empty,
                FooterButtonText = "Delete",
                EmptyStateText = "No known spells",
                SpellSummaryText = $"Known spells: 0   Cached definitions: {spellCount}",
                EffectSummaryText = "No selected spell",
            };

            if (!playerStats.HasPlayer || contentDb == null || !EntityManager.Exists(playerStats.PlayerEntity) || !EntityManager.HasBuffer<PlayerKnownSpell>(playerStats.PlayerEntity))
                return model;

            var knownSpells = EntityManager.GetBuffer<PlayerKnownSpell>(playerStats.PlayerEntity);
            var entries = new List<SpellWindowEntryViewModel>(knownSpells.Length);
            int selectedIndex = knownSpells.Length == 0 ? -1 : Math.Clamp(state.SelectedSpellIndex, 0, knownSpells.Length - 1);
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentDb.Data.Spells.Length)
                    continue;

                ref readonly var spell = ref contentDb.Get(spellHandle);
                entries.Add(new SpellWindowEntryViewModel
                {
                    Name = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim(),
                    CostText = spell.Cost.ToString(),
                    TypeText = ResolveSpellTypeName(spell.SpellType),
                    Selected = i == selectedIndex,
                });
            }

            model.Entries = entries.ToArray();
            model.SpellSummaryText = $"Known spells: {entries.Count}   Cached definitions: {spellCount}";
            if (selectedIndex >= 0 && selectedIndex < knownSpells.Length)
            {
                var spellHandle = knownSpells[selectedIndex].Spell;
                if (spellHandle.IsValid && spellHandle.Index >= 0 && spellHandle.Index < contentDb.Data.Spells.Length)
                {
                    ref readonly var spell = ref contentDb.Get(spellHandle);
                    model.EffectSummaryText = $"{ResolveSpellName(spell)}   Cost {spell.Cost}   {ResolveSpellTypeName(spell.SpellType)}";
                    model.Effects = BuildSpellEffectRows(contentDb, spell);
                }
            }

            return model;
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
                    Name = ResolveMagicEffectName(contentDb, effect.EffectId),
                    DetailText = BuildEffectDetail(effect),
                };
            }

            return rows;
        }

        static string ResolveSpellName(in SpellDef spell)
            => string.IsNullOrWhiteSpace(spell.Name) ? spell.Id ?? "--" : spell.Name.Trim();

        static string ResolveSpellTypeName(int type)
        {
            return type switch
            {
                0 => "Spell",
                1 => "Ability",
                2 => "Blight",
                3 => "Disease",
                4 => "Curse",
                5 => "Power",
                _ => "Spell",
            };
        }

        static string ResolveMagicEffectName(RuntimeContentDatabase contentDb, short effectId)
        {
            if (contentDb != null && contentDb.TryGetMagicEffectHandle(effectId, out var handle))
            {
                ref readonly var def = ref contentDb.Get(handle);
                if (!string.IsNullOrWhiteSpace(def.Description))
                {
                    string description = def.Description.Trim();
                    int sentenceBreak = description.IndexOf('.');
                    if (sentenceBreak > 0)
                        description = description.Substring(0, sentenceBreak);
                    return description.Length > 48 ? description.Substring(0, 48).Trim() : description;
                }
            }

            return $"Effect {effectId}";
        }

        static string BuildEffectDetail(in MagicEffectInstanceDef effect)
        {
            var parts = new List<string>(4);
            if (effect.MagnitudeMin != 0 || effect.MagnitudeMax != 0)
                parts.Add(effect.MagnitudeMin == effect.MagnitudeMax
                    ? $"mag {effect.MagnitudeMin}"
                    : $"mag {effect.MagnitudeMin}-{effect.MagnitudeMax}");
            if (effect.Duration > 0)
                parts.Add($"{effect.Duration}s");
            if (effect.Area > 0)
                parts.Add($"area {effect.Area}");
            parts.Add(effect.Range switch
            {
                0 => "self",
                1 => "touch",
                2 => "target",
                _ => "range ?",
            });
            return string.Join(", ", parts);
        }
    }
}

