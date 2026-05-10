using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Material = UnityEngine.Material;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    internal struct TerrainCellSpawnResult
    {
        public Entity Entity;
        public byte BuiltTerrain;
        public byte TerrainColliderCount;
    }

    internal static class WorldTerrainStaticSpawnUtility
    {
        internal static TerrainCellSpawnResult SpawnTerrainCell(EntityManager em, int2 coord, Entity sectionEntity, bool active)
        {
            if (sectionEntity == Entity.Null || !em.Exists(sectionEntity))
                return default;
            var header = em.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
            if ((header.Flags & CacheFormat.CellFlagHasTerrain) == 0)
                return default;

            var managed = WorldResources.LoadedManaged.TryGetValue(coord, out var existingManaged)
                ? existingManaged
                : new WorldResources.PerCellManaged();
            managed.TerrainMesh = BuildTerrainMesh(em, sectionEntity, coord);
            managed.TerrainMat = BuildTerrainMaterial(em, sectionEntity, coord);
            managed.SplatMap = (managed.TerrainMat != null && managed.TerrainMat != WorldResources.TerrainFallbackMat)
                ? managed.TerrainMat.GetTexture("_Splat") as Texture2D
                : null;
            managed.TerrainRma = new RenderMeshArray(
                new Material[] { managed.TerrainMat },
                new Mesh[] { managed.TerrainMesh });

            Entity terrainEntity = CreateTerrainEntity(em, coord, managed, active);
            byte terrainColliderCount = 0;
            if (em.HasComponent<RuntimeCellSectionTerrainCollider>(sectionEntity))
            {
                var terrBlob = em.GetComponentData<RuntimeCellSectionTerrainCollider>(sectionEntity).Blob;
                RuntimeColliderAttachmentUtility.AttachSource(
                    em,
                    terrainEntity,
                    terrBlob,
                    RuntimeColliderKind.TerrainCell,
                    active);
                CreateTerrainPickEntity(em, coord, terrBlob, active);
                terrainColliderCount = 1;
            }

            WorldResources.LoadedManaged[coord] = managed;
            return new TerrainCellSpawnResult
            {
                Entity = terrainEntity,
                BuiltTerrain = 1,
                TerrainColliderCount = terrainColliderCount,
            };
        }

        internal static Entity SpawnStaticCellCollider(EntityManager em, int2 coord, BlobAssetReference<Collider> blob, bool active)
        {
            if (!blob.IsCreated)
                return Entity.Null;

            var staticEntity = em.CreateEntity();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            em.AddComponentData(staticEntity, LocalTransform.FromPositionRotationScale(
                new float3(coord.x * cellMeters, 0f, coord.y * cellMeters),
                quaternion.identity,
                1f));
            em.AddComponentData(staticEntity, new LocalToWorld
            {
                Value = float4x4.Translate(new float3(coord.x * cellMeters, 0f, coord.y * cellMeters))
            });
            em.AddComponent<Unity.Transforms.Static>(staticEntity);
            em.AddComponentData(staticEntity, new CellLink { Value = coord });
            WorldResources.RegisterExteriorCellEntity(coord, staticEntity);
            RuntimeColliderAttachmentUtility.AttachSource(
                em,
                staticEntity,
                blob,
                RuntimeColliderKind.StaticCell,
                active);
            return staticEntity;
        }

        static Entity CreateTerrainEntity(EntityManager em, int2 coord, WorldResources.PerCellManaged managed, bool active)
        {
            Entity terrainEntity = em.CreateEntity();
            RenderMeshUtility.AddComponents(
                terrainEntity,
                em,
                WorldResources.Desc,
                managed.TerrainRma,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.SetComponentEnabled<MaterialMeshInfo>(terrainEntity, active);

            float ox = coord.x * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = coord.y * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            em.AddComponentData(terrainEntity, LocalTransform.FromPositionRotationScale(
                new float3(ox, 0, oz),
                quaternion.identity,
                1f));

            Bounds meshBounds = managed.TerrainMesh.bounds;
            em.SetComponentData(terrainEntity, new RenderBounds
            {
                Value = new AABB
                {
                    Center = new float3(meshBounds.center.x, meshBounds.center.y, meshBounds.center.z),
                    Extents = new float3(meshBounds.extents.x, meshBounds.extents.y, meshBounds.extents.z),
                }
            });

            em.AddComponentData(terrainEntity, new CellCoord { Value = coord });
            em.AddComponentData(terrainEntity, new CellLink { Value = coord });
            WorldResources.RegisterExteriorCellEntity(coord, terrainEntity);
            em.AddComponent<Unity.Transforms.Static>(terrainEntity);
            return terrainEntity;
        }

        static Entity CreateTerrainPickEntity(EntityManager em, int2 coord, BlobAssetReference<Collider> terrainBlob, bool active)
        {
            if (!terrainBlob.IsCreated)
                return Entity.Null;

            Entity pickEntity = em.CreateEntity();
            float ox = coord.x * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = coord.y * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            em.AddComponentData(pickEntity, LocalTransform.FromPositionRotationScale(
                new float3(ox, 0f, oz),
                quaternion.identity,
                1f));
            em.AddComponentData(pickEntity, new LocalToWorld
            {
                Value = float4x4.Translate(new float3(ox, 0f, oz))
            });
            em.AddComponent<Unity.Transforms.Static>(pickEntity);
            em.AddComponentData(pickEntity, new CellLink { Value = coord });
            em.AddComponent<InteractionPickSurfaceTag>(pickEntity);
            WorldResources.RegisterExteriorCellEntity(coord, pickEntity);

            var pickBlob = terrainBlob.Value.Clone();
            RuntimeColliderAttachmentUtility.AttachSource(
                em,
                pickEntity,
                pickBlob,
                RuntimeColliderKind.InteractionPick,
                active,
                temporary: true);
            return pickEntity;
        }

        static Material BuildTerrainMaterial(EntityManager em, Entity sectionEntity, int2 coord)
        {
            if (WorldResources.TerrainShader == null
                || WorldResources.TerrainTemplate == null
                || WorldResources.Cache?.TerrainLayers == null
                || WorldResources.Cache.TerrainLayers.Array == null
                || WorldResources.Cache.TerrainLayers.LayerMeta0 == null
                || WorldResources.Cache.TerrainLayers.LayerMeta1 == null
                || !em.HasBuffer<RuntimeCellSectionTerrainLayer>(sectionEntity))
            {
                return WorldResources.TerrainFallbackMat;
            }

            var mat = new Material(WorldResources.TerrainTemplate)
            {
                name = $"VV:Terrain({coord.x},{coord.y})",
            };
            mat.SetTexture("_LayerArray", WorldResources.Cache.TerrainLayers.Array);
            mat.SetTexture("_LayerMeta0", WorldResources.Cache.TerrainLayers.LayerMeta0);
            mat.SetTexture("_LayerMeta1", WorldResources.Cache.TerrainLayers.LayerMeta1);

            var splat = new Texture2D(16, 16, TextureFormat.R16, mipChain: false, linear: true)
            {
                name = $"VV:Splat({coord.x},{coord.y})",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var layerBuffer = em.GetBuffer<RuntimeCellSectionTerrainLayer>(sectionEntity);
            var layers = new ushort[layerBuffer.Length];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = layerBuffer[i].Value;
            splat.SetPixelData(layers, 0);
            splat.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            mat.SetTexture("_Splat", splat);
            return mat;
        }

        static Mesh BuildTerrainMesh(EntityManager em, Entity sectionEntity, int2 coord)
        {
            const int N = 65;
            var heightBuffer = em.GetBuffer<RuntimeCellSectionTerrainHeight>(sectionEntity);
            DynamicBuffer<RuntimeCellSectionTerrainNormal> normalBuffer = default;
            bool hasNormals = em.HasBuffer<RuntimeCellSectionTerrainNormal>(sectionEntity);
            if (hasNormals)
                normalBuffer = em.GetBuffer<RuntimeCellSectionTerrainNormal>(sectionEntity);
            float spacingMw = LandRecordSize.CellUnitsMw / (float)(N - 1);
            float spacingU = spacingMw * WorldScale.MwUnitsToMeters;

            var verts = new Vector3[N * N];
            var uvs = new Vector2[N * N];
            var normals = new Vector3[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    verts[i] = new Vector3(x * spacingU, heightBuffer[i].Value, y * spacingU);
                    uvs[i] = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
                    if (hasNormals)
                    {
                        float nx = normalBuffer[i * 3 + 0].Value / 127f;
                        float ny = normalBuffer[i * 3 + 1].Value / 127f;
                        float nz = normalBuffer[i * 3 + 2].Value / 127f;
                        normals[i] = new Vector3(nx, nz, ny).normalized;
                    }
                }
            }

            var tris = new int[(N - 1) * (N - 1) * 6];
            int t = 0;
            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    int v00 = y * N + x;
                    int v10 = y * N + x + 1;
                    int v01 = (y + 1) * N + x;
                    int v11 = (y + 1) * N + x + 1;
                    tris[t++] = v00; tris[t++] = v01; tris[t++] = v10;
                    tris[t++] = v10; tris[t++] = v01; tris[t++] = v11;
                }
            }

            var mesh = new Mesh { name = $"Terrain({coord.x},{coord.y})" };
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            if (hasNormals)
                mesh.SetNormals(normals);
            else
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
