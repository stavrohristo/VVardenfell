using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Rendering
{
    public struct ActorProceduralVertexGpu
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;
        public int4 BoneIndices0;
        public int4 BoneIndices1;
        public float4 Weights0;
        public float4 Weights1;
    }

    public struct ActorProceduralMatrixGpu
    {
        public float4 Row0;
        public float4 Row1;
        public float4 Row2;
    }

    public struct ActorProceduralDrawGpu
    {
        public int FirstIndex;
        public int FirstVertex;
        public int BoneMatrixOffset;
        public int BoneMatrixSource;
        public int TextureSlice;
        public int Padding0;
        public int Padding1;
        public int Padding2;
        public ActorProceduralMatrixGpu LocalToWorld;
    }

    public struct ActorProceduralSortEntry : IComparable<ActorProceduralSortEntry>
    {
        public ulong SortKey;
        public int DrawIndex;

        public int CompareTo(ActorProceduralSortEntry other)
        {
            return SortKey.CompareTo(other.SortKey);
        }
    }

    public struct ActorProceduralBatchTypeInfo
    {
        public int BucketIndex;
        public int MaterialIndex;
        public int IndexCount;
    }

    public struct ActorProceduralSkinMeshRuntimeInfo
    {
        public int BatchTypeId;
        public int BoneMatrixCount;
        public int TextureSlice;
        public int FirstIndex;
        public int IndexCount;
        public int FirstVertex;
    }

    public struct ActorProceduralDrawBatch
    {
        public int BucketIndex;
        public int MaterialIndex;
        public int DrawBase;
        public int DrawCount;
        public int IndexCount;
    }

    public enum ActorProceduralRenderSet
    {
        Forward,
        Shadow,
    }

    public sealed class ActorProceduralRenderResources : IDisposable
    {
        public static readonly int VerticesId = Shader.PropertyToID("_ActorVertices");
        public static readonly int IndicesId = Shader.PropertyToID("_ActorIndices");
        public static readonly int CpuBoneMatricesId = Shader.PropertyToID("_ActorCpuBoneMatrices");
        public static readonly int GpuBoneMatricesId = Shader.PropertyToID("_ActorGpuBoneMatrices");
        public static readonly int DrawsId = Shader.PropertyToID("_ActorDraws");
        public static readonly int DrawIndexId = Shader.PropertyToID("_ActorDrawIndex");
        public static readonly int DrawBaseId = Shader.PropertyToID("_ActorDrawBase");
        static readonly int BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
        const int ProceduralIndirectArgsCount = 4;
        const GraphicsBuffer.UsageFlags DynamicBufferUsage = GraphicsBuffer.UsageFlags.LockBufferForWrite;

        public NativeList<ActorProceduralDrawGpu> Draws;
        public NativeList<ActorProceduralDrawBatch> Batches;
        public NativeList<ActorProceduralMatrixGpu> BoneMatrices;
        public NativeList<ActorProceduralDrawGpu> ShadowDraws;
        public NativeList<ActorProceduralDrawBatch> ShadowBatches;
        public NativeList<ActorProceduralMatrixGpu> ShadowBoneMatrices;

        NativeList<ActorProceduralDrawGpu> _packedDraws;
        NativeList<int> _packedBatchTypeIds;
        NativeList<int> _batchTypeCounts;
        NativeList<int> _batchTypeOffsets;
        NativeList<int> _batchTypeWriteHeads;
        NativeList<ActorProceduralBatchTypeInfo> _batchTypeInfos;
        NativeList<ActorProceduralSkinMeshRuntimeInfo> _skinMeshRuntimeInfos;
        NativeList<ActorProceduralVertexGpu> _vertices;
        NativeList<int> _indices;
        NativeList<uint> _indirectArgs;
        NativeList<uint> _shadowIndirectArgs;

        GraphicsBuffer _vertexBuffer;
        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _boneMatrixBuffer;
        GraphicsBuffer _shadowBoneMatrixBuffer;
        GraphicsBuffer _gpuBoneMatrixBuffer;
        GraphicsBuffer _drawBuffer;
        GraphicsBuffer _shadowDrawBuffer;
        GraphicsBuffer _indirectArgsBuffer;
        GraphicsBuffer _shadowIndirectArgsBuffer;
        Material[][] _materials;
        Texture2DArray _manualWhiteArray;
        Shader _shader;
        int _geometrySkinMeshCount = -1;
        int _invalidBatchTypeId = -1;
        bool _geometryUploaded;

        public ActorProceduralRenderResources()
        {
            _vertices = new NativeList<ActorProceduralVertexGpu>(Allocator.Persistent);
            _indices = new NativeList<int>(Allocator.Persistent);
            _indirectArgs = new NativeList<uint>(Allocator.Persistent);
            _shadowIndirectArgs = new NativeList<uint>(Allocator.Persistent);
            _packedDraws = new NativeList<ActorProceduralDrawGpu>(Allocator.Persistent);
            _packedBatchTypeIds = new NativeList<int>(Allocator.Persistent);
            _batchTypeCounts = new NativeList<int>(Allocator.Persistent);
            _batchTypeOffsets = new NativeList<int>(Allocator.Persistent);
            _batchTypeWriteHeads = new NativeList<int>(Allocator.Persistent);
            _batchTypeInfos = new NativeList<ActorProceduralBatchTypeInfo>(Allocator.Persistent);
            _skinMeshRuntimeInfos = new NativeList<ActorProceduralSkinMeshRuntimeInfo>(Allocator.Persistent);
            Draws = new NativeList<ActorProceduralDrawGpu>(Allocator.Persistent);
            Batches = new NativeList<ActorProceduralDrawBatch>(Allocator.Persistent);
            BoneMatrices = new NativeList<ActorProceduralMatrixGpu>(Allocator.Persistent);
            ShadowDraws = new NativeList<ActorProceduralDrawGpu>(Allocator.Persistent);
            ShadowBatches = new NativeList<ActorProceduralDrawBatch>(Allocator.Persistent);
            ShadowBoneMatrices = new NativeList<ActorProceduralMatrixGpu>(Allocator.Persistent);
        }

        public bool IsReadyForDraw =>
            _vertexBuffer != null
            && _indexBuffer != null
            && (_boneMatrixBuffer != null || _gpuBoneMatrixBuffer != null)
            && _drawBuffer != null
            && Draws.Length > 0
            && Batches.Length > 0;

        public bool IsReadyForShadowDraw =>
            _vertexBuffer != null
            && _indexBuffer != null
            && (_shadowBoneMatrixBuffer != null || _gpuBoneMatrixBuffer != null)
            && _shadowDrawBuffer != null
            && ShadowDraws.Length > 0
            && ShadowBatches.Length > 0;

        public GraphicsBuffer IndirectArgsBuffer => _indirectArgsBuffer;
        public int VertexCount => _vertices.IsCreated ? _vertices.Length : 0;
        public int IndexCount => _indices.IsCreated ? _indices.Length : 0;
        public int IndirectArgsCount => _indirectArgs.IsCreated ? _indirectArgs.Length : 0;
        public bool HasVertexBuffer => _vertexBuffer != null;
        public bool HasIndexBuffer => _indexBuffer != null;
        public bool HasBoneMatrixBuffer => _boneMatrixBuffer != null || _gpuBoneMatrixBuffer != null;
        public bool HasDrawBuffer => _drawBuffer != null;
        public bool HasIndirectArgsBuffer => _indirectArgsBuffer != null;

        public void EnsureStaticResources(ref ActorAnimationCatalogBlob catalog)
        {
            EnsureMaterials();
            EnsureBatchTypes(ref catalog);
            EnsureGeometry(ref catalog);
        }

        public void BeginFrame()
        {
            Draws.Clear();
            Batches.Clear();
            BoneMatrices.Clear();
            _indirectArgs.Clear();
            ShadowDraws.Clear();
            ShadowBatches.Clear();
            ShadowBoneMatrices.Clear();
            _shadowIndirectArgs.Clear();
        }

        public void ReserveFrameCapacity(int drawCount, int boneMatrixCount)
        {
            ReserveFrameCapacity(ActorProceduralRenderSet.Forward, drawCount, boneMatrixCount);
        }

        public void ReserveFrameCapacity(ActorProceduralRenderSet set, int drawCount, int boneMatrixCount)
        {
            var draws = GetDraws(set);
            var batches = GetBatches(set);
            var boneMatrices = GetBoneMatrices(set);
            var indirectArgs = GetIndirectArgs(set);

            if (drawCount > draws.Capacity)
                draws.Capacity = drawCount;
            if (drawCount > _packedDraws.Capacity)
                _packedDraws.Capacity = drawCount;
            if (drawCount > _packedBatchTypeIds.Capacity)
                _packedBatchTypeIds.Capacity = drawCount;
            if (boneMatrixCount > boneMatrices.Capacity)
                boneMatrices.Capacity = boneMatrixCount;
            int batchTypeCount = _batchTypeInfos.IsCreated ? _batchTypeInfos.Length : 0;
            if (batchTypeCount > batches.Capacity)
                batches.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeCounts.Capacity)
                _batchTypeCounts.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeOffsets.Capacity)
                _batchTypeOffsets.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeWriteHeads.Capacity)
                _batchTypeWriteHeads.Capacity = batchTypeCount;
            int indirectArgCount = drawCount * ProceduralIndirectArgsCount;
            if (indirectArgCount > indirectArgs.Capacity)
                indirectArgs.Capacity = indirectArgCount;
        }

        public void PrepareFrameData(int drawCount, int boneMatrixCount)
        {
            PrepareFrameData(ActorProceduralRenderSet.Forward, drawCount, boneMatrixCount);
        }

        public void PrepareFrameData(ActorProceduralRenderSet set, int drawCount, int boneMatrixCount)
        {
            ReserveFrameCapacity(set, drawCount, boneMatrixCount);
            var draws = GetDraws(set);
            var batches = GetBatches(set);
            var boneMatrices = GetBoneMatrices(set);
            var indirectArgs = GetIndirectArgs(set);

            _packedDraws.ResizeUninitialized(drawCount);
            _packedBatchTypeIds.ResizeUninitialized(drawCount);
            draws.ResizeUninitialized(drawCount);
            boneMatrices.ResizeUninitialized(boneMatrixCount);
            int batchTypeCount = _batchTypeInfos.IsCreated ? _batchTypeInfos.Length : 0;
            batches.ResizeUninitialized(batchTypeCount);
            _batchTypeCounts.ResizeUninitialized(batchTypeCount);
            _batchTypeOffsets.ResizeUninitialized(batchTypeCount);
            _batchTypeWriteHeads.ResizeUninitialized(batchTypeCount);
            indirectArgs.Clear();
        }

        public NativeArray<ActorProceduralDrawBatch> BatchScratch => Batches.AsArray();
        public NativeArray<ActorProceduralDrawBatch> ShadowBatchScratch => ShadowBatches.AsArray();
        public NativeArray<int> BatchTypeCounts => _batchTypeCounts.AsArray();
        public NativeArray<int> BatchTypeOffsets => _batchTypeOffsets.AsArray();
        public NativeArray<int> BatchTypeWriteHeads => _batchTypeWriteHeads.AsArray();
        public NativeArray<ActorProceduralBatchTypeInfo> BatchTypeInfos => _batchTypeInfos.AsArray();
        public NativeArray<ActorProceduralDrawGpu> PackedDraws => _packedDraws.AsArray();
        public NativeArray<int> PackedBatchTypeIds => _packedBatchTypeIds.AsArray();
        public NativeArray<ActorProceduralSkinMeshRuntimeInfo> SkinMeshRuntimeInfos => _skinMeshRuntimeInfos.AsArray();
        public int InvalidBatchTypeId => _invalidBatchTypeId;

        public void FinalizeBatchCount(int batchCount)
        {
            FinalizeBatchCount(ActorProceduralRenderSet.Forward, batchCount);
        }

        public void FinalizeBatchCount(ActorProceduralRenderSet set, int batchCount)
        {
            var batches = GetBatches(set);
            if (batchCount <= 0)
            {
                batches.Clear();
                return;
            }

            batches.ResizeUninitialized(batchCount);
        }

        public void LoadManualPreview(
            ActorProceduralVertexGpu[] vertices,
            int[] indices,
            ActorProceduralMatrixGpu[] boneMatrices,
            ActorProceduralDrawGpu[] draws,
            ActorProceduralDrawBatch[] batches)
        {
            EnsureManualPreviewMaterial();

            _vertices.Clear();
            _indices.Clear();
            BeginFrame();

            if (vertices != null)
            {
                for (int i = 0; i < vertices.Length; i++)
                    _vertices.Add(vertices[i]);
            }

            if (indices != null)
            {
                for (int i = 0; i < indices.Length; i++)
                    _indices.Add(indices[i]);
            }

            if (boneMatrices != null)
            {
                for (int i = 0; i < boneMatrices.Length; i++)
                    BoneMatrices.Add(boneMatrices[i]);
            }

            if (draws != null)
            {
                for (int i = 0; i < draws.Length; i++)
                    Draws.Add(draws[i]);
            }

            if (batches != null)
            {
                for (int i = 0; i < batches.Length; i++)
                    Batches.Add(batches[i]);
            }

            EnsureBuffer(ref _vertexBuffer, _vertices.Length, UnsafeUtility.SizeOf<ActorProceduralVertexGpu>(), "VV:ActorPreviewVertices");
            EnsureBuffer(ref _indexBuffer, _indices.Length, sizeof(int), "VV:ActorPreviewIndices");
            if (_vertices.Length > 0)
                _vertexBuffer.SetData(_vertices.AsArray());
            if (_indices.Length > 0)
                _indexBuffer.SetData(_indices.AsArray());

            _geometrySkinMeshCount = -1;
            _geometryUploaded = true;
            UploadFrame();
        }

        public int AddBoneMatrices(DynamicBuffer<ActorBone> bones)
        {
            int offset = BoneMatrices.Length;
            for (int i = 0; i < bones.Length; i++)
                BoneMatrices.Add(ToGpuMatrix(bones[i].LocalToRoot));
            return offset;
        }

        public void AddDraw(
            ref ActorAnimationCatalogBlob catalog,
            in ActorProceduralDraw draw,
            DynamicBuffer<ActorBone> bones,
            float4x4 localToWorld)
        {
            if ((uint)draw.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                return;

            var skinMesh = catalog.SkinMeshes[draw.SkinMeshIndex];
            ResolveTexture(draw.TextureIndex, out int bucketIndex, out int textureSlice);
            int materialIndex = math.clamp(draw.MaterialIndex, 0, math.max(0, WorldResources.BlendVariantCount - 1));
            int boneMatrixOffset = AddSkinBoneMatrices(ref catalog, skinMesh, bones, draw.AttachBoneIndex, draw.RigidMirrorX);

            int drawIndex = Draws.Length;
            Draws.Add(new ActorProceduralDrawGpu
            {
                FirstIndex = skinMesh.FirstIndexIndex,
                FirstVertex = skinMesh.FirstVertexIndex,
                BoneMatrixOffset = boneMatrixOffset,
                BoneMatrixSource = 0,
                TextureSlice = textureSlice,
                LocalToWorld = ToGpuMatrix(localToWorld),
            });

            AppendBatch(bucketIndex, materialIndex, drawIndex, skinMesh.IndexCount);
        }

        int AddSkinBoneMatrices(
            ref ActorAnimationCatalogBlob catalog,
            ActorSkinMeshBlob skinMesh,
            DynamicBuffer<ActorBone> bones,
            int attachBoneIndex,
            byte rigidMirrorX)
        {
            int offset = BoneMatrices.Length;

            if (skinMesh.IsRigid != 0)
            {
                float4x4 attach = (uint)attachBoneIndex < (uint)bones.Length
                    ? bones[attachBoneIndex].LocalToRoot
                    : float4x4.identity;
                float4x4 mirror = rigidMirrorX != 0
                    ? new float4x4(
                        new float4(-1f, 0f, 0f, 0f),
                        new float4(0f, 1f, 0f, 0f),
                        new float4(0f, 0f, 1f, 0f),
                        new float4(0f, 0f, 0f, 1f))
                    : float4x4.identity;
                float4x4 gts = ActorAnimationSpaceConversion.SourceAffineToUnity(skinMesh.GeometryToSkeleton);
                float4x4 localAttach = math.mul(mirror, gts);
                BoneMatrices.Add(ToGpuMatrix(math.mul(attach, localAttach)));
                return offset;
            }

            int firstSkinBone = skinMesh.FirstSkinBoneIndex;
            int skinBoneCount = skinMesh.SkinBoneCount;
            int end = math.min(catalog.SkinBones.Length, firstSkinBone + skinBoneCount);
            float4x4 geometryToSkeleton = ActorAnimationSpaceConversion.SourceAffineToUnity(skinMesh.GeometryToSkeleton);

            for (int i = firstSkinBone; i < end; i++)
            {
                var skinBone = catalog.SkinBones[i];
                int actorBoneIndex = ResolveActorBoneIndex(bones, skinBone);
                float4x4 pose = bones[actorBoneIndex].LocalToRoot;
                float4x4 bindPose = ActorAnimationSpaceConversion.SourceAffineToUnity(skinBone.BindPose);
                BoneMatrices.Add(ToGpuMatrix(math.mul(math.mul(geometryToSkeleton, pose), bindPose)));
            }

            if (BoneMatrices.Length == offset)
                BoneMatrices.Add(ToGpuMatrix(float4x4.identity));

            return offset;
        }

        int ResolveActorBoneIndex(DynamicBuffer<ActorBone> bones, ActorSkinBoneBlob skinBone)
        {
            int boneIndex = skinBone.BoneIndex;
            if ((uint)boneIndex >= (uint)bones.Length)
            {
                throw new InvalidOperationException(
                    $"Actor skin bone '{skinBone.Name}' references invalid baked bone index {boneIndex} for actor with {bones.Length} bones.");
            }

            return boneIndex;
        }

        public void UploadFrame()
        {
            BuildIndirectArgs(ActorProceduralRenderSet.Forward);
            BuildIndirectArgs(ActorProceduralRenderSet.Shadow);
            EnsureBuffer(ref _boneMatrixBuffer, BoneMatrices.Length, UnsafeUtility.SizeOf<ActorProceduralMatrixGpu>(), "VV:ActorBoneMatrices", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _drawBuffer, Draws.Length, UnsafeUtility.SizeOf<ActorProceduralDrawGpu>(), "VV:ActorDraws", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _indirectArgsBuffer, _indirectArgs.Length, sizeof(uint), "VV:ActorIndirectArgs", GraphicsBuffer.Target.IndirectArguments, DynamicBufferUsage);
            EnsureBuffer(ref _shadowBoneMatrixBuffer, ShadowBoneMatrices.Length, UnsafeUtility.SizeOf<ActorProceduralMatrixGpu>(), "VV:ActorShadowBoneMatrices", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _shadowDrawBuffer, ShadowDraws.Length, UnsafeUtility.SizeOf<ActorProceduralDrawGpu>(), "VV:ActorShadowDraws", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _shadowIndirectArgsBuffer, _shadowIndirectArgs.Length, sizeof(uint), "VV:ActorShadowIndirectArgs", GraphicsBuffer.Target.IndirectArguments, DynamicBufferUsage);

            if (BoneMatrices.Length > 0)
                WriteBuffer(_boneMatrixBuffer, BoneMatrices.AsArray());
            if (Draws.Length > 0)
                WriteBuffer(_drawBuffer, Draws.AsArray());
            if (_indirectArgs.Length > 0)
                WriteBuffer(_indirectArgsBuffer, _indirectArgs.AsArray());
            if (ShadowBoneMatrices.Length > 0)
                WriteBuffer(_shadowBoneMatrixBuffer, ShadowBoneMatrices.AsArray());
            if (ShadowDraws.Length > 0)
                WriteBuffer(_shadowDrawBuffer, ShadowDraws.AsArray());
            if (_shadowIndirectArgs.Length > 0)
                WriteBuffer(_shadowIndirectArgsBuffer, _shadowIndirectArgs.AsArray());

        }

        public Material GetMaterial(int bucketIndex, int materialIndex)
        {
            if (_materials == null || (uint)bucketIndex >= (uint)_materials.Length)
                return null;

            var bucket = _materials[bucketIndex];
            if (bucket == null || bucket.Length == 0)
                return null;

            return bucket[math.clamp(materialIndex, 0, bucket.Length - 1)];
        }

        public void Bind(MaterialPropertyBlock block)
        {
            Bind(block, ActorProceduralRenderSet.Forward);
        }

        public void Bind(MaterialPropertyBlock block, ActorProceduralRenderSet set)
        {
            GraphicsBuffer boneBuffer = set == ActorProceduralRenderSet.Shadow ? _shadowBoneMatrixBuffer : _boneMatrixBuffer;
            GraphicsBuffer drawBuffer = set == ActorProceduralRenderSet.Shadow ? _shadowDrawBuffer : _drawBuffer;
            block.SetBuffer(VerticesId, _vertexBuffer);
            block.SetBuffer(IndicesId, _indexBuffer);
            block.SetBuffer(CpuBoneMatricesId, boneBuffer ?? _gpuBoneMatrixBuffer);
            block.SetBuffer(GpuBoneMatricesId, _gpuBoneMatrixBuffer ?? boneBuffer);
            block.SetBuffer(DrawsId, drawBuffer);
        }

        public void DrawBatches(RasterCommandBuffer cmd, MaterialPropertyBlock properties, int shaderPass)
        {
            DrawBatches(cmd, properties, shaderPass, ActorProceduralRenderSet.Forward);
        }

        public void DrawBatches(RasterCommandBuffer cmd, MaterialPropertyBlock properties, int shaderPass, ActorProceduralRenderSet set)
        {
            var batches = GetBatches(set);
            GraphicsBuffer indirectArgsBuffer = set == ActorProceduralRenderSet.Shadow ? _shadowIndirectArgsBuffer : _indirectArgsBuffer;
            for (int i = 0; i < batches.Length; i++)
            {
                var batch = batches[i];
                Material material = GetMaterial(batch.BucketIndex, batch.MaterialIndex);
                if (material == null || batch.DrawCount <= 0 || batch.IndexCount <= 0)
                    continue;

                properties.SetInt(DrawBaseId, batch.DrawBase);
                cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    material,
                        shaderPass,
                        MeshTopology.Triangles,
                        indirectArgsBuffer,
                        GetIndirectArgsOffsetBytes(i),
                        properties);
            }
        }

        public void SetGpuBoneMatrixBuffer(GraphicsBuffer buffer, int boneMatrixCount)
        {
            _gpuBoneMatrixBuffer = buffer;
        }

        public void ClearGpuBoneMatrixBuffer()
        {
            _gpuBoneMatrixBuffer = null;
        }

        public int GetIndirectArgsOffsetBytes(int batchIndex)
        {
            return batchIndex * ProceduralIndirectArgsCount * sizeof(uint);
        }

        public void Dispose()
        {
            ReleaseBuffer(ref _vertexBuffer);
            ReleaseBuffer(ref _indexBuffer);
            ReleaseBuffer(ref _boneMatrixBuffer);
            ReleaseBuffer(ref _shadowBoneMatrixBuffer);
            ReleaseBuffer(ref _drawBuffer);
            ReleaseBuffer(ref _shadowDrawBuffer);
            ReleaseBuffer(ref _indirectArgsBuffer);
            ReleaseBuffer(ref _shadowIndirectArgsBuffer);

            if (_materials != null)
            {
                for (int bucket = 0; bucket < _materials.Length; bucket++)
                {
                    var bucketMaterials = _materials[bucket];
                    if (bucketMaterials == null)
                        continue;

                    for (int i = 0; i < bucketMaterials.Length; i++)
                        if (bucketMaterials[i] != null)
                            UnityEngine.Object.Destroy(bucketMaterials[i]);
                }
                _materials = null;
            }

            if (_manualWhiteArray != null)
            {
                UnityEngine.Object.Destroy(_manualWhiteArray);
                _manualWhiteArray = null;
            }

            if (_vertices.IsCreated) _vertices.Dispose();
            if (_indices.IsCreated) _indices.Dispose();
            if (_indirectArgs.IsCreated) _indirectArgs.Dispose();
            if (_shadowIndirectArgs.IsCreated) _shadowIndirectArgs.Dispose();
            if (_packedDraws.IsCreated) _packedDraws.Dispose();
            if (_packedBatchTypeIds.IsCreated) _packedBatchTypeIds.Dispose();
            if (_batchTypeCounts.IsCreated) _batchTypeCounts.Dispose();
            if (_batchTypeOffsets.IsCreated) _batchTypeOffsets.Dispose();
            if (_batchTypeWriteHeads.IsCreated) _batchTypeWriteHeads.Dispose();
            if (_batchTypeInfos.IsCreated) _batchTypeInfos.Dispose();
            if (_skinMeshRuntimeInfos.IsCreated) _skinMeshRuntimeInfos.Dispose();
            if (Draws.IsCreated) Draws.Dispose();
            if (Batches.IsCreated) Batches.Dispose();
            if (BoneMatrices.IsCreated) BoneMatrices.Dispose();
            if (ShadowDraws.IsCreated) ShadowDraws.Dispose();
            if (ShadowBatches.IsCreated) ShadowBatches.Dispose();
            if (ShadowBoneMatrices.IsCreated) ShadowBoneMatrices.Dispose();
        }

        void EnsureBatchTypes(ref ActorAnimationCatalogBlob catalog)
        {
            if (_skinMeshRuntimeInfos.IsCreated && _skinMeshRuntimeInfos.Length == catalog.SkinMeshes.Length)
                return;

            _skinMeshRuntimeInfos.Clear();
            _batchTypeInfos.Clear();

            if (catalog.SkinMeshes.Length <= 0)
                return;

            var uniqueKeys = new Dictionary<ulong, ActorProceduralBatchTypeInfo>(catalog.SkinMeshes.Length);
            for (int i = 0; i < catalog.SkinMeshes.Length; i++)
            {
                var skinMesh = catalog.SkinMeshes[i];
                ResolveTexture(skinMesh.TextureIndex, out int bucketIndex, out int textureSlice);
                int materialIndex = math.clamp(skinMesh.MaterialIndex, 0, math.max(0, WorldResources.BlendVariantCount - 1));
                ulong sortKey = ComposeSortKey(bucketIndex, materialIndex, skinMesh.IndexCount);
                if (!uniqueKeys.ContainsKey(sortKey))
                {
                    uniqueKeys.Add(sortKey, new ActorProceduralBatchTypeInfo
                    {
                        BucketIndex = bucketIndex,
                        MaterialIndex = materialIndex,
                        IndexCount = skinMesh.IndexCount,
                    });
                }
            }

            var sortedKeys = new ulong[uniqueKeys.Count];
            uniqueKeys.Keys.CopyTo(sortedKeys, 0);
            Array.Sort(sortedKeys);

            var batchTypeIdByKey = new Dictionary<ulong, int>(sortedKeys.Length);
            for (int i = 0; i < sortedKeys.Length; i++)
            {
                ulong key = sortedKeys[i];
                batchTypeIdByKey[key] = i;
                _batchTypeInfos.Add(uniqueKeys[key]);
            }

            _invalidBatchTypeId = _batchTypeInfos.Length;
            _batchTypeInfos.Add(new ActorProceduralBatchTypeInfo
            {
                BucketIndex = 0,
                MaterialIndex = 0,
                IndexCount = 0,
            });

            for (int i = 0; i < catalog.SkinMeshes.Length; i++)
            {
                var skinMesh = catalog.SkinMeshes[i];
                ResolveTexture(skinMesh.TextureIndex, out int bucketIndex, out int textureSlice);
                int materialIndex = math.clamp(skinMesh.MaterialIndex, 0, math.max(0, WorldResources.BlendVariantCount - 1));
                ulong sortKey = ComposeSortKey(bucketIndex, materialIndex, skinMesh.IndexCount);
                _skinMeshRuntimeInfos.Add(new ActorProceduralSkinMeshRuntimeInfo
                {
                    BatchTypeId = batchTypeIdByKey.TryGetValue(sortKey, out int batchTypeId) ? batchTypeId : -1,
                    BoneMatrixCount = math.max(1, skinMesh.SkinBoneCount),
                    TextureSlice = textureSlice,
                    FirstIndex = skinMesh.FirstIndexIndex,
                    IndexCount = skinMesh.IndexCount,
                    FirstVertex = skinMesh.FirstVertexIndex,
                });
            }
        }

        void EnsureGeometry(ref ActorAnimationCatalogBlob catalog)
        {
            if (_geometryUploaded && _geometrySkinMeshCount == catalog.SkinMeshes.Length)
                return;

            _vertices.Clear();
            _indices.Clear();
            for (int i = 0; i < catalog.SkinVertices.Length; i++)
            {
                var source = catalog.SkinVertices[i];
                _vertices.Add(new ActorProceduralVertexGpu
                {
                    Position = source.Position,
                    Normal = source.Normal,
                    Uv = source.Uv,
                    BoneIndices0 = source.BoneIndices0,
                    BoneIndices1 = source.BoneIndices1,
                    Weights0 = source.Weights0,
                    Weights1 = source.Weights1,
                });
            }

            for (int i = 0; i < catalog.SkinIndices.Length; i++)
                _indices.Add(catalog.SkinIndices[i]);

            EnsureBuffer(ref _vertexBuffer, _vertices.Length, UnsafeUtility.SizeOf<ActorProceduralVertexGpu>(), "VV:ActorVertices");
            EnsureBuffer(ref _indexBuffer, _indices.Length, sizeof(int), "VV:ActorIndices");

            if (_vertices.Length > 0)
                _vertexBuffer.SetData(_vertices.AsArray());
            if (_indices.Length > 0)
                _indexBuffer.SetData(_indices.AsArray());

            _geometrySkinMeshCount = catalog.SkinMeshes.Length;
            _geometryUploaded = true;
        }

        void EnsureMaterials()
        {
            var arrays = WorldResources.RefBaseArrays;
            int bucketCount = arrays?.Length ?? 0;
            int variantCount = WorldResources.BlendVariantCount;
            if (bucketCount <= 0 || variantCount <= 0)
                return;

            if (_materials != null && _materials.Length == bucketCount)
                return;

            _shader = _shader != null ? _shader : Shader.Find("VVardenfell/MwActorProcedural");
            if (_shader == null)
                return;

            _materials = new Material[bucketCount][];
            var cacheMaterials = WorldResources.Cache?.Materials;
            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                _materials[bucket] = new Material[variantCount];
                for (int variant = 0; variant < variantCount; variant++)
                {
                    var material = new Material(_shader)
                    {
                        name = $"VV:ActorProcedural[b{bucket}:m{variant}]",
                        enableInstancing = false,
                        doubleSidedGI = true,
                    };

                    material.SetTexture(BaseArrayId, arrays[bucket]);
                    ApplyDefaultLitProperties(material);
                    CopyAlphaSettings(cacheMaterials, bucket, variant, variantCount, material);
                    _materials[bucket][variant] = material;
                }
            }
        }

        static void ApplyDefaultLitProperties(Material material)
        {
            material.SetColor(BaseColorId, Color.white);
            material.SetFloat(MetallicId, 0f);
            material.SetFloat(SmoothnessId, 0.25f);
            material.SetColor(SpecColorId, new Color(0.2f, 0.2f, 0.2f, 1f));
            material.SetColor(EmissionColorId, Color.black);
            material.SetFloat(OcclusionStrengthId, 1f);
        }

        static void CopyAlphaSettings(Material[] sourceMaterials, int bucket, int variant, int variantCount, Material material)
        {
            int sourceIndex = bucket * variantCount + variant;
            Material source = sourceMaterials != null && (uint)sourceIndex < (uint)sourceMaterials.Length
                ? sourceMaterials[sourceIndex]
                : null;

            if (source == null)
            {
                material.SetFloat(SrcBlendId, (float)BlendMode.One);
                material.SetFloat(DstBlendId, (float)BlendMode.Zero);
                material.SetFloat(ZWriteId, 1f);
                material.renderQueue = (int)RenderQueue.Geometry;
                return;
            }

            material.SetFloat(SrcBlendId, source.HasProperty(SrcBlendId) ? source.GetFloat(SrcBlendId) : (float)BlendMode.One);
            material.SetFloat(DstBlendId, source.HasProperty(DstBlendId) ? source.GetFloat(DstBlendId) : (float)BlendMode.Zero);
            material.SetFloat(ZWriteId, source.HasProperty(ZWriteId) ? source.GetFloat(ZWriteId) : 1f);
            material.SetFloat(CutoffId, source.HasProperty(CutoffId) ? source.GetFloat(CutoffId) : 0.5f);
            material.renderQueue = source.renderQueue;
            material.SetOverrideTag("RenderType", source.GetTag("RenderType", false, "Opaque"));

            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
                material.EnableKeyword("_ALPHATEST_ON");
            else
                material.DisableKeyword("_ALPHATEST_ON");

            if (source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        void EnsureManualPreviewMaterial()
        {
            _shader = _shader != null ? _shader : Shader.Find("VVardenfell/MwActorProcedural");
            if (_shader == null)
                return;

            if (_manualWhiteArray == null)
            {
                _manualWhiteArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false)
                {
                    name = "VV:ActorPreviewWhiteArray",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                };
                _manualWhiteArray.SetPixels(new[] { Color.white }, 0);
                _manualWhiteArray.Apply(false, true);
            }

            if (_materials != null && _materials.Length == 1 && _materials[0]?.Length == 1 && _materials[0][0] != null)
                return;

            if (_materials != null)
            {
                for (int bucket = 0; bucket < _materials.Length; bucket++)
                {
                    var bucketMaterials = _materials[bucket];
                    if (bucketMaterials == null)
                        continue;

                    for (int i = 0; i < bucketMaterials.Length; i++)
                        if (bucketMaterials[i] != null)
                            UnityEngine.Object.Destroy(bucketMaterials[i]);
                }
            }

            var material = new Material(_shader)
            {
                name = "VV:ActorPreviewProceduralWhite",
                enableInstancing = false,
                doubleSidedGI = true,
            };
            material.SetTexture(BaseArrayId, _manualWhiteArray);
            ApplyDefaultLitProperties(material);
            material.SetFloat(SrcBlendId, (float)BlendMode.One);
            material.SetFloat(DstBlendId, (float)BlendMode.Zero);
            material.SetFloat(ZWriteId, 1f);
            material.SetFloat(CutoffId, 0.5f);
            material.renderQueue = (int)RenderQueue.Geometry;
            _materials = new[] { new[] { material } };
        }

        void AppendBatch(int bucketIndex, int materialIndex, int drawIndex, int indexCount)
        {
            if (Batches.Length > 0)
            {
                var last = Batches[Batches.Length - 1];
                if (last.BucketIndex == bucketIndex
                    && last.MaterialIndex == materialIndex
                    && last.IndexCount == indexCount
                    && last.DrawBase + last.DrawCount == drawIndex)
                {
                    last.DrawCount++;
                    Batches[Batches.Length - 1] = last;
                    return;
                }
            }

            Batches.Add(new ActorProceduralDrawBatch
            {
                BucketIndex = bucketIndex,
                MaterialIndex = materialIndex,
                DrawBase = drawIndex,
                DrawCount = 1,
                IndexCount = indexCount,
            });
        }

        public static ulong ComposeSortKey(int bucketIndex, int materialIndex, int indexCount)
        {
            return ((ulong)(uint)bucketIndex << 48)
                | ((ulong)(uint)materialIndex << 32)
                | (uint)math.max(0, indexCount);
        }

        public static int ExtractBucketIndex(ulong sortKey)
        {
            return (int)((sortKey >> 48) & 0xFFFFu);
        }

        public static int ExtractMaterialIndex(ulong sortKey)
        {
            return (int)((sortKey >> 32) & 0xFFFFu);
        }

        public static int ExtractIndexCount(ulong sortKey)
        {
            return (int)(sortKey & 0xFFFFFFFFu);
        }

        NativeList<ActorProceduralDrawGpu> GetDraws(ActorProceduralRenderSet set)
            => set == ActorProceduralRenderSet.Shadow ? ShadowDraws : Draws;

        NativeList<ActorProceduralDrawBatch> GetBatches(ActorProceduralRenderSet set)
            => set == ActorProceduralRenderSet.Shadow ? ShadowBatches : Batches;

        NativeList<ActorProceduralMatrixGpu> GetBoneMatrices(ActorProceduralRenderSet set)
            => set == ActorProceduralRenderSet.Shadow ? ShadowBoneMatrices : BoneMatrices;

        NativeList<uint> GetIndirectArgs(ActorProceduralRenderSet set)
            => set == ActorProceduralRenderSet.Shadow ? _shadowIndirectArgs : _indirectArgs;

        void BuildIndirectArgs(ActorProceduralRenderSet set)
        {
            var indirectArgs = GetIndirectArgs(set);
            var batches = GetBatches(set);
            indirectArgs.Clear();
            for (int i = 0; i < batches.Length; i++)
            {
                var batch = batches[i];
                indirectArgs.Add((uint)math.max(0, batch.IndexCount));
                indirectArgs.Add((uint)math.max(0, batch.DrawCount));
                indirectArgs.Add(0);
                indirectArgs.Add(0);
            }
        }

        static void ResolveTexture(int textureIndex, out int bucketIndex, out int textureSlice)
        {
            if (textureIndex >= 0
                && WorldResources.TexBucketInfo.IsCreated
                && textureIndex < WorldResources.TexBucketInfo.Length)
            {
                int2 bucketSlice = WorldResources.TexBucketInfo[textureIndex];
                bucketIndex = bucketSlice.x;
                textureSlice = bucketSlice.y;
                return;
            }

            bucketIndex = WorldResources.FallbackBucketSlice.x;
            textureSlice = WorldResources.FallbackBucketSlice.y;
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

        static void EnsureBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string name,
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured,
            GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            count = math.max(1, count);
            if (buffer != null
                && buffer.count >= count
                && buffer.stride == stride
                && buffer.usageFlags == usageFlags)
                return;

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(target, usageFlags, count, stride)
            {
                name = name,
            };
        }

        static void WriteBuffer<T>(GraphicsBuffer buffer, NativeArray<T> data) where T : unmanaged
        {
            var dst = buffer.LockBufferForWrite<T>(0, data.Length);
            dst.CopyFrom(data);
            buffer.UnlockBufferAfterWrite<T>(data.Length);
        }

        static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }
    }
}
