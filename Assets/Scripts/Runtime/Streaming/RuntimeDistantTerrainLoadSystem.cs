using System;
using System.IO;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    public partial class RuntimeDistantTerrainLoadSystem : SystemBase
    {
        EntityQuery _headerQuery;
        EntityQuery _terrainQuery;

        protected override void OnCreate()
        {
            _headerQuery = GetEntityQuery(ComponentType.ReadOnly<RuntimeDistantTerrainPayloadHeader>());
            _terrainQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeDistantTerrainTag>(),
                    ComponentType.ReadOnly<RuntimeCellSectionTerrainRenderResource>(),
                    ComponentType.ReadOnly<CellCoord>(),
                    ComponentType.ReadOnly<RenderBounds>(),
                    ComponentType.ReadWrite<MaterialMeshInfo>(),
                    ComponentType.ReadOnly<TerrainSplatSlice>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
        }

        protected override void OnUpdate()
        {
        }

        public void LoadAndMaterialize(RuntimeMaterializationResources resources)
        {
            if (_headerQuery.CalculateEntityCount() > 0)
            {
                ValidateLoadedPayload(EntityManager, CachePaths.RuntimeDistantTerrain);
                return;
            }

            if (!File.Exists(CachePaths.RuntimeDistantTerrain))
                throw new FileNotFoundException($"Runtime distant terrain payload '{CachePaths.RuntimeDistantTerrain}' does not exist; rebake required.", CachePaths.RuntimeDistantTerrain);

            RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorldHeader(
                CachePaths.RuntimeDistantTerrain,
                out var renderReferences,
                out long payloadOffset,
                out int payloadLength);
            if (renderReferences.Length != 0)
                throw new InvalidDataException($"Runtime distant terrain payload '{CachePaths.RuntimeDistantTerrain}' has {renderReferences.Length} render object references; rebake required.");
            var unityObjects = resources.ResolveRenderObjectReferences(renderReferences, $"Runtime distant terrain '{CachePaths.RuntimeDistantTerrain}'");
            using var reader = new RuntimeCellSectionPayloadBinaryReader(CachePaths.RuntimeDistantTerrain, payloadOffset, payloadLength);
            using (var distantWorld = DeserializeToTempWorld(CachePaths.RuntimeDistantTerrain, reader, unityObjects))
                EntityManager.MoveEntitiesFrom(distantWorld.EntityManager);

            ValidateLoadedPayload(EntityManager, CachePaths.RuntimeDistantTerrain);
            RuntimeRenderIdBindingUtility.BindDistantTerrain(EntityManager, resources, _terrainQuery);
        }

        static World DeserializeToTempWorld(string path, RuntimeCellSectionPayloadBinaryReader reader, object[] unityObjects)
        {
            var world = new World($"VV.DistantTerrainLoad({Path.GetFileName(path)})");
            try
            {
                var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                SerializeUtility.DeserializeWorld(tx, reader, unityObjects);
                world.EntityManager.EndExclusiveEntityTransaction();
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        static void ValidateLoadedPayload(EntityManager em, string path)
        {
            using var headerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeDistantTerrainPayloadHeader>());
            int headerCount = headerQuery.CalculateEntityCount();
            if (headerCount != 1)
                throw new InvalidDataException($"Runtime distant terrain payload '{path}' has {headerCount} headers; expected exactly one.");

            Entity header = headerQuery.GetSingletonEntity();
            var headerData = em.GetComponentData<RuntimeDistantTerrainPayloadHeader>(header);
            if (headerData.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"Runtime distant terrain payload '{path}' pipeline {headerData.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebake required.");

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
            int terrainCount = terrainQuery.CalculateEntityCount();
            if (terrainCount != headerData.TerrainCount)
                throw new InvalidDataException($"Runtime distant terrain payload '{path}' has {terrainCount} terrain entities; header expects {headerData.TerrainCount}.");

            using var entities = terrainQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                    throw new InvalidDataException($"Runtime distant terrain payload '{path}' serialized RenderMeshArray; rebake required.");
                if (em.HasComponent<RuntimeCellSectionMember>(entity)
                    || em.HasComponent<RuntimeCellSectionTerrainCollider>(entity)
                    || em.HasComponent<RuntimeCellSectionStaticCollider>(entity)
                    || em.HasComponent<RuntimeColliderSource>(entity)
                    || em.HasComponent<LogicalRefTag>(entity)
                    || em.HasComponent<PlacedRefIdentity>(entity)
                    || em.HasComponent<MorrowindScriptInstance>(entity)
                    || em.HasComponent<ActorSpawnSource>(entity))
                {
                    throw new InvalidDataException($"Runtime distant terrain payload '{path}' contains gameplay, section, script, actor, or physics data.");
                }
            }
        }
    }
}
