using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [BurstCompile]
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

        [BurstCompile]
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
            state.SelectedItemDetailsText = BuildSelectedItemDetails(ref contentBlob, inventory, selectedIndex);
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

            if (!RuntimeContentMetadataResolver.TryResolveCarryableFixed(ref contentBlob, entry.Content, out var metadata))
                return false;

            if (!RuntimeContentMetadataResolver.MatchesCategory(metadata, (InventoryWindowCategory)state.ActiveCategory))
                return false;

            FixedString64Bytes filter = TrimAscii(state.FilterText);
            if (filter.IsEmpty)
                return true;

            return ContainsIgnoreCase(metadata.DisplayName, filter);
        }

        static FixedString512Bytes BuildSelectedItemDetails(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= inventory.Length)
                return RuntimeContentMetadataResolver.BuildSelectItemDetailsFixed();

            var entry = inventory[selectedIndex];
            if (!RuntimeContentMetadataResolver.TryResolveCarryableFixed(ref contentBlob, entry.Content, out var metadata))
                return RuntimeContentMetadataResolver.BuildSelectItemDetailsFixed();

            return RuntimeContentMetadataResolver.BuildCarryableDetailsFixed(metadata, entry.Count);
        }

        static InventoryWindowCategory ClampCategory(InventoryWindowCategory category)
        {
            return category is >= InventoryWindowCategory.All and <= InventoryWindowCategory.Misc
                ? category
                : InventoryWindowCategory.All;
        }

        static FixedString64Bytes TrimAscii(FixedString64Bytes value)
        {
            int start = 0;
            int end = value.Length - 1;
            while (start <= end && IsAsciiWhiteSpace(value[start]))
                start++;
            while (end >= start && IsAsciiWhiteSpace(value[end]))
                end--;

            var result = default(FixedString64Bytes);
            for (int i = start; i <= end; i++)
                result.Append((char)value[i]);
            return result;
        }

        static bool ContainsIgnoreCase(FixedString128Bytes value, FixedString64Bytes needle)
        {
            if (needle.Length == 0)
                return true;
            if (needle.Length > value.Length)
                return false;

            int lastStart = value.Length - needle.Length;
            for (int start = 0; start <= lastStart; start++)
            {
                bool matched = true;
                for (int i = 0; i < needle.Length; i++)
                {
                    if (ToLowerAscii(value[start + i]) == ToLowerAscii(needle[i]))
                        continue;

                    matched = false;
                    break;
                }

                if (matched)
                    return true;
            }

            return false;
        }

        static byte ToLowerAscii(byte value)
            => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

        static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v';

    }
}
