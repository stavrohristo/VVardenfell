using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using Object = UnityEngine.Object;

namespace VVardenfell.Importer.Bake
{
    internal static class RuntimeSpawnPrefabBakery
    {
        public static void Write(string path, ModelPrefabCatalogData catalog, TextureBakery textures, MaterialBakery materials, CollisionBakery collisions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var records = catalog?.Records ?? Array.Empty<ModelPrefabDef>();

            using var prefabWorld = new World("VV.RuntimeSpawnPrefabBake");
            var em = prefabWorld.EntityManager;
            using var renderBake = new RendererBakeResources(MaxRenderMeshIndex(records), materials.Count);

            Entity header = em.CreateEntity();
            em.AddComponentData(header, new RuntimeSpawnPrefabCacheHeader
            {
                FormatVersion = CacheFormat.FormatVersion,
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                ModelPrefabCount = records.Length,
            });

            for (int i = 0; i < records.Length; i++)
                BuildPrefab(em, records[i], i, textures, renderBake, collisions);

            using (var writer = new MemoryBinaryWriter(em))
            {
                SerializeUtility.SerializeWorld(em, writer, out var referencedObjects);
                if (referencedObjects.Length != 0)
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' serialized {referencedObjects.Length} Unity render object references; direct runtime render ID prefabs must serialize none.");
                var bytes = new byte[writer.Length];
                unsafe
                {
                    Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
                }
                RuntimeRenderObjectReferenceFile.WriteWrappedEntityWorld(path, bytes, referencedObjects, renderBake.ObjectReferences);
            }

            Validate(path, records.Length);
        }

        public static bool IsCurrent(string path, int expectedModelPrefabCount)
        {
            if (!File.Exists(path))
                return false;
            try
            {
                Validate(path, expectedModelPrefabCount);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void BuildPrefab(
            EntityManager em,
            ModelPrefabDef def,
            int modelPrefabIndex,
            TextureBakery textures,
            RendererBakeResources renderBake,
            CollisionBakery collisions)
        {
            var nodes = def?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            if (nodes.Length == 0)
                return;

            var entities = new Entity[nodes.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                var node = nodes[i] ?? throw new InvalidDataException($"Model prefab {modelPrefabIndex} has a null node {i}.");
                Entity entity = em.CreateEntity();
                entities[i] = entity;
                em.AddComponent<Prefab>(entity);
                em.AddComponentData(entity, new RuntimeSpawnPrefabNode
                {
                    ModelPrefabIndex = modelPrefabIndex,
                    NodeIndex = i,
                    ParentIndex = node.ParentIndex,
                    Kind = (byte)node.Kind,
                });
                em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                    new float3(node.PosX, node.PosY, node.PosZ),
                    new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                    node.Scale));
                em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                AddStableNodeRuntimeMetadata(em, entity, def, node, modelPrefabIndex, i, root: Entity.Null);
            }

            int rootIndex = math.clamp(def.RootNodeIndex, 0, entities.Length - 1);
            Entity root = entities[rootIndex];
            em.AddComponentData(root, new RuntimeSpawnPrefabRoot
            {
                ModelPrefabIndex = modelPrefabIndex,
                CollisionIndex = def.CollisionIndex,
            });
            em.AddComponentData(root, new ModelPrefabRoot { ModelPrefabIndex = modelPrefabIndex });
            if (def.CollisionIndex >= 0)
                ValidateGlobalColliderIndex(collisions, def.CollisionIndex, $"Model prefab {modelPrefabIndex} root");

            for (int i = 0; i < entities.Length; i++)
                PatchObjectAnimationRoot(em, entities[i], root);

            var linked = em.AddBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < entities.Length; i++)
                linked.Add(new LinkedEntityGroup { Value = entities[i] });

