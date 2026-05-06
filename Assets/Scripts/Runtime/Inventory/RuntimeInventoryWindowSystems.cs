using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct InventoryWindowStateSystem : ISystem
    {
        EntityQuery _playerInventoryQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerInventoryQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());

            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<InventoryWindowState>();
            systemState.RequireForUpdate<InventoryWindowRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate(_playerInventoryQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<InventoryWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<InventoryWindowRequest>().ValueRW;

            ApplyRequests(ref state, ref request);

            state.Visible = shell.InventoryOpen;

            if (state.Visible == 0)
            {
                state.SelectedItemDetailsText = default;
                return;
            }

            Entity playerInventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(playerInventoryEntity, true);
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            state.ActiveCategory = (byte)ClampCategory((InventoryWindowCategory)state.ActiveCategory);

            int selectedIndex = ValidateSelectedIndex(ref contentBlob, inventory, ref state);
            if (selectedIndex < 0)
                selectedIndex = FindFirstVisibleInventoryIndex(ref contentBlob, inventory, state);

            state.SelectedInventoryIndex = selectedIndex;
            state.SelectedItemDetailsText = RuntimeFixedStringUtility.ToFixed512DetailsOrDefault(BuildSelectedItemDetails(ref contentBlob, inventory, selectedIndex));
        }

        static void ApplyRequests(ref InventoryWindowState state, ref InventoryWindowRequest request)
        {
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);

            if (request.PendingCategoryChange != 0)
                state.ActiveCategory = (byte)ClampCategory((InventoryWindowCategory)request.ActiveCategory);

            if (request.PendingFilterTextChange != 0)
                state.FilterText = request.FilterText;

            if (request.PendingSelectionChange != 0)
                state.SelectedInventoryIndex = request.SelectedInventoryIndex;

            request = default;
        }

        static int ValidateSelectedIndex(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, ref InventoryWindowState state)
        {
            int selectedIndex = state.SelectedInventoryIndex;
            if (selectedIndex < 0 || selectedIndex >= inventory.Length)
                return -1;

            return MatchesFilters(ref contentBlob, inventory[selectedIndex], state) ? selectedIndex : -1;
        }

        static int FindFirstVisibleInventoryIndex(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, in InventoryWindowState state)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (MatchesFilters(ref contentBlob, inventory[i], state))
                    return i;
            }

            return -1;
        }

        public static bool MatchesFilters(ref RuntimeContentBlob contentBlob, PlayerInventoryItem entry, in InventoryWindowState state)
        {
            if (!entry.Content.IsValid)
                return false;

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, entry.Content, out var metadata))
                return false;

            if (!RuntimeContentMetadataResolver.MatchesCategory(metadata, (InventoryWindowCategory)state.ActiveCategory))
                return false;

            string filter = state.FilterText.ToString();
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return metadata.DisplayName.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string BuildSelectedItemDetails(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= inventory.Length)
                return "Select an item to inspect.";

            var entry = inventory[selectedIndex];
            if (!RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, entry.Content, out var metadata))
                return "Select an item to inspect.";

            return RuntimeContentMetadataResolver.BuildCarryableDetails(metadata, entry.Count);
        }

        static InventoryWindowCategory ClampCategory(InventoryWindowCategory category)
        {
            return category is >= InventoryWindowCategory.All and <= InventoryWindowCategory.Misc
                ? category
                : InventoryWindowCategory.All;
        }

    }
}
