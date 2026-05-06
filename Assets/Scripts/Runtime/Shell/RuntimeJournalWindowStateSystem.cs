using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial struct JournalWindowStateSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<JournalWindowState>();
            systemState.RequireForUpdate<JournalWindowRequest>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<JournalWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<JournalWindowRequest>().ValueRW;

            bool opening = state.Visible == 0 && shell.JournalOpen != 0;
            RuntimeWindowGeometryUtility.ApplyRectRequest(ref state.Rect, ref request.RectRequest);
            if (opening)
            {
                state.Mode = 0;
                state.OverlayOpen = 0;
                state.Page = -1;
            }
            if (request.PendingShowAllChange != 0)
                state.ShowAll = request.ShowAll == 0 ? (byte)0 : (byte)1;
            if (request.PendingSelectionChange != 0)
                state.SelectedDialogueIndex = request.SelectedDialogueIndex;
            if (request.PendingModeChange != 0)
                state.Mode = request.Mode;
            if (request.PendingOverlayChange != 0)
                state.OverlayOpen = request.OverlayOpen == 0 ? (byte)0 : (byte)1;
            if (request.PendingPageChange != 0)
                state.Page = request.Page;
            if (request.PendingScrollChange != 0)
            {
                state.QuestScrollY = Clamp01(request.QuestScrollY);
                state.EntryScrollY = Clamp01(request.EntryScrollY);
            }
            else
            {
                state.QuestScrollY = Clamp01(state.QuestScrollY);
                state.EntryScrollY = Clamp01(state.EntryScrollY);
            }

            state.Visible = shell.JournalOpen != 0 ? (byte)1 : (byte)0;
            request = default;
        }

        static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
