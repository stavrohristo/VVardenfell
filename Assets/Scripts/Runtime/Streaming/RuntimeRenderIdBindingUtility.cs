using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal static class RuntimeRenderIdBindingUtility
    {
        static readonly ProfilerMarker k_BindSectionRenderIds = new("VV.Render.BindSectionIds");
        static readonly ProfilerMarker k_BindSpawnPrefabRenderIds = new("VV.Render.BindSpawnPrefabIds");
        static readonly ProfilerMarker k_BindDistantTerrainRenderIds = new("VV.Render.BindDistantTerrainIds");
        static readonly ProfilerCounterValue<int> k_BoundSectionRenderEntityCount = new(ProfilerCategory.Scripts, "VV.Render.BoundSectionEntityCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_BoundSpawnPrefabRenderEntityCount = new(ProfilerCategory.Scripts, "VV.Render.BoundSpawnPrefabEntityCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_BoundDistantTerrainEntityCount = new(ProfilerCategory.Scripts, "VV.Render.BoundDistantTerrainEntityCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_StreamedRenderMeshArrayCount = new(ProfilerCategory.Scripts, "VV.Render.StreamedRenderMeshArrayCount", ProfilerMarkerDataUnit.Count);

        public static int BindCellSection(EntityManager em, RuntimeMaterializationResources resources, Entity sectionEntity)
        {
            using (k_BindSectionRenderIds.Auto())
            {
                int bound = 0;
                var terrains = RequireBuffer<RuntimeCellSectionTerrainEntity>(em, sectionEntity, "terrain render entities");
                for (int i = 0; i < terrains.Length; i++)
                    bound += BindTerrain(em, resources, RequireExisting(em, terrains[i].Value, "terrain render entity"));

                var placed = RequireBuffer<RuntimeCellSectionRenderEntity>(em, sectionEntity, "placed render entities");
                for (int i = 0; i < placed.Length; i++)
                    bound += BindPlaced(em, resources, RequireExisting(em, placed[i].Value, "placed render entity"));

                var combined = RequireBuffer<RuntimeCellSectionCombinedRenderEntity>(em, sectionEntity, "combined render entities");
                for (int i = 0; i < combined.Length; i++)
                    bound += BindCombined(em, resources, RequireExisting(em, combined[i].Value, "combined render entity"));

                k_BoundSectionRenderEntityCount.Value = bound;
                k_StreamedRenderMeshArrayCount.Value = 0;
                return bound;
            }
        }

        public static int BindTerrainOnly(EntityManager em, RuntimeMaterializationResources resources, Entity sectionEntity)
        {
            using (k_BindSectionRenderIds.Auto())
            {
                int bound = 0;
                var terrains = RequireBuffer<RuntimeCellSectionTerrainEntity>(em, sectionEntity, "terrain render entities");
                for (int i = 0; i < terrains.Length; i++)
                    bound += BindTerrain(em, resources, RequireExisting(em, terrains[i].Value, "terrain render entity"));
                k_BoundSectionRenderEntityCount.Value = bound;
                k_StreamedRenderMeshArrayCount.Value = 0;
                return bound;
            }
        }

        public static int BindSpawnPrefabs(EntityManager em, RuntimeMaterializationResources resources, EntityQuery renderResourceQuery)
        {
            using (k_BindSpawnPrefabRenderIds.Auto())
            {
                using var entities = renderResourceQuery.ToEntityArray(Allocator.Temp);
                using var renderResources = renderResourceQuery.ToComponentDataArray<RuntimeSpawnPrefabRenderResource>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                    BindPlaced(em, resources, entities[i], renderResources[i]);
                k_BoundSpawnPrefabRenderEntityCount.Value = entities.Length;
                k_StreamedRenderMeshArrayCount.Value = 0;
                return entities.Length;
            }
        }

        public static int BindDistantTerrain(EntityManager em, RuntimeMaterializationResources resources, EntityQuery terrainQuery)
        {
            using (k_BindDistantTerrainRenderIds.Auto())
            {
                using var entities = terrainQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                    BindTerrain(em, resources, entities[i]);
                k_BoundDistantTerrainEntityCount.Value = entities.Length;
                k_StreamedRenderMeshArrayCount.Value = 0;
                return entities.Length;
            }
        }

        static int BindPlaced(EntityManager em, RuntimeMaterializationResources resources, Entity entity)
        {
            if (!em.HasComponent<RuntimeSpawnPrefabRenderResource>(entity))
                throw new InvalidOperationException("[VVardenfell][Rendering] placed render entity is missing logical render resource; rebake required.");
            return BindPlaced(em, resources, entity, em.GetComponentData<RuntimeSpawnPrefabRenderResource>(entity));
        }

        static int BindPlaced(EntityManager em, RuntimeMaterializationResources resources, Entity entity, RuntimeSpawnPrefabRenderResource resource)
        {
            RequireDirectIdBindable(em, entity, "[VVardenfell][Rendering] placed render entity");
            var meshId = RequireMeshId(resources, resource.MeshIndex, "[VVardenfell][Rendering] placed render entity");
            int bucketIndex = ResolveTextureBucketIndex(resources, resource.TextureIndex, "[VVardenfell][Rendering] placed render entity");
            int materialIndex = ResolveVariantIndex(bucketIndex, resources.BlendVariantCount, resource.MaterialIndex, resources.RegisteredRefMaterials?.Length ?? 0, "[VVardenfell][Rendering] placed render entity");
            var materialId = RequireMaterialId(resources.RegisteredRefMaterials, materialIndex, "[VVardenfell][Rendering] placed render entity");
            em.SetComponentData(entity, new MaterialMeshInfo(materialId, meshId));
            return 1;
        }

        static int BindCombined(EntityManager em, RuntimeMaterializationResources resources, Entity entity)
        {
            RequireDirectIdBindable(em, entity, "[VVardenfell][Rendering] combined render entity");
            if (!em.HasComponent<RuntimeCellSectionCombinedRenderResource>(entity))
                throw new InvalidOperationException("[VVardenfell][Rendering] combined render entity is missing logical render resource; rebake required.");
            var resource = em.GetComponentData<RuntimeCellSectionCombinedRenderResource>(entity);
            var meshId = RequireMeshId(resources, resource.MeshIndex, "[VVardenfell][Rendering] combined render entity");
            int bucketIndex = ResolveCombinedBucketIndex(resources, resource.TextureBucketKey, "[VVardenfell][Rendering] combined render entity");
            int materialIndex = ResolveVariantIndex(bucketIndex, resources.CombinedRenderVariantCount, resource.MaterialIndex, resources.RegisteredCombinedMaterials?.Length ?? 0, "[VVardenfell][Rendering] combined render entity");
            var materialId = RequireMaterialId(resources.RegisteredCombinedMaterials, materialIndex, "[VVardenfell][Rendering] combined render entity");
            em.SetComponentData(entity, new MaterialMeshInfo(materialId, meshId));
            return 1;
        }

        static int BindTerrain(EntityManager em, RuntimeMaterializationResources resources, Entity entity)
        {
            RequireDirectIdBindable(em, entity, "[VVardenfell][Rendering] terrain render entity");
            if (!em.HasComponent<RuntimeCellSectionTerrainRenderResource>(entity))
                throw new InvalidOperationException("[VVardenfell][Rendering] terrain render entity is missing logical render resource; rebake required.");
            var resource = em.GetComponentData<RuntimeCellSectionTerrainRenderResource>(entity);
            var meshId = RequireMeshId(resources, resource.MeshIndex, "[VVardenfell][Rendering] terrain render entity");
            if (resources.RegisteredTerrainMaterial.value == 0)
                throw new InvalidOperationException("[VVardenfell][Rendering] terrain material is not pre-registered.");
            em.SetComponentData(entity, new MaterialMeshInfo(resources.RegisteredTerrainMaterial, meshId));
            return 1;
        }

        static void RequireDirectIdBindable(EntityManager em, Entity entity, string context)
        {
            if (entity == Entity.Null || !em.Exists(entity))
                throw new InvalidOperationException($"{context} is missing; rebake required.");
            if (!em.HasComponent<MaterialMeshInfo>(entity))
                throw new InvalidOperationException($"{context} is missing MaterialMeshInfo; rebake required.");
            if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
            {
                k_StreamedRenderMeshArrayCount.Value = 1;
                throw new InvalidOperationException($"{context} serialized a RenderMeshArray shared component; rebake required.");
            }
        }

        static DynamicBuffer<T> RequireBuffer<T>(EntityManager em, Entity sectionEntity, string label)
            where T : unmanaged, IBufferElementData
        {
            if (sectionEntity == Entity.Null || !em.Exists(sectionEntity) || !em.HasBuffer<T>(sectionEntity))
                throw new InvalidOperationException($"[VVardenfell][Rendering] section root is missing {label}; rebake required.");
            return em.GetBuffer<T>(sectionEntity);
        }

        static Entity RequireExisting(EntityManager em, Entity entity, string label)
        {
            if (entity == Entity.Null || !em.Exists(entity))
                throw new InvalidOperationException($"[VVardenfell][Rendering] section buffer references missing {label}; rebake required.");
            return entity;
        }

        static BatchMeshID RequireMeshId(RuntimeMaterializationResources resources, int meshIndex, string context)
        {
            var meshes = resources.RegisteredMeshes;
            if (meshes == null || (uint)meshIndex >= (uint)meshes.Length)
                throw new InvalidOperationException($"{context} references mesh {meshIndex}, outside pre-registered mesh table.");
            var id = meshes[meshIndex];
            if (id.value == 0)
                throw new InvalidOperationException($"{context} references unregistered mesh {meshIndex}.");
            return id;
        }

        static BatchMaterialID RequireMaterialId(BatchMaterialID[] materials, int materialIndex, string context)
        {
            if (materials == null || (uint)materialIndex >= (uint)materials.Length)
                throw new InvalidOperationException($"{context} references material {materialIndex}, outside pre-registered material table.");
            var id = materials[materialIndex];
            if (id.value == 0)
                throw new InvalidOperationException($"{context} references unregistered material {materialIndex}.");
            return id;
        }

        static int ResolveTextureBucketIndex(RuntimeMaterializationResources resources, int textureIndex, string context)
        {
            if (textureIndex >= 0 && resources.TexBucketInfo.IsCreated && textureIndex < resources.TexBucketInfo.Length)
            {
                int bucketIndex = resources.TexBucketInfo[textureIndex].x;
                if (bucketIndex >= 0)
                    return bucketIndex;
            }

            int fallbackBucket = resources.FallbackBucketSlice.x;
            if (fallbackBucket < 0)
                throw new InvalidOperationException($"{context} cannot resolve texture {textureIndex} to a material bucket.");
            return fallbackBucket;
        }

        static int ResolveCombinedBucketIndex(RuntimeMaterializationResources resources, int textureBucketKey, string context)
        {
            if (textureBucketKey == 0)
                throw new InvalidOperationException($"{context} has no texture bucket key; rebake required.");
            if (resources.RefBucketIndexByKey == null || !resources.RefBucketIndexByKey.TryGetValue(textureBucketKey, out int bucketIndex))
                throw new InvalidOperationException($"{context} references missing texture bucket key {textureBucketKey}; rebake required.");
            if (bucketIndex < 0)
                throw new InvalidOperationException($"{context} resolved invalid texture bucket index {bucketIndex}.");
            return bucketIndex;
        }

        static int ResolveVariantIndex(int bucketIndex, int variantCount, int materialIndex, int tableLength, string context)
        {
            if (variantCount <= 0)
                throw new InvalidOperationException($"{context} has no registered material variants.");
            if ((uint)materialIndex >= (uint)variantCount)
                throw new InvalidOperationException($"{context} references material variant {materialIndex}, outside variant count {variantCount}; rebake required.");
            long globalIndex = (long)bucketIndex * variantCount + materialIndex;
            if (globalIndex < 0 || globalIndex > int.MaxValue || globalIndex >= tableLength)
                throw new InvalidOperationException($"{context} resolved material index {globalIndex}, outside pre-registered table length {tableLength}.");
            return (int)globalIndex;
        }
    }
}
