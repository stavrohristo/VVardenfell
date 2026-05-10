using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    internal static class RuntimeSpawnPrefabMaterializer
    {
        public static void LoadAndMaterialize(EntityManager em, CacheLoader cache)
        {
            if (cache?.ModelPrefabCatalog?.Records == null)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] model_prefabs.bin is not loaded.");
            if (!File.Exists(CachePaths.RuntimeSpawnPrefabs))
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] runtime_spawn_prefabs.entities is missing; rebake required.");

            using (var prefabWorld = new World("VV.RuntimeSpawnPrefabLoad"))
            {
                byte[] bytes = File.ReadAllBytes(CachePaths.RuntimeSpawnPrefabs);
                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                        var tx = prefabWorld.EntityManager.BeginExclusiveEntityTransaction();
                        SerializeUtility.DeserializeWorld(tx, reader);
                        prefabWorld.EntityManager.EndExclusiveEntityTransaction();
                    }
                }

                em.MoveEntitiesFrom(prefabWorld.EntityManager);
            }

            ValidateHeader(em, cache.ModelPrefabCatalog.Records.Length);
            BuildRegistry(em, cache);
            PatchRuntimeComponents(em, cache);
            PatchRenderResources(em, cache);
            PatchPickColliders(em);
        }

        static void ValidateHeader(EntityManager em, int expectedModelPrefabCount)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeSpawnPrefabCacheHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] cache must contain exactly one RuntimeSpawnPrefabCacheHeader; rebake required.");

            var header = query.GetSingleton<RuntimeSpawnPrefabCacheHeader>();
            if (header.FormatVersion != CacheFormat.FormatVersion)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache format mismatch (found {header.FormatVersion}, expected {CacheFormat.FormatVersion}); rebake required.");
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache pipeline mismatch (found {header.PipelineVersion}, expected {CacheFormat.WorldBakePipelineVersion}); rebake required.");
            if (header.ModelPrefabCount != expectedModelPrefabCount)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache model count mismatch (found {header.ModelPrefabCount}, expected {expectedModelPrefabCount}); rebake required.");
        }

        static void PatchRenderResources(EntityManager em, CacheLoader cache)
        {
            var renderArrayCache = new Dictionary<long, RenderMeshArray>();
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRenderResource>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var resources = query.ToComponentDataArray<RuntimeSpawnPrefabRenderResource>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var resource = resources[i];
                if ((uint)resource.MeshIndex >= (uint)(cache.Meshes?.Length ?? 0))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {resource.ModelPrefabIndex} node {resource.NodeIndex} references missing mesh {resource.MeshIndex}; rebake required.");

                int bucketIndex = resource.TextureIndex >= 0
                                  && WorldResources.TexBucketInfo.IsCreated
                                  && resource.TextureIndex < WorldResources.TexBucketInfo.Length
                    ? WorldResources.TexBucketInfo[resource.TextureIndex].x
                    : WorldResources.FallbackBucketSlice.x;
                int textureSlice = resource.TextureIndex >= 0
                                   && WorldResources.TexBucketInfo.IsCreated
                                   && resource.TextureIndex < WorldResources.TexBucketInfo.Length
                    ? WorldResources.TexBucketInfo[resource.TextureIndex].y
                    : WorldResources.FallbackBucketSlice.y;

                var rma = GetOrCreateRenderMeshArray(cache, renderArrayCache, bucketIndex, resource.MeshIndex);
                if (!em.HasComponent<ModelPrefabRenderLeaf>(entity))
                {
                    em.AddComponentData(entity, new ModelPrefabRenderLeaf
                    {
                        NodeIndex = resource.NodeIndex,
                        MeshIndex = resource.MeshIndex,
                        MaterialIndex = resource.MaterialIndex,
                        TextureIndex = resource.TextureIndex,
                    });
                }
                RenderMeshUtility.AddComponents(
                    entity,
                    em,
                    WorldResources.Desc,
                    rma,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(math.max(0, resource.MaterialIndex), 0));
                em.AddComponentData(entity, new TextureSlice { Value = textureSlice });
                em.AddComponentData(entity, new RenderBounds
                {
                    Value = new AABB
                    {
                        Center = resource.BoundsCenter,
                        Extents = resource.BoundsExtents,
                    }
                });
            }
        }

        static void PatchRuntimeComponents(EntityManager em, CacheLoader cache)
        {
            var records = cache.ModelPrefabCatalog.Records;
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabNode>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var nodes = query.ToComponentDataArray<RuntimeSpawnPrefabNode>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var nodeRef = nodes[i];
                if ((uint)nodeRef.ModelPrefabIndex >= (uint)records.Length)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] node references model prefab {nodeRef.ModelPrefabIndex}, outside table length {records.Length}; rebake required.");
                var def = records[nodeRef.ModelPrefabIndex];
                var sourceNodes = def?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
                if ((uint)nodeRef.NodeIndex >= (uint)sourceNodes.Length)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} is outside source node table; rebake required.");

                var sourceNode = sourceNodes[nodeRef.NodeIndex];
                if (!em.HasComponent<ModelPrefabNodeTag>(entity))
                    em.AddComponent<ModelPrefabNodeTag>(entity);

                if (def.ObjectAnimation?.IsEnabled == true)
                {
                    Entity root = WorldResources.ModelPrefabs[nodeRef.ModelPrefabIndex];
                    if (root == Entity.Null)
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} has no root for object animation; rebake required.");
                    em.AddComponentData(entity, new ObjectAnimationNode
                    {
                        Root = root,
                        ModelPrefabIndex = nodeRef.ModelPrefabIndex,
                        NodeIndex = nodeRef.NodeIndex,
                        ParentIndex = sourceNode.ParentIndex,
                        BindPosition = new float3(sourceNode.PosX, sourceNode.PosY, sourceNode.PosZ),
                        BindRotation = new quaternion(sourceNode.RotX, sourceNode.RotY, sourceNode.RotZ, sourceNode.RotW),
                        BindScale = sourceNode.Scale,
                    });
                }

                if ((ModelPrefabNodeKind)nodeRef.Kind == ModelPrefabNodeKind.Billboard)
                {
                    em.AddComponent<ModelBillboardTag>(entity);
                    em.AddComponentData(entity, new ModelBillboardState
                    {
                        BaseLocalRotation = new quaternion(sourceNode.RotX, sourceNode.RotY, sourceNode.RotZ, sourceNode.RotW),
                    });
                }
            }
        }

        static RenderMeshArray GetOrCreateRenderMeshArray(
            CacheLoader cache,
            Dictionary<long, RenderMeshArray> renderArrayCache,
            int bucketIndex,
            int meshIndex)
        {
            long key = ((long)bucketIndex << 32) ^ (uint)meshIndex;
            if (renderArrayCache.TryGetValue(key, out var existing))
                return existing;

            if (WorldResources.BlendVariantCount <= 0)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] ref texture bucket material variants are not loaded.");
            int materialStart = bucketIndex * WorldResources.BlendVariantCount;
            if (materialStart < 0 || materialStart + WorldResources.BlendVariantCount > (cache.Materials?.Length ?? 0))
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] texture bucket {bucketIndex} is outside material table; rebake required.");

            var materials = new Material[WorldResources.BlendVariantCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = cache.Materials[materialStart + i];

            var rma = new RenderMeshArray(materials, new[] { cache.Meshes[meshIndex] });
            renderArrayCache[key] = rma;
            return rma;
        }

        static void PatchPickColliders(EntityManager em)
        {
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabPickCollider>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var colliders = query.ToComponentDataArray<RuntimeSpawnPrefabPickCollider>(Allocator.Temp);
            BlobAssetReference<Collider>[] blobs = WorldResources.ColliderBlobs;
            for (int i = 0; i < entities.Length; i++)
            {
                int colliderIndex = colliders[i].ColliderIndex;
                if (blobs == null || (uint)colliderIndex >= (uint)blobs.Length || !blobs[colliderIndex].IsCreated)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {colliderIndex} is missing; rebake required.");

                RuntimeColliderAttachmentUtility.AttachSource(
                    em,
                    entities[i],
                    blobs[colliderIndex],
                    RuntimeColliderKind.InteractionPick,
                    active: false);
                if (!em.HasComponent<InteractionPickSurfaceTag>(entities[i]))
                    em.AddComponent<InteractionPickSurfaceTag>(entities[i]);
            }
        }

        static void BuildRegistry(EntityManager em, CacheLoader cache)
        {
            var records = cache.ModelPrefabCatalog.Records;
            WorldResources.ModelPrefabs = new Entity[records.Length];

            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRoot>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            using var roots = query.ToEntityArray(Allocator.Temp);
            using var rootData = query.ToComponentDataArray<RuntimeSpawnPrefabRoot>(Allocator.Temp);
            var registryEntries = new RuntimeSpawnPrefabRegistryEntry[roots.Length];
            int registryEntryCount = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = rootData[i];
                if ((uint)root.ModelPrefabIndex >= (uint)records.Length)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] root references model prefab {root.ModelPrefabIndex}, outside table length {records.Length}; rebake required.");
                if (WorldResources.ModelPrefabs[root.ModelPrefabIndex] != Entity.Null)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] duplicate root for model prefab {root.ModelPrefabIndex}; rebake required.");

                WorldResources.ModelPrefabs[root.ModelPrefabIndex] = roots[i];
                if (!em.HasComponent<ModelPrefabRoot>(roots[i]))
                    em.AddComponentData(roots[i], new ModelPrefabRoot { ModelPrefabIndex = root.ModelPrefabIndex });
                registryEntries[registryEntryCount++] = new RuntimeSpawnPrefabRegistryEntry
                {
                    ModelPrefabIndex = root.ModelPrefabIndex,
                    CollisionIndex = root.CollisionIndex,
                    PrefabRoot = roots[i],
                };
            }

            for (int i = 0; i < records.Length; i++)
            {
                var def = records[i];
                if (def?.Nodes == null || def.Nodes.Length == 0)
                    continue;
                if (WorldResources.ModelPrefabs[i] == Entity.Null)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {i} has no serialized runtime root; rebake required.");
            }

            Entity registry = em.CreateEntity(
                ComponentType.ReadWrite<RuntimeSpawnPrefabRegistry>(),
                ComponentType.ReadWrite<RuntimeSpawnPrefabRegistryEntry>());
            var entries = em.GetBuffer<RuntimeSpawnPrefabRegistryEntry>(registry);
            entries.ResizeUninitialized(registryEntryCount);
            for (int i = 0; i < registryEntryCount; i++)
                entries[i] = registryEntries[i];

            WorldModelPrefabUtility.BuildRuntimeSpawnPrefabLookups(cache);
        }
    }
}
