using System;
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
        public int IndexCount;
        public int FirstVertex;
        public int BoneMatrixOffset;
        public int TextureSlice;
        public int Padding0;
        public int Padding1;
        public int Padding2;
        public ActorProceduralMatrixGpu LocalToWorld;
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
        public static readonly int BoneMatricesId = Shader.PropertyToID("_ActorBoneMatrices");
        public static readonly int DrawsId = Shader.PropertyToID("_ActorDraws");
        public static readonly int DrawIndexId = Shader.PropertyToID("_ActorDrawIndex");
        static readonly int BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        const int ProceduralIndirectArgsCount = 4;

        public NativeList<ActorProceduralDrawGpu> Draws;
        public NativeList<ActorProceduralDrawBatch> Batches;
        public NativeList<ActorProceduralMatrixGpu> BoneMatrices;

        readonly NativeList<ActorProceduralVertexGpu> _vertices;
        readonly NativeList<int> _indices;
        readonly NativeList<uint> _indirectArgs;

        GraphicsBuffer _vertexBuffer;
        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _boneMatrixBuffer;
        GraphicsBuffer _drawBuffer;
        GraphicsBuffer _indirectArgsBuffer;
        Material[][] _materials;
        Shader _shader;
        int _geometrySkinMeshCount = -1;
        bool _geometryUploaded;

        public ActorProceduralRenderResources()
        {
            _vertices = new NativeList<ActorProceduralVertexGpu>(Allocator.Persistent);
            _indices = new NativeList<int>(Allocator.Persistent);
            _indirectArgs = new NativeList<uint>(Allocator.Persistent);
            Draws = new NativeList<ActorProceduralDrawGpu>(Allocator.Persistent);
            Batches = new NativeList<ActorProceduralDrawBatch>(Allocator.Persistent);
            BoneMatrices = new NativeList<ActorProceduralMatrixGpu>(Allocator.Persistent);
        }

        public bool IsReadyForDraw =>
            _vertexBuffer != null
            && _indexBuffer != null
            && _boneMatrixBuffer != null
            && _drawBuffer != null
            && Draws.Length > 0
            && Batches.Length > 0;

        public GraphicsBuffer IndirectArgsBuffer => _indirectArgsBuffer;
        public int VertexCount => _vertices.IsCreated ? _vertices.Length : 0;
        public int IndexCount => _indices.IsCreated ? _indices.Length : 0;
        public int IndirectArgsCount => _indirectArgs.IsCreated ? _indirectArgs.Length : 0;
        public bool HasVertexBuffer => _vertexBuffer != null;
        public bool HasIndexBuffer => _indexBuffer != null;
        public bool HasBoneMatrixBuffer => _boneMatrixBuffer != null;
        public bool HasDrawBuffer => _drawBuffer != null;
        public bool HasIndirectArgsBuffer => _indirectArgsBuffer != null;

        public void EnsureStaticResources(ref ActorAnimationCatalogBlob catalog)
        {
            EnsureMaterials();
            EnsureGeometry(ref catalog);
        }

        public void BeginFrame()
        {
            Draws.Clear();
            Batches.Clear();
            BoneMatrices.Clear();
            _indirectArgs.Clear();
        }

        public int AddBoneMatrices(DynamicBuffer<ActorBone> bones)
        {
            int offset = BoneMatrices.Length;
            for (int i = 0; i < bones.Length; i++)
                BoneMatrices.Add(ToGpuMatrix(bones[i].SkinMatrix));
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
                IndexCount = skinMesh.IndexCount,
                FirstVertex = skinMesh.FirstVertexIndex,
                BoneMatrixOffset = boneMatrixOffset,
                TextureSlice = textureSlice,
                Padding0 = 0,
                Padding1 = 0,
                Padding2 = 0,
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
                BoneMatrices.Add(ToGpuMatrix(math.mul(attach, localAttach)));
                return offset;
            }

            int firstSkinBone = skinMesh.FirstSkinBoneIndex;
            int skinBoneCount = skinMesh.SkinBoneCount;
            int end = math.min(catalog.SkinBones.Length, firstSkinBone + skinBoneCount);

            for (int i = firstSkinBone; i < end; i++)
            {
                var skinBone = catalog.SkinBones[i];
                int actorBoneIndex = ResolveActorBoneIndex(bones, skinBone);
                float4x4 pose = bones[actorBoneIndex].LocalToRoot;
                BoneMatrices.Add(ToGpuMatrix(math.mul(pose, skinBone.BindPose)));
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

            if (!skinBone.Name.IsEmpty && !BoneNamesMatch(bones[boneIndex].Name.ToString(), skinBone.Name.ToString()))
            {
                throw new InvalidOperationException(
                    $"Actor skin bone '{skinBone.Name}' baked index {boneIndex} resolves to actor bone '{bones[boneIndex].Name}'.");
            }

            return boneIndex;
        }

        static bool BoneNamesMatch(string actorBoneName, string skinBoneName)
        {
            return string.Equals(actorBoneName, skinBoneName, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(CanonicalBoneName(actorBoneName), CanonicalBoneName(skinBoneName), StringComparison.OrdinalIgnoreCase);
        }

        static string CanonicalBoneName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string value = name.Trim().ToLowerInvariant();
            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");

            return value;
        }

        public void UploadFrame()
        {
            BuildIndirectArgs();
            EnsureBuffer(ref _boneMatrixBuffer, BoneMatrices.Length, UnsafeUtility.SizeOf<ActorProceduralMatrixGpu>(), "VV:ActorBoneMatrices");
            EnsureBuffer(ref _drawBuffer, Draws.Length, UnsafeUtility.SizeOf<ActorProceduralDrawGpu>(), "VV:ActorDraws");
            EnsureBuffer(ref _indirectArgsBuffer, _indirectArgs.Length, sizeof(uint), "VV:ActorIndirectArgs", GraphicsBuffer.Target.IndirectArguments);

            if (BoneMatrices.Length > 0)
                _boneMatrixBuffer.SetData(BoneMatrices.AsArray());
            if (Draws.Length > 0)
                _drawBuffer.SetData(Draws.AsArray());
            if (_indirectArgs.Length > 0)
                _indirectArgsBuffer.SetData(_indirectArgs.AsArray());

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
            block.SetBuffer(BoneMatricesId, _boneMatrixBuffer);
            block.SetBuffer(DrawsId, _drawBuffer);
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

            if (_vertices.IsCreated) _vertices.Dispose();
            if (_indices.IsCreated) _indices.Dispose();
            if (_indirectArgs.IsCreated) _indirectArgs.Dispose();
            if (Draws.IsCreated) Draws.Dispose();
            if (Batches.IsCreated) Batches.Dispose();
            if (BoneMatrices.IsCreated) BoneMatrices.Dispose();
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
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured)
        {
            count = math.max(1, count);
            if (buffer != null && buffer.count >= count && buffer.stride == stride)
                return;

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(target, count, stride)
            {
                name = name,
            };
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