            for (int i = 0; i < entities.Length; i++)
            {
                var node = nodes[i];
                Entity entity = entities[i];

                if (i != rootIndex && node.ParentIndex >= 0 && node.ParentIndex < entities.Length)
                    em.AddComponentData(entity, new Parent { Value = entities[node.ParentIndex] });

                if (node.Kind == ModelPrefabNodeKind.RenderLeaf && node.GlobalMeshIndex >= 0)
                {
                    int textureSlice = ResolveTextureSlice(textures, node.TextureIndex);
                    em.AddComponentData(entity, new RuntimeSpawnPrefabRenderResource
                    {
                        ModelPrefabIndex = modelPrefabIndex,
                        NodeIndex = i,
                        MeshIndex = node.GlobalMeshIndex,
                        MaterialIndex = node.MaterialIndex,
                        TextureIndex = node.TextureIndex,
                        BoundsCenter = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                        BoundsExtents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                    });
                    em.AddComponentData(entity, new ModelPrefabRenderLeaf
                    {
                        NodeIndex = i,
                        MeshIndex = node.GlobalMeshIndex,
                        MaterialIndex = node.MaterialIndex,
                        TextureIndex = node.TextureIndex,
                    });
                    RenderMeshUtility.AddComponents(
                        entity,
                        em,
                        renderBake.RenderDesc,
                        renderBake.RefRenderMeshArray,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(node.MaterialIndex, node.GlobalMeshIndex));
                    em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
                    em.SetComponentData(entity, new RenderBounds
                    {
                        Value = new AABB
                        {
                            Center = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                            Extents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                        }
                    });
                    em.AddComponentData(entity, new TextureSlice { Value = textureSlice });
                    StripBakeOnlyRenderMeshArray(em, entity);

                    if (node.PickColliderIndex >= 0)
                    {
                        if (entity == root && def.CollisionIndex >= 0)
                            throw new InvalidDataException($"Model prefab {modelPrefabIndex} node {i} cannot bake both root and pick collider sources on one entity.");
                        ValidateGlobalColliderIndex(collisions, node.PickColliderIndex, $"Model prefab {modelPrefabIndex} node {i}");
                        em.AddComponentData(entity, new RuntimeSpawnPrefabPickCollider
                        {
                            ColliderIndex = node.PickColliderIndex,
                        });
                        em.AddComponent<InteractionPickSurfaceTag>(entity);
                    }
                }
            }
        }

        static void ValidateGlobalColliderIndex(CollisionBakery collisions, int index, string context)
        {
            if (collisions == null || (uint)index >= (uint)collisions.Count)
                throw new InvalidDataException($"{context} references missing global collider index {index}.");
        }

        static void AddStableNodeRuntimeMetadata(
            EntityManager em,
            Entity entity,
            ModelPrefabDef def,
            ModelPrefabNodeDef node,
            int modelPrefabIndex,
            int nodeIndex,
            Entity root)
        {
            em.AddComponent<ModelPrefabNodeTag>(entity);

            if (def.ObjectAnimation?.IsEnabled == true)
            {
                em.AddComponentData(entity, new ObjectAnimationNode
                {
                    Root = root,
                    ModelPrefabIndex = modelPrefabIndex,
                    NodeIndex = nodeIndex,
                    ParentIndex = node.ParentIndex,
                    BindPosition = new float3(node.PosX, node.PosY, node.PosZ),
                    BindRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                    BindScale = node.Scale,
                });
            }

            if (node.Kind == ModelPrefabNodeKind.Billboard)
            {
                em.AddComponent<ModelBillboardTag>(entity);
                em.AddComponentData(entity, new ModelBillboardState
                {
                    BaseLocalRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                });
            }
        }

        static void PatchObjectAnimationRoot(EntityManager em, Entity entity, Entity root)
        {
            if (!em.HasComponent<ObjectAnimationNode>(entity))
                return;
            var node = em.GetComponentData<ObjectAnimationNode>(entity);
            node.Root = root;
            em.SetComponentData(entity, node);
        }

        static int ResolveTextureSlice(TextureBakery textures, int textureIndex)
        {
            int bucketKey = textures.GetBucketKey(textureIndex);
            return textures.GetBucketSliceOrFallback(textureIndex, bucketKey);
        }

        static int MaxRenderMeshIndex(ModelPrefabDef[] records)
        {
            int max = 0;
            for (int i = 0; i < records.Length; i++)
            {
                var nodes = records[i]?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
                for (int n = 0; n < nodes.Length; n++)
                {
                    var node = nodes[n];
                    if (node != null && node.Kind == ModelPrefabNodeKind.RenderLeaf)
                        max = math.max(max, node.GlobalMeshIndex);
                }
            }
            return max;
        }

