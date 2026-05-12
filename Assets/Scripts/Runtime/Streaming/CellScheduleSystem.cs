using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Given the camera cell and the set of already-loaded cells, produces:
    ///   - <see cref="LoadQueue"/>: coords to load this frame (capped to MaxLoadsPerFrame).
    ///   - <see cref="UnloadList.PendingEntityDestroy"/>: coords outside the view radius
    ///     (capped to MaxUnloadsPerFrame).
    ///
    /// Fully Burst. Runs every frame but the (2r+1)² window walk is tiny (~49 cells at r=3).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CameraCellTrackerSystem))]
    public partial struct CellScheduleSystem : ISystem
    {
        EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<StreamingConfig, AvailableCells, LoadedCellsMap, LoadQueue, UnloadList>()
                .Build();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var cfg      = _query.GetSingleton<StreamingConfig>();
            var avail    = _query.GetSingleton<AvailableCells>();
            var loaded   = _query.GetSingleton<LoadedCellsMap>();
            var queue    = _query.GetSingleton<LoadQueue>();
            var unload   = _query.GetSingleton<UnloadList>();

            if (cfg.ExteriorStreamingPaused)
            {
                unload.PendingEntityDestroy.Clear();
                return;
            }

            // LoadQueue is drained by the managed worker; we only append cells that
            // aren't already in-flight. Rather than scan the queue for duplicates on
            // every frame, we rebuild from scratch — the worker clears entries it
            // consumes, so the queue contents are the "still needed but not popped yet"
            // subset. On a fresh frame we clear and repopulate.
            queue.Queue.Clear();

            int r = cfg.ViewRadius;
            int loadBudget = cfg.MaxLoadsPerFrame;
            int2 cam = cfg.CameraCell;

            // Build a desired set on-the-fly inside a scratch hashset so we can diff
            // against LoadedCellsMap for the unload pass.
            var desired = new NativeHashSet<int2>((2 * r + 1) * (2 * r + 1), Allocator.Temp);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    var coord = new int2(cam.x + dx, cam.y + dy);
                    if (!avail.Set.Contains(coord)) continue;
                    desired.Add(coord);

                    // Enqueue if the full streamable section is not resident yet, or if
                    // resident content is currently inactive. Terrain may be pre-seeded
                    // before full cell streaming so the player cannot fall through the
                    // world; that terrain-only state must not block full section loading.
                    if ((!loaded.Streamed.Contains(coord) || !loaded.Active.Contains(coord)) && loadBudget > 0)
                    {
                        queue.Queue.Enqueue(coord);
                        loadBudget--;
                    }
                }
            }

            // Pending-entity-destroy list is drained by the unload system each frame,
            // but if the unload system hasn't consumed last frame's (e.g., main thread
            // stall), treat it as still-valid work and don't blow it away. Instead,
            // append new unloads.
            int unloadBudget = cfg.MaxUnloadsPerFrame - unload.PendingEntityDestroy.Length;
            if (unloadBudget > 0)
            {
                // Only currently-active cells outside view go to the unload list. Cells in
                // Map but not Active are already disabled and don't need re-unloading.
                foreach (var coord in loaded.Active)
                {
                    if (!desired.Contains(coord))
                    {
                        unload.PendingEntityDestroy.Add(coord);
                        if (--unloadBudget <= 0) break;
                    }
                }
            }

            desired.Dispose();
        }
    }
}
