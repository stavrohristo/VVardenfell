#if VVARDENFELL_ACTOR_GPU_ANIMATION
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    [UpdateBefore(typeof(ActorProceduralRenderUploadSystem))]
    public partial class ActorGpuAnimationComputeSystem : SystemBase
    {
        static readonly ProfilerMarker k_CountWork = new("VV.ActorGpuAnimation.CountWork");
        static readonly ProfilerMarker k_PackFrame = new("VV.ActorGpuAnimation.PackFrame");
        static readonly ProfilerMarker k_Dispatch = new("VV.ActorGpuAnimation.Dispatch");

        EntityQuery _gpuActorQuery;
        NativeList<ActorGpuAnimationCount> _counts;
        NativeList<ActorGpuAnimationOffset> _offsets;
        NativeList<ActorGpuAnimationActorGpu> _actors;
        NativeList<ActorGpuAnimationLayerGpu> _layers;
        NativeList<ActorGpuAnimationSkinMeshInstanceGpu> _skinMeshInstances;
        NativeReference<ActorGpuAnimationCount> _totals;

        struct ActorGpuAnimationCount
        {
            public int ValidActorCount;
            public int LayerCount;
            public int SkinMeshCount;
            public int BoneMatrixCount;
        }

        struct ActorGpuAnimationOffset
        {
            public int ActorOffset;
            public int LayerOffset;
            public int SkinMeshOffset;
            public int BoneMatrixOffset;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
            _gpuActorQuery = SystemAPI.QueryBuilder()
                .WithAll<ActorGpuAnimationState, ActorSkeleton, ActorGpuAnimationRequest, ActorSkinMesh, GPUAnimation, ActorRenderVisible>()
                .Build();
            _counts = new NativeList<ActorGpuAnimationCount>(Allocator.Persistent);
            _offsets = new NativeList<ActorGpuAnimationOffset>(Allocator.Persistent);
            _actors = new NativeList<ActorGpuAnimationActorGpu>(Allocator.Persistent);
            _layers = new NativeList<ActorGpuAnimationLayerGpu>(Allocator.Persistent);
            _skinMeshInstances = new NativeList<ActorGpuAnimationSkinMeshInstanceGpu>(Allocator.Persistent);
            _totals = new NativeReference<ActorGpuAnimationCount>(Allocator.Persistent);
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<ActorAnimationLodSettings>())
            {
                var settings = SystemAPI.GetSingleton<ActorAnimationLodSettings>();
                ActorGpuAnimationValidation.Enabled = settings.ValidationEnabled != 0;
                ActorGpuAnimationValidation.ActorIndex = math.max(0, settings.ValidationActorIndex);
            }
            else
            {
                ActorGpuAnimationValidation.Enabled = false;
                ActorGpuAnimationValidation.ActorIndex = 0;
            }

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            var gpuResources = WorldResources.ActorGpuAnimation;
            if (gpuResources == null)
            {
                gpuResources = new ActorGpuAnimationResources();
                WorldResources.ActorGpuAnimation = gpuResources;
            }

            var renderResources = WorldResources.ActorProceduralRenderer;
            if (renderResources == null)
            {
                renderResources = new ActorProceduralRenderResources();
                WorldResources.ActorProceduralRenderer = renderResources;
            }

            if (!gpuResources.IsSupported)
            {
                renderResources.ClearGpuBoneMatrixBuffer();
                return;
            }

            ref var catalog = ref catalogRef.Value;
            gpuResources.EnsureStaticResources(ref catalog);
            gpuResources.BeginFrame();

            int entityCount = _gpuActorQuery.CalculateEntityCount();
            if (entityCount <= 0)
            {
                renderResources.ClearGpuBoneMatrixBuffer();
                return;
            }

            EnsureScratchListLength(ref _counts, entityCount);
            EnsureScratchListLength(ref _offsets, entityCount);

            var frameBuildHandle = Dependency;
            using (k_CountWork.Auto())
            {
                var countHandle = new CountGpuAnimationWorkJob
                {
                    Catalog = catalogRef,
                    Counts = _counts.AsArray(),
                }.ScheduleParallel(_gpuActorQuery, Dependency);

                frameBuildHandle = new BuildGpuAnimationOffsetsJob
                {
                    Counts = _counts.AsArray(),
                    Offsets = _offsets.AsArray(),
                    Totals = _totals,
                }.Schedule(countHandle);
                frameBuildHandle.Complete();
            }

            ActorGpuAnimationCount totalCounts = _totals.Value;
            if (totalCounts.ValidActorCount <= 0 || totalCounts.BoneMatrixCount <= 0)
            {
                renderResources.ClearGpuBoneMatrixBuffer();
                return;
            }

            gpuResources.ReserveFrameCapacity(
                totalCounts.ValidActorCount,
                totalCounts.LayerCount,
                totalCounts.SkinMeshCount,
                totalCounts.BoneMatrixCount);

            EnsureScratchListLength(ref _actors, totalCounts.ValidActorCount);
            EnsureScratchListLength(ref _layers, totalCounts.LayerCount);
            EnsureScratchListLength(ref _skinMeshInstances, totalCounts.SkinMeshCount);

            using (k_PackFrame.Auto())
            {
                var packHandle = new PackGpuAnimationFrameJob
                {
                    Catalog = catalogRef,
                    Offsets = _offsets.AsArray(),
                    Actors = _actors.AsArray(),
                    Layers = _layers.AsArray(),
                    SkinMeshInstances = _skinMeshInstances.AsArray(),
                }.ScheduleParallel(_gpuActorQuery, frameBuildHandle);
                packHandle.Complete();
            }

            using (k_Dispatch.Auto())
            {
                gpuResources.Dispatch(_actors.AsArray(), _layers.AsArray(), _skinMeshInstances.AsArray());
            }
            renderResources.SetGpuBoneMatrixBuffer(gpuResources.BoneMatrixBuffer, gpuResources.BoneMatrixCount);

            if (ActorGpuAnimationValidation.Enabled)
                ValidateSelectedActor(ref catalog, gpuResources);
        }

        protected override void OnDestroy()
        {
            if (_counts.IsCreated)
                _counts.Dispose();
            if (_offsets.IsCreated)
                _offsets.Dispose();
            if (_actors.IsCreated)
                _actors.Dispose();
            if (_layers.IsCreated)
                _layers.Dispose();
            if (_skinMeshInstances.IsCreated)
                _skinMeshInstances.Dispose();
            if (_totals.IsCreated)
                _totals.Dispose();
            WorldResources.ActorGpuAnimation?.Dispose();
            WorldResources.ActorGpuAnimation = null;
            WorldResources.ActorProceduralRenderer?.ClearGpuBoneMatrixBuffer();
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
                in ActorSkeleton skeleton,
                [ReadOnly] DynamicBuffer<ActorGpuAnimationRequest> requests,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                var count = default(ActorGpuAnimationCount);
                if (!Catalog.IsCreated
                    || (uint)skeleton.SkeletonIndex >= (uint)Catalog.Value.Skeletons.Length
                    || skinMeshes.Length == 0)
                {
                    Counts[entityIndex] = count;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                count.LayerCount = GetValidLayerCount(requests, ref catalog);
                count.BoneMatrixCount = CountOutputBoneMatrices(skinMeshes, ref catalog);
                if (count.LayerCount <= 0 || count.BoneMatrixCount <= 0)
                {
                    Counts[entityIndex] = default;
                    return;
                }

                count.ValidActorCount = 1;
                count.SkinMeshCount = skinMeshes.Length;
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
                        SkinMeshOffset = running.SkinMeshCount,
                        BoneMatrixOffset = running.BoneMatrixCount,
                    };
                    var count = Counts[i];
                    running.ValidActorCount += count.ValidActorCount;
                    running.LayerCount += count.LayerCount;
                    running.SkinMeshCount += count.SkinMeshCount;
                    running.BoneMatrixCount += count.BoneMatrixCount;
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
            [NativeDisableParallelForRestriction] public NativeArray<ActorGpuAnimationSkinMeshInstanceGpu> SkinMeshInstances;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                RefRW<ActorGpuAnimationState> gpuState,
                in ActorSkeleton skeleton,
                [ReadOnly] DynamicBuffer<ActorGpuAnimationRequest> requests,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                if (!Catalog.IsCreated
                    || (uint)skeleton.SkeletonIndex >= (uint)Catalog.Value.Skeletons.Length
                    || skinMeshes.Length == 0)
                {
                    gpuState.ValueRW = default;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                int layerCount = GetValidLayerCount(requests, ref catalog);
                int actorBoneMatrixCount = CountOutputBoneMatrices(skinMeshes, ref catalog);
                if (layerCount <= 0 || actorBoneMatrixCount <= 0)
                {
                    gpuState.ValueRW = default;
                    return;
                }

                ActorGpuAnimationOffset offsets = Offsets[entityIndex];
                int actorIndex = offsets.ActorOffset;
                int layerOffset = offsets.LayerOffset;
                int skinMeshOffset = offsets.SkinMeshOffset;
                int boneMatrixOffset = offsets.BoneMatrixOffset;

                layerCount = WriteLayers(requests, ref catalog, Layers, layerOffset);
                actorBoneMatrixCount = 0;
                for (int i = 0; i < skinMeshes.Length; i++)
                {
                    var skinMeshRef = skinMeshes[i];
                    if ((uint)skinMeshRef.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    {
                        SkinMeshInstances[skinMeshOffset + i] = new ActorGpuAnimationSkinMeshInstanceGpu
                        {
                            SkinMeshIndex = -1,
                            AttachBoneIndex = skinMeshRef.AttachBoneIndex,
                            RigidMirrorX = skinMeshRef.RigidMirrorX,
                            BoneMatrixOffset = boneMatrixOffset + actorBoneMatrixCount,
                        };
                        continue;
                    }

                    int skinBoneCount = 1;
                    skinBoneCount = math.max(1, catalog.SkinMeshes[skinMeshRef.SkinMeshIndex].SkinBoneCount);

                    SkinMeshInstances[skinMeshOffset + i] = new ActorGpuAnimationSkinMeshInstanceGpu
                    {
                        SkinMeshIndex = skinMeshRef.SkinMeshIndex,
                        AttachBoneIndex = skinMeshRef.AttachBoneIndex,
                        RigidMirrorX = skinMeshRef.RigidMirrorX,
                        BoneMatrixOffset = boneMatrixOffset + actorBoneMatrixCount,
                    };
                    actorBoneMatrixCount += skinBoneCount;
                }

                gpuState.ValueRW = new ActorGpuAnimationState
                {
                    SkeletonIndex = skeleton.SkeletonIndex,
                    LayerOffset = layerOffset,
                    LayerCount = layerCount,
                    SkinMeshOffset = skinMeshOffset,
                    SkinMeshCount = skinMeshes.Length,
                    BoneMatrixOffset = boneMatrixOffset,
                    BoneMatrixCount = actorBoneMatrixCount,
                };

                Actors[actorIndex] = new ActorGpuAnimationActorGpu
                {
                    SkeletonIndex = skeleton.SkeletonIndex,
                    FirstLayerIndex = layerOffset,
                    LayerCount = layerCount,
                    FirstSkinMeshIndex = skinMeshOffset,
                    SkinMeshCount = skinMeshes.Length,
                    BoneMatrixOffset = boneMatrixOffset,
                    BoneMatrixCount = actorBoneMatrixCount,
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

        void ValidateSelectedActor(ref ActorAnimationCatalogBlob catalog, ActorGpuAnimationResources gpuResources)
        {
            int selectedActorIndex = math.max(0, ActorGpuAnimationValidation.ActorIndex);
            int actorIndex = 0;
            foreach (var (controller, skeleton, gpuState, requests, skinMeshes, entity) in
                     SystemAPI.Query<RefRO<ActorAnimationController>, RefRO<ActorSkeleton>, RefRO<ActorGpuAnimationState>, DynamicBuffer<ActorGpuAnimationRequest>, DynamicBuffer<ActorSkinMesh>>()
                         .WithAll<GPUAnimation, ActorRenderVisible>()
                         .WithEntityAccess())
            {
                if (actorIndex++ != selectedActorIndex)
                    continue;

                ActorGpuAnimationValidation.Validate(
                    entity,
                    ref catalog,
                    controller.ValueRO,
                    skeleton.ValueRO,
                    gpuState.ValueRO,
                    requests,
                    skinMeshes,
                    gpuResources.BoneMatrixBuffer);
                return;
            }
        }
    }
}
#endif
