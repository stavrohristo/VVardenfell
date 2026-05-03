using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class ContainerWindowStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<ContainerWindowRequest>();
            RequireForUpdate<ContainerSessionItem>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<ContainerWindowRequest>().ValueRW;
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var contentDb = RuntimeContentDatabase.Active;

            ApplyRequests(ref state, ref request);
            state.Visible = shell.ContainerOpen;

            if (state.Visible == 0 || state.OpenPlacedRefId == 0u)
            {
                state.SelectedItemDetailsText = default;
                state.SelectedItemIndex = -1;
                return;
            }

            int selectedIndex = ValidateSelection(items, state.OpenPlacedRefId, state.SelectedItemIndex);
            if (selectedIndex < 0)
                selectedIndex = ContainerLootUtility.FindFirstItemIndex(items, state.OpenPlacedRefId);

            state.SelectedItemIndex = selectedIndex;
            state.SelectedItemDetailsText = RuntimeFixedStringUtility.ToFixed512DetailsOrDefault(BuildSelectedItemDetails(contentDb, items, state.OpenPlacedRefId, selectedIndex));
        }

        static void ApplyRequests(ref ContainerWindowState state, ref ContainerWindowRequest request)
        {
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);

            if (request.PendingSelectionChange != 0)
                state.SelectedItemIndex = request.SelectedItemIndex;

            request.PendingSelectionChange = 0;
        }

        static int ValidateSelection(DynamicBuffer<ContainerSessionItem> items, uint placedRefId, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= items.Length)
                return -1;

            var entry = items[selectedIndex];
            return entry.PlacedRefId == placedRefId && entry.Count > 0
                ? selectedIndex
                : -1;
        }

        static string BuildSelectedItemDetails(RuntimeContentDatabase contentDb, DynamicBuffer<ContainerSessionItem> items, uint placedRefId, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= items.Length)
                return "Container is empty.";

            var entry = items[selectedIndex];
            if (entry.PlacedRefId != placedRefId || entry.Count <= 0 || contentDb == null)
                return "Container is empty.";

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(contentDb, entry.Content, out var metadata))
                return "Container is empty.";

            return RuntimeContentMetadataResolver.BuildCarryableDetails(metadata, Math.Max(1, entry.Count));
        }

    }
}
