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
    public partial class InventoryWindowStateSystem : SystemBase
    {
        EntityQuery _playerInventoryQuery;

        protected override void OnCreate()
        {
            _playerInventoryQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());

            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<InventoryWindowRequest>();
            RequireForUpdate(_playerInventoryQuery);
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<InventoryWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<InventoryWindowRequest>().ValueRW;
            Entity playerInventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(playerInventoryEntity, true);
            var contentDb = RuntimeContentDatabase.Active;

            ApplyRequests(ref state, ref request);

            state.Visible = shell.InventoryOpen;

            if (state.Visible == 0)
            {
                state.SelectedItemDetailsText = default;
                return;
            }

            state.ActiveCategory = (byte)ClampCategory((InventoryWindowCategory)state.ActiveCategory);

            int selectedIndex = ValidateSelectedIndex(contentDb, inventory, ref state);
            if (selectedIndex < 0)
                selectedIndex = FindFirstVisibleInventoryIndex(contentDb, inventory, state);

            state.SelectedInventoryIndex = selectedIndex;
            state.SelectedItemDetailsText = RuntimeFixedStringUtility.ToFixed512DetailsOrDefault(BuildSelectedItemDetails(contentDb, inventory, selectedIndex));
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

        static int ValidateSelectedIndex(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, ref InventoryWindowState state)
        {
            int selectedIndex = state.SelectedInventoryIndex;
            if (selectedIndex < 0 || selectedIndex >= inventory.Length)
                return -1;

            return MatchesFilters(contentDb, inventory[selectedIndex], state) ? selectedIndex : -1;
        }

        static int FindFirstVisibleInventoryIndex(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, in InventoryWindowState state)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (MatchesFilters(contentDb, inventory[i], state))
                    return i;
            }

            return -1;
        }

        public static bool MatchesFilters(RuntimeContentDatabase contentDb, PlayerInventoryItem entry, in InventoryWindowState state)
        {
            if (contentDb == null || !entry.Content.IsValid)
                return false;

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(contentDb, entry.Content, out var metadata))
                return false;

            if (!RuntimeContentMetadataResolver.MatchesCategory(metadata, (InventoryWindowCategory)state.ActiveCategory))
                return false;

            string filter = state.FilterText.ToString();
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return metadata.DisplayName.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string BuildSelectedItemDetails(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, int selectedIndex)
        {
            if (contentDb == null || selectedIndex < 0 || selectedIndex >= inventory.Length)
                return "Select an item to inspect.";

            var entry = inventory[selectedIndex];
            if (!RuntimeContentMetadataResolver.TryResolveCarryable(contentDb, entry.Content, out var metadata))
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
