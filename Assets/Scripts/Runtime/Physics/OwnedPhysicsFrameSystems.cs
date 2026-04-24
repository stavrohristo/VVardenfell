using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Systems;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class MorrowindOwnedPhysicsBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MorrowindPhysicsFrameState>())
            {
            }
            else
            {
                Entity stateEntity = EntityManager.CreateEntity();
                EntityManager.SetName(stateEntity, "VVardenfell.MorrowindPhysicsFrame");
                EntityManager.AddComponentData(stateEntity, new MorrowindPhysicsFrameState());
            }

            var physicsGroup = World.GetExistingSystemManaged<PhysicsSystemGroup>();
            bool autoDisabled = physicsGroup != null;
            if (physicsGroup != null)
                physicsGroup.Enabled = false;

            ref var frameState = ref SystemAPI.GetSingletonRW<MorrowindPhysicsFrameState>().ValueRW;
            frameState.AutoPhysicsDisabled = (byte)(autoDisabled ? 1 : 0);
            if (frameState.BootLogged == 0)
            {
                frameState.BootLogged = 1;
            }

            Enabled = false;
        }
    }

    [UpdateInGroup(typeof(MorrowindOwnedPhysicsSystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    [UpdateBefore(typeof(MorrowindPhysicsQuerySystemGroup))]
    public partial class MorrowindOwnedPhysicsDriverSystem : SystemBase
    {
        PhysicsSystemGroup _physicsGroup;
        PhysicsInitializeGroup _physicsInitializeGroup;
        PhysicsSimulationGroup _physicsSimulationGroup;
        SystemHandle _exportPhysicsWorldHandle;

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPhysicsFrameState>();
            RequireForUpdate<FixedTickSystem.Singleton>();
        }

        protected override void OnStartRunning()
        {
            _physicsGroup = World.GetExistingSystemManaged<PhysicsSystemGroup>();
            _physicsInitializeGroup = World.GetExistingSystemManaged<PhysicsInitializeGroup>();
            _physicsSimulationGroup = World.GetExistingSystemManaged<PhysicsSimulationGroup>();
            _exportPhysicsWorldHandle = World.Unmanaged.GetExistingUnmanagedSystem<ExportPhysicsWorld>();
        }

        protected override void OnUpdate()
        {
            if (_physicsGroup != null && _physicsGroup.Enabled)
                _physicsGroup.Enabled = false;

            ref var frameState = ref SystemAPI.GetSingletonRW<MorrowindPhysicsFrameState>().ValueRW;
            uint fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick + 1u;
            frameState.FixedTick = fixedTick;
            frameState.SnapshotTick = fixedTick;
            frameState.BuildSequence++;

            _physicsInitializeGroup?.Update();
            _physicsSimulationGroup?.Update();
            if (!_exportPhysicsWorldHandle.Equals(SystemHandle.Null))
                _exportPhysicsWorldHandle.Update(World.Unmanaged);
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderFirst = true)]
    public partial class MorrowindPhysicsQueryFrameStampSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            ref var frameState = ref SystemAPI.GetSingletonRW<MorrowindPhysicsFrameState>().ValueRW;
            frameState.QuerySequence++;
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial class CellStreamingColliderSyncSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<PendingCellPhysicsLoad>();
            RequireForUpdate<PendingCellPhysicsUnload>();
        }

        protected override void OnUpdate()
        {
            ref var pendingLoads = ref SystemAPI.GetSingletonRW<PendingCellPhysicsLoad>().ValueRW;
            ref var pendingUnloads = ref SystemAPI.GetSingletonRW<PendingCellPhysicsUnload>().ValueRW;
            if (pendingLoads.Cells.Length == 0 && pendingUnloads.Cells.Length == 0)
                return;

            using var loadSet = new NativeParallelHashSet<int2>(math.max(1, pendingLoads.Cells.Length), Allocator.Temp);
            using var unloadSet = new NativeParallelHashSet<int2>(math.max(1, pendingUnloads.Cells.Length), Allocator.Temp);

            for (int i = 0; i < pendingLoads.Cells.Length; i++)
                loadSet.Add(pendingLoads.Cells[i]);
            for (int i = 0; i < pendingUnloads.Cells.Length; i++)
            {
                int2 cell = pendingUnloads.Cells[i];
                if (!loadSet.Contains(cell))
                    unloadSet.Add(cell);
            }

            using var entitiesToEnable = new NativeList<Entity>(Allocator.Temp);
            using var entitiesToDisable = new NativeList<Entity>(Allocator.Temp);
            foreach (var (cellLink, entity) in SystemAPI
                         .Query<RefRO<CellLink>>()
                         .WithAll<RuntimeColliderSource>()
                         .WithEntityAccess())
            {
                int2 cell = cellLink.ValueRO.Value;
                if (loadSet.Contains(cell))
                {
                    entitiesToEnable.Add(entity);
                    continue;
                }

                if (unloadSet.Contains(cell))
                    entitiesToDisable.Add(entity);
            }

            for (int i = 0; i < entitiesToEnable.Length; i++)
                RuntimeColliderPhysicsUtility.EnablePhysics(EntityManager, entitiesToEnable[i]);
            for (int i = 0; i < entitiesToDisable.Length; i++)
                RuntimeColliderPhysicsUtility.DisablePhysics(EntityManager, entitiesToDisable[i]);

            pendingLoads.Cells.Clear();
            pendingUnloads.Cells.Clear();
        }
    }
}
