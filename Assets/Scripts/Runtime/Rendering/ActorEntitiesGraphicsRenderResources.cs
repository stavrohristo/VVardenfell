using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Streaming;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Rendering
{
    public struct ActorSkinMeshRenderInfo
    {
        public int BucketIndex;
        public int MaterialIndex;
        public int MeshIndex;
        public int MirroredMeshIndex;
        public int TextureSlice;
        public int VertexCount;
    }

    public sealed class ActorEntitiesGraphicsRenderResources : IDisposable
    {
        static readonly int BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        Mesh[] _meshes;
        Material[][] _materials;
        RenderMeshArray[] _renderMeshArrays;
        Entity[] _prototypes;
        Entity[] _shadowOnlyPrototypes;
        ActorSkinMeshRenderInfo[] _skinMeshInfos;
        ulong _catalogSignature;

        public bool IsReady => HasManagedResources() && HasPrototypeArrays();

        public void Ensure(EntityManager entityManager, ref ActorAnimationCatalogBlob catalog)
        {
            ulong signature = BuildSignature(ref catalog);
            bool signatureMatches = signature == _catalogSignature;
            if (signatureMatches && HasManagedResources() && HasValidPrototypeEntities(entityManager))
                return;

            int bucketCount = WorldResources.RefBaseArrays?.Length ?? 0;
            int variantCount = WorldResources.BlendVariantCount;
            if (bucketCount <= 0 || variantCount <= 0)
                throw new InvalidOperationException("Actor Entities Graphics render resources require loaded texture buckets and blend variants.");

            if (signatureMatches && HasManagedResources())
            {
                DisposeEntityResources(entityManager);
                BuildPrototypes(entityManager, bucketCount);
                return;
            }

            DisposeEntityResources(entityManager);
            DisposeManagedResources();

            Shader shader = Shader.Find("VVardenfell/MwActorEntitiesGraphics");
            if (shader == null)
                throw new InvalidOperationException("Missing shader 'VVardenfell/MwActorEntitiesGraphics'.");

            BuildMeshes(ref catalog);
            BuildSkinMeshInfos(ref catalog);
            BuildMaterials(shader, bucketCount, variantCount);
            BuildRenderMeshArrays(bucketCount);
            BuildPrototypes(entityManager, bucketCount);
            _catalogSignature = signature;
        }

        bool HasManagedResources()
            => _meshes != null
               && _materials != null
               && _renderMeshArrays != null
               && _skinMeshInfos != null;

        bool HasPrototypeArrays()
            => _prototypes != null
               && _prototypes.Length > 0
               && _shadowOnlyPrototypes != null
               && _shadowOnlyPrototypes.Length == _prototypes.Length;

        bool HasValidPrototypeEntities(EntityManager entityManager)
        {
            if (!HasPrototypeArrays())
                return false;

            for (int i = 0; i < _prototypes.Length; i++)
            {
                if (!IsCompleteRenderPrototype(entityManager, _prototypes[i]))
                    return false;
                if (!IsCompleteRenderPrototype(entityManager, _shadowOnlyPrototypes[i]))
                    return false;
            }

            return true;
        }

        static bool IsCompleteRenderPrototype(EntityManager entityManager, Entity prototype)
        {
            if (prototype == Entity.Null || !entityManager.Exists(prototype))
                return false;

            return entityManager.HasComponent<LocalTransform>(prototype)
                   && entityManager.HasComponent<LocalToWorld>(prototype)
                   && entityManager.HasComponent<MaterialMeshInfo>(prototype)
                   && entityManager.HasComponent<RenderBounds>(prototype)
                   && entityManager.HasComponent<WorldRenderBounds>(prototype)
                   && entityManager.HasComponent<PerInstanceCullingTag>(prototype)
                   && entityManager.HasComponent(prototype, ComponentType.ReadWrite<RenderFilterSettings>())
                   && entityManager.HasComponent(prototype, ComponentType.ReadWrite<RenderMeshArray>())
                   && entityManager.HasComponent(prototype, ComponentType.ChunkComponent<ChunkWorldRenderBounds>())
                   && entityManager.HasComponent(prototype, ComponentType.ChunkComponent<EntitiesGraphicsChunkInfo>());
        }

        public bool TryGetPrototype(int bucketIndex, out Entity prototype)
        {
            if (_prototypes != null && (uint)bucketIndex < (uint)_prototypes.Length)
            {
                prototype = _prototypes[bucketIndex];
                return prototype != Entity.Null;
            }

            prototype = Entity.Null;
            return false;
        }

        public bool TryGetShadowOnlyPrototype(int bucketIndex, out Entity prototype)
        {
            if (_shadowOnlyPrototypes != null && (uint)bucketIndex < (uint)_shadowOnlyPrototypes.Length)
            {
                prototype = _shadowOnlyPrototypes[bucketIndex];
                return prototype != Entity.Null;
            }

            prototype = Entity.Null;
            return false;
        }

        public bool TryGetSkinMeshInfo(int skinMeshIndex, out ActorSkinMeshRenderInfo info)
        {
            if (_skinMeshInfos != null && (uint)skinMeshIndex < (uint)_skinMeshInfos.Length)
            {
                info = _skinMeshInfos[skinMeshIndex];
                return info.MeshIndex >= 0;
            }

            info = default;
            return false;
        }

        public void Dispose()
        {
            DisposeManagedResources();
            _prototypes = null;
            _shadowOnlyPrototypes = null;
            _catalogSignature = 0UL;
        }

        public void DisposeEntityResources(EntityManager entityManager)
        {
            if (_prototypes == null && _shadowOnlyPrototypes == null)
                return;

            DestroyPrototypes(entityManager, _prototypes);
            DestroyPrototypes(entityManager, _shadowOnlyPrototypes);

            _prototypes = null;
            _shadowOnlyPrototypes = null;
        }

        static void DestroyPrototypes(EntityManager entityManager, Entity[] prototypes)
        {
            if (prototypes == null)
                return;

            for (int i = 0; i < prototypes.Length; i++)
            {
                Entity prototype = prototypes[i];
                if (prototype != Entity.Null && entityManager.Exists(prototype))
                    entityManager.DestroyEntity(prototype);
            }
        }

        void BuildMeshes(ref ActorAnimationCatalogBlob catalog)
        {
            _meshes = new Mesh[catalog.SkinMeshes.Length * 2];
            Mesh placeholder = null;
            for (int i = 0; i < catalog.SkinMeshes.Length; i++)
            {
                int meshIndex = i * 2;
                var skinMesh = catalog.SkinMeshes[i];
                if (skinMesh.VertexCount <= 0 || skinMesh.IndexCount <= 0)
                {
                    placeholder ??= CreatePlaceholderMesh();
                    _meshes[meshIndex] = placeholder;
                    _meshes[meshIndex + 1] = placeholder;
                    continue;
                }

                var vertices = new Vector3[skinMesh.VertexCount];
                var normals = new Vector3[skinMesh.VertexCount];
                var uvs = new Vector2[skinMesh.VertexCount];
                for (int vertex = 0; vertex < skinMesh.VertexCount; vertex++)
                {
                    var source = catalog.SkinVertices[skinMesh.FirstVertexIndex + vertex];
                    vertices[vertex] = new Vector3(source.Position.x, source.Position.y, source.Position.z);
                    normals[vertex] = new Vector3(source.Normal.x, source.Normal.y, source.Normal.z);
                    uvs[vertex] = new Vector2(source.Uv.x, source.Uv.y);
                }

                var indices = new int[skinMesh.IndexCount];
                for (int index = 0; index < skinMesh.IndexCount; index++)
                    indices[index] = catalog.SkinIndices[skinMesh.FirstIndexIndex + index];

                var bounds = new Bounds(
                    new Vector3(skinMesh.BoundsCenter.x, skinMesh.BoundsCenter.y, skinMesh.BoundsCenter.z),
                    ToVector3(math.max(skinMesh.BoundsExtents, new float3(0.05f)) * 2f));
                _meshes[meshIndex] = CreateActorMesh(
                    $"VV:ActorSkinMesh[{i}]",
                    vertices,
                    normals,
                    uvs,
                    indices,
                    bounds,
                    skinMesh.VertexCount);
                _meshes[meshIndex + 1] = CreateActorMesh(
                    $"VV:ActorSkinMesh[{i}].MirrorWinding",
                    vertices,
                    normals,
                    uvs,
                    BuildMirroredWindingIndices(indices),
                    bounds,
                    skinMesh.VertexCount);
            }
        }

        static Mesh CreateActorMesh(
            string name,
            Vector3[] vertices,
            Vector3[] normals,
            Vector2[] uvs,
            int[] indices,
            Bounds bounds,
            int vertexCount)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = indices,
                bounds = bounds,
            };
            mesh.UploadMeshData(true);
            return mesh;
        }

        static int[] BuildMirroredWindingIndices(int[] indices)
        {
            var result = new int[indices?.Length ?? 0];
            for (int i = 0; i + 2 < result.Length; i += 3)
            {
                result[i] = indices[i];
                result[i + 1] = indices[i + 2];
                result[i + 2] = indices[i + 1];
            }

            return result;
        }

        static Mesh CreatePlaceholderMesh()
        {
            var mesh = new Mesh
            {
                name = "VV:ActorSkinMeshPlaceholder",
                vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.zero, Vector2.zero },
                triangles = new[] { 0, 1, 2 },
                bounds = new Bounds(Vector3.zero, Vector3.one * 0.01f),
            };
            mesh.UploadMeshData(true);
            return mesh;
        }

        void BuildSkinMeshInfos(ref ActorAnimationCatalogBlob catalog)
        {
            _skinMeshInfos = new ActorSkinMeshRenderInfo[catalog.SkinMeshes.Length];
            for (int i = 0; i < _skinMeshInfos.Length; i++)
            {
                var skinMesh = catalog.SkinMeshes[i];
                ResolveTexture(skinMesh.TextureIndex, out int bucketIndex, out int textureSlice);
                _skinMeshInfos[i] = new ActorSkinMeshRenderInfo
                {
                    BucketIndex = bucketIndex,
                    MaterialIndex = math.clamp(skinMesh.MaterialIndex, 0, math.max(0, WorldResources.BlendVariantCount - 1)),
                    MeshIndex = skinMesh.VertexCount > 0 && skinMesh.IndexCount > 0 ? i * 2 : -1,
                    MirroredMeshIndex = skinMesh.VertexCount > 0 && skinMesh.IndexCount > 0 ? i * 2 + 1 : -1,
                    TextureSlice = textureSlice,
                    VertexCount = math.max(0, skinMesh.VertexCount),
                };
            }
        }

        void BuildMaterials(Shader shader, int bucketCount, int variantCount)
        {
            _materials = new Material[bucketCount][];
            Material[] cacheMaterials = WorldResources.Cache?.Materials;
            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                _materials[bucket] = new Material[variantCount];
                for (int variant = 0; variant < variantCount; variant++)
                {
                    var material = new Material(shader)
                    {
                        name = $"VV:ActorEntitiesGraphics[b{bucket}:m{variant}]",
                        enableInstancing = true,
                        doubleSidedGI = true,
                    };

                    material.SetTexture(BaseArrayId, WorldResources.RefBaseArrays[bucket]);
                    CopyAlphaSettings(cacheMaterials, bucket, variant, variantCount, material);
                    _materials[bucket][variant] = material;
                }
            }
        }

        void BuildRenderMeshArrays(int bucketCount)
        {
            _renderMeshArrays = new RenderMeshArray[bucketCount];
            for (int bucket = 0; bucket < bucketCount; bucket++)
                _renderMeshArrays[bucket] = new RenderMeshArray(_materials[bucket], _meshes);
        }

        void BuildPrototypes(EntityManager entityManager, int bucketCount)
        {
            _prototypes = new Entity[bucketCount];
            _shadowOnlyPrototypes = new Entity[bucketCount];
            int prototypeMeshIndex = ResolvePrototypeMeshIndex();
            var renderDesc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.On,
                receiveShadows: true,
                staticShadowCaster: false);
            var shadowOnlyDesc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.ShadowsOnly,
                receiveShadows: true,
                staticShadowCaster: false);

            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                _prototypes[bucket] = CreatePrototype(
                    entityManager,
                    $"VVardenfell.ActorRenderPrefab[b{bucket}]",
                    renderDesc,
                    bucket,
                    prototypeMeshIndex);
                _shadowOnlyPrototypes[bucket] = CreatePrototype(
                    entityManager,
                    $"VVardenfell.ActorShadowOnlyRenderPrefab[b{bucket}]",
                    shadowOnlyDesc,
                    bucket,
                    prototypeMeshIndex);
            }
        }

        int ResolvePrototypeMeshIndex()
        {
            if (_meshes == null || _meshes.Length == 0)
                throw new InvalidOperationException("Actor Entities Graphics render resources require at least one generated actor mesh.");

            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                    return i;
            }

            throw new InvalidOperationException("Actor Entities Graphics render resources could not find a valid generated actor mesh.");
        }

        Entity CreatePrototype(
            EntityManager entityManager,
            string name,
            in RenderMeshDescription desc,
            int bucket,
            int prototypeMeshIndex)
        {
            Entity prototype = entityManager.CreateEntity();
            RenderMeshUtility.AddComponents(
                prototype,
                entityManager,
                desc,
                _renderMeshArrays[bucket],
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, prototypeMeshIndex));
            if (entityManager.HasComponent<LocalTransform>(prototype))
                entityManager.SetComponentData(prototype, LocalTransform.Identity);
            else
                entityManager.AddComponentData(prototype, LocalTransform.Identity);

            if (entityManager.HasComponent<LocalToWorld>(prototype))
                entityManager.SetComponentData(prototype, new LocalToWorld { Value = float4x4.identity });
            else
                entityManager.AddComponentData(prototype, new LocalToWorld { Value = float4x4.identity });
            entityManager.AddComponentData(prototype, default(TextureSlice));
            entityManager.AddComponentData(prototype, default(ActorDeformedMeshIndex));
            entityManager.AddComponentData(prototype, default(ActorRenderMeshInstance));
            entityManager.AddComponent<Prefab>(prototype);
            if (!IsCompleteRenderPrototype(entityManager, prototype))
                throw new InvalidOperationException($"Actor Entities Graphics prototype '{name}' was created without the required render component composition.");

            return prototype;
        }

        void DisposeManagedResources()
        {
            if (_materials != null)
            {
                for (int bucket = 0; bucket < _materials.Length; bucket++)
                {
                    Material[] bucketMaterials = _materials[bucket];
                    if (bucketMaterials == null)
                        continue;

                    for (int i = 0; i < bucketMaterials.Length; i++)
                        if (bucketMaterials[i] != null)
                            UnityEngine.Object.Destroy(bucketMaterials[i]);
                }
            }

            if (_meshes != null)
            {
                var disposedMeshes = new HashSet<Mesh>();
                for (int i = 0; i < _meshes.Length; i++)
                {
                    Mesh mesh = _meshes[i];
                    if (mesh != null && disposedMeshes.Add(mesh))
                        UnityEngine.Object.Destroy(mesh);
                }
            }

            _materials = null;
            _meshes = null;
            _renderMeshArrays = null;
            _skinMeshInfos = null;
        }

        static void ResolveTexture(int textureIndex, out int bucketIndex, out int textureSlice)
        {
            if (textureIndex >= 0
                && WorldResources.TexBucketInfo.IsCreated
                && (uint)textureIndex < (uint)WorldResources.TexBucketInfo.Length)
            {
                int2 bucketSlice = WorldResources.TexBucketInfo[textureIndex];
                bucketIndex = math.max(0, bucketSlice.x);
                textureSlice = math.max(0, bucketSlice.y);
                return;
            }

            bucketIndex = math.max(0, WorldResources.FallbackBucketSlice.x);
            textureSlice = math.max(0, WorldResources.FallbackBucketSlice.y);
        }

        static void CopyAlphaSettings(Material[] sourceMaterials, int bucket, int variant, int variantCount, Material material)
        {
            int sourceIndex = bucket * variantCount + variant;
            Material source = sourceMaterials != null && (uint)sourceIndex < (uint)sourceMaterials.Length
                ? sourceMaterials[sourceIndex]
                : null;

            material.SetFloat(SrcBlendId, source != null && source.HasProperty(SrcBlendId) ? source.GetFloat(SrcBlendId) : (float)BlendMode.One);
            material.SetFloat(DstBlendId, source != null && source.HasProperty(DstBlendId) ? source.GetFloat(DstBlendId) : (float)BlendMode.Zero);
            material.SetFloat(ZWriteId, source != null && source.HasProperty(ZWriteId) ? source.GetFloat(ZWriteId) : 1f);
            material.SetFloat(CutoffId, source != null && source.HasProperty(CutoffId) ? source.GetFloat(CutoffId) : 0.5f);
            material.renderQueue = source != null ? source.renderQueue : (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", source != null ? source.GetTag("RenderType", false, "Opaque") : "Opaque");

            if (source != null && source.IsKeywordEnabled("_ALPHATEST_ON"))
                material.EnableKeyword("_ALPHATEST_ON");
            if (source != null && source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        static ulong BuildSignature(ref ActorAnimationCatalogBlob catalog)
        {
            ulong hash = 1469598103934665603UL;
            Add(ref hash, (uint)catalog.SkinMeshes.Length);
            Add(ref hash, (uint)catalog.SkinVertices.Length);
            Add(ref hash, (uint)catalog.SkinIndices.Length);
            Add(ref hash, (uint)(WorldResources.RefBaseArrays?.Length ?? 0));
            Add(ref hash, (uint)WorldResources.BlendVariantCount);
            return hash;
        }

        static void Add(ref ulong hash, uint value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        static Vector3 ToVector3(float3 value) => new(value.x, value.y, value.z);
    }
}
