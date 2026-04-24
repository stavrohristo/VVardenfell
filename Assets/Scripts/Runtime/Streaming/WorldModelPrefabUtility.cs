using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
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
            var modelLookup = new Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor>(modelDefs.Length, System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < modelDefs.Length; i++)
            {
                var def = modelDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.ModelPath))
                    continue;

                modelLookup[def.ModelPath] = new WorldResources.RuntimeSpawnPrefabDescriptor
                {
                    ModelPrefabIndex = i,
                    CollisionIndex = def.CollisionIndex,
                    Supported = 1,
                };
            }

            var contentDb = cache?.ContentDatabase;
            if (contentDb == null)
            {
                WorldResources.SpawnableCreaturePrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableItemPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableLightPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                return;
            }

            var creatures = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.ActorCount];
            for (int i = 0; i < creatures.Length; i++)
            {
                ref readonly var actor = ref contentDb.Get(ActorDefHandle.FromIndex(i));
                if (actor.Kind != ActorDefKind.Creature)
                    continue;

                creatures[i] = ResolveSpawnDescriptor(modelLookup, actor.Model);
            }

            var items = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.ItemCount];
            for (int i = 0; i < items.Length; i++)
            {
                ref readonly var item = ref contentDb.Get(ItemDefHandle.FromIndex(i));
                items[i] = ResolveSpawnDescriptor(modelLookup, item.Model);
            }

            var lights = new WorldResources.RuntimeSpawnPrefabDescriptor[contentDb.LightCount];
            for (int i = 0; i < lights.Length; i++)
            {
                ref readonly var light = ref contentDb.Get(LightDefHandle.FromIndex(i));
                lights[i] = ResolveSpawnDescriptor(modelLookup, light.Model);
            }

            WorldResources.SpawnableCreaturePrefabs = creatures;
            WorldResources.SpawnableItemPrefabs = items;
            WorldResources.SpawnableLightPrefabs = lights;
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
                        MeshIndex = node.GlobalMeshIndex,
                        MaterialIndex = node.MaterialIndex,
                        TextureIndex = node.TextureIndex,
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

            return root;
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

        static WorldResources.RuntimeSpawnPrefabDescriptor ResolveSpawnDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return default;

            string normalizedPath = NormalizeContentModelPath(modelPath);
            return modelLookup.TryGetValue(normalizedPath, out var descriptor) ? descriptor : default;
        }

        static string NormalizeContentModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim();
            if (trimmed.StartsWith("meshes\\", System.StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return $"meshes\\{trimmed}";
        }
    }
}