        static void StripBakeOnlyRenderMeshArray(EntityManager em, Entity entity)
        {
            if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                em.RemoveComponent(entity, ComponentType.ReadWrite<RenderMeshArray>());
        }

        static void Validate(string path, int expectedModelPrefabCount)
        {
            using var world = new World("VV.RuntimeSpawnPrefabValidate");
            RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorld(path, out var renderReferences, out var bytes);
            if (renderReferences == null || renderReferences.Length != 0)
                throw new InvalidDataException($"runtime spawn prefab cache '{path}' must not contain serialized Unity render object references.");
            var unityObjects = CreateValidationObjects(renderReferences);
            try
            {
                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                        var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                        SerializeUtility.DeserializeWorld(tx, reader, unityObjects);
                        world.EntityManager.EndExclusiveEntityTransaction();
                    }
                }
            }
            finally
            {
                DestroyValidationObjects(unityObjects);
            }

            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeSpawnPrefabCacheHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException($"runtime spawn prefab cache '{path}' did not deserialize exactly one header.");

            var header = query.GetSingleton<RuntimeSpawnPrefabCacheHeader>();
            if (header.FormatVersion != CacheFormat.FormatVersion
                || header.PipelineVersion != CacheFormat.WorldBakePipelineVersion
                || header.ModelPrefabCount != expectedModelPrefabCount)
            {
                throw new InvalidDataException($"runtime spawn prefab cache '{path}' failed version/count validation.");
            }

            ValidateRenderLeaves(world.EntityManager, path);
            ValidatePrefabNodes(world.EntityManager, path);
            ValidateColliders(world.EntityManager, path);
        }

        static void ValidatePrefabNodes(EntityManager em, string path)
        {
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabNode>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!em.HasComponent<ModelPrefabNodeTag>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' node is missing ModelPrefabNodeTag.");
                if (em.HasComponent<ObjectAnimationNode>(entity)
                    && em.GetComponentData<ObjectAnimationNode>(entity).Root == Entity.Null)
                {
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' object-animation node has no root.");
                }
                if (em.HasComponent<ModelBillboardTag>(entity) && !em.HasComponent<ModelBillboardState>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' billboard node is missing ModelBillboardState.");
            }
        }

        static void ValidateColliders(EntityManager em, string path)
        {
            using (var roots = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRoot>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            }))
            {
                using var entities = roots.ToEntityArray(Unity.Collections.Allocator.Temp);
                using var rootData = roots.ToComponentDataArray<RuntimeSpawnPrefabRoot>(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!em.HasComponent<ModelPrefabRoot>(entity))
                        throw new InvalidDataException($"runtime spawn prefab cache '{path}' root is missing ModelPrefabRoot.");
                    if (rootData[i].CollisionIndex >= 0)
                    {
                        if (em.HasComponent<RuntimeColliderSource>(entity))
                            throw new InvalidDataException($"runtime spawn prefab cache '{path}' root serialized reusable RuntimeColliderSource; rebake required.");
                    }
                }
            }

            using var picks = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabPickCollider>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var pickEntities = picks.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < pickEntities.Length; i++)
            {
                Entity entity = pickEntities[i];
                if (!em.HasComponent<InteractionPickSurfaceTag>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' pick collider is missing InteractionPickSurfaceTag.");
                if (em.HasComponent<RuntimeColliderSource>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' pick collider serialized reusable RuntimeColliderSource; rebake required.");
            }
        }

        static void ValidateRenderLeaves(EntityManager em, string path)
        {
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRenderResource>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var resources = query.ToComponentDataArray<RuntimeSpawnPrefabRenderResource>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var resource = resources[i];
                if (resource.MeshIndex < 0 || resource.MaterialIndex < 0)
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf has invalid logical render resource.");
                if (!em.HasComponent<MaterialMeshInfo>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf is missing MaterialMeshInfo.");
                if (!em.HasComponent<RenderBounds>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf is missing RenderBounds.");
                if (!em.HasComponent<TextureSlice>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf is missing TextureSlice.");
                if (!em.HasComponent<ModelPrefabRenderLeaf>(entity))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf is missing ModelPrefabRenderLeaf.");
                if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf serialized a RenderMeshArray shared component.");
                var info = em.GetComponentData<MaterialMeshInfo>(entity);
                if (info.Mesh >= 0 || info.Material >= 0)
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf has runtime MaterialMeshInfo ids.");
                int meshIndex = MaterialMeshInfo.StaticIndexToArrayIndex(info.Mesh);
                int materialIndex = MaterialMeshInfo.StaticIndexToArrayIndex(info.Material);
                if (meshIndex < 0 || materialIndex < 0)
                    throw new InvalidDataException($"runtime spawn prefab cache '{path}' render leaf has invalid logical MaterialMeshInfo placeholders.");
            }
        }

        struct RendererBakeResources : IDisposable
        {
            public readonly RenderMeshDescription RenderDesc;
            public readonly RenderMeshArray RefRenderMeshArray;
            public readonly System.Collections.Generic.Dictionary<Object, RuntimeRenderObjectReference> ObjectReferences;
            readonly Mesh _mesh;
            readonly Material[] _materials;

            public RendererBakeResources(int maxMeshIndex, int refMaterialCount)
            {
                RenderDesc = new RenderMeshDescription(
                    shadowCastingMode: ShadowCastingMode.On,
                    receiveShadows: true,
                    staticShadowCaster: true);
                ObjectReferences = new System.Collections.Generic.Dictionary<Object, RuntimeRenderObjectReference>(
                    RuntimeRenderObjectReferenceFile.UnityObjectReferenceComparer.Instance);
                int refMaterialCountSafe = math.max(1, refMaterialCount);

                _mesh = new Mesh { name = "VV:BakePlaceholderMesh" };
                _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                _mesh.triangles = new[] { 0, 1, 2 };
                _mesh.bounds = new Bounds(Vector3.zero, Vector3.one);

                _materials = new Material[refMaterialCountSafe];
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? throw new InvalidDataException("[VVardenfell][RendererBake] URP/Lit shader is required for bake-only renderer authoring.");
                for (int i = 0; i < _materials.Length; i++)
                {
                    _materials[i] = new Material(shader)
                    {
                        name = $"VV:BakePlaceholderMaterial{i}",
                        enableInstancing = true,
                    };
                }

                var meshes = new Mesh[math.max(1, maxMeshIndex + 1)];
                for (int i = 0; i < meshes.Length; i++)
                    meshes[i] = _mesh;
                RefRenderMeshArray = new RenderMeshArray(_materials, meshes);
            }

            public void Dispose()
            {
                for (int i = 0; i < _materials.Length; i++)
                    DestroyBakeObject(_materials[i]);
                DestroyBakeObject(_mesh);
            }
        }

        static Object[] CreateValidationObjects(RuntimeRenderObjectReference[] references)
        {
            var objects = new Object[references?.Length ?? 0];
            if (objects.Length == 0)
                return objects;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidDataException("[VVardenfell][RendererBake] URP/Lit shader is required for validation.");
            for (int i = 0; i < objects.Length; i++)
            {
                var reference = references[i];
                switch (reference.Kind)
                {
                    case RuntimeRenderObjectReferenceKind.Mesh:
                        var mesh = new Mesh { name = $"VV:ValidateMesh[{reference.Index}]" };
                        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                        mesh.triangles = new[] { 0, 1, 2 };
                        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                        objects[i] = mesh;
                        break;
                    case RuntimeRenderObjectReferenceKind.RefMaterial:
                    case RuntimeRenderObjectReferenceKind.CombinedMaterial:
                    case RuntimeRenderObjectReferenceKind.TerrainMaterial:
                        objects[i] = new Material(shader)
                        {
                            name = $"VV:ValidateMaterial[{reference.Kind}:{reference.Index}]",
                            enableInstancing = true,
                        };
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported render object reference kind {(int)reference.Kind}.");
                }
            }
            return objects;
        }

        static void DestroyValidationObjects(Object[] objects)
        {
            if (objects == null)
                return;
            for (int i = 0; i < objects.Length; i++)
                DestroyBakeObject(objects[i]);
        }

        static void DestroyBakeObject(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
