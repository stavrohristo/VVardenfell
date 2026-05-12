using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Importer.Bake
{
    internal static partial class WorldBakeService
    {
        static void WriteRuntimeDistantTerrain(string path, StagedCellData[] stagedCells)
        {
            if (stagedCells == null)
                throw new InvalidDataException("[VVardenfell][DistantTerrain] cannot build distant terrain without staged cells.");

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            int terrainCount = CountDistantTerrainCells(stagedCells);
            using var distantWorld = new World("VV.DistantTerrainBake");
            var em = distantWorld.EntityManager;

            Entity header = em.CreateEntity();
            em.AddComponentData(header, new RuntimeDistantTerrainPayloadHeader
            {
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                TerrainCount = terrainCount,
            });

            using var renderBake = new RendererBakeResources(MaxDistantTerrainMeshIndex(stagedCells), 0);
            for (int i = 0; i < stagedCells.Length; i++)
            {
                var staged = stagedCells[i];
                if (!ShouldBakeDistantTerrain(staged))
                    continue;
                CreateDistantTerrainEntity(em, staged, renderBake);
            }

            if (!TryValidateAuthoredDistantTerrain(em, terrainCount, out string error))
                throw new InvalidDataException($"Authored invalid distant terrain payload '{path}': {error}");

            using var writer = new MemoryBinaryWriter(em);
            SerializeUtility.SerializeWorld(em, writer, out var referencedObjects);
            if (referencedObjects.Length != 0)
                throw new InvalidDataException($"Distant terrain payload '{path}' serialized {referencedObjects.Length} Unity render object references; direct runtime render ID terrain must serialize none.");

            var bytes = new byte[writer.Length];
            unsafe
            {
                Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
            }
            RuntimeRenderObjectReferenceFile.WriteWrappedEntityWorld(path, bytes, referencedObjects, renderBake.ObjectReferences);
        }

        static void CreateDistantTerrainEntity(EntityManager em, StagedCellData staged, RendererBakeResources renderBake)
        {
            if (staged.TerrainMeshIndex < 0)
                throw new InvalidDataException($"{staged.WorkItem.Key} distant terrain has no baked render mesh index.");
            if (staged.TerrainSplatSlice < 0)
                throw new InvalidDataException($"{staged.WorkItem.Key} distant terrain has no baked splat slice.");

            int2 cell = new(SectionGridX(staged), SectionGridY(staged));
            GetTerrainLocalBounds(staged.Land, out float3 boundsCenter, out float3 boundsExtents);

            Entity entity = em.CreateEntity();
            em.AddComponent<RuntimeDistantTerrainTag>(entity);
            em.AddComponentData(entity, new CellCoord { Value = cell });
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                new float3(cell.x * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters, 0f, cell.y * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters),
                quaternion.identity,
                1f));
            em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
            em.AddComponent<Unity.Transforms.Static>(entity);
            em.AddComponentData(entity, new RuntimeCellSectionTerrainRenderResource
            {
                MeshIndex = staged.TerrainMeshIndex,
                SplatSlice = staged.TerrainSplatSlice,
                BoundsCenter = boundsCenter,
                BoundsExtents = boundsExtents,
            });

            RenderMeshUtility.AddComponents(
                entity,
                em,
                renderBake.RenderDesc,
                renderBake.TerrainRenderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, staged.TerrainMeshIndex));
            em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
            em.SetComponentData(entity, new RenderBounds
            {
                Value = new AABB
                {
                    Center = boundsCenter,
                    Extents = boundsExtents,
                }
            });
            em.AddComponentData(entity, new TerrainSplatSlice { Value = staged.TerrainSplatSlice });
            StripBakeOnlyRenderMeshArray(em, entity);
        }

        static bool TryValidateAuthoredDistantTerrain(EntityManager em, int expectedTerrainCount, out string error)
        {
            error = null;
            using var headerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeDistantTerrainPayloadHeader>());
            if (headerQuery.CalculateEntityCount() != 1)
            {
                error = "payload must contain exactly one distant terrain header.";
                return false;
            }

            Entity header = headerQuery.GetSingletonEntity();
            var headerData = em.GetComponentData<RuntimeDistantTerrainPayloadHeader>(header);
            if (headerData.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
            {
                error = $"pipeline version {headerData.PipelineVersion} does not match {CacheFormat.WorldBakePipelineVersion}.";
                return false;
            }
            if (headerData.TerrainCount != expectedTerrainCount)
            {
                error = $"header terrain count {headerData.TerrainCount} does not match expected {expectedTerrainCount}.";
                return false;
            }

            using var terrainQuery = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeDistantTerrainTag>(),
                    ComponentType.ReadOnly<RuntimeCellSectionTerrainRenderResource>(),
                    ComponentType.ReadOnly<CellCoord>(),
                    ComponentType.ReadOnly<RenderBounds>(),
                    ComponentType.ReadOnly<MaterialMeshInfo>(),
                    ComponentType.ReadOnly<TerrainSplatSlice>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            if (terrainQuery.CalculateEntityCount() != expectedTerrainCount)
            {
                error = $"terrain entity count {terrainQuery.CalculateEntityCount()} does not match expected {expectedTerrainCount}.";
                return false;
            }

            using var entities = terrainQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                {
                    error = "distant terrain entity serialized RenderMeshArray; direct render ID payloads must not.";
                    return false;
                }
                if (em.HasComponent<RuntimeCellSectionMember>(entity)
                    || em.HasComponent<RuntimeCellSectionTerrainCollider>(entity)
                    || em.HasComponent<RuntimeCellSectionStaticCollider>(entity)
                    || em.HasComponent<RuntimeColliderSource>(entity)
                    || em.HasComponent<LogicalRefTag>(entity)
                    || em.HasComponent<PlacedRefIdentity>(entity)
                    || em.HasComponent<MorrowindScriptInstance>(entity)
                    || em.HasComponent<ActorSpawnSource>(entity))
                {
                    error = "distant terrain entity contains gameplay, section, script, actor, or physics data.";
                    return false;
                }
            }

            return true;
        }

        static int CountDistantTerrainCells(StagedCellData[] stagedCells)
        {
            int count = 0;
            for (int i = 0; i < stagedCells.Length; i++)
            {
                if (ShouldBakeDistantTerrain(stagedCells[i]))
                    count++;
            }
            return count;
        }

        static int MaxDistantTerrainMeshIndex(StagedCellData[] stagedCells)
        {
            int max = 0;
            for (int i = 0; i < stagedCells.Length; i++)
            {
                if (ShouldBakeDistantTerrain(stagedCells[i]))
                    max = math.max(max, stagedCells[i].TerrainMeshIndex);
            }
            return max;
        }

        static bool ShouldBakeDistantTerrain(StagedCellData staged)
        {
            if (staged == null || staged.WorkItem.IsInterior)
                return false;
            return (BuildCellSectionFlags(staged) & CacheFormat.CellFlagHasTerrain) != 0;
        }
    }
}
