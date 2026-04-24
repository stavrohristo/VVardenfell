using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial class StatsWindowStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<StatsWindowState>();
            RequireForUpdate<StatsWindowRequest>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<StatsWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<StatsWindowRequest>().ValueRW;
            RuntimeSuiteWindowStateUtility.ApplyRectRequest(ref state.NormalizedX, ref state.NormalizedY, ref state.NormalizedWidth, ref state.NormalizedHeight, ref request.PendingRectUpdate, request.NormalizedX, request.NormalizedY, request.NormalizedWidth, request.NormalizedHeight);
            state.Visible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 ? (byte)1 : (byte)0;
            request = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial class SpellWindowStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<SpellWindowState>();
            RequireForUpdate<SpellWindowRequest>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<SpellWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<SpellWindowRequest>().ValueRW;
            RuntimeSuiteWindowStateUtility.ApplyRectRequest(ref state.NormalizedX, ref state.NormalizedY, ref state.NormalizedWidth, ref state.NormalizedHeight, ref request.PendingRectUpdate, request.NormalizedX, request.NormalizedY, request.NormalizedWidth, request.NormalizedHeight);
            if (request.PendingSelectionChange != 0)
                state.SelectedSpellIndex = request.SelectedSpellIndex;
            if (request.PendingFilterTextChange != 0)
                state.FilterText = request.FilterText;
            state.Visible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 ? (byte)1 : (byte)0;
            request = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial class MapWindowStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<MapWindowState>();
            RequireForUpdate<MapWindowRequest>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<MapWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<MapWindowRequest>().ValueRW;
            bool wasVisible = state.Visible != 0;
            byte desiredVisible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 ? (byte)1 : (byte)0;
            RuntimeSuiteWindowStateUtility.ApplyRectRequest(ref state.NormalizedX, ref state.NormalizedY, ref state.NormalizedWidth, ref state.NormalizedHeight, ref request.PendingRectUpdate, request.NormalizedX, request.NormalizedY, request.NormalizedWidth, request.NormalizedHeight);
            if (request.PendingModeChange != 0)
                state.Mode = NormalizeMapMode(request.Mode);
            else
                state.Mode = NormalizeMapMode(state.Mode);
            NormalizeMapViewport(ref state);
            if (request.PendingViewportChange != 0)
            {
                if (NormalizeMapMode(request.ViewportMode) == (byte)MapWindowMode.Global)
                {
                    state.GlobalPanX = request.PanX;
                    state.GlobalPanY = request.PanY;
                    state.GlobalZoom = ClampZoom(request.Zoom, state.GlobalZoom);
                }
                else
                {
                    state.LocalPanX = request.PanX;
                    state.LocalPanY = request.PanY;
                    state.LocalZoom = ClampZoom(request.Zoom, state.LocalZoom);
                }
            }
            if (request.PendingCenterOnPlayer != 0)
            {
                if (state.Mode == (byte)MapWindowMode.Global)
                {
                    state.GlobalPanX = 0f;
                    state.GlobalPanY = 0f;
                }
                else
                {
                    state.LocalPanX = 0f;
                    state.LocalPanY = 0f;
                }
            }
            if (!wasVisible && desiredVisible != 0 && state.Pinned == 0)
            {
                state.LocalPanX = 0f;
                state.LocalPanY = 0f;
                state.GlobalPanX = 0f;
                state.GlobalPanY = 0f;
            }
            state.Visible = desiredVisible;
            request = default;
        }

        static byte NormalizeMapMode(byte mode)
        {
            return mode == (byte)MapWindowMode.Global ? (byte)MapWindowMode.Global : (byte)MapWindowMode.Local;
        }

        static void NormalizeMapViewport(ref MapWindowState state)
        {
            state.LocalZoom = ClampZoom(state.LocalZoom, 1f);
            state.GlobalZoom = ClampZoom(state.GlobalZoom, 1f);
        }

        static float ClampZoom(float requested, float fallback)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0f)
                requested = fallback > 0f ? fallback : 1f;
            return Math.Clamp(requested, 0.125f, 4f);
        }
    }

    static class RuntimeSuiteWindowStateUtility
    {
        public static void ApplyRectRequest(
            ref float normalizedX,
            ref float normalizedY,
            ref float normalizedWidth,
            ref float normalizedHeight,
            ref byte pendingRectUpdate,
            float requestX,
            float requestY,
            float requestWidth,
            float requestHeight)
        {
            if (pendingRectUpdate == 0)
                return;

            normalizedX = Clamp01(requestX);
            normalizedY = Clamp01(requestY);
            normalizedWidth = ClampDimension(requestWidth, normalizedWidth);
            normalizedHeight = ClampDimension(requestHeight, normalizedHeight);

            if (normalizedX + normalizedWidth > 1f)
                normalizedX = Math.Max(0f, 1f - normalizedWidth);
            if (normalizedY + normalizedHeight > 1f)
                normalizedY = Math.Max(0f, 1f - normalizedHeight);

            pendingRectUpdate = 0;
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
