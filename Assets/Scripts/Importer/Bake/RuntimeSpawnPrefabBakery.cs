using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    internal static class RuntimeSpawnPrefabBakery
    {
        public static void Write(string path, ModelPrefabCatalogData catalog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var records = catalog?.Records ?? Array.Empty<ModelPrefabDef>();

            using var prefabWorld = new World("VV.RuntimeSpawnPrefabBake");
            var em = prefabWorld.EntityManager;

            Entity header = em.CreateEntity();
            em.AddComponentData(header, new RuntimeSpawnPrefabCacheHeader
            {
                FormatVersion = CacheFormat.FormatVersion,
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                ModelPrefabCount = records.Length,
            });

            for (int i = 0; i < records.Length; i++)
                BuildPrefab(em, records[i], i);

            using (var writer = new MemoryBinaryWriter(em))
            {
                SerializeUtility.SerializeWorld(em, writer);
                var bytes = new byte[writer.Length];
                unsafe
                {
                    Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
                }
                File.WriteAllBytes(path, bytes);
            }

            Validate(path, records.Length);
        }

        static void BuildPrefab(EntityManager em, ModelPrefabDef def, int modelPrefabIndex)
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
            }

            int rootIndex = math.clamp(def.RootNodeIndex, 0, entities.Length - 1);
            Entity root = entities[rootIndex];
            em.AddComponentData(root, new RuntimeSpawnPrefabRoot
            {
                ModelPrefabIndex = modelPrefabIndex,
                CollisionIndex = def.CollisionIndex,
            });

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

                    if (node.PickColliderIndex >= 0)
                    {
                        em.AddComponentData(entity, new RuntimeSpawnPrefabPickCollider
                        {
                            ColliderIndex = node.PickColliderIndex,
                        });
                    }
                }
            }
        }

        static void Validate(string path, int expectedModelPrefabCount)
        {
            using var world = new World("VV.RuntimeSpawnPrefabValidate");
            byte[] bytes = File.ReadAllBytes(path);
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                    var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                    SerializeUtility.DeserializeWorld(tx, reader);
                    world.EntityManager.EndExclusiveEntityTransaction();
                }
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
        }
    }
}
