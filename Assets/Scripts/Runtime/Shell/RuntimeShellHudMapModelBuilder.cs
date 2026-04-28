using System;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        RuntimeHudViewModel BuildHudModel(
            bool showHud,
            RuntimeContentDatabase contentDb,
            in InteractionPresentationState interaction,
            in PlayerPresentationStats playerStats,
            in LocationPresentation location,
            in InventoryWindowState inventoryState,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in SpellWindowState spellState)
        {
            string itemLabel = ResolveSelectedInventoryLabel(contentDb, inventoryState, inventory);
            string spellLabel = ResolveSelectedSpellLabel(contentDb, playerStats, spellState, out string spellIconPath, out string spellTooltip);
            return new RuntimeHudViewModel
            {
                Visible = showHud,
                // Final crosshair visibility is gameplay state AND the user
                // preference - the Options "Show Crosshair" checkbox flips the
                // preference, hiding the crosshair even when the gameplay
                // interaction state would normally display it.
                ShowCrosshair = showHud && interaction.ShowCrosshair != 0 && HudUserPreferences.ShowCrosshair,
                FocusText = showHud && interaction.ShowFocus != 0 ? interaction.FocusText.ToString() : null,
                NotificationText = showHud && interaction.ShowNotification != 0 ? interaction.NotificationText.ToString() : null,
                WeaponSpellText = showHud ? BuildWeaponSpellText(itemLabel, spellLabel) : string.Empty,
                CellNameText = showHud ? location.DisplayName : string.Empty,
                HealthFillNormalized = playerStats.HasPlayer ? playerStats.HealthFill : 0f,
                MagickaFillNormalized = playerStats.HasPlayer ? playerStats.MagickaFill : 0f,
                FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f,
                WeaponStatusNormalized = string.IsNullOrWhiteSpace(itemLabel) ? 0f : 1f,
                SpellStatusNormalized = string.IsNullOrWhiteSpace(spellLabel) ? 0f : 1f,
                SneakStatusNormalized = 0.6f,
                WeaponLabel = "W",
                SpellLabel = "S",
                SneakLabel = "Sneak",
                SelectedSpellIconPath = spellIconPath,
                SelectedSpellTooltip = spellTooltip,
                ActiveEffects = BuildActiveEffectIcons(contentDb, playerStats),
                ShowEnemyHealth = false,
                EnemyHealthFillNormalized = 0f,
                ShowSneakIndicator = false,
                LocalMap = showHud ? LocalMapPresentationCache.BuildViewModel(
                    playerStats,
                    location.InteriorActive,
                    zoom: 5f,
                    showShroud: true,
                    showMarkers: false) : null,
            };
        }

        static string ResolveSelectedInventoryLabel(
            RuntimeContentDatabase contentDb,
            in InventoryWindowState state,
            DynamicBuffer<PlayerInventoryItem> inventory)
        {
            int selected = state.SelectedInventoryIndex;
            if (selected < 0 || selected >= inventory.Length)
                return string.Empty;

            var entry = inventory[selected];
            if (entry.Count <= 0 || !entry.Content.IsValid)
                return string.Empty;

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(contentDb, entry.Content, out var metadata))
                return string.Empty;

            return metadata.DisplayName ?? string.Empty;
        }

        string ResolveSelectedSpellLabel(
            RuntimeContentDatabase contentDb,
            in PlayerPresentationStats playerStats,
            in SpellWindowState state,
            out string iconPath,
            out string tooltip)
        {
            iconPath = string.Empty;
            tooltip = null;
            if (!playerStats.HasPlayer
                || contentDb == null
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<PlayerKnownSpell>(playerStats.PlayerEntity))
            {
                return string.Empty;
            }

            var knownSpells = EntityManager.GetBuffer<PlayerKnownSpell>(playerStats.PlayerEntity);
            int selected = state.SelectedSpellIndex;
            if (selected < 0 || selected >= knownSpells.Length)
                return string.Empty;

            var handle = knownSpells[selected].Spell;
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= contentDb.Data.Spells.Length)
                return string.Empty;

            ref readonly var spell = ref contentDb.Get(handle);
            string label = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id ?? string.Empty : spell.Name.Trim();
            tooltip = label;
            if (spell.EffectStartIndex >= 0 && spell.EffectCount > 0 && contentDb.Data.MagicEffectInstances != null)
            {
                int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
                if (available > 0)
                {
                    var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex];
                    iconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(contentDb, effect.EffectId);
                }
            }

            return label;
        }

        static string BuildWeaponSpellText(string itemLabel, string spellLabel)
        {
            bool hasItem = !string.IsNullOrWhiteSpace(itemLabel);
            bool hasSpell = !string.IsNullOrWhiteSpace(spellLabel);
            if (hasItem && hasSpell)
                return $"{itemLabel}  |  {spellLabel}";
            if (hasItem)
                return itemLabel;
            if (hasSpell)
                return spellLabel;
            return string.Empty;
        }

        static MapWindowViewModel BuildMapModel(in MapWindowState state, in LocationPresentation location, in PlayerPresentationStats playerStats)
        {
            var mode = state.Mode == (byte)MapWindowMode.Global ? MapWindowMode.Global : MapWindowMode.Local;
            return new MapWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Map",
                Mode = mode,
                GlobalEnabled = !location.InteriorActive,
                ToggleButtonText = mode == MapWindowMode.Global ? "Local" : "World",
                ViewSummaryText = location.InteriorActive ? "Interior local map data is not rendered yet." : string.Empty,
                LocationText = string.IsNullOrWhiteSpace(location.DisplayName) ? "--" : location.DisplayName,
                RegionText = location.RegionText,
                CellText = location.CellText,
                StreamingText = location.StreamingText,
                InteriorActive = location.InteriorActive,
                LocalMap = LocalMapPresentationCache.BuildViewModel(
                    playerStats,
                    location.InteriorActive,
                    zoom: state.LocalZoom <= 0f ? 1f : state.LocalZoom,
                    panCellX: state.LocalPanX,
                    panCellY: state.LocalPanY,
                    showShroud: true),
                GlobalMap = GlobalMapPresentationCache.BuildViewModel(
                    playerStats,
                    state.GlobalPanX,
                    state.GlobalPanY,
                    state.GlobalZoom <= 0f ? 1f : state.GlobalZoom),
            };
        }
    }
}
