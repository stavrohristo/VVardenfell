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

    public sealed class ActorProceduralRenderResources : IDisposable
    {
        public static readonly int VerticesId = Shader.PropertyToID("_ActorVertices");
        public static readonly int IndicesId = Shader.PropertyToID("_ActorIndices");
        public static readonly int CpuBoneMatricesId = Shader.PropertyToID("_ActorCpuBoneMatrices");
        public static readonly int GpuBoneMatricesId = Shader.PropertyToID("_ActorGpuBoneMatrices");
        public static readonly int DrawsId = Shader.PropertyToID("_ActorDraws");
        public static readonly int DrawIndexId = Shader.PropertyToID("_ActorDrawIndex");
        public static readonly int DrawBaseId = Shader.PropertyToID("_ActorDrawBase");
        public static readonly int MainLightDirectionId = Shader.PropertyToID("_ActorMainLightDirection");
        public static readonly int MainLightColorId = Shader.PropertyToID("_ActorMainLightColor");
        public static readonly int MainLightValidId = Shader.PropertyToID("_ActorMainLightValid");
        static readonly int BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        const int ProceduralIndirectArgsCount = 4;
        const GraphicsBuffer.UsageFlags DynamicBufferUsage = GraphicsBuffer.UsageFlags.LockBufferForWrite;

        public NativeList<ActorProceduralDrawGpu> Draws;
        public NativeList<ActorProceduralDrawBatch> Batches;
        public NativeList<ActorProceduralMatrixGpu> BoneMatrices;

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

        GraphicsBuffer _vertexBuffer;
        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _boneMatrixBuffer;
        GraphicsBuffer _gpuBoneMatrixBuffer;
        GraphicsBuffer _drawBuffer;
        GraphicsBuffer _indirectArgsBuffer;
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
        }

        public bool IsReadyForDraw =>
            _vertexBuffer != null
            && _indexBuffer != null
            && (_boneMatrixBuffer != null || _gpuBoneMatrixBuffer != null)
            && _drawBuffer != null
            && Draws.Length > 0
            && Batches.Length > 0;

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
        }

        public void ReserveFrameCapacity(int drawCount, int boneMatrixCount)
        {
            if (drawCount > Draws.Capacity)
                Draws.Capacity = drawCount;
            if (drawCount > _packedDraws.Capacity)
                _packedDraws.Capacity = drawCount;
            if (drawCount > _packedBatchTypeIds.Capacity)
                _packedBatchTypeIds.Capacity = drawCount;
            if (boneMatrixCount > BoneMatrices.Capacity)
                BoneMatrices.Capacity = boneMatrixCount;
            int batchTypeCount = _batchTypeInfos.IsCreated ? _batchTypeInfos.Length : 0;
            if (batchTypeCount > Batches.Capacity)
                Batches.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeCounts.Capacity)
                _batchTypeCounts.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeOffsets.Capacity)
                _batchTypeOffsets.Capacity = batchTypeCount;
            if (batchTypeCount > _batchTypeWriteHeads.Capacity)
                _batchTypeWriteHeads.Capacity = batchTypeCount;
            int indirectArgCount = drawCount * ProceduralIndirectArgsCount;
            if (indirectArgCount > _indirectArgs.Capacity)
                _indirectArgs.Capacity = indirectArgCount;
        }

        public void PrepareFrameData(int drawCount, int boneMatrixCount)
        {
            ReserveFrameCapacity(drawCount, boneMatrixCount);
            _packedDraws.ResizeUninitialized(drawCount);
            _packedBatchTypeIds.ResizeUninitialized(drawCount);
            Draws.ResizeUninitialized(drawCount);
            BoneMatrices.ResizeUninitialized(boneMatrixCount);
            int batchTypeCount = _batchTypeInfos.IsCreated ? _batchTypeInfos.Length : 0;
            Batches.ResizeUninitialized(batchTypeCount);
            _batchTypeCounts.ResizeUninitialized(batchTypeCount);
            _batchTypeOffsets.ResizeUninitialized(batchTypeCount);
            _batchTypeWriteHeads.ResizeUninitialized(batchTypeCount);
            _indirectArgs.Clear();
        }

        public NativeArray<ActorProceduralDrawBatch> BatchScratch => Batches.AsArray();
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
            if (batchCount <= 0)
            {
                Batches.Clear();
                return;
            }

            Batches.ResizeUninitialized(batchCount);
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
            BuildIndirectArgs();
            EnsureBuffer(ref _boneMatrixBuffer, BoneMatrices.Length, UnsafeUtility.SizeOf<ActorProceduralMatrixGpu>(), "VV:ActorBoneMatrices", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _drawBuffer, Draws.Length, UnsafeUtility.SizeOf<ActorProceduralDrawGpu>(), "VV:ActorDraws", GraphicsBuffer.Target.Structured, DynamicBufferUsage);
            EnsureBuffer(ref _indirectArgsBuffer, _indirectArgs.Length, sizeof(uint), "VV:ActorIndirectArgs", GraphicsBuffer.Target.IndirectArguments, DynamicBufferUsage);

            if (BoneMatrices.Length > 0)
                WriteBuffer(_boneMatrixBuffer, BoneMatrices.AsArray());
            if (Draws.Length > 0)
                WriteBuffer(_drawBuffer, Draws.AsArray());
            if (_indirectArgs.Length > 0)
                WriteBuffer(_indirectArgsBuffer, _indirectArgs.AsArray());

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
            block.SetBuffer(VerticesId, _vertexBuffer);
            block.SetBuffer(IndicesId, _indexBuffer);
            block.SetBuffer(CpuBoneMatricesId, _boneMatrixBuffer ?? _gpuBoneMatrixBuffer);
            block.SetBuffer(GpuBoneMatricesId, _gpuBoneMatrixBuffer ?? _boneMatrixBuffer);
            block.SetBuffer(DrawsId, _drawBuffer);
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
            ReleaseBuffer(ref _drawBuffer);
            ReleaseBuffer(ref _indirectArgsBuffer);

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
                    CopyAlphaSettings(cacheMaterials, bucket, variant, variantCount, material);
                    _materials[bucket][variant] = material;
                }
            }
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

        void BuildIndirectArgs()
        {
            _indirectArgs.Clear();
            for (int i = 0; i < Batches.Length; i++)
            {
                var batch = Batches[i];
                _indirectArgs.Add((uint)math.max(0, batch.IndexCount));
                _indirectArgs.Add((uint)math.max(0, batch.DrawCount));
                _indirectArgs.Add(0);
                _indirectArgs.Add(0);
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
