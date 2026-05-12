using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Profiling;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial struct MorrowindOwnedPhysicsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<PhysicsStep>();
            systemState.RequireForUpdate<MorrowindOwnedPhysicsBootstrapRequest>();
            Entity stateEntity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.AddComponentData(stateEntity, new MorrowindPhysicsFrameState());
        }

        public void OnUpdate(ref SystemState systemState)
        {

            var physicsGroup = systemState.World.GetExistingSystemManaged<PhysicsSystemGroup>();
            bool autoDisabled = physicsGroup != null;
            if (physicsGroup != null)
                physicsGroup.Enabled = false;

            ref var step = ref SystemAPI.GetSingletonRW<PhysicsStep>().ValueRW;
            step.IncrementalStaticBroadphase = true;
            step.IncrementalDynamicBroadphase = true;
            

            ref var frameState = ref SystemAPI.GetSingletonRW<MorrowindPhysicsFrameState>().ValueRW;
            frameState.AutoPhysicsDisabled = (byte)(autoDisabled ? 1 : 0);
            if (frameState.BootLogged == 0)
            {
                frameState.BootLogged = 1;
            }

            RuntimeBootstrapRequestUtility.Consume<MorrowindOwnedPhysicsBootstrapRequest>(systemState.EntityManager);
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
            RequireForUpdate<PhysicsWorldSingleton>();
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
            _exportPhysicsWorldHandle.Update(World.Unmanaged);
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderFirst = true)]
    public partial struct MorrowindPhysicsQueryFrameStampSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var frameState = ref SystemAPI.GetSingletonRW<MorrowindPhysicsFrameState>().ValueRW;
            frameState.QuerySequence++;
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial struct CellStreamingColliderSyncSystem : ISystem
    {
        EntityQuery _mutationQueueQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _mutationQueueQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                ComponentType.ReadWrite<RuntimePhysicsMutationRequest>(),
                ComponentType.ReadWrite<PhysicsFlushRequested>());
            systemState.RequireForUpdate<PendingCellPhysicsLoad>();
            systemState.RequireForUpdate<PendingCellPhysicsUnload>();
            systemState.RequireForUpdate<RuntimeSectionRegistry>();
            systemState.RequireForUpdate(_mutationQueueQuery);
        }

        public void OnUpdate(ref SystemState systemState)
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
            var registry = SystemAPI.GetSingleton<RuntimeSectionRegistry>();
            var loadEnumerator = loadSet.GetEnumerator();
            while (loadEnumerator.MoveNext())
                AddSectionColliderEntities(ref systemState, registry, loadEnumerator.Current, entitiesToEnable);
            var unloadEnumerator = unloadSet.GetEnumerator();
            while (unloadEnumerator.MoveNext())
                AddSectionColliderEntities(ref systemState, registry, unloadEnumerator.Current, entitiesToDisable);

            if (entitiesToEnable.Length > 0 || entitiesToDisable.Length > 0)
            {
                Entity queueEntity = _mutationQueueQuery.GetSingletonEntity();
                var mutations = systemState.EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
                for (int i = 0; i < entitiesToEnable.Length; i++)
                    RuntimePhysicsMutationQueueUtility.EnqueueEnable(ref mutations, entitiesToEnable[i]);
                for (int i = 0; i < entitiesToDisable.Length; i++)
                    RuntimePhysicsMutationQueueUtility.EnqueueDisable(ref mutations, entitiesToDisable[i]);
                RuntimePhysicsMutationQueueUtility.MarkFlushRequested(systemState.EntityManager, queueEntity);
            }

            pendingLoads.Cells.Clear();
            pendingUnloads.Cells.Clear();
        }

        static void AddSectionColliderEntities(
            ref SystemState systemState,
            RuntimeSectionRegistry registry,
            int2 cell,
            NativeList<Entity> entities)
        {
            if (!registry.ExteriorSections.IsCreated
                || !registry.ExteriorSections.TryGetValue(cell, out Entity sectionEntity)
                || sectionEntity == Entity.Null
                || !systemState.EntityManager.Exists(sectionEntity))
            {
                return;
            }

            if (!systemState.EntityManager.HasBuffer<RuntimeCellSectionColliderEntity>(sectionEntity))
                throw new System.InvalidOperationException("[VVardenfell][CellSection] section root is missing collider entity buffer; rebake required.");
            var colliders = systemState.EntityManager.GetBuffer<RuntimeCellSectionColliderEntity>(sectionEntity);
            for (int i = 0; i < colliders.Length; i++)
            {
                Entity entity = colliders[i].Value;
                if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                    throw new System.InvalidOperationException("[VVardenfell][CellSection] collider buffer references a missing entity; rebake required.");
                if (!systemState.EntityManager.HasComponent<RuntimeColliderSource>(entity))
                    throw new System.InvalidOperationException("[VVardenfell][CellSection] collider entity is missing RuntimeColliderSource; resource binding must run first.");
                entities.Add(entity);
            }
        }
    }
}
