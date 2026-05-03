using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
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
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);
            state.Visible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 && shell.StatsMenuDisabled == 0 ? (byte)1 : (byte)0;
            request = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
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
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);
            if (request.PendingSelectionChange != 0)
                state.SelectedSpellIndex = request.SelectedSpellIndex;
            if (request.PendingFilterTextChange != 0)
                state.FilterText = request.FilterText;
            state.Visible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 && shell.MagicMenuDisabled == 0 ? (byte)1 : (byte)0;
            request = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
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
            byte desiredVisible = shell.InventoryOpen != 0 && shell.ContainerOpen == 0 && shell.MapMenuDisabled == 0 ? (byte)1 : (byte)0;
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);
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
}
