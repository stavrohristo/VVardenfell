using System;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        RuntimeHudViewModel BuildHudModel(
            bool showHud,
            bool showHudChrome,
            ref RuntimeContentBlob contentBlob,
            in InteractionPresentationState interaction,
            in RuntimeHudPreferences hudPreferences,
            in PlayerPresentationStats playerStats,
            in LocationPresentation location,
            in InventoryWindowState inventoryState,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in SpellWindowState spellState,
            in RuntimeSubtitleState subtitle,
            in RuntimeEnemyHealthBarState enemyHealth)
        {
            string itemLabel = ResolveSelectedInventoryLabel(ref contentBlob, inventoryState, inventory);
            string spellLabel = ResolveSelectedSpellLabel(ref contentBlob, playerStats, inventory, spellState, out string spellIconPath, out string spellTooltip, out float spellStatus);
            _hudModel.Visible = showHud;
            _hudModel.ShowHudChrome = showHudChrome;
            // Final crosshair visibility is gameplay state AND the user
            // preference - the Options "Show Crosshair" checkbox flips the
            // preference, hiding the crosshair even when the gameplay
            // interaction state would normally display it.
            _hudModel.ShowCrosshair = showHud && interaction.ShowCrosshair != 0 && hudPreferences.ShowCrosshair != 0;
            _hudModel.FocusText = showHud ? ResolveFocusText(interaction) : null;
            _hudModel.NotificationText = showHudChrome ? ResolveNotificationText(interaction) : null;
            _hudModel.SubtitleText = showHud ? ResolveSubtitleText(subtitle) : null;
            _hudModel.WeaponSpellText = showHudChrome ? BuildWeaponSpellText(itemLabel, spellLabel) : string.Empty;
            _hudModel.CellNameText = showHudChrome ? location.DisplayName : string.Empty;
            _hudModel.HealthFillNormalized = playerStats.HasPlayer ? playerStats.HealthFill : 0f;
            _hudModel.MagickaFillNormalized = playerStats.HasPlayer ? playerStats.MagickaFill : 0f;
            _hudModel.FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f;
            _hudModel.WeaponStatusNormalized = showHudChrome && !string.IsNullOrWhiteSpace(itemLabel) ? 1f : 0f;
            _hudModel.SpellStatusNormalized = showHudChrome && !string.IsNullOrWhiteSpace(spellLabel) ? spellStatus : 0f;
            _hudModel.SneakStatusNormalized = 0.6f;
            _hudModel.WeaponLabel = "W";
            _hudModel.SpellLabel = "S";
            _hudModel.SneakLabel = "Sneak";
            _hudModel.SelectedSpellIconPath = showHudChrome ? spellIconPath : null;
            _hudModel.SelectedSpellTooltip = showHudChrome ? spellTooltip : null;
            _hudModel.ActiveEffects = showHudChrome ? BuildHudActiveEffectIcons(ref contentBlob, playerStats) : Array.Empty<RuntimeMagicEffectIconViewModel>();
            ResolveEnemyHealthBar(showHudChrome, enemyHealth, out _hudModel.ShowEnemyHealth, out _hudModel.EnemyHealthFillNormalized, out _hudModel.EnemyHealthAlpha);
            _hudModel.ShowSneakIndicator = false;
            _hudModel.LocalMap = showHudChrome ? LocalMapPresentationCache.FillViewModel(
                _hudLocalMapModel,
                playerStats,
                location.InteriorActive,
                zoom: 5f,
                showShroud: true,
                showMarkers: false) : null;
            return _hudModel;
        }

        void ResolveEnemyHealthBar(
            bool showHud,
            in RuntimeEnemyHealthBarState enemyHealth,
            out bool show,
            out float fill,
            out float alpha)
        {
            show = false;
            fill = 0f;
            alpha = 1f;
            if (!showHud
                || enemyHealth.Visible == 0
                || enemyHealth.SecondsRemaining <= 0f
                || enemyHealth.Target == Entity.Null
                || !EntityManager.Exists(enemyHealth.Target))
            {
                return;
            }

            if (EntityManager.HasComponent<PlayerTag>(enemyHealth.Target))
                throw new InvalidOperationException("[VVardenfell][HUD] Enemy health target is the player.");
            if (!EntityManager.HasComponent<ActorVitalSet>(enemyHealth.Target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={enemyHealth.TargetPlacedRefId} has no ActorVitalSet.");

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(enemyHealth.Target);
            if (vitals.ModifiedHealthBase <= 0f)
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={enemyHealth.TargetPlacedRefId} has non-positive modified health base {vitals.ModifiedHealthBase}.");

            show = true;
            fill = vitals.CurrentHealth < 1f ? 0f : Mathf.Clamp01(vitals.CurrentHealth / vitals.ModifiedHealthBase);
            alpha = enemyHealth.FadeSeconds > 0f
                ? Mathf.Clamp01(enemyHealth.SecondsRemaining / enemyHealth.FadeSeconds)
                : 1f;
        }

        static string ResolveSelectedInventoryLabel(
            ref RuntimeContentBlob contentBlob,
            in InventoryWindowState state,
            DynamicBuffer<PlayerInventoryItem> inventory)
        {
            int selected = state.SelectedInventoryIndex;
            if (selected < 0 || selected >= inventory.Length)
                return string.Empty;

            var entry = inventory[selected];
            if (entry.Count <= 0 || !entry.Content.IsValid)
                return string.Empty;

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, entry.Content, out var metadata))
                return string.Empty;

            return metadata.DisplayName ?? string.Empty;
        }

        string ResolveSelectedSpellLabel(
            ref RuntimeContentBlob contentBlob,
            in PlayerPresentationStats playerStats,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in SpellWindowState state,
            out string iconPath,
            out string tooltip,
            out float status)
        {
            iconPath = string.Empty;
            tooltip = null;
            status = 0f;
            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorKnownSpell>(playerStats.PlayerEntity))
            {
                return string.Empty;
            }

            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell)
            {
                var handle = state.SelectedSpell;
                if (!handle.IsValid || handle.Index < 0 || handle.Index >= contentBlob.Spells.Length)
                    return string.Empty;

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
                string name = spell.Name.ToString();
                string label = string.IsNullOrWhiteSpace(name) ? spell.Id.ToString() : name;
                tooltip = label;
                var activeEffects = EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity)
                    ? EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true)
                    : default;
                status = ResolveSpellChance(ref contentBlob, ref spell, playerStats, activeEffects) / 100f;
                if (spell.EffectStartIndex >= 0 && spell.EffectCount > 0)
                {
                    int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
                    if (available > 0)
                    {
                        var effect = contentBlob.MagicEffectInstances[spell.EffectStartIndex];
                        iconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId);
                    }
                }

                return label;
            }

            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.EnchantedItem
                && state.SelectedItemContent.Kind == ContentReferenceKind.Item
                && state.SelectedItemContent.HandleValue > 0)
            {
                ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref contentBlob, new ItemDefHandle { Value = state.SelectedItemContent.HandleValue });
                string label = RuntimeContentMetadataResolver.ResolveDisplayName(ref item, "Magic Item");
                tooltip = label;
                iconPath = item.Icon.ToString();
                status = ResolveSelectedEnchantedItemCharge(ref contentBlob, inventory, state);
                return label;
            }

            return string.Empty;
        }

        static float ResolveSelectedEnchantedItemCharge(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in SpellWindowState state)
        {
            if (state.SelectedInventoryIndex < 0 || state.SelectedInventoryIndex >= inventory.Length)
                return 0f;

            var item = inventory[state.SelectedInventoryIndex];
            if (item.Count <= 0
                || item.Content.Kind != state.SelectedItemContent.Kind
                || item.Content.HandleValue != state.SelectedItemContent.HandleValue
                || !state.SelectedEnchantment.IsValid)
            {
                return 0f;
            }

            ref RuntimeEnchantmentDefBlob enchantment = ref RuntimeContentBlobUtility.Get(ref contentBlob, state.SelectedEnchantment);
            if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce)
                return 1f;

            float maxCharge = MorrowindEnchantmentUtility.CalculateCharge(ref contentBlob, ref enchantment);
            float currentCharge = MorrowindEnchantmentUtility.ResolveCurrentCharge(ref contentBlob, ref enchantment, item.EnchantmentCharge);
            return maxCharge > 0f ? Mathf.Clamp01(currentCharge / maxCharge) : 0f;
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
