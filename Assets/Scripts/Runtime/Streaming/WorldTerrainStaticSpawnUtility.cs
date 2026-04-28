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
        internal static TerrainCellSpawnResult SpawnTerrainCell(EntityManager em, int2 coord, CellData data, bool active)
        {
            if (data == null || !data.HasTerrain)
                return default;

            var managed = new WorldResources.PerCellManaged();
            managed.TerrainMesh = BuildTerrainMesh(data);
            managed.TerrainMat = BuildTerrainMaterial(data);
            managed.SplatMap = (managed.TerrainMat != null && managed.TerrainMat != WorldResources.TerrainFallbackMat)
                ? managed.TerrainMat.GetTexture("_Splat") as Texture2D
                : null;
            managed.TerrainRma = new RenderMeshArray(
                new Material[] { managed.TerrainMat },
                new Mesh[] { managed.TerrainMesh });

            Entity terrainEntity = CreateTerrainEntity(em, coord, managed);
            byte terrainColliderCount = 0;
            if (WorldResources.TryGetTerrainCollider(coord, out var terrBlob))
            {
                RuntimeColliderAttachmentUtility.AttachSource(
                    em,
                    terrainEntity,
                    terrBlob,
                    RuntimeColliderKind.TerrainCell,
                    active);
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
            em.SetName(staticEntity, $"CellStatic({coord.x},{coord.y})");
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

        internal static bool TrySampleTerrainHeight(CellData data, float localX, float localZ, out float height)
        {
            const int N = 65;
            height = 0f;
            if (data?.Heights == null || data.Heights.Length < N * N)
                return false;

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float sampleX = math.clamp(localX / cellMeters * (N - 1), 0f, N - 1);
            float sampleZ = math.clamp(localZ / cellMeters * (N - 1), 0f, N - 1);
            int x0 = (int)math.floor(sampleX);
            int z0 = (int)math.floor(sampleZ);
            int x1 = math.min(x0 + 1, N - 1);
            int z1 = math.min(z0 + 1, N - 1);
            float tx = sampleX - x0;
            float tz = sampleZ - z0;

            float h00 = data.Heights[z0 * N + x0];
            float h10 = data.Heights[z0 * N + x1];
            float h01 = data.Heights[z1 * N + x0];
            float h11 = data.Heights[z1 * N + x1];
            height = math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
            return true;
        }

        static Entity CreateTerrainEntity(EntityManager em, int2 coord, WorldResources.PerCellManaged managed)
        {
            Entity terrainEntity = em.CreateEntity();
            em.SetName(terrainEntity, $"Terrain({coord.x},{coord.y})");
            RenderMeshUtility.AddComponents(
                terrainEntity,
                em,
                WorldResources.Desc,
                managed.TerrainRma,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

            float ox = coord.x * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float oz = coord.y * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            em.AddComponentData(terrainEntity, LocalTransform.FromPositionRotationScale(
                new float3(ox, 0, oz),
                quaternion.identity,
                1f));

            float cellHalf = LandRecordSize.CellUnitsMw * 0.5f * WorldScale.MwUnitsToMeters;
            em.SetComponentData(terrainEntity, new RenderBounds
            {
                Value = new AABB
                {
                    Center = new float3(cellHalf, 0f, cellHalf),
                    Extents = new float3(cellHalf, 1000f, cellHalf),
                }
            });

            em.AddComponentData(terrainEntity, new CellCoord { Value = coord });
            em.AddComponentData(terrainEntity, new CellLink { Value = coord });
            WorldResources.RegisterExteriorCellEntity(coord, terrainEntity);
            em.AddComponent<Unity.Transforms.Static>(terrainEntity);
            return terrainEntity;
        }

        static Material BuildTerrainMaterial(CellData data)
        {
            if (WorldResources.TerrainShader == null
                || WorldResources.TerrainTemplate == null
                || WorldResources.Cache?.TerrainLayers == null
                || WorldResources.Cache.TerrainLayers.Array == null
                || data.LayerGrid == null)
            {
                return WorldResources.TerrainFallbackMat;
            }

            var mat = new Material(WorldResources.TerrainTemplate)
            {
                name = $"VV:Terrain({data.GridX},{data.GridY})",
            };
            mat.SetTexture("_LayerArray", WorldResources.Cache.TerrainLayers.Array);

            var splat = new Texture2D(16, 16, TextureFormat.R16, mipChain: false, linear: true)
            {
                name = $"VV:Splat({data.GridX},{data.GridY})",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            splat.SetPixelData(data.LayerGrid, 0);
            splat.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            mat.SetTexture("_Splat", splat);
            return mat;
        }

        static Mesh BuildTerrainMesh(CellData data)
        {
            const int N = 65;
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
                    verts[i] = new Vector3(x * spacingU, data.Heights[i], y * spacingU);
                    uvs[i] = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
                    if (data.Normals != null)
                    {
                        float nx = data.Normals[i * 3 + 0] / 127f;
                        float ny = data.Normals[i * 3 + 1] / 127f;
                        float nz = data.Normals[i * 3 + 2] / 127f;
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

            var mesh = new Mesh { name = $"Terrain({data.GridX},{data.GridY})" };
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            if (data.Normals != null)
                mesh.SetNormals(normals);
            else
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
