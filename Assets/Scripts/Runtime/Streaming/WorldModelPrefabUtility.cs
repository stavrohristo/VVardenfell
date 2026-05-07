using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldModelPrefabUtility
    {
        public static bool EnsureModelPrefabBuilt(EntityManager em, CacheLoader cache, int modelPrefabIndex)
        {
            if (cache?.ModelPrefabCatalog?.Records == null)
                return false;

            var modelDefs = cache.ModelPrefabCatalog.Records;
            if ((uint)modelPrefabIndex >= (uint)modelDefs.Length)
                return false;

            if (WorldResources.ModelPrefabs == null || WorldResources.ModelPrefabs.Length != modelDefs.Length)
                WorldResources.ModelPrefabs = new Entity[modelDefs.Length];

            Entity existing = WorldResources.ModelPrefabs[modelPrefabIndex];
            if (existing != Entity.Null && em.Exists(existing))
                return true;

            var localRenderArrayCache = new Dictionary<long, RenderMeshArray>();
            Entity built = BuildModelPrefabEntityGraph(em, cache, modelDefs[modelPrefabIndex], modelPrefabIndex, localRenderArrayCache);
            WorldResources.ModelPrefabs[modelPrefabIndex] = built;
            return built != Entity.Null;
        }

        public static void BuildRuntimeSpawnPrefabLookups(CacheLoader cache)
        {
            var modelDefs = cache?.ModelPrefabCatalog?.Records ?? System.Array.Empty<ModelPrefabDef>();
            var modelLookup = BuildModelDescriptorLookup(modelDefs);

            var contentBlob = cache?.ContentBlob ?? default;
            if (!contentBlob.IsCreated)
            {
                WorldResources.SpawnableCreaturePrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableItemPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableLightPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                return;
            }

            ref RuntimeContentBlob content = ref contentBlob.Value;
            var creatures = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Actors.Length];
            for (int i = 0; i < creatures.Length; i++)
            {
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, ActorDefHandle.FromIndex(i));
                if (actor.Kind != ActorDefKind.Creature)
                    continue;

                creatures[i] = ResolveModelDescriptor(modelLookup, actor.Model.ToString());
            }

            var items = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref content, ItemDefHandle.FromIndex(i));
                items[i] = ResolveModelDescriptor(modelLookup, item.Model.ToString());
            }

            var lights = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                ref RuntimeLightDefBlob light = ref RuntimeContentBlobUtility.Get(ref content, LightDefHandle.FromIndex(i));
                lights[i] = ResolveModelDescriptor(modelLookup, light.Model.ToString());
            }

            WorldResources.SpawnableCreaturePrefabs = creatures;
            WorldResources.SpawnableItemPrefabs = items;
            WorldResources.SpawnableLightPrefabs = lights;
        }

        internal static Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> BuildModelDescriptorLookup(ModelPrefabDef[] modelDefs)
        {
            var lookup = new Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor>(
                modelDefs?.Length ?? 0,
                System.StringComparer.OrdinalIgnoreCase);
            if (modelDefs == null)
                return lookup;

            for (int i = 0; i < modelDefs.Length; i++)
            {
                var def = modelDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.ModelPath))
                    continue;

                lookup[NormalizeContentModelPath(def.ModelPath)] = new WorldResources.RuntimeSpawnPrefabDescriptor
                {
                    ModelPrefabIndex = i,
                    CollisionIndex = def.CollisionIndex,
                    Supported = 1,
                };
            }

            return lookup;
        }

        internal static bool TryResolveModelDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath,
            out WorldResources.RuntimeSpawnPrefabDescriptor descriptor)
        {
            descriptor = ResolveModelDescriptor(modelLookup, modelPath);
            return descriptor.IsSupported;
        }

        static Entity BuildModelPrefabEntityGraph(
            EntityManager em,
            CacheLoader cache,
            ModelPrefabDef def,
            int modelPrefabIndex,
            Dictionary<long, RenderMeshArray> renderArrayCache)
        {
            if (def == null || def.Nodes == null || def.Nodes.Length == 0)
                return Entity.Null;

            var entities = new Entity[def.Nodes.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = em.CreateEntity();
                em.AddComponent<Prefab>(entities[i]);
                em.AddComponent<ModelPrefabNodeTag>(entities[i]);
                em.AddComponentData(entities[i], LocalTransform.FromPositionRotationScale(
                    new float3(def.Nodes[i].PosX, def.Nodes[i].PosY, def.Nodes[i].PosZ),
                    new quaternion(def.Nodes[i].RotX, def.Nodes[i].RotY, def.Nodes[i].RotZ, def.Nodes[i].RotW),
                    def.Nodes[i].Scale));
                em.AddComponentData(entities[i], new LocalToWorld { Value = float4x4.identity });
            }

            int rootIndex = math.clamp(def.RootNodeIndex, 0, entities.Length - 1);
            Entity root = entities[rootIndex];
            em.SetName(root, $"VVardenfell.ModelPrefab[{modelPrefabIndex}]");
            em.AddComponentData(root, new ModelPrefabRoot { ModelPrefabIndex = modelPrefabIndex });
            var linkedGroup = em.AddBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < entities.Length; i++)
                linkedGroup.Add(new LinkedEntityGroup { Value = entities[i] });

            for (int i = 0; i < entities.Length; i++)
            {
                var node = def.Nodes[i];
                var entity = entities[i];

                if (def.ObjectAnimation?.IsEnabled == true)
                {
                    em.AddComponentData(entity, new ObjectAnimationNode
                    {
                        Root = root,
                        ModelPrefabIndex = modelPrefabIndex,
                        NodeIndex = i,
                        ParentIndex = node.ParentIndex,
                        BindPosition = new float3(node.PosX, node.PosY, node.PosZ),
                        BindRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                        BindScale = node.Scale,
                    });
                }

                if (i != rootIndex && node.ParentIndex >= 0 && node.ParentIndex < entities.Length)
                    em.AddComponentData(entity, new Parent { Value = entities[node.ParentIndex] });

                if (node.Kind == ModelPrefabNodeKind.RenderLeaf && node.GlobalMeshIndex >= 0)
                {
                    int bucketIndex = node.TextureIndex >= 0 && node.TextureIndex < WorldResources.TexBucketInfo.Length
                        ? WorldResources.TexBucketInfo[node.TextureIndex].x
                        : WorldResources.FallbackBucketSlice.x;
                    int textureSlice = node.TextureIndex >= 0 && node.TextureIndex < WorldResources.TexBucketInfo.Length
                        ? WorldResources.TexBucketInfo[node.TextureIndex].y
                        : WorldResources.FallbackBucketSlice.y;

                    var rma = GetOrCreateLeafRenderMeshArray(cache, renderArrayCache, bucketIndex, node.GlobalMeshIndex);
                    RenderMeshUtility.AddComponents(
                        entity,
                        em,
                        WorldResources.Desc,
                        rma,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(math.max(0, node.MaterialIndex), 0));
                    em.AddComponentData(entity, new TextureSlice { Value = textureSlice });
                    em.AddComponentData(entity, new RenderBounds
                    {
                        Value = new AABB
                        {
                            Center = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                            Extents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                        }
                    });
                    em.AddComponentData(entity, new ModelPrefabRenderLeaf
                    {
                        NodeIndex = i,
                        MeshIndex = node.GlobalMeshIndex,
                        MaterialIndex = node.MaterialIndex,
                        TextureIndex = node.TextureIndex,
                    });

                    AttachInteractionPickCollider(em, entity, node, modelPrefabIndex, i);
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

            return root;
        }

        static void AttachInteractionPickCollider(
            EntityManager em,
            Entity entity,
            ModelPrefabNodeDef node,
            int modelPrefabIndex,
            int nodeIndex)
        {
            if (node.PickColliderIndex < 0)
                return;

            var colliderBlobs = WorldResources.ColliderBlobs;
            if (colliderBlobs == null
                || (uint)node.PickColliderIndex >= (uint)colliderBlobs.Length
                || !colliderBlobs[node.PickColliderIndex].IsCreated)
            {
                throw new System.InvalidOperationException(
                    $"Model prefab {modelPrefabIndex} node {nodeIndex} references missing interaction pick collider {node.PickColliderIndex}.");
            }

            RuntimeColliderAttachmentUtility.AttachSource(
                em,
                entity,
                colliderBlobs[node.PickColliderIndex],
                RuntimeColliderKind.InteractionPick,
                active: false);
            em.AddComponent<InteractionPickSurfaceTag>(entity);
        }

        static RenderMeshArray GetOrCreateLeafRenderMeshArray(
            CacheLoader cache,
            Dictionary<long, RenderMeshArray> renderArrayCache,
            int bucketIndex,
            int meshIndex)
        {
            long key = ((long)bucketIndex << 32) ^ (uint)meshIndex;
            if (renderArrayCache.TryGetValue(key, out var existing))
                return existing;

            var materials = new Material[WorldResources.BlendVariantCount];
            int materialStart = bucketIndex * WorldResources.BlendVariantCount;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = cache.Materials[materialStart + i];

            var rma = new RenderMeshArray(materials, new[] { cache.Meshes[meshIndex] });
            renderArrayCache[key] = rma;
            return rma;
        }

        static WorldResources.RuntimeSpawnPrefabDescriptor ResolveModelDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return default;

            string normalizedPath = NormalizeContentModelPath(modelPath);
            return modelLookup.TryGetValue(normalizedPath, out var descriptor) ? descriptor : default;
        }

        internal static string NormalizeContentModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\"))
                trimmed = trimmed.Replace("\\\\", "\\");
            if (trimmed.StartsWith("meshes\\", System.StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return $"meshes\\{trimmed}";
        }
    }
}
