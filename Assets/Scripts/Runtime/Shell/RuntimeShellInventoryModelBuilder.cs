using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static InventoryWindowViewModel BuildInventoryModel(
            RuntimeContentDatabase contentDb,
            in InventoryWindowState state,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in PlayerPresentationStats playerStats)
        {
            var viewModel = new InventoryWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Inventory",
                Category = (InventoryWindowCategory)state.ActiveCategory,
                FilterText = state.FilterText.ToString(),
                ArmorSummary = "Armor Rating --",
                DetailText = string.IsNullOrWhiteSpace(state.SelectedItemDetailsText.ToString())
                    ? "Select an item to inspect."
                    : state.SelectedItemDetailsText.ToString(),
                WeightBarFillNormalized = playerStats.HasPlayer ? playerStats.EncumbranceFill : 0f,
            };

            var entries = new List<InventoryWindowEntryViewModel>(inventory.Length);
            int visibleCount = 0;
            float totalWeight = 0f;
            bool hasWeightData = false;

            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                int count = Math.Max(1, entry.Count);
                if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                {
                    if (metadata.Weight >= 0f)
                    {
                        totalWeight += metadata.Weight * count;
                        hasWeightData = true;
                    }
                }

                if (!InventoryWindowStateSystem.MatchesFilters(contentDb, entry, state))
                    continue;

                visibleCount++;
                entries.Add(BuildEntryViewModel(contentDb, entry, i, i == state.SelectedInventoryIndex));
            }

            viewModel.WeightLabel = hasWeightData && playerStats.HasPlayer
                ? $"Encumbrance {playerStats.DerivedMovement.Encumbrance:0.0} / {playerStats.DerivedMovement.CarryCapacity:0.0}"
                : hasWeightData
                ? $"Encumbrance {totalWeight:0.0} / --"
                : "Encumbrance -- / --";
            viewModel.DetailText = visibleCount == 0
                ? "No items match the current filter."
                : viewModel.DetailText;
            viewModel.Entries = entries.ToArray();
            return viewModel;
        }

        static InventoryWindowEntryViewModel BuildEntryViewModel(RuntimeContentDatabase contentDb, PlayerInventoryItem entry, int inventoryIndex, bool selected)
        {
            string name = "Unknown item";
            string iconPath = string.Empty;
            string weightText = "--";
            string valueText = "--";

            if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
            {
                name = metadata.DisplayName;
                iconPath = metadata.IconPath ?? string.Empty;
                if (metadata.Weight >= 0f)
                    weightText = metadata.Weight.ToString("0.0");
                if (metadata.Value >= 0)
                    valueText = metadata.Value.ToString();
            }

            // Equipped gating hook. When the equipment system lands, swap this
            // for a query against ActorEquipmentSlots (or similar) that asks
            // "is PlayerInventoryItem[inventoryIndex] currently equipped in
            // any slot?". The inventory grid reads this flag and renders the
            // gold MW_Box filigree border only around equipped tiles,
            // matching vanilla MW. Until equipment lands, every item
            // reports false and the grid renders borderless.
            bool equipped = ResolveEquippedState(entry, inventoryIndex);

            return new InventoryWindowEntryViewModel
            {
                InventoryIndex = inventoryIndex,
                Name = name,
                IconPath = iconPath,
                CountText = Math.Max(1, entry.Count).ToString(),
                WeightText = weightText,
                ValueText = valueText,
                SecondaryLeftText = $"wt {weightText}",
                SecondaryRightText = $"val {valueText}",
                EquippedText = equipped ? "Equipped" : string.Empty,
                Selected = selected,
                Equipped = equipped,
            };
        }

        /// <summary>
        /// Placeholder stub for the inventory→equipment lookup. The equipment
        /// system is not online yet; this always returns false so the
        /// inventory renders borderless today. Replace the body with a real
        /// slot query (weapon / cuirass / helmet / gauntlets / etc.) once
        /// ActorEquipmentSlots (or whatever the eventual component is named)
        /// exists. The caller is in BuildEntryViewModel which has access to
        /// PlayerPresentationStats through the outer BuildInventoryModel
        /// scope if slot lookup needs it.
        /// </summary>
        static bool ResolveEquippedState(PlayerInventoryItem entry, int inventoryIndex)
        {
            _ = entry;
            _ = inventoryIndex;
            return false;
        }

        static ContainerWindowViewModel BuildContainerModel(
            RuntimeContentDatabase contentDb,
            in ContainerWindowState state,
            DynamicBuffer<ContainerSessionItem> items)
        {
            var viewModel = new ContainerWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = string.IsNullOrWhiteSpace(state.Title.ToString()) ? "Container" : state.Title.ToString(),
                DetailText = string.IsNullOrWhiteSpace(state.SelectedItemDetailsText.ToString())
                    ? "Container is empty."
                    : state.SelectedItemDetailsText.ToString(),
            };

            var entries = new List<InventoryWindowEntryViewModel>();
            for (int i = 0; i < items.Length; i++)
            {
                var entry = items[i];
                if (entry.PlacedRefId != state.OpenPlacedRefId || entry.Count <= 0)
                    continue;

                entries.Add(BuildContainerEntryViewModel(contentDb, entry, i, i == state.SelectedItemIndex));
            }

            viewModel.CanTakeSelected = state.SelectedItemIndex >= 0 && state.SelectedItemIndex < items.Length
                && items[state.SelectedItemIndex].PlacedRefId == state.OpenPlacedRefId
                && items[state.SelectedItemIndex].Count > 0;
            viewModel.CanTakeAll = entries.Count > 0;
            viewModel.EmptyStateText = entries.Count == 0 ? "Empty" : string.Empty;
            viewModel.Entries = entries.ToArray();
            return viewModel;
        }

        static InventoryWindowEntryViewModel BuildContainerEntryViewModel(RuntimeContentDatabase contentDb, ContainerSessionItem entry, int itemIndex, bool selected)
        {
            string name = "Unknown item";
            string iconPath = string.Empty;
            string weightText = "--";
            string valueText = "--";

            if (InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
            {
                name = metadata.DisplayName;
                iconPath = metadata.IconPath ?? string.Empty;
                if (metadata.Weight >= 0f)
                    weightText = metadata.Weight.ToString("0.0");
                if (metadata.Value >= 0)
                    valueText = metadata.Value.ToString();
            }

            return new InventoryWindowEntryViewModel
            {
                InventoryIndex = itemIndex,
                Name = name,
                IconPath = iconPath,
                CountText = Math.Max(1, entry.Count).ToString(),
                WeightText = weightText,
                ValueText = valueText,
                SecondaryLeftText = $"wt {weightText}",
                SecondaryRightText = $"val {valueText}",
                EquippedText = string.Empty,
                Selected = selected,
            };
        }
    }
}

