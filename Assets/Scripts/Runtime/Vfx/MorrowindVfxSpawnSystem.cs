using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Vfx
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    public partial struct MorrowindVfxSpawnSystem : ISystem
    {
        EntityQuery _spawnQuery;
        EntityQuery _removeQuery;
        EntityQuery _wakeQuery;
        EntityQuery _runtimeQuery;
        EntityQuery _materializationResourcesQuery;
        EntityQuery _presentationResourcesQuery;
        NativeList<VfxSpawnWorkItem> _spawnWork;
        NativeList<VfxRemoveWorkItem> _removeWork;
        NativeList<MorrowindVfxResources.FollowRequest> _followRequests;
        NativeList<MorrowindVfxResources.FollowResult> _followResults;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<MorrowindVfxSpawnRequest> _spawnHandle;
        ComponentTypeHandle<MorrowindVfxRemoveRequest> _removeHandle;
        ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState systemState)
        {
            MorrowindVfxRenderDispatch.EnsureRegistered();
            _spawnQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxSpawnRequest>());
            _removeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRemoveRequest>());
            _runtimeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRuntimeState>());
            _materializationResourcesQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeMaterializationResources>());
            _presentationResourcesQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeVfxPresentationResources>());
            _wakeQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<MorrowindVfxSpawnRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRemoveRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRuntimeState>(),
                },
            });

            _spawnWork = new NativeList<VfxSpawnWorkItem>(64, Allocator.Persistent);
            _removeWork = new NativeList<VfxRemoveWorkItem>(64, Allocator.Persistent);
            _followRequests = new NativeList<MorrowindVfxResources.FollowRequest>(64, Allocator.Persistent);
            _followResults = new NativeList<MorrowindVfxResources.FollowResult>(64, Allocator.Persistent);
            _entityHandle = systemState.GetEntityTypeHandle();
            _spawnHandle = systemState.GetComponentTypeHandle<MorrowindVfxSpawnRequest>(isReadOnly: true);
            _removeHandle = systemState.GetComponentTypeHandle<MorrowindVfxRemoveRequest>(isReadOnly: true);
            _transformLookup = systemState.GetComponentLookup<LocalTransform>(isReadOnly: true);

            systemState.RequireForUpdate(_wakeQuery);
            systemState.RequireForUpdate<RuntimePresentationEnabled>();
            systemState.RequireForUpdate(_materializationResourcesQuery);
            systemState.RequireForUpdate(_presentationResourcesQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            CacheLoader cache = RuntimeMaterializationResources.Require(systemState.EntityManager).Cache;
            if (cache == null)
                throw new InvalidOperationException("[VVardenfell][VFX] Runtime cache is not loaded.");

            MorrowindVfxRenderDispatch.EnsureRegistered();
            var presentationResources = RuntimeVfxPresentationResources.Require(systemState.EntityManager);
            var resources = presentationResources.Resources;
            if (resources == null)
            {
                resources = new MorrowindVfxResources(cache);
                presentationResources.Resources = resources;
            }
            EnsureRuntimeState(ref systemState);

            CollectSpawnAndRemoveRequests(ref systemState);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < _spawnWork.Length; i++)
            {
                var item = _spawnWork[i];
                resources.Spawn(cache, item.Request);
                ecb.DestroyEntity(item.Entity);
            }

            for (int i = 0; i < _removeWork.Length; i++)
            {
                var item = _removeWork[i];
                resources.Remove(item.Request.Owner, item.Request.EffectId);
                ecb.DestroyEntity(item.Entity);
            }

            ResolveFollowPositions(ref systemState, resources);
            resources.Tick(SystemAPI.Time.DeltaTime, _followResults.AsArray());
            if (resources.InstanceCount <= 0)
                ClearRuntimeState(ref systemState, ref ecb);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_spawnWork.IsCreated)
                _spawnWork.Dispose();
            if (_removeWork.IsCreated)
                _removeWork.Dispose();
            if (_followRequests.IsCreated)
                _followRequests.Dispose();
            if (_followResults.IsCreated)
                _followResults.Dispose();
        }

        void CollectSpawnAndRemoveRequests(ref SystemState systemState)
        {
            int spawnCount = _spawnQuery.CalculateEntityCount();
            int removeCount = _removeQuery.CalculateEntityCount();
            EnsureListCapacity(ref _spawnWork, spawnCount);
            EnsureListCapacity(ref _removeWork, removeCount);
            _spawnWork.Clear();
            _removeWork.Clear();

            _entityHandle.Update(ref systemState);
            _spawnHandle.Update(ref systemState);
            _removeHandle.Update(ref systemState);

            JobHandle dependency = new CollectVfxSpawnRequestsJob
            {
                EntityHandle = _entityHandle,
                SpawnHandle = _spawnHandle,
                WorkItems = _spawnWork.AsParallelWriter(),
            }.ScheduleParallel(_spawnQuery, systemState.Dependency);

            dependency = new CollectVfxRemoveRequestsJob
            {
                EntityHandle = _entityHandle,
                RemoveHandle = _removeHandle,
                WorkItems = _removeWork.AsParallelWriter(),
            }.ScheduleParallel(_removeQuery, dependency);

            systemState.Dependency = dependency;
            systemState.Dependency.Complete();
        }

        void ResolveFollowPositions(ref SystemState systemState, MorrowindVfxResources resources)
        {
            resources.CollectFollowRequests(_followRequests);
            EnsureListLength(ref _followResults, _followRequests.Length);
            if (_followRequests.Length == 0)
                return;

            _transformLookup.Update(ref systemState);
            systemState.Dependency = new ResolveVfxFollowPositionsJob
            {
                TransformLookup = _transformLookup,
                Requests = _followRequests.AsArray(),
                Results = _followResults.AsArray(),
            }.Schedule(_followRequests.Length, 32, systemState.Dependency);
            systemState.Dependency.Complete();
        }

        void EnsureRuntimeState(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            Entity entity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.AddComponentData(entity, new MorrowindVfxRuntimeState());
        }

        void ClearRuntimeState(ref SystemState systemState, ref EntityCommandBuffer ecb)
        {
            if (!SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            ecb.DestroyEntity(SystemAPI.GetSingletonEntity<MorrowindVfxRuntimeState>());
        }

        static void EnsureListCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            if (list.Capacity < capacity)
                list.Capacity = capacity;
        }

        static void EnsureListLength<T>(ref NativeList<T> list, int length)
            where T : unmanaged
        {
            EnsureListCapacity(ref list, length);
            list.ResizeUninitialized(length);
        }

        struct VfxSpawnWorkItem
        {
            public Entity Entity;
            public MorrowindVfxSpawnRequest Request;
        }

        struct VfxRemoveWorkItem
        {
            public Entity Entity;
            public MorrowindVfxRemoveRequest Request;
        }

        [BurstCompile]
        struct CollectVfxSpawnRequestsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindVfxSpawnRequest> SpawnHandle;
            public NativeList<VfxSpawnWorkItem>.ParallelWriter WorkItems;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var requests = chunk.GetNativeArray(ref SpawnHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    WorkItems.AddNoResize(new VfxSpawnWorkItem
                    {
                        Entity = entities[i],
                        Request = requests[i],
                    });
                }
            }
        }

        [BurstCompile]
        struct CollectVfxRemoveRequestsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindVfxRemoveRequest> RemoveHandle;
            public NativeList<VfxRemoveWorkItem>.ParallelWriter WorkItems;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var requests = chunk.GetNativeArray(ref RemoveHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    WorkItems.AddNoResize(new VfxRemoveWorkItem
                    {
                        Entity = entities[i],
                        Request = requests[i],
                    });
                }
            }
        }

        [BurstCompile]
        struct ResolveVfxFollowPositionsJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> TransformLookup;
            [ReadOnly] public NativeArray<MorrowindVfxResources.FollowRequest> Requests;
            [NativeDisableParallelForRestriction] public NativeArray<MorrowindVfxResources.FollowResult> Results;

            public void Execute(int index)
            {
                var request = Requests[index];
                var result = new MorrowindVfxResources.FollowResult
                {
                    InstanceIndex = request.InstanceIndex,
                    Found = 0,
                };

                if (TransformLookup.HasComponent(request.FollowEntity))
                {
                    result.Found = 1;
                    result.Position = TransformLookup[request.FollowEntity].Position;
                }

                Results[index] = result;
            }
        }
    }
}
