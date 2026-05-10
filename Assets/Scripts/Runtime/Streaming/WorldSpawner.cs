using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    public static class WorldSpawner
    {
        public static void SpawnInteriorCell(World world, Entity sectionEntity, float3 worldOffset, Entity transitionEntity, ref LogicalRefLookup logicalRefs)
        {
            WorldInteriorSpawnUtility.SpawnInteriorCell(world, sectionEntity, worldOffset, transitionEntity, ref logicalRefs);
            ActiveExplicitRefLookupLifecycleUtility.MarkDirty(world.EntityManager);
        }

        public static bool TrySpawnInteriorCellByHash(
            World world,
            ulong interiorCellHash,
            float3 worldOffset,
            Entity transitionEntity,
            ref LogicalRefLookup logicalRefs,
            out FixedString128Bytes interiorCellId)
        {
            interiorCellId = default;
            if (interiorCellHash == 0UL)
                return false;

            string cellId = WorldResources.ResolveInteriorCellId(interiorCellHash);
            if (string.IsNullOrWhiteSpace(cellId))
                return false;

            string path = CachePaths.InteriorCellSectionFile(cellId);
            var loaded = RuntimeCellSectionFile.LoadIntoWorld(world.EntityManager, path, isInterior: true, cellId: cellId);
            interiorCellId = loaded.Header.CellId;
            WorldResources.InteriorSectionEntitiesByHash[interiorCellHash] = loaded.SectionEntity;
            SpawnInteriorCell(world, loaded.SectionEntity, worldOffset, transitionEntity, ref logicalRefs);
            return true;
        }

        public static bool TryGetInteriorStaticCollider(ulong interiorCellHash, out Unity.Entities.BlobAssetReference<Unity.Physics.Collider> collider)
        {
            collider = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || interiorCellHash == 0UL)
                return false;
            if (!WorldResources.InteriorSectionEntitiesByHash.TryGetValue(interiorCellHash, out Entity sectionEntity))
                return false;
            var em = world.EntityManager;
            if (!em.Exists(sectionEntity) || !em.HasComponent<RuntimeCellSectionStaticCollider>(sectionEntity))
                return false;
            collider = em.GetComponentData<RuntimeCellSectionStaticCollider>(sectionEntity).Blob;
            return collider.IsCreated;
        }

        public static bool TrySpawnExteriorCellByCoord(
            World world,
            int2 coord,
            ref LoadedCellsMap loaded,
            ref LogicalRefLookup logicalRefs,
            bool active,
            bool gateTerrainByRadius)
        {
            string path = CachePaths.ExteriorCellSectionFile(coord.x, coord.y);
            var result = RuntimeCellSectionFile.LoadIntoWorld(world.EntityManager, path, isInterior: false);
            return SpawnExteriorCell(world, coord, result.SectionEntity, ref loaded, ref logicalRefs, active, gateTerrainByRadius);
        }

        public static bool SpawnExteriorCell(
            World world,
            int2 coord,
            Entity sectionEntity,
            ref LoadedCellsMap loaded,
            ref LogicalRefLookup logicalRefs,
            bool active,
            bool gateTerrainByRadius)
        {
            if (world == null || sectionEntity == Entity.Null)
                return false;

            var em = world.EntityManager;
            bool streamableAlreadySpawned = loaded.Streamed.IsCreated && loaded.Streamed.Contains(coord);
            Entity terrainEntity = Entity.Null;

            if (!streamableAlreadySpawned)
            {
                var header = em.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
                if ((header.Flags & CacheFormat.CellFlagHasTerrain) != 0)
                    terrainEntity = WorldTerrainStaticSpawnUtility.SpawnTerrainCell(em, coord, sectionEntity, active: false).Entity;

                if (em.HasComponent<RuntimeCellSectionStaticCollider>(sectionEntity))
                {
                    var staticBlob = em.GetComponentData<RuntimeCellSectionStaticCollider>(sectionEntity).Blob;
                    WorldTerrainStaticSpawnUtility.SpawnStaticCellCollider(em, coord, staticBlob, active);
                }

                Entity[] combinedChunkEntities = CombinedCellRenderSpawnUtility.SpawnChunks(em, coord, sectionEntity, active);
                int refCount = em.GetBuffer<RuntimeCellSectionRef>(sectionEntity).Length;
                if (refCount > 0)
                {
                    var spawnedRefEntities = new Entity[refCount];
                    var refArray = new NativeArray<RefEntry>(refCount, Allocator.Temp);
                    var coordArray = new NativeArray<int2>(refCount, Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < refCount; i++)
                        {
                            RefEntry entry = em.GetBuffer<RuntimeCellSectionRef>(sectionEntity)[i].Value;
                            refArray[i] = entry;
                            coordArray[i] = coord;
                            spawnedRefEntities[i] = WorldRefSpawnUtility.SpawnExteriorRef(em, entry, coord);
                        }

                        var contentBlob = WorldResources.Cache?.ContentBlob ?? default;
                        if (!contentBlob.IsCreated)
                            throw new System.InvalidOperationException("[VVardenfell][ContentBlob] WorldSpawner requires runtime content blob for logical refs.");
                        WorldRefSpawnUtility.BuildLogicalRefs(
                            em,
                            contentBlob,
                            refArray,
                            coordArray,
                            spawnedRefEntities,
                            false,
                            default,
                            float3.zero,
                            ref logicalRefs,
                            null,
                            out _);
                        CombinedCellRenderSpawnUtility.AttachMembershipLinks(em, sectionEntity, combinedChunkEntities, ref logicalRefs);
                    }
                    finally
                    {
                        coordArray.Dispose();
                        refArray.Dispose();
                    }
                    ActiveExplicitRefLookupLifecycleUtility.MarkDirty(em);
                }

                WorldResources.ExteriorSectionEntities[coord] = sectionEntity;
                if (loaded.Streamed.IsCreated)
                    loaded.Streamed.Add(coord);
            }

            if (loaded.Map.IsCreated && terrainEntity != Entity.Null)
                loaded.Map[coord] = terrainEntity;
            if (active && loaded.Active.IsCreated && loaded.Active.Add(coord))
                loaded.ActiveRevision++;
            if (loaded.SectionStates.IsCreated)
            {
                loaded.SectionStates[coord] = active
                    ? (byte)CellSectionLoadState.Active
                    : (byte)CellSectionLoadState.LoadedInactive;
            }
            if (active)
                WorldExteriorPhysicsUtility.SetCellPhysicsActive(em, coord, true);
            else if (!streamableAlreadySpawned)
                SetExteriorCellActiveState(em, coord, false, gateTerrainByRadius);

            return true;
        }

        public static void SetExteriorCellActiveState(EntityManager em, int2 coord, bool active, bool gateTerrainByRadius)
        {
            WorldExteriorVisibilityUtility.SetExteriorCellActiveState(em, coord, active, gateTerrainByRadius);
        }

        public static void HideExteriorVisibility(World world, ref LoadedCellsMap loaded)
        {
            WorldExteriorVisibilityUtility.HideExteriorVisibility(world, ref loaded);
        }

        public static void SyncExteriorVisibility(
            World world,
            in StreamingConfig config,
            in AvailableCells available,
            ref LoadedCellsMap loaded)
        {
            WorldExteriorVisibilityUtility.SyncExteriorVisibility(world, config, available, ref loaded);
        }
    }
}
