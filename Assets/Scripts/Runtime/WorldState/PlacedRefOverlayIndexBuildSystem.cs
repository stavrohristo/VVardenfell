using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(ScriptVisibleSaveStateProjectionSystem))]
    public partial struct PlacedRefOverlayIndexBuildSystem : ISystem
    {
        EntityQuery _dirtyQuery;
        EntityQuery _ownerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _ownerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<PlacedRefOverlayRuntimeIndex>(),
                ComponentType.ReadOnly<PlacedRefOverlayState>(),
                ComponentType.ReadOnly<PlacedRefOverlayScriptInstance>(),
                ComponentType.ReadOnly<PlacedRefOverlayScriptLocalValue>(),
                ComponentType.ReadOnly<PlacedRefOverlayActorInventoryItem>(),
                ComponentType.ReadOnly<PlacedRefOverlayContainerItem>());
            _dirtyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<PlacedRefOverlayRuntimeIndex>(),
                ComponentType.ReadOnly<PlacedRefOverlayIndexDirty>(),
                ComponentType.ReadOnly<PlacedRefOverlayState>(),
                ComponentType.ReadOnly<PlacedRefOverlayScriptInstance>(),
                ComponentType.ReadOnly<PlacedRefOverlayScriptLocalValue>(),
                ComponentType.ReadOnly<PlacedRefOverlayActorInventoryItem>(),
                ComponentType.ReadOnly<PlacedRefOverlayContainerItem>());
            systemState.RequireForUpdate(_dirtyQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = _ownerQuery.GetSingletonEntity();
            var index = SystemAPI.GetComponentRW<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            var states = SystemAPI.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            var scriptInstances = SystemAPI.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity);
            var scriptLocals = SystemAPI.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity);
            var actorInventory = SystemAPI.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity);
            var containerItems = SystemAPI.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity);

            int capacity = math.max(
                math.max(states.Length, scriptInstances.Length),
                math.max(math.max(scriptLocals.Length, actorInventory.Length), containerItems.Length));
            ScriptVisibleSaveStateUtility.PrepareOverlayIndexForRebuild(ref index.ValueRW, capacity);

            JobHandle inputDependency = systemState.Dependency;
            JobHandle stateJob = new IndexStatesJob
            {
                States = states.AsNativeArray(),
                Writer = index.ValueRO.StateByPlacedRefId.AsParallelWriter(),
            }.Schedule(states.Length, 64, inputDependency);
            JobHandle scriptInstanceJob = new IndexScriptInstancesJob
            {
                ScriptInstances = scriptInstances.AsNativeArray(),
                Writer = index.ValueRO.ScriptInstancesByPlacedRefId.AsParallelWriter(),
            }.Schedule(scriptInstances.Length, 64, inputDependency);
            JobHandle scriptLocalJob = new IndexScriptLocalsJob
            {
                ScriptLocals = scriptLocals.AsNativeArray(),
                Writer = index.ValueRO.ScriptLocalsByPlacedRefId.AsParallelWriter(),
            }.Schedule(scriptLocals.Length, 64, inputDependency);
            JobHandle actorInventoryJob = new IndexActorInventoryJob
            {
                ActorInventory = actorInventory.AsNativeArray(),
                Writer = index.ValueRO.ActorInventoryByPlacedRefId.AsParallelWriter(),
            }.Schedule(actorInventory.Length, 64, inputDependency);
            JobHandle containerItemsJob = new IndexContainerItemsJob
            {
                ContainerItems = containerItems.AsNativeArray(),
                Writer = index.ValueRO.ContainerItemsByPlacedRefId.AsParallelWriter(),
            }.Schedule(containerItems.Length, 64, inputDependency);

            JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(stateJob, scriptInstanceJob, scriptLocalJob),
                JobHandle.CombineDependencies(actorInventoryJob, containerItemsJob)).Complete();
            systemState.EntityManager.SetComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity, false);
        }

        [BurstCompile]
        struct IndexStatesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PlacedRefOverlayState> States;
            public NativeParallelHashMap<uint, int>.ParallelWriter Writer;

            public void Execute(int index)
            {
                uint placedRefId = States[index].PlacedRefId;
                if (placedRefId != 0u)
                    Writer.TryAdd(placedRefId, index);
            }
        }

        [BurstCompile]
        struct IndexScriptInstancesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PlacedRefOverlayScriptInstance> ScriptInstances;
            public NativeParallelMultiHashMap<uint, int>.ParallelWriter Writer;

            public void Execute(int index)
            {
                uint placedRefId = ScriptInstances[index].PlacedRefId;
                if (placedRefId != 0u)
                    Writer.Add(placedRefId, index);
            }
        }

        [BurstCompile]
        struct IndexScriptLocalsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PlacedRefOverlayScriptLocalValue> ScriptLocals;
            public NativeParallelMultiHashMap<uint, int>.ParallelWriter Writer;

            public void Execute(int index)
            {
                uint placedRefId = ScriptLocals[index].PlacedRefId;
                if (placedRefId != 0u)
                    Writer.Add(placedRefId, index);
            }
        }

        [BurstCompile]
        struct IndexActorInventoryJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PlacedRefOverlayActorInventoryItem> ActorInventory;
            public NativeParallelMultiHashMap<uint, int>.ParallelWriter Writer;

            public void Execute(int index)
            {
                uint placedRefId = ActorInventory[index].PlacedRefId;
                if (placedRefId != 0u)
                    Writer.Add(placedRefId, index);
            }
        }

        [BurstCompile]
        struct IndexContainerItemsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PlacedRefOverlayContainerItem> ContainerItems;
            public NativeParallelMultiHashMap<uint, int>.ParallelWriter Writer;

            public void Execute(int index)
            {
                uint placedRefId = ContainerItems[index].PlacedRefId;
                if (placedRefId != 0u)
                    Writer.Add(placedRefId, index);
            }
        }
    }
}
