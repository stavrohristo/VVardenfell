using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class ActorProceduralRenderUploadSystem : SystemBase
    {
        EntityQuery _uploadQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
            RequireForUpdate<ActorSkinMesh>();
            RequireForUpdate<ActorProceduralDraw>();
            _uploadQuery = GetEntityQuery(
                ComponentType.ReadWrite<ActorProceduralRenderState>(),
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<ActorBone>(),
                ComponentType.ReadOnly<ActorProceduralDraw>());
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            var resources = WorldResources.ActorProceduralRenderer;
            if (resources == null)
            {
                resources = new ActorProceduralRenderResources();
                WorldResources.ActorProceduralRenderer = resources;
            }

            Dependency.Complete();
            ref var catalog = ref catalogRef.Value;
            resources.EnsureStaticResources(ref catalog);
            resources.BeginFrame();

            int entityCount = _uploadQuery.CalculateEntityCount();
            if (entityCount <= 0)
            {
                resources.UploadFrame();
                return;
            }

            int estimatedDraws = 0;
            int estimatedBoneMatrices = 0;
            int entityIndex = 0;
            var offsets = new NativeArray<int2>(entityCount, Allocator.TempJob);
            foreach (var (_, _, bones, draws) in
                     SystemAPI.Query<RefRO<ActorProceduralRenderState>, RefRO<LocalToWorld>, DynamicBuffer<ActorBone>, DynamicBuffer<ActorProceduralDraw>>())
            {
                if (bones.Length == 0 || draws.Length == 0)
                {
                    offsets[entityIndex++] = new int2(estimatedDraws, estimatedBoneMatrices);
                    continue;
                }

                offsets[entityIndex++] = new int2(estimatedDraws, estimatedBoneMatrices);
                estimatedDraws += draws.Length;
                for (int i = 0; i < draws.Length; i++)
                    estimatedBoneMatrices += math.max(1, draws[i].BoneMatrixCount);
            }
            resources.PrepareFrameData(estimatedDraws, estimatedBoneMatrices);

            Dependency = new PackActorProceduralGpuJob
            {
                Catalog = catalogRef,
                EntityOffsets = offsets,
                TextureBucketInfo = WorldResources.TexBucketInfo,
                FallbackBucketSlice = WorldResources.FallbackBucketSlice,
                BlendVariantCount = WorldResources.BlendVariantCount,
                Draws = resources.Draws.AsArray(),
                BoneMatrices = resources.BoneMatrices.AsArray(),
                BatchScratch = resources.Batches.AsArray(),
            }.ScheduleParallel(_uploadQuery, Dependency);
            Dependency.Complete();
            offsets.Dispose();

            resources.CompactBatches();
            resources.UploadFrame();
        }

        protected override void OnDestroy()
        {
            WorldResources.ActorProceduralRenderer?.Dispose();
            WorldResources.ActorProceduralRenderer = null;
        }

        [BurstCompile]
        partial struct PackActorProceduralGpuJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            [ReadOnly] public NativeArray<int2> EntityOffsets;
            [ReadOnly] public NativeArray<int2> TextureBucketInfo;
            public int2 FallbackBucketSlice;
            public int BlendVariantCount;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralDrawGpu> Draws;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralMatrixGpu> BoneMatrices;
            [NativeDisableParallelForRestriction] public NativeArray<ActorProceduralDrawBatch> BatchScratch;

            void Execute(
                [EntityIndexInQuery] int entityIndex,
                ref ActorProceduralRenderState renderState,
                in LocalToWorld localToWorld,
                DynamicBuffer<ActorBone> bones,
                DynamicBuffer<ActorProceduralDraw> actorDraws)
            {
                int2 offsets = EntityOffsets[entityIndex];
                int drawCursor = offsets.x;
                int boneCursor = offsets.y;
                renderState.BoneMatrixOffset = boneCursor;
                renderState.DrawStart = drawCursor;
                renderState.BoneMatrixCount = 0;
                renderState.DrawCount = 0;

                if (!Catalog.IsCreated || bones.Length == 0 || actorDraws.Length == 0)
                    return;

                ref var catalog = ref Catalog.Value;
                float4x4 actorLocalToWorld = localToWorld.Value;
                for (int i = 0; i < actorDraws.Length; i++)
                {
                    var draw = actorDraws[i];
                    if ((uint)draw.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    {
                        Draws[drawCursor] = default;
                        BatchScratch[drawCursor] = new ActorProceduralDrawBatch
                        {
                            BucketIndex = FallbackBucketSlice.x,
                            MaterialIndex = 0,
                            DrawBase = drawCursor,
                            DrawCount = 1,
                            IndexCount = 0,
                        };
                        drawCursor++;
                        continue;
                    }

                    var skinMesh = catalog.SkinMeshes[draw.SkinMeshIndex];
                    int boneMatrixOffset = boneCursor;
                    AddSkinBoneMatrices(ref catalog, skinMesh, bones, draw.AttachBoneIndex, draw.RigidMirrorX, ref boneCursor);
                    ResolveTexture(draw.TextureIndex, TextureBucketInfo, FallbackBucketSlice, out int bucketIndex, out int textureSlice);
                    int materialIndex = math.clamp(draw.MaterialIndex, 0, math.max(0, BlendVariantCount - 1));

                    Draws[drawCursor] = new ActorProceduralDrawGpu
                    {
                        FirstIndex = skinMesh.FirstIndexIndex,
                        IndexCount = skinMesh.IndexCount,
                        FirstVertex = skinMesh.FirstVertexIndex,
                        BoneMatrixOffset = boneMatrixOffset,
                        TextureSlice = textureSlice,
                        Padding0 = 0,
                        Padding1 = 0,
                        Padding2 = 0,
                        LocalToWorld = ToGpuMatrix(actorLocalToWorld),
                    };
                    BatchScratch[drawCursor] = new ActorProceduralDrawBatch
                    {
                        BucketIndex = bucketIndex,
                        MaterialIndex = materialIndex,
                        DrawBase = drawCursor,
                        DrawCount = 1,
                        IndexCount = skinMesh.IndexCount,
                    };
                    drawCursor++;
                }

                renderState.BoneMatrixCount = boneCursor - offsets.y;
                renderState.DrawCount = drawCursor - offsets.x;
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

            static void ResolveTexture(
                int textureIndex,
                NativeArray<int2> textureBucketInfo,
                int2 fallbackBucketSlice,
                out int bucketIndex,
                out int textureSlice)
            {
                if (textureIndex >= 0
                    && textureBucketInfo.IsCreated
                    && textureIndex < textureBucketInfo.Length)
                {
                    int2 bucketSlice = textureBucketInfo[textureIndex];
                    bucketIndex = bucketSlice.x;
                    textureSlice = bucketSlice.y;
                    return;
                }

                bucketIndex = fallbackBucketSlice.x;
                textureSlice = fallbackBucketSlice.y;
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
    }
}
