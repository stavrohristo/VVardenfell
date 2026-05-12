using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    public sealed class RuntimeMaterializationResources : IComponentData
    {
        public CacheLoader Cache;
        public BlobAssetReference<RuntimeContentBlob> ContentBlob;
        public ModelPrefabDef[] ModelPrefabRecords;
        public MaterialRecord[] MaterialRecords;
        public Mesh[] Meshes;
        public UnityEngine.Material TerrainMaterial;
        public BatchMeshID[] RegisteredMeshes;
        public BatchMaterialID[] RegisteredRefMaterials;
        public BatchMaterialID[] RegisteredCombinedMaterials;
        public BatchMaterialID RegisteredTerrainMaterial;
        public Texture2DArray TerrainSplats;
        public NativeArray<int2> TexBucketInfo;
        public int2 FallbackBucketSlice;
        public int[] RefBucketKeys;
        public Dictionary<int, int> RefBucketIndexByKey;
        public int BlendVariantCount;
        public int CombinedRenderVariantCount;
        public BlobAssetReference<Collider>[] ColliderBlobs;
        public Entity[] ModelPrefabs;
        public RuntimeSpawnPrefabDescriptor[] SpawnableCreaturePrefabs;
        public RuntimeSpawnPrefabDescriptor[] SpawnableItemPrefabs;
        public RuntimeSpawnPrefabDescriptor[] SpawnableLightPrefabs;

        public bool TryGetRuntimeSpawnPrefab(ContentReference content, out RuntimeSpawnPrefabDescriptor descriptor)
        {
            descriptor = default;
            if (!content.IsValid)
                return false;

            switch (content.Kind)
            {
                case ContentReferenceKind.Actor:
                    return TryGetDescriptor(SpawnableCreaturePrefabs, content.HandleValue, out descriptor);
                case ContentReferenceKind.Item:
                    return TryGetDescriptor(SpawnableItemPrefabs, content.HandleValue, out descriptor);
                case ContentReferenceKind.Light:
                    return TryGetDescriptor(SpawnableLightPrefabs, content.HandleValue, out descriptor);
                default:
                    return false;
            }
        }

        public void ClearReferences()
        {
            Cache = null;
            ContentBlob = default;
            ModelPrefabRecords = null;
            MaterialRecords = null;
            Meshes = null;
            TerrainMaterial = null;
            RegisteredMeshes = null;
            RegisteredRefMaterials = null;
            RegisteredCombinedMaterials = null;
            RegisteredTerrainMaterial = default;
            TerrainSplats = null;
            TexBucketInfo = default;
            FallbackBucketSlice = default;
            RefBucketKeys = null;
            RefBucketIndexByKey = null;
            BlendVariantCount = 0;
            CombinedRenderVariantCount = 0;
            ColliderBlobs = null;
            ModelPrefabs = null;
            SpawnableCreaturePrefabs = null;
            SpawnableItemPrefabs = null;
            SpawnableLightPrefabs = null;
        }

        public UnityEngine.Object[] ResolveRenderObjectReferences(RuntimeRenderObjectReference[] references, string context)
        {
            if (references == null)
                throw new System.IO.InvalidDataException($"{context} has no render object reference table.");

            var objects = new UnityEngine.Object[references.Length];
            for (int i = 0; i < references.Length; i++)
            {
                var reference = references[i];
                objects[i] = reference.Kind switch
                {
                    RuntimeRenderObjectReferenceKind.Mesh => RequireObject(Meshes, reference.Index, $"{context} mesh"),
                    RuntimeRenderObjectReferenceKind.RefMaterial => RequireObject(Cache?.Materials, reference.Index, $"{context} ref material"),
                    RuntimeRenderObjectReferenceKind.CombinedMaterial => RequireObject(Cache?.CombinedMaterials, reference.Index, $"{context} combined material"),
                    RuntimeRenderObjectReferenceKind.TerrainMaterial => RequireTerrainMaterial(reference.Index, context),
                    _ => throw new System.IO.InvalidDataException($"{context} has unsupported render object reference kind {(int)reference.Kind}."),
                };
            }

            return objects;
        }

        static T RequireObject<T>(T[] objects, int index, string label)
            where T : UnityEngine.Object
        {
            if (objects == null || (uint)index >= (uint)objects.Length || objects[index] == null)
                throw new System.IO.InvalidDataException($"{label} reference {index} is missing; rebake required.");
            return objects[index];
        }

        UnityEngine.Object RequireTerrainMaterial(int index, string context)
        {
            if (index != 0)
                throw new System.IO.InvalidDataException($"{context} terrain material reference index {index} is invalid; rebake required.");
            if (TerrainMaterial == null)
                throw new System.IO.InvalidDataException($"{context} terrain material is not loaded.");
            return TerrainMaterial;
        }

        static bool TryGetDescriptor(RuntimeSpawnPrefabDescriptor[] descriptors, int handleValue, out RuntimeSpawnPrefabDescriptor descriptor)
        {
            descriptor = default;
            if (descriptors != null && (uint)(handleValue - 1) < (uint)descriptors.Length)
            {
                descriptor = descriptors[handleValue - 1];
                return descriptor.IsSupported;
            }
            return false;
        }

        public static RuntimeMaterializationResources Require(EntityManager entityManager)
        {
            var query = RuntimeMaterializationResourcesQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Materialization] RuntimeMaterializationResources singleton is missing.");

            var resources = entityManager.GetComponentData<RuntimeMaterializationResources>(query.GetSingletonEntity());
            if (resources == null)
                throw new InvalidOperationException("[VVardenfell][Materialization] RuntimeMaterializationResources is null.");
            return resources;
        }

        static class RuntimeMaterializationResourcesQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeMaterializationResources>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
