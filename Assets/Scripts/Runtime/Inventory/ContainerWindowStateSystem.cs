using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
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
            state.SelectedItemDetailsText = ContainerLootUtility.ToFixedDetails(BuildSelectedItemDetails(contentDb, items, state.OpenPlacedRefId, selectedIndex));
        }

        static void ApplyRequests(ref ContainerWindowState state, ref ContainerWindowRequest request)
        {
            if (request.PendingRectUpdate != 0)
            {
                state.NormalizedX = Clamp01(request.NormalizedX);
                state.NormalizedY = Clamp01(request.NormalizedY);
                state.NormalizedWidth = ClampDimension(request.NormalizedWidth, state.NormalizedWidth);
                state.NormalizedHeight = ClampDimension(request.NormalizedHeight, state.NormalizedHeight);

                if (state.NormalizedX + state.NormalizedWidth > 1f)
                    state.NormalizedX = Math.Max(0f, 1f - state.NormalizedWidth);
                if (state.NormalizedY + state.NormalizedHeight > 1f)
                    state.NormalizedY = Math.Max(0f, 1f - state.NormalizedHeight);
            }

            if (request.PendingSelectionChange != 0)
                state.SelectedItemIndex = request.SelectedItemIndex;

            request.PendingRectUpdate = 0;
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

            if (!InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                return "Container is empty.";

            return InventoryWindowStateSystem.BuildCarryableDetails(metadata, Math.Max(1, entry.Count));
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return Math.Clamp(value, 0f, 1f);
        }

        static float ClampDimension(float requested, float fallback)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0f)
                requested = fallback > 0f ? fallback : 0.1f;

            return Math.Clamp(requested, 0.1f, 1f);
        }
    }
}
