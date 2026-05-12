using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    public partial class RuntimeSpawnPrefabLoadSystem : SystemBase
    {
        EntityQuery _headerQuery;
        EntityQuery _renderResourcesQuery;
        EntityQuery _nodesQuery;
        EntityQuery _pickCollidersQuery;
        EntityQuery _rootsQuery;

        protected override void OnCreate()
        {
            _headerQuery = GetEntityQuery(ComponentType.ReadOnly<RuntimeSpawnPrefabCacheHeader>());
            _renderResourcesQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRenderResource>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            _nodesQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabNode>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            _pickCollidersQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabPickCollider>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            _rootsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeSpawnPrefabRoot>(),
                    ComponentType.ReadOnly<Prefab>(),
                },
                Options = EntityQueryOptions.IncludePrefab,
            });
            Enabled = false;
        }

        protected override void OnUpdate()
        {
        }

        public void LoadAndMaterialize(RuntimeMaterializationResources resources)
        {
            if (resources?.ModelPrefabRecords == null)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] model_prefabs.bin is not loaded.");
            if (!File.Exists(CachePaths.RuntimeSpawnPrefabs))
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] runtime_spawn_prefabs.entities is missing; rebake required.");

            using (var prefabWorld = new World("VV.RuntimeSpawnPrefabLoad"))
            {
                RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorld(CachePaths.RuntimeSpawnPrefabs, out var renderReferences, out var bytes);
                ValidateEmptyRenderObjectTable(renderReferences);
                var unityObjects = resources.ResolveRenderObjectReferences(renderReferences, "[VVardenfell][SpawnPrefabs]");
                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                        var tx = prefabWorld.EntityManager.BeginExclusiveEntityTransaction();
                        SerializeUtility.DeserializeWorld(tx, reader, unityObjects);
                        prefabWorld.EntityManager.EndExclusiveEntityTransaction();
                    }
                }

                EntityManager.MoveEntitiesFrom(prefabWorld.EntityManager);
            }

            ValidateHeader(resources.ModelPrefabRecords.Length);
            ValidateRuntimeComponents(resources);
            RuntimeRenderIdBindingUtility.BindSpawnPrefabs(EntityManager, resources, _renderResourcesQuery);
            ValidateBakedRenderResources(resources);
            PatchColliderSources(resources);
            ValidatePickColliders(resources);
            BuildRegistry(resources);
        }

        void ValidateHeader(int expectedModelPrefabCount)
        {
            if (_headerQuery.CalculateEntityCount() != 1)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] cache must contain exactly one RuntimeSpawnPrefabCacheHeader; rebake required.");

            var header = _headerQuery.GetSingleton<RuntimeSpawnPrefabCacheHeader>();
            if (header.FormatVersion != CacheFormat.FormatVersion)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache format mismatch (found {header.FormatVersion}, expected {CacheFormat.FormatVersion}); rebake required.");
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache pipeline mismatch (found {header.PipelineVersion}, expected {CacheFormat.WorldBakePipelineVersion}); rebake required.");
            if (header.ModelPrefabCount != expectedModelPrefabCount)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache model count mismatch (found {header.ModelPrefabCount}, expected {expectedModelPrefabCount}); rebake required.");
        }

        void ValidateBakedRenderResources(RuntimeMaterializationResources resources)
        {
            using var entities = _renderResourcesQuery.ToEntityArray(Allocator.Temp);
            using var renderResources = _renderResourcesQuery.ToComponentDataArray<RuntimeSpawnPrefabRenderResource>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var resource = renderResources[i];
                string context = $"[VVardenfell][SpawnPrefabs] model prefab {resource.ModelPrefabIndex} node {resource.NodeIndex}";
                int textureSlice = ResolveTextureSlice(resources, resource.TextureIndex);
                ValidateMeshAndMaterial(resources, resource.MeshIndex, resource.MaterialIndex, context);
                ValidatePrebakedRenderEntity(entity, resource, textureSlice, context);
            }
        }

        void ValidatePrebakedRenderEntity(Entity entity, RuntimeSpawnPrefabRenderResource resource, int textureSlice, string context)
        {
            if (!EntityManager.HasComponent<ModelPrefabRenderLeaf>(entity))
                throw new InvalidDataException($"{context} is missing baked ModelPrefabRenderLeaf; rebake required.");
            if (!EntityManager.HasComponent<MaterialMeshInfo>(entity))
                throw new InvalidDataException($"{context} is missing baked MaterialMeshInfo; rebake required.");
            if (!EntityManager.HasComponent<RenderBounds>(entity))
                throw new InvalidDataException($"{context} is missing baked RenderBounds; rebake required.");
            if (!EntityManager.HasComponent<TextureSlice>(entity))
                throw new InvalidDataException($"{context} is missing baked TextureSlice; rebake required.");
            if (EntityManager.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                throw new InvalidDataException($"{context} serialized a RenderMeshArray shared component; rebake required.");
            var leaf = EntityManager.GetComponentData<ModelPrefabRenderLeaf>(entity);
            if (leaf.NodeIndex != resource.NodeIndex
                || leaf.MeshIndex != resource.MeshIndex
                || leaf.MaterialIndex != resource.MaterialIndex
                || leaf.TextureIndex != resource.TextureIndex)
            {
                throw new InvalidDataException($"{context} baked render leaf does not match render resource; rebake required.");
            }
            var info = EntityManager.GetComponentData<MaterialMeshInfo>(entity);
            ValidateMaterialMeshInfo(info, context);
            if (EntityManager.GetComponentData<TextureSlice>(entity).Value != textureSlice)
                throw new InvalidDataException($"{context} baked texture slice is stale; rebake required.");
        }

        static void ValidateMaterialMeshInfo(MaterialMeshInfo info, string context)
        {
            if (info.Mesh <= 0 || info.Material <= 0)
                throw new InvalidDataException($"{context} was not patched with direct runtime MaterialMeshInfo ids.");
        }

        static void ValidateEmptyRenderObjectTable(RuntimeRenderObjectReference[] renderReferences)
        {
            if (renderReferences == null)
                throw new InvalidDataException("[VVardenfell][SpawnPrefabs] cache has no render object reference table.");
            if (renderReferences.Length != 0)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] cache contains {renderReferences.Length} serialized Unity render object references; rebake required for direct runtime render IDs.");
        }

        void PatchColliderSources(RuntimeMaterializationResources resources)
        {
            PatchRootColliderSources(resources);
            PatchPickColliderSources(resources);
        }

        void PatchRootColliderSources(RuntimeMaterializationResources resources)
        {
            using var entities = _rootsQuery.ToEntityArray(Allocator.Temp);
            using var roots = _rootsQuery.ToComponentDataArray<RuntimeSpawnPrefabRoot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (roots[i].CollisionIndex < 0)
                {
                    if (EntityManager.HasComponent<RuntimeColliderSource>(entities[i])
                        && !EntityManager.HasComponent<RuntimeSpawnPrefabPickCollider>(entities[i]))
                    {
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {roots[i].ModelPrefabIndex} root has unexpected RuntimeColliderSource; rebake required.");
                    }
                    continue;
                }

                if (EntityManager.HasComponent<RuntimeColliderSource>(entities[i]))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {roots[i].ModelPrefabIndex} root serialized reusable RuntimeColliderSource; rebake required.");
                var collider = RequireGlobalCollider(resources, roots[i].CollisionIndex, $"[VVardenfell][SpawnPrefabs] model prefab {roots[i].ModelPrefabIndex} root");
                if (!RuntimeColliderAttachmentUtility.AttachSource(EntityManager, entities[i], collider, RuntimeColliderKind.RuntimeSpawn, active: false))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] failed to attach root collider {roots[i].CollisionIndex}; rebake required.");
            }
        }

        void PatchPickColliderSources(RuntimeMaterializationResources resources)
        {
            using var entities = _pickCollidersQuery.ToEntityArray(Allocator.Temp);
            using var picks = _pickCollidersQuery.ToComponentDataArray<RuntimeSpawnPrefabPickCollider>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!EntityManager.HasComponent<InteractionPickSurfaceTag>(entities[i]))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {picks[i].ColliderIndex} is missing baked InteractionPickSurfaceTag; rebake required.");
                if (EntityManager.HasComponent<RuntimeColliderSource>(entities[i]))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {picks[i].ColliderIndex} serialized reusable RuntimeColliderSource; rebake required.");
                var collider = RequireGlobalCollider(resources, picks[i].ColliderIndex, "[VVardenfell][SpawnPrefabs] pick collider");
                if (!RuntimeColliderAttachmentUtility.AttachSource(EntityManager, entities[i], collider, RuntimeColliderKind.InteractionPick, active: false))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] failed to attach pick collider {picks[i].ColliderIndex}; rebake required.");
            }
        }

        static BlobAssetReference<Collider> RequireGlobalCollider(RuntimeMaterializationResources resources, int index, string context)
        {
            var blobs = resources.ColliderBlobs;
            if (blobs == null || (uint)index >= (uint)blobs.Length || !blobs[index].IsCreated)
                throw new InvalidDataException($"{context} references missing global collider {index}; rebake required.");
            return blobs[index];
        }

        static int ResolveTextureSlice(RuntimeMaterializationResources resources, int textureIndex)
        {
            if (textureIndex >= 0 && resources.TexBucketInfo.IsCreated && textureIndex < resources.TexBucketInfo.Length)
                return resources.TexBucketInfo[textureIndex].y;
            return resources.FallbackBucketSlice.y;
        }

        static void ValidateMeshAndMaterial(RuntimeMaterializationResources resources, int meshIndex, int materialIndex, string context)
        {
            if (resources.Meshes == null || (uint)meshIndex >= (uint)resources.Meshes.Length)
                throw new InvalidDataException($"{context} references missing mesh {meshIndex}; rebake required.");
            if ((uint)materialIndex >= (uint)resources.BlendVariantCount)
                throw new InvalidDataException($"{context} references material variant {materialIndex}, outside loaded variant count {resources.BlendVariantCount}; rebake required.");
        }

        void ValidateRuntimeComponents(RuntimeMaterializationResources resources)
        {
            var records = resources.ModelPrefabRecords;
            using var entities = _nodesQuery.ToEntityArray(Allocator.Temp);
            using var nodes = _nodesQuery.ToComponentDataArray<RuntimeSpawnPrefabNode>(Allocator.Temp);
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
                if (!EntityManager.HasComponent<ModelPrefabNodeTag>(entity))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} is missing baked ModelPrefabNodeTag; rebake required.");

                if (def.ObjectAnimation?.IsEnabled == true)
                {
                    if (!EntityManager.HasComponent<ObjectAnimationNode>(entity))
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} is missing baked ObjectAnimationNode; rebake required.");
                    ValidateObjectAnimationNode(
                        EntityManager.GetComponentData<ObjectAnimationNode>(entity),
                        nodeRef.ModelPrefabIndex,
                        nodeRef.NodeIndex,
                        sourceNode,
                        $"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex}");
                }
                else if (EntityManager.HasComponent<ObjectAnimationNode>(entity))
                {
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} has unexpected ObjectAnimationNode; rebake required.");
                }

                if ((ModelPrefabNodeKind)nodeRef.Kind == ModelPrefabNodeKind.Billboard)
                {
                    if (!EntityManager.HasComponent<ModelBillboardTag>(entity))
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} is missing baked ModelBillboardTag; rebake required.");
                    if (!EntityManager.HasComponent<ModelBillboardState>(entity))
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} is missing baked ModelBillboardState; rebake required.");
                    var state = EntityManager.GetComponentData<ModelBillboardState>(entity);
                    var expected = new quaternion(sourceNode.RotX, sourceNode.RotY, sourceNode.RotZ, sourceNode.RotW);
                    if (!ApproximatelyEqual(state.BaseLocalRotation, expected))
                        throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} has stale ModelBillboardState; rebake required.");
                }
                else if (EntityManager.HasComponent<ModelBillboardTag>(entity) || EntityManager.HasComponent<ModelBillboardState>(entity))
                {
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {nodeRef.ModelPrefabIndex} node {nodeRef.NodeIndex} has unexpected billboard components; rebake required.");
                }
            }
        }

        static void ValidateObjectAnimationNode(
            ObjectAnimationNode node,
            int modelPrefabIndex,
            int nodeIndex,
            ModelPrefabNodeDef sourceNode,
            string context)
        {
            if (node.Root == Entity.Null)
                throw new InvalidDataException($"{context} has no baked object-animation root; rebake required.");
            if (node.ModelPrefabIndex != modelPrefabIndex
                || node.NodeIndex != nodeIndex
                || node.ParentIndex != sourceNode.ParentIndex
                || !ApproximatelyEqual(node.BindPosition, new float3(sourceNode.PosX, sourceNode.PosY, sourceNode.PosZ))
                || !ApproximatelyEqual(node.BindRotation, new quaternion(sourceNode.RotX, sourceNode.RotY, sourceNode.RotZ, sourceNode.RotW))
                || math.abs(node.BindScale - sourceNode.Scale) > 0.0001f)
            {
                throw new InvalidDataException($"{context} has stale ObjectAnimationNode data; rebake required.");
            }
        }

        static bool ApproximatelyEqual(float3 a, float3 b)
            => math.lengthsq(a - b) <= 0.000001f;

        static bool ApproximatelyEqual(quaternion a, quaternion b)
            => math.lengthsq(a.value - b.value) <= 0.000001f;

        void ValidatePickColliders(RuntimeMaterializationResources resources)
        {
            using var entities = _pickCollidersQuery.ToEntityArray(Allocator.Temp);
            using var colliders = _pickCollidersQuery.ToComponentDataArray<RuntimeSpawnPrefabPickCollider>(Allocator.Temp);
            BlobAssetReference<Collider>[] blobs = resources.ColliderBlobs;
            for (int i = 0; i < entities.Length; i++)
            {
                int colliderIndex = colliders[i].ColliderIndex;
                if (blobs == null || (uint)colliderIndex >= (uint)blobs.Length || !blobs[colliderIndex].IsCreated)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {colliderIndex} is missing; rebake required.");

                if (!EntityManager.HasComponent<InteractionPickSurfaceTag>(entities[i]))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {colliderIndex} is missing baked InteractionPickSurfaceTag; rebake required.");
                if (!EntityManager.HasComponent<RuntimeColliderSource>(entities[i]))
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {colliderIndex} was not patched with RuntimeColliderSource; rebake required.");
                var source = EntityManager.GetComponentData<RuntimeColliderSource>(entities[i]);
                if (!source.Value.IsCreated || source.Kind != RuntimeColliderKind.InteractionPick)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] pick collider {colliderIndex} has invalid baked RuntimeColliderSource; rebake required.");
            }
        }

        void BuildRegistry(RuntimeMaterializationResources resources)
        {
            var records = resources.ModelPrefabRecords;
            resources.ModelPrefabs = new Entity[records.Length];

            using var roots = _rootsQuery.ToEntityArray(Allocator.Temp);
            using var rootData = _rootsQuery.ToComponentDataArray<RuntimeSpawnPrefabRoot>(Allocator.Temp);
            var registryEntries = new RuntimeSpawnPrefabRegistryEntry[roots.Length];
            int registryEntryCount = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = rootData[i];
                if ((uint)root.ModelPrefabIndex >= (uint)records.Length)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] root references model prefab {root.ModelPrefabIndex}, outside table length {records.Length}; rebake required.");
                if (resources.ModelPrefabs[root.ModelPrefabIndex] != Entity.Null)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] duplicate root for model prefab {root.ModelPrefabIndex}; rebake required.");

                resources.ModelPrefabs[root.ModelPrefabIndex] = roots[i];
                ValidatePrebakedRoot(resources, roots[i], root);
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
                if (resources.ModelPrefabs[i] == Entity.Null)
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {i} has no serialized runtime root; rebake required.");
            }

            Entity registry = EntityManager.CreateEntity(
                ComponentType.ReadWrite<RuntimeSpawnPrefabRegistry>(),
                ComponentType.ReadWrite<RuntimeSpawnPrefabRegistryEntry>());
            var entries = EntityManager.GetBuffer<RuntimeSpawnPrefabRegistryEntry>(registry);
            entries.ResizeUninitialized(registryEntryCount);
            for (int i = 0; i < registryEntryCount; i++)
                entries[i] = registryEntries[i];

            WorldModelPrefabUtility.BuildRuntimeSpawnPrefabLookups(resources);
        }

        void ValidatePrebakedRoot(RuntimeMaterializationResources resources, Entity entity, RuntimeSpawnPrefabRoot root)
        {
            if (!EntityManager.HasComponent<ModelPrefabRoot>(entity))
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root is missing baked ModelPrefabRoot; rebake required.");
            if (EntityManager.GetComponentData<ModelPrefabRoot>(entity).ModelPrefabIndex != root.ModelPrefabIndex)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root has stale ModelPrefabRoot; rebake required.");

            if (root.CollisionIndex < 0)
            {
                if (EntityManager.HasComponent<RuntimeColliderSource>(entity)
                    && !EntityManager.HasComponent<RuntimeSpawnPrefabPickCollider>(entity))
                {
                    throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root has unexpected RuntimeColliderSource; rebake required.");
                }
                return;
            }

            var blobs = resources.ColliderBlobs;
            if (blobs == null || (uint)root.CollisionIndex >= (uint)blobs.Length || !blobs[root.CollisionIndex].IsCreated)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root references missing collider {root.CollisionIndex}; rebake required.");
            if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root was not patched with RuntimeColliderSource; rebake required.");
            var source = EntityManager.GetComponentData<RuntimeColliderSource>(entity);
            if (!source.Value.IsCreated || source.Kind != RuntimeColliderKind.RuntimeSpawn)
                throw new InvalidDataException($"[VVardenfell][SpawnPrefabs] model prefab {root.ModelPrefabIndex} root has invalid baked RuntimeColliderSource; rebake required.");
        }
    }
}
