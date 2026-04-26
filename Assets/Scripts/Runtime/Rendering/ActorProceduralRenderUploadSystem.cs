using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Jobs;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class ActorProceduralRenderUploadSystem : SystemBase
    {
        static readonly ProfilerMarker k_EnsureStaticResources = new("VV.ActorProcedural.Upload.EnsureStaticResources");
        static readonly ProfilerMarker k_EstimateCounts = new("VV.ActorProcedural.Upload.EstimateCounts");
        static readonly ProfilerMarker k_SchedulePackJob = new("VV.ActorProcedural.Upload.SchedulePackJob");
        static readonly ProfilerMarker k_CompletePackJob = new("VV.ActorProcedural.Upload.CompletePackJob");
        static readonly ProfilerMarker k_SortAndBuildBatches = new("VV.ActorProcedural.Upload.SortAndBuildBatches");
        static readonly ProfilerMarker k_GroupDraws = new("VV.ActorProcedural.Upload.GroupDraws");
        static readonly ProfilerMarker k_BuildBatches = new("VV.ActorProcedural.Upload.BuildBatches");
        static readonly ProfilerMarker k_UploadFrame = new("VV.ActorProcedural.Upload.FrameBuffers");

        EntityQuery _uploadQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
            RequireForUpdate<ActorSkinMesh>();
            _uploadQuery = SystemAPI.QueryBuilder()
                .WithAll<ActorRenderVisible, LocalToWorld, ActorGpuAnimationState, ActorBone, ActorSkinMesh>()
                .WithPresent<GPUAnimation>()
                .Build();
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            Dependency.Complete();

            int entityCount = _uploadQuery.CalculateEntityCount();
            if (entityCount <= 0)
            {
                var emptyResources = WorldResources.ActorProceduralRenderer;
                if (emptyResources == null)
                {
                    emptyResources = new ActorProceduralRenderResources();
                    WorldResources.ActorProceduralRenderer = emptyResources;
                }

                ref var emptyCatalog = ref catalogRef.Value;
                using (k_EnsureStaticResources.Auto())
                {
                    emptyResources.EnsureStaticResources(ref emptyCatalog);
                }

                emptyResources.BeginFrame();
                using (k_UploadFrame.Auto())
                {
                    emptyResources.UploadFrame();
                }
                return;
            }

            using var offsets = new NativeArray<int2>(entityCount, Allocator.TempJob);
            using var drawCounts = new NativeArray<int>(entityCount, Allocator.TempJob);
            using var boneCounts = new NativeArray<int>(entityCount, Allocator.TempJob);
            using var totalCounts = new NativeReference<int2>(Allocator.TempJob);

            JobHandle countHandle;
            var resources = WorldResources.ActorProceduralRenderer;
            if (resources == null)
            {
                resources = new ActorProceduralRenderResources();
                WorldResources.ActorProceduralRenderer = resources;
            }

            using (k_EstimateCounts.Auto())
            {
                countHandle = new CountActorProceduralWorkJob
                {
                    Catalog = catalogRef,
                    DrawCounts = drawCounts,
                    BoneCounts = boneCounts,
                }.ScheduleParallel(_uploadQuery, default);
            }

            ref var catalog = ref catalogRef.Value;
            using (k_EnsureStaticResources.Auto())
            {
                resources.EnsureStaticResources(ref catalog);
            }

            using (k_EstimateCounts.Auto())
            {
                var offsetsHandle = new BuildActorProceduralOffsetsJob
                {
                    DrawCounts = drawCounts,
                    BoneCounts = boneCounts,
                    Offsets = offsets,
                    Totals = totalCounts,
                }.Schedule(countHandle);
                offsetsHandle.Complete();
            }

            resources.BeginFrame();
            int2 totals = totalCounts.Value;
            resources.PrepareFrameData(totals.x, totals.y);

            using (k_SchedulePackJob.Auto())
            {
                Dependency = new PackActorProceduralGpuJob
                {
                    Catalog = catalogRef,
                    EntityOffsets = offsets,
                    SkinMeshRuntimeInfos = resources.SkinMeshRuntimeInfos,
                    InvalidBatchTypeId = resources.InvalidBatchTypeId,
                    PackedDraws = resources.PackedDraws,
                    PackedBatchTypeIds = resources.PackedBatchTypeIds,
                    BoneMatrices = resources.BoneMatrices.AsArray(),
                }.ScheduleParallel(_uploadQuery, Dependency);
            }

            using (k_CompletePackJob.Auto())
            {
                Dependency.Complete();
            }

            using var batchCount = new NativeArray<int>(1, Allocator.TempJob);
            using (k_SortAndBuildBatches.Auto())
            {
                using (k_GroupDraws.Auto())
                {
                    var buildHandle = new BuildSortedDrawBatchesJob
                    {
                        PackedDraws = resources.PackedDraws,
                        PackedBatchTypeIds = resources.PackedBatchTypeIds,
                        BatchTypeInfos = resources.BatchTypeInfos,
                        BatchTypeCounts = resources.BatchTypeCounts,
                        BatchTypeOffsets = resources.BatchTypeOffsets,
                        BatchTypeWriteHeads = resources.BatchTypeWriteHeads,
                        Draws = resources.Draws.AsArray(),
                        BatchScratch = resources.BatchScratch,
                        BatchCount = batchCount,
                    }.Schedule();
                    buildHandle.Complete();
                }
            }
            resources.FinalizeBatchCount(batchCount[0]);

            using (k_UploadFrame.Auto())
            {
                resources.UploadFrame();
            }
        }

        protected override void OnDestroy()
        {
            WorldResources.ActorProceduralRenderer?.Dispose();
            WorldResources.ActorProceduralRenderer = null;
        }

        static bool IsValidGpuState(in ActorGpuAnimationState gpuState, int expectedSkinMeshCount, ref ActorAnimationCatalogBlob catalog)
        {
            if ((uint)gpuState.SkeletonIndex >= (uint)catalog.Skeletons.Length)
                return false;
            if (gpuState.SkinMeshCount != expectedSkinMeshCount)
                return false;
            if (gpuState.BoneMatrixCount <= 0 || gpuState.BoneMatrixOffset < 0)
                return false;
            return true;
        }

        [BurstCompile]
        partial struct CountActorProceduralWorkJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            [NativeDisableParallelForRestriction] public NativeArray<int> DrawCounts;
            [NativeDisableParallelForRestriction] public NativeArray<int> BoneCounts;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                EnabledRefRO<GPUAnimation> gpuAnimation,
                in ActorGpuAnimationState gpuState,
                [ReadOnly] DynamicBuffer<ActorBone> bones,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                if (!Catalog.IsCreated || skinMeshes.Length == 0)
                {
                    DrawCounts[entityIndex] = 0;
                    BoneCounts[entityIndex] = 0;
                    return;
                }

                if (gpuAnimation.ValueRO)
                {
                    if (!ActorProceduralRenderUploadSystem.IsValidGpuState(gpuState, skinMeshes.Length, ref Catalog.Value))
                    {
                        DrawCounts[entityIndex] = 0;
                        BoneCounts[entityIndex] = 0;
                        return;
                    }

                    DrawCounts[entityIndex] = skinMeshes.Length;
                    BoneCounts[entityIndex] = 0;
                    return;
                }

                if (bones.Length == 0)
                {
                    DrawCounts[entityIndex] = 0;
                    BoneCounts[entityIndex] = 0;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                int drawCount = skinMeshes.Length;
                int boneCount = 0;
                for (int i = 0; i < drawCount; i++)
                {
                    int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                    if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                        boneCount += 1;
                    else
                        boneCount += math.max(1, catalog.SkinMeshes[skinMeshIndex].SkinBoneCount);
                }

                DrawCounts[entityIndex] = drawCount;
                BoneCounts[entityIndex] = boneCount;
            }
        }

        [BurstCompile]
        struct BuildActorProceduralOffsetsJob : IJob
        {
            [ReadOnly] public NativeArray<int> DrawCounts;
            [ReadOnly] public NativeArray<int> BoneCounts;
            [NativeDisableParallelForRestriction] public NativeArray<int2> Offsets;
            [NativeDisableParallelForRestriction] public NativeReference<int2> Totals;

            public void Execute()
            {
                int totalDraws = 0;
                int totalBones = 0;
                for (int i = 0; i < Offsets.Length; i++)
                {
                    Offsets[i] = new int2(totalDraws, totalBones);
                    totalDraws += DrawCounts[i];
                    totalBones += BoneCounts[i];
                }

                Totals.Value = new int2(totalDraws, totalBones);
            }
        }

        [BurstCompile]
        partial struct PackActorProceduralGpuJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            [ReadOnly] public NativeArray<int2> EntityOffsets;
            [ReadOnly] public NativeArray<ActorProceduralSkinMeshRuntimeInfo> SkinMeshRuntimeInfos;
            public int InvalidBatchTypeId;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralDrawGpu> PackedDraws;
            [NativeDisableParallelForRestriction] public NativeArray<int> PackedBatchTypeIds;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralMatrixGpu> BoneMatrices;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                EnabledRefRO<GPUAnimation> gpuAnimation,
                in LocalToWorld localToWorld,
                in ActorGpuAnimationState gpuState,
                [ReadOnly] DynamicBuffer<ActorBone> bones,
                [ReadOnly] DynamicBuffer<ActorSkinMesh> skinMeshes)
            {
                int2 offsets = EntityOffsets[entityIndex];
                int drawCursor = offsets.x;
                int boneCursor = offsets.y;

                if (!Catalog.IsCreated || skinMeshes.Length == 0)
                    return;

                ref var catalog = ref Catalog.Value;
                float4x4 actorLocalToWorld = localToWorld.Value;
                int gpuBoneCursor = gpuState.BoneMatrixOffset;
                bool gpuAnimationEnabled = gpuAnimation.ValueRO;
                bool useGpuAnimation = gpuAnimationEnabled && ActorProceduralRenderUploadSystem.IsValidGpuState(gpuState, skinMeshes.Length, ref catalog);
                if (gpuAnimationEnabled && !useGpuAnimation)
                    return;

                for (int i = 0; i < skinMeshes.Length; i++)
                {
                    var skinMeshRef = skinMeshes[i];
                    if ((uint)skinMeshRef.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    {
                        PackedDraws[drawCursor] = default;
                        PackedBatchTypeIds[drawCursor] = InvalidBatchTypeId;
                        drawCursor++;
                        continue;
                    }

                    var skinMesh = catalog.SkinMeshes[skinMeshRef.SkinMeshIndex];
                    var runtimeInfo = (uint)skinMeshRef.SkinMeshIndex < (uint)SkinMeshRuntimeInfos.Length
                        ? SkinMeshRuntimeInfos[skinMeshRef.SkinMeshIndex]
                        : default;
                    int boneMatrixOffset;
                    int boneMatrixSource;
                    if (gpuAnimationEnabled)
                    {
                        if (gpuBoneCursor + runtimeInfo.BoneMatrixCount > gpuState.BoneMatrixOffset + gpuState.BoneMatrixCount)
                        {
                            PackedDraws[drawCursor] = default;
                            PackedBatchTypeIds[drawCursor] = InvalidBatchTypeId;
                            drawCursor++;
                            continue;
                        }

                        boneMatrixOffset = gpuBoneCursor;
                        gpuBoneCursor += runtimeInfo.BoneMatrixCount;
                        boneMatrixSource = 1;
                    }
                    else
                    {
                        boneMatrixOffset = boneCursor;
                        AddSkinBoneMatrices(ref catalog, skinMesh, bones, skinMeshRef.AttachBoneIndex, skinMeshRef.RigidMirrorX, ref boneCursor);
                        boneMatrixSource = 0;
                    }

                    PackedDraws[drawCursor] = new ActorProceduralDrawGpu
                    {
                        FirstIndex = runtimeInfo.FirstIndex,
                        FirstVertex = runtimeInfo.FirstVertex,
                        BoneMatrixOffset = boneMatrixOffset,
                        BoneMatrixSource = boneMatrixSource,
                        TextureSlice = runtimeInfo.TextureSlice,
                        LocalToWorld = ToGpuMatrix(actorLocalToWorld),
                    };
                    PackedBatchTypeIds[drawCursor] = runtimeInfo.BatchTypeId;
                    drawCursor++;
                }
            }

            void AddSkinBoneMatrices(
                ref ActorAnimationCatalogBlob catalog,
                ActorSkinMeshBlob skinMesh,
                DynamicBuffer<ActorBone> bones,
                int attachBoneIndex,
                byte rigidMirrorX,
                ref int boneCursor)
            {
                if (skinMesh.IsRigid != 0)
                {
                    float4x4 attach = (uint)attachBoneIndex < (uint)bones.Length
                        ? bones[attachBoneIndex].LocalToRoot
                        : float4x4.identity;
                    float4x4 attachOffset = math.lengthsq(skinMesh.RigidOffset) > 0f
                        ? float4x4.Translate(skinMesh.RigidOffset)
                        : float4x4.identity;
                    float4x4 mirror = rigidMirrorX != 0
                        ? new float4x4(
                            new float4(-1f, 0f, 0f, 0f),
                            new float4(0f, 1f, 0f, 0f),
                            new float4(0f, 0f, 1f, 0f),
                            new float4(0f, 0f, 0f, 1f))
                        : float4x4.identity;
                    float4x4 localAttach = math.mul(math.mul(attachOffset, mirror), skinMesh.GeometryToSkeleton);
                    BoneMatrices[boneCursor++] = ToGpuMatrix(math.mul(attach, localAttach));
                    return;
                }

                int firstSkinBone = skinMesh.FirstSkinBoneIndex;
                int skinBoneCount = skinMesh.SkinBoneCount;
                int end = math.min(catalog.SkinBones.Length, firstSkinBone + skinBoneCount);
                int start = boneCursor;
                for (int i = firstSkinBone; i < end; i++)
                {
                    var skinBone = catalog.SkinBones[i];
                    int actorBoneIndex = skinBone.BoneIndex;
                    float4x4 pose = (uint)actorBoneIndex < (uint)bones.Length
                        ? bones[actorBoneIndex].LocalToRoot
                        : float4x4.identity;
                    BoneMatrices[boneCursor++] = ToGpuMatrix(math.mul(pose, skinBone.BindPose));
                }

                if (boneCursor == start)
                    BoneMatrices[boneCursor++] = ToGpuMatrix(float4x4.identity);
            }

            static ActorProceduralMatrixGpu ToGpuMatrix(float4x4 matrix)
            {
                return new ActorProceduralMatrixGpu
                {
                    Row0 = new float4(matrix.c0.x, matrix.c1.x, matrix.c2.x, matrix.c3.x),
                    Row1 = new float4(matrix.c0.y, matrix.c1.y, matrix.c2.y, matrix.c3.y),
                    Row2 = new float4(matrix.c0.z, matrix.c1.z, matrix.c2.z, matrix.c3.z),
                };
            }
        }

        [BurstCompile]
        struct BuildSortedDrawBatchesJob : IJob
        {
            [ReadOnly] public NativeArray<ActorProceduralDrawGpu> PackedDraws;
            [ReadOnly] public NativeArray<int> PackedBatchTypeIds;
            [ReadOnly] public NativeArray<ActorProceduralBatchTypeInfo> BatchTypeInfos;
            [NativeDisableParallelForRestriction] public NativeArray<int> BatchTypeCounts;
            [NativeDisableParallelForRestriction] public NativeArray<int> BatchTypeOffsets;
            [NativeDisableParallelForRestriction] public NativeArray<int> BatchTypeWriteHeads;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralDrawGpu> Draws;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralDrawBatch> BatchScratch;
            [NativeDisableParallelForRestriction] public NativeArray<int> BatchCount;

            public void Execute()
            {
                if (PackedDraws.Length == 0)
                {
                    BatchCount[0] = 0;
                    return;
                }

                for (int i = 0; i < BatchTypeCounts.Length; i++)
                    BatchTypeCounts[i] = 0;

                for (int i = 0; i < PackedBatchTypeIds.Length; i++)
                {
                    int batchTypeId = PackedBatchTypeIds[i];
                    if ((uint)batchTypeId >= (uint)BatchTypeCounts.Length)
                        continue;

                    BatchTypeCounts[batchTypeId]++;
                }

                int prefix = 0;
                int batchWrite = 0;
                for (int batchTypeId = 0; batchTypeId < BatchTypeCounts.Length; batchTypeId++)
                {
                    int count = BatchTypeCounts[batchTypeId];
                    BatchTypeOffsets[batchTypeId] = prefix;
                    BatchTypeWriteHeads[batchTypeId] = prefix;

                    if (count > 0)
                    {
                        var batchType = BatchTypeInfos[batchTypeId];
                        if (batchType.IndexCount > 0)
                        {
                            BatchScratch[batchWrite++] = new ActorProceduralDrawBatch
                            {
                                BucketIndex = batchType.BucketIndex,
                                MaterialIndex = batchType.MaterialIndex,
                                DrawBase = prefix,
                                DrawCount = count,
                                IndexCount = batchType.IndexCount,
                            };
                        }
                    }

                    prefix += count;
                }

                for (int i = 0; i < PackedDraws.Length; i++)
                {
                    int batchTypeId = PackedBatchTypeIds[i];
                    if ((uint)batchTypeId >= (uint)BatchTypeWriteHeads.Length)
                        continue;

                    int writeIndex = BatchTypeWriteHeads[batchTypeId];
                    Draws[writeIndex] = PackedDraws[i];
                    BatchTypeWriteHeads[batchTypeId] = writeIndex + 1;
                }

                BatchCount[0] = batchWrite;
            }
        }
    }
}
