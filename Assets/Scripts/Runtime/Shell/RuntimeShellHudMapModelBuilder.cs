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
            _hudModel.Visible = showHud;
            // Final crosshair visibility is gameplay state AND the user
            // preference - the Options "Show Crosshair" checkbox flips the
            // preference, hiding the crosshair even when the gameplay
            // interaction state would normally display it.
            _hudModel.ShowCrosshair = showHud && interaction.ShowCrosshair != 0 && HudUserPreferences.ShowCrosshair;
            _hudModel.FocusText = showHud ? ResolveFocusText(interaction) : null;
            _hudModel.NotificationText = showHud ? ResolveNotificationText(interaction) : null;
            _hudModel.WeaponSpellText = showHud ? BuildWeaponSpellText(itemLabel, spellLabel) : string.Empty;
            _hudModel.CellNameText = showHud ? location.DisplayName : string.Empty;
            _hudModel.HealthFillNormalized = playerStats.HasPlayer ? playerStats.HealthFill : 0f;
            _hudModel.MagickaFillNormalized = playerStats.HasPlayer ? playerStats.MagickaFill : 0f;
            _hudModel.FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f;
            _hudModel.WeaponStatusNormalized = string.IsNullOrWhiteSpace(itemLabel) ? 0f : 1f;
            _hudModel.SpellStatusNormalized = string.IsNullOrWhiteSpace(spellLabel) ? 0f : 1f;
            _hudModel.SneakStatusNormalized = 0.6f;
            _hudModel.WeaponLabel = "W";
            _hudModel.SpellLabel = "S";
            _hudModel.SneakLabel = "Sneak";
            _hudModel.SelectedSpellIconPath = spellIconPath;
            _hudModel.SelectedSpellTooltip = spellTooltip;
            _hudModel.ActiveEffects = BuildHudActiveEffectIcons(contentDb, playerStats);
            _hudModel.ShowEnemyHealth = false;
            _hudModel.EnemyHealthFillNormalized = 0f;
            _hudModel.ShowSneakIndicator = false;
            _hudModel.LocalMap = showHud ? LocalMapPresentationCache.FillViewModel(
                _hudLocalMapModel,
                playerStats,
                location.InteriorActive,
                zoom: 5f,
                showShroud: true,
                showMarkers: false) : null;
            return _hudModel;
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
            string label = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id ?? string.Empty : spell.Name;
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

        string BuildWeaponSpellText(string itemLabel, string spellLabel)
        {
            bool hasItem = !string.IsNullOrWhiteSpace(itemLabel);
            bool hasSpell = !string.IsNullOrWhiteSpace(spellLabel);
            itemLabel = hasItem ? itemLabel : string.Empty;
            spellLabel = hasSpell ? spellLabel : string.Empty;
            if (string.Equals(_lastWeaponLabel, itemLabel, StringComparison.Ordinal)
                && string.Equals(_lastSpellLabel, spellLabel, StringComparison.Ordinal))
                return _cachedWeaponSpellText;

            _lastWeaponLabel = itemLabel;
            _lastSpellLabel = spellLabel;
            if (hasItem && hasSpell)
                _cachedWeaponSpellText = string.Concat(itemLabel, "  |  ", spellLabel);
            else if (hasItem)
                _cachedWeaponSpellText = itemLabel;
            else if (hasSpell)
                _cachedWeaponSpellText = spellLabel;
            else
                _cachedWeaponSpellText = string.Empty;

            return _cachedWeaponSpellText;
        }

        static MapWindowViewModel BuildMapModel(in MapWindowState state, in LocationPresentation location, in PlayerPresentationStats playerStats)
        {
            var mode = state.Mode == (byte)MapWindowMode.Global ? MapWindowMode.Global : MapWindowMode.Local;
            return new MapWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
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
