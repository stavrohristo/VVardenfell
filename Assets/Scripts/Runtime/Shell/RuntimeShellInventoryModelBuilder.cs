using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static InventoryWindowViewModel BuildInventoryModel(
            ref RuntimeContentBlob contentBlob,
            in InventoryWindowState state,
            DynamicBuffer<PlayerInventoryItem> inventory,
            in PlayerPresentationStats playerStats,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            var viewModel = new InventoryWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
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
                if (RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, entry.Content, out var metadata))
                {
                    if (metadata.Weight >= 0f)
                    {
                        totalWeight += metadata.Weight * count;
                        hasWeightData = true;
                    }
                }

                if (!InventoryWindowStateSystem.MatchesFilters(ref contentBlob, entry, state))
                    continue;

                visibleCount++;
                entries.Add(RuntimeShellCarryableEntryBuilder.Build(
                    ref contentBlob,
                    entry.Content,
                    entry.Count,
                    i,
                    i == state.SelectedInventoryIndex,
                    ResolveEquippedState(equipment, entry, i)));
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

        static bool ResolveEquippedState(DynamicBuffer<ActorEquipmentSlot> equipment, PlayerInventoryItem entry, int inventoryIndex)
        {
            if (!equipment.IsCreated)
                return false;

            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex
                    && slot.Content.Kind == entry.Content.Kind
                    && slot.Content.HandleValue == entry.Content.HandleValue)
                {
                    return true;
                }
            }

            return false;
        }

        static ContainerWindowViewModel BuildContainerModel(
            ref RuntimeContentBlob contentBlob,
            in ContainerWindowState state,
            DynamicBuffer<ContainerSessionItem> items)
        {
            var viewModel = new ContainerWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
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

                entries.Add(RuntimeShellCarryableEntryBuilder.Build(
                    ref contentBlob,
                    entry.Content,
                    entry.Count,
                    i,
                    i == state.SelectedItemIndex));
            }

            viewModel.CanTakeSelected = state.SelectedItemIndex >= 0 && state.SelectedItemIndex < items.Length
                && items[state.SelectedItemIndex].PlacedRefId == state.OpenPlacedRefId
                && items[state.SelectedItemIndex].Count > 0;
            viewModel.CanTakeAll = entries.Count > 0;
            viewModel.EmptyStateText = entries.Count == 0 ? "Empty" : string.Empty;
            viewModel.Entries = entries.ToArray();
            return viewModel;
        }

    }
}

