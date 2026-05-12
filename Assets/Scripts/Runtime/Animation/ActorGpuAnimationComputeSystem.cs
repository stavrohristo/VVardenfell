using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(MorrowindPresentationSystemGroup))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    public partial struct ActorGpuAnimationComputeSystem : ISystem
    {
        static readonly ProfilerMarker k_CountWork = new("VV.ActorGpuAnimation.CountWork");
        static readonly ProfilerMarker k_PackFrame = new("VV.ActorGpuAnimation.PackFrame");
        const int WorkloadLogFrameInterval = 120;

        EntityQuery _gpuActorQuery;
        NativeList<ActorGpuAnimationCount> _counts;
        NativeList<ActorGpuAnimationOffset> _offsets;
        NativeReference<ActorGpuAnimationCount> _totals;

        struct ActorGpuAnimationCount
        {
            public int ValidActorCount;
            public int LayerCount;
            public int BoneCount;
            public int SkinMeshWorkCount;
            public int BoneMatrixCount;
            public int DeformedVertexCount;
            public int MaxSkinMeshWorkCount;
        }

        struct ActorGpuAnimationOffset
        {
            public int ActorOffset;
            public int LayerOffset;
            public int BoneOffset;
            public int SkinMeshWorkOffset;
            public int BoneMatrixOffset;
        }

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<ActorAnimationRuntimeSettings>();
            systemState.RequireForUpdate<RuntimeActorPresentationResources>();
            _gpuActorQuery = SystemAPI.QueryBuilder()
                .WithAll<ActorGpuAnimationState, ActorSkeleton, ActorGpuAnimationRequest, ActorSkinMesh, ActorHeadAnimationState, ActorRenderVisible>()
                .Build();
            _counts = new NativeList<ActorGpuAnimationCount>(Allocator.Persistent);
            _offsets = new NativeList<ActorGpuAnimationOffset>(Allocator.Persistent);
            _totals = new NativeReference<ActorGpuAnimationCount>(Allocator.Persistent);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var settings = SystemAPI.GetSingleton<ActorAnimationRuntimeSettings>();
            ActorGpuAnimationValidation.Enabled = settings.ValidationEnabled != 0;
            ActorGpuAnimationValidation.ActorIndex = math.max(0, settings.ValidationActorIndex);
            if (ActorGpuAnimationValidation.Enabled)
                throw new InvalidOperationException("Actor GPU animation validation currently requires a render-dispatch validation path; disable validation for normal render-owned dispatch.");

            if (settings.Mode == ActorAnimationRuntimeMode.Cpu)
            {
                throw new InvalidOperationException("Actor Entities Graphics rendering requires ActorAnimationRuntimeSettings.Mode = Gpu.");
            }

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            var gpuResources = RuntimeActorPresentationResources.Require(systemState.EntityManager).GpuAnimation;
            if (gpuResources == null)
                throw new InvalidOperationException("[VVardenfell][ActorGpuAnimation] Actor GPU animation resources are not loaded.");

            if (!gpuResources.IsSupported)
            {
                throw new InvalidOperationException(
                    "Actor Entities Graphics rendering requires GPU animation, but compute shader support or ActorGpuAnimation resources are unavailable.");
            }

            ref var catalog = ref catalogRef.Value;
            gpuResources.EnsureStaticResources(ref catalog);
            gpuResources.BeginFrame();

            int entityCount = _gpuActorQuery.CalculateEntityCount();
            if (entityCount <= 0)
            {
                return;
            }

            EnsureScratchListLength(ref _counts, entityCount);
            EnsureScratchListLength(ref _offsets, entityCount);

            using (k_CountWork.Auto())
            {
                var countHandle = new CountGpuAnimationWorkJob
                {
                    Catalog = catalogRef,
                    Counts = _counts.AsArray(),
                }.ScheduleParallel(_gpuActorQuery, systemState.Dependency);

                systemState.Dependency = new BuildGpuAnimationOffsetsJob
                {
                    Counts = _counts.AsArray(),
                    Offsets = _offsets.AsArray(),
                    Totals = _totals,
                }.Schedule(countHandle);
                systemState.Dependency.Complete();
            }

            ActorGpuAnimationCount totalCounts = _totals.Value;
            if (totalCounts.ValidActorCount <= 0 || totalCounts.BoneMatrixCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Actor GPU animation found {_gpuActorQuery.CalculateEntityCount()} renderable actor(s), but no valid GPU animation frame could be prepared.");
            }

            gpuResources.ReserveFrameCapacity(
                totalCounts.ValidActorCount,
                totalCounts.LayerCount,
                totalCounts.BoneCount,
                totalCounts.SkinMeshWorkCount,
                gpuResources.AllocatedBoneMatrixCount);

            var upload = gpuResources.BeginFrameUpload(
                totalCounts.ValidActorCount,
                totalCounts.LayerCount,
                totalCounts.SkinMeshWorkCount);

            using (k_PackFrame.Auto())
            {
                var packHandle = new PackGpuAnimationFrameJob
                {
                    Catalog = catalogRef,
                    Offsets = _offsets.AsArray(),
                    Actors = upload.Actors,
                    Layers = upload.Layers,
                    SkinMeshes = upload.SkinMeshes,
                }.ScheduleParallel(_gpuActorQuery, systemState.Dependency);
                systemState.Dependency = packHandle;
                systemState.Dependency.Complete();
            }
            gpuResources.EndFrameUpload(upload);
            gpuResources.PrepareFrameForRender(
                totalCounts.ValidActorCount,
                totalCounts.LayerCount,
                totalCounts.BoneCount,
                totalCounts.SkinMeshWorkCount,
                gpuResources.AllocatedBoneMatrixCount,
                gpuResources.AllocatedDeformedVertexCount);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_counts.IsCreated)
                _counts.Dispose();
            if (_offsets.IsCreated)
                _offsets.Dispose();
            if (_totals.IsCreated)
                _totals.Dispose();
        }

        static void EnsureScratchListLength<T>(ref NativeList<T> list, int length) where T : unmanaged
        {
            if (length > list.Capacity)
                list.Capacity = length;

            list.ResizeUninitialized(length);
        }

        [BurstCompile]
        partial struct CountGpuAnimationWorkJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationCount> Counts;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                in ActorGpuAnimationState gpuState,
                in ActorSkeleton skeleton,
                in ActorHeadAnimationState headAnimation,
                [ReadOnly] DynamicBuffer<ActorGpuAnimationRequest> requests,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                var count = default(ActorGpuAnimationCount);
                if (!Catalog.IsCreated
                    || (uint)skeleton.SkeletonIndex >= (uint)Catalog.Value.Skeletons.Length
                    || skinMeshes.Length == 0
                    || gpuState.BoneMatrixOffset < 0
                    || gpuState.BoneMatrixCount <= 0
                    || gpuState.DeformedVertexOffset < 0
                    || gpuState.DeformedVertexCount <= 0)
                {
                    Counts[entityIndex] = count;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                count.BoneMatrixCount = CountOutputBoneMatrices(skinMeshes, ref catalog);
                if (count.BoneMatrixCount <= 0 || count.BoneMatrixCount > gpuState.BoneMatrixCount)
                {
                    Counts[entityIndex] = default;
                    return;
                }

                var skeletonBlob = catalog.Skeletons[skeleton.SkeletonIndex];
                count.LayerCount = GetValidLayerCount(requests, ref catalog);
                count.ValidActorCount = 1;
                count.BoneCount = math.max(0, skeletonBlob.BoneCount);
                count.SkinMeshWorkCount = CountValidSkinMeshes(skinMeshes, ref catalog);
                count.DeformedVertexCount = gpuState.DeformedVertexCount;
                count.MaxSkinMeshWorkCount = count.SkinMeshWorkCount;
                Counts[entityIndex] = count;
            }
        }

        [BurstCompile]
        struct BuildGpuAnimationOffsetsJob : IJob
        {
            [ReadOnly] public NativeArray<ActorGpuAnimationCount> Counts;
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationOffset> Offsets;
            [NativeDisableParallelForRestriction] public NativeReference<ActorGpuAnimationCount> Totals;

            public void Execute()
            {
                var running = default(ActorGpuAnimationCount);
                for (int i = 0; i < Counts.Length; i++)
                {
                    Offsets[i] = new ActorGpuAnimationOffset
                    {
                        ActorOffset = running.ValidActorCount,
                        LayerOffset = running.LayerCount,
                        BoneOffset = running.BoneCount,
                        SkinMeshWorkOffset = running.SkinMeshWorkCount,
                        BoneMatrixOffset = running.BoneMatrixCount,
                    };
                    var count = Counts[i];
                    running.ValidActorCount += count.ValidActorCount;
                    running.LayerCount += count.LayerCount;
                    running.BoneCount += count.BoneCount;
                    running.SkinMeshWorkCount += count.SkinMeshWorkCount;
                    running.BoneMatrixCount += count.BoneMatrixCount;
                    running.DeformedVertexCount += count.DeformedVertexCount;
                    running.MaxSkinMeshWorkCount = math.max(running.MaxSkinMeshWorkCount, count.SkinMeshWorkCount);
                }

                Totals.Value = running;
            }
        }

        [BurstCompile]
        partial struct PackGpuAnimationFrameJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            [ReadOnly] public NativeArray<ActorGpuAnimationOffset> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationActorGpu> Actors;
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationLayerGpu> Layers;
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationSkinMeshWorkGpu> SkinMeshes;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                RefRW<ActorGpuAnimationState> gpuState,
                in ActorSkeleton skeleton,
                in ActorHeadAnimationState headAnimation,
                [ReadOnly] DynamicBuffer<ActorGpuAnimationRequest> requests,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                if (!Catalog.IsCreated
                    || (uint)skeleton.SkeletonIndex >= (uint)Catalog.Value.Skeletons.Length
                    || skinMeshes.Length == 0
                    || gpuState.ValueRO.BoneMatrixOffset < 0
                    || gpuState.ValueRO.BoneMatrixCount <= 0
                    || gpuState.ValueRO.DeformedVertexOffset < 0
                    || gpuState.ValueRO.DeformedVertexCount <= 0)
                {
                    gpuState.ValueRW.Valid = 0;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                int layerCount = GetValidLayerCount(requests, ref catalog);
                int actorBoneMatrixCount = CountOutputBoneMatrices(skinMeshes, ref catalog);
                if (actorBoneMatrixCount <= 0 || actorBoneMatrixCount > gpuState.ValueRO.BoneMatrixCount)
                {
                    gpuState.ValueRW.Valid = 0;
                    return;
                }

                ActorGpuAnimationOffset offsets = Offsets[entityIndex];
                int actorIndex = offsets.ActorOffset;
                int layerOffset = offsets.LayerOffset;
                int boneOffset = offsets.BoneOffset;
                int skinMeshWorkOffset = offsets.SkinMeshWorkOffset;
                int boneMatrixOffset = gpuState.ValueRO.BoneMatrixOffset;
                int actorDeformedVertexOffset = gpuState.ValueRO.DeformedVertexOffset;
                int deformedVertexOffset = actorDeformedVertexOffset;
                var skeletonBlob = catalog.Skeletons[skeleton.SkeletonIndex];

                layerCount = WriteLayers(requests, ref catalog, Layers, layerOffset);

                int skinMeshWorkCount = 0;
                actorBoneMatrixCount = 0;
                for (int i = 0; i < skinMeshes.Length; i++)
                {
                    var skinMeshRef = skinMeshes[i];
                    if ((uint)skinMeshRef.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                        continue;

                    var skinMesh = catalog.SkinMeshes[skinMeshRef.SkinMeshIndex];
                    SkinMeshes[skinMeshWorkOffset + skinMeshWorkCount] = new ActorGpuAnimationSkinMeshWorkGpu
                    {
                        ActorIndex = actorIndex,
                        SkinMeshIndex = skinMeshRef.SkinMeshIndex,
                        AttachBoneIndex = skinMeshRef.AttachBoneIndex,
                        RigidMirrorX = skinMeshRef.RigidMirrorX,
                        BoneMatrixOffset = boneMatrixOffset + actorBoneMatrixCount,
                        DeformedVertexOffset = deformedVertexOffset,
                    };
                    skinMeshWorkCount++;
                    actorBoneMatrixCount += math.max(1, skinMesh.SkinBoneCount);
                    deformedVertexOffset += math.max(0, skinMesh.VertexCount);
                }

                gpuState.ValueRW.SkeletonIndex = skeleton.SkeletonIndex;
                gpuState.ValueRW.LayerOffset = layerOffset;
                gpuState.ValueRW.LayerCount = layerCount;
                gpuState.ValueRW.SkinMeshOffset = skinMeshWorkOffset;
                gpuState.ValueRW.SkinMeshCount = skinMeshWorkCount;
                gpuState.ValueRW.Valid = 1;

                Actors[actorIndex] = new ActorGpuAnimationActorGpu
                {
                    SkeletonIndex = skeleton.SkeletonIndex,
                    FirstLayerIndex = layerOffset,
                    LayerCount = layerCount,
                    LocalBoneOffset = boneOffset,
                    BoneCount = skeletonBlob.BoneCount,
                    BoneMatrixOffset = boneMatrixOffset,
                    BoneMatrixCount = actorBoneMatrixCount,
                    FirstSkinMeshWorkIndex = skinMeshWorkOffset,
                    SkinMeshWorkCount = skinMeshWorkCount,
                    DeformedVertexOffset = actorDeformedVertexOffset,
                    DeformedVertexCount = deformedVertexOffset - actorDeformedVertexOffset,
                    HeadAnimationTime = headAnimation.HasHeadMorph != 0 ? headAnimation.CurrentTime : 0f,
                };
            }
        }

        static int GetValidLayerCount(DynamicBuffer<ActorGpuAnimationRequest> requests, ref ActorAnimationCatalogBlob catalog)
        {
            int count = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                if (IsValidLayer(requests[i], ref catalog))
                    count++;
            }

            return count;
        }

        static int WriteLayers(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            ref ActorAnimationCatalogBlob catalog,
            NativeArray<ActorGpuAnimationLayerGpu> layers,
            int layerOffset)
        {
            int count = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (!IsValidLayer(request, ref catalog))
                    continue;

                layers[layerOffset + count] = new ActorGpuAnimationLayerGpu
                {
                    ClipIndex = request.ClipIndex,
                    Time = request.Time,
                    Weight = request.Weight,
                    Priority = request.Priority,
                    Mask = (uint)request.Mask,
                    HasPreviousLayer = request.HasPreviousLayer,
                };
                count++;
            }

            return count;
        }

        static bool IsValidLayer(ActorGpuAnimationRequest request, ref ActorAnimationCatalogBlob catalog)
        {
            return request.Weight > 0f && (uint)request.ClipIndex < (uint)catalog.Clips.Length;
        }

        static int CountOutputBoneMatrices(DynamicBuffer<ActorSkinMesh> skinMeshes, ref ActorAnimationCatalogBlob catalog)
        {
            int count = 0;
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                count += math.max(1, catalog.SkinMeshes[skinMeshIndex].SkinBoneCount);
            }

            return count;
        }

        static int CountValidSkinMeshes(DynamicBuffer<ActorSkinMesh> skinMeshes, ref ActorAnimationCatalogBlob catalog)
        {
            int count = 0;
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                if ((uint)skinMeshes[i].SkinMeshIndex < (uint)catalog.SkinMeshes.Length)
                    count++;
            }

            return count;
        }

    }
}
