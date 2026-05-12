using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    static class RuntimeSpawnProjectionUtility
    {
        static readonly float3 InteriorWorldOffset = float3.zero;

        public struct RestoreAliveRefsProjection
        {
            public Entity StreamingEntity;
            public Entity TransitionEntity;
            public Entity RegistryEntity;
            public RuntimeSpawnedRef[] Snapshot;
            public RestoreAliveRefMaterialization[] Materializations;
            public LogicalRefLookup LogicalLookup;
            public LoadedCellsMap Loaded;
            public InteriorTransitionState Transition;
            public AvailableCells Available;
            public bool ChangedAvailable;
        }

        public struct RestoreAliveRefMaterialization
        {
            public int SnapshotIndex;
            public uint RuntimeRefId;
            public bool IsInterior;
            public int2 ExteriorCell;
            public bool ExteriorActive;
        }

        public static bool TryRestoreAliveRefsForCurrentWorld(
            EntityManager entityManager)
        {
            var contentBlob = RequireRuntimeContentBlob(entityManager);
            ref RuntimeContentBlob content = ref contentBlob.Value;
            var worldCellBlob = RequireRuntimeWorldCellBlob(entityManager);
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            var materializationResources = RuntimeMaterializationResources.Require(entityManager);
            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            if (!TryQueueRestoreAliveRefsCreatePhase(
                    entityManager,
                    ref content,
                    ref worldCells,
                    materializationResources,
                    ref createEcb,
                    out var projection))
            {
                createEcb.Dispose();
                return false;
            }

            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref createEcb);

            var materializeEcb = new EntityCommandBuffer(Allocator.Temp);
            QueueRestoreAliveRefsMaterializePhase(
                entityManager,
                ref materializeEcb,
                ref projection);
            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref materializeEcb);
            ApplyRestoreAliveRefsProjection(entityManager, projection);
            return true;
        }

        public static void MarkUnloaded(EntityManager entityManager, uint runtimeRefId)
        {
            if (!RuntimeSpawnRegistryUtility.IsRuntimeRefId(runtimeRefId))
                return;

            if (!TryGetRegistryEntity(entityManager, out Entity registryEntity))
                return;

            var registry = entityManager.GetBuffer<RuntimeSpawnedRef>(registryEntity);
            for (int i = 0; i < registry.Length; i++)
            {
                var entry = registry[i];
                if (entry.RuntimeRefId != runtimeRefId)
                    continue;

                entry.LogicalEntity = Entity.Null;
                registry[i] = entry;
                return;
            }
        }

        public static bool MarkDestroyed(EntityManager entityManager, uint runtimeRefId)
        {
            if (!RuntimeSpawnRegistryUtility.IsRuntimeRefId(runtimeRefId))
                return false;

            if (!TryGetRegistryEntity(entityManager, out Entity registryEntity))
                return false;

            var registry = entityManager.GetBuffer<RuntimeSpawnedRef>(registryEntity);
            for (int i = 0; i < registry.Length; i++)
            {
                var entry = registry[i];
                if (entry.RuntimeRefId != runtimeRefId)
                    continue;

                if (entry.Alive == 0)
                    return false;

                entry.Alive = 0;
                entry.LogicalEntity = Entity.Null;
                registry[i] = entry;
                return true;
            }

            return false;
        }

        public static bool TryQueueRestoreAliveRefsCreatePhase(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            ref RuntimeWorldCellBlob worldCells,
            RuntimeMaterializationResources materializationResources,
            ref EntityCommandBuffer ecb,
            out RestoreAliveRefsProjection projection)
        {
            projection = default;
            if (!TryGetRegistryEntity(entityManager, out Entity registryEntity))
                return false;

            Entity streamingEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LogicalRefLookup>(entityManager);
            Entity transitionEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteriorTransitionState>(entityManager);
            if (streamingEntity == Entity.Null || transitionEntity == Entity.Null)
                return false;

            var logicalLookup = entityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var available = entityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = entityManager.GetComponentData<LoadedCellsMap>(streamingEntity);
            var config = entityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var registry = entityManager.GetBuffer<RuntimeSpawnedRef>(registryEntity);
            var snapshot = new RuntimeSpawnedRef[registry.Length];
            for (int i = 0; i < registry.Length; i++)
                snapshot[i] = registry[i];

            bool changedAvailable = false;
            var materializations = new RestoreAliveRefMaterialization[snapshot.Length];
            int materializationCount = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                if (entry.IsInterior != 0 && entry.InteriorCellHash == 0UL)
                {
                    entry.InteriorCellHash = InteriorCellIdHash.Hash(entry.InteriorCellId);
                    snapshot[i] = entry;
                }

                if (entry.Alive == 0)
                {
                    entry.LogicalEntity = Entity.Null;
                    snapshot[i] = entry;
                    continue;
                }

                if (entry.IsInterior == 0)
                {
                    if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, entry.ExteriorCell, out _))
                        throw new System.InvalidOperationException($"[VVardenfell][Save] alive runtime ref 0x{entry.RuntimeRefId:X8} references missing exterior cell ({entry.ExteriorCell.x},{entry.ExteriorCell.y}).");

                    EnsureExteriorCapacity(ref available);
                    available.Set.Add(entry.ExteriorCell);
                    changedAvailable = true;
                }
                else if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, entry.InteriorCellHash, out _))
                {
                    throw new System.InvalidOperationException($"[VVardenfell][Save] alive runtime ref 0x{entry.RuntimeRefId:X8} references missing interior hash 0x{entry.InteriorCellHash:X16}.");
                }

                bool shouldSpawn = entry.IsInterior == 0
                    || (transition.InteriorActive != 0 && entry.InteriorCellHash == transition.ActiveInteriorCellHash);
                if (!shouldSpawn)
                {
                    entry.LogicalEntity = Entity.Null;
                    snapshot[i] = entry;
                    continue;
                }

                if (entry.LogicalEntity != Entity.Null && entityManager.Exists(entry.LogicalEntity))
                    continue;

                bool actorSpawn = entry.Content.Kind == ContentReferenceKind.Actor;
                RuntimeSpawnPrefabDescriptor descriptor = default;
                if (!actorSpawn && !materializationResources.TryGetRuntimeSpawnPrefab(entry.Content, out descriptor))
                {
                    Debug.LogWarning($"[VVardenfell][Save] skipped runtime ref 0x{entry.RuntimeRefId:X8}: no spawnable prefab descriptor for {entry.Content.Kind}:{entry.Content.HandleValue}.");
                    continue;
                }

                bool exteriorActive = entry.IsInterior != 0 || IsExteriorCellActive(config, entry.ExteriorCell);
                bool queued = actorSpawn
                    ? RuntimeSpawnFactory.QueueActorSpawn(
                        entityManager,
                        ref ecb,
                        ref content,
                        entry.Content,
                        entry.RuntimeRefId,
                        entry.Position,
                        entry.Rotation,
                        math.max(0.0001f, entry.Scale),
                        entry.IsInterior != 0,
                        entry.ExteriorCell,
                        entry.InteriorCellId,
                        entry.PersistencePolicy)
                    : RuntimeSpawnFactory.QueueSpawn(
                        entityManager,
                        ref ecb,
                        ref content,
                        materializationResources,
                        descriptor,
                        entry.Content,
                        entry.RuntimeRefId,
                        entry.Position,
                        entry.Rotation,
                        math.max(0.0001f, entry.Scale),
                        entry.IsInterior != 0,
                        entry.ExteriorCell,
                        entry.InteriorCellId,
                        exteriorActive,
                        ref logicalLookup,
                        transitionEntity,
                        entry.PersistencePolicy);

                if (!queued)
                    continue;

                materializations[materializationCount++] = new RestoreAliveRefMaterialization
                {
                    SnapshotIndex = i,
                    RuntimeRefId = entry.RuntimeRefId,
                    IsInterior = entry.IsInterior != 0,
                    ExteriorCell = entry.ExteriorCell,
                    ExteriorActive = exteriorActive,
                };
            }

            var compactMaterializations = new RestoreAliveRefMaterialization[materializationCount];
            if (materializationCount > 0)
                System.Array.Copy(materializations, compactMaterializations, materializationCount);
            projection = new RestoreAliveRefsProjection
            {
                StreamingEntity = streamingEntity,
                TransitionEntity = transitionEntity,
                RegistryEntity = registryEntity,
                Snapshot = snapshot,
                Materializations = compactMaterializations,
                LogicalLookup = logicalLookup,
                Loaded = loaded,
                Transition = transition,
                Available = available,
                ChangedAvailable = changedAvailable,
            };
            return true;
        }

        public static void QueueRestoreAliveRefsMaterializePhase(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            ref RestoreAliveRefsProjection projection)
        {
            if (projection.Materializations == null)
                return;

            for (int i = 0; i < projection.Materializations.Length; i++)
            {
                var materialization = projection.Materializations[i];
                Entity logicalEntity = RuntimeSpawnFactory.QueueMaterializeSpawn(
                    entityManager,
                    ref ecb,
                    materialization.RuntimeRefId,
                    materialization.IsInterior,
                    materialization.ExteriorCell,
                    materialization.ExteriorActive,
                    ref projection.LogicalLookup,
                    projection.TransitionEntity);

                if ((uint)materialization.SnapshotIndex >= (uint)projection.Snapshot.Length)
                    continue;

                var entry = projection.Snapshot[materialization.SnapshotIndex];
                entry.LogicalEntity = logicalEntity;
                projection.Snapshot[materialization.SnapshotIndex] = entry;
                ActiveExplicitRefLookupLifecycleUtility.QueueDynamicAddIfActive(
                    entityManager,
                    logicalEntity,
                    projection.Loaded,
                    projection.Transition);
            }
        }

        public static void ApplyRestoreAliveRefsProjection(
            EntityManager entityManager,
            in RestoreAliveRefsProjection projection)
        {
            if (projection.RegistryEntity == Entity.Null || projection.Snapshot == null)
                return;

            if (projection.ChangedAvailable)
                entityManager.SetComponentData(projection.StreamingEntity, projection.Available);

            entityManager.SetComponentData(projection.StreamingEntity, projection.LogicalLookup);

            var registry = entityManager.GetBuffer<RuntimeSpawnedRef>(projection.RegistryEntity);
            registry.Clear();
            for (int i = 0; i < projection.Snapshot.Length; i++)
                registry.Add(projection.Snapshot[i]);
        }

        public static bool TryRestoreWorldLocation(
            World world,
            EntityManager entityManager,
            in WorldSavePayload payload,
            out string error)
        {
            error = null;
            Entity streamingEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LogicalRefLookup>(entityManager);
            Entity transitionEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteractionRuntimeState>(entityManager);
            if (streamingEntity == Entity.Null || transitionEntity == Entity.Null || runtimeEntity == Entity.Null)
            {
                error = "Required world streaming state is not ready for save replay.";
                return false;
            }

            var config = entityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var available = entityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = entityManager.GetComponentData<LoadedCellsMap>(streamingEntity);
            var sectionRegistry = entityManager.GetComponentData<RuntimeSectionRegistry>(streamingEntity);
            var logicalLookup = entityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var interiorSections = RequireInteriorSections(world);
            var exteriorSections = RequireExteriorSections(world);

            if (payload.InteriorActive && !string.IsNullOrWhiteSpace(payload.ActiveInteriorCellId))
            {
                ulong activeInteriorCellHash = InteriorCellIdHash.Hash(payload.ActiveInteriorCellId);
                var worldCellBlob = RequireRuntimeWorldCellBlob(entityManager);
                ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
                if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, activeInteriorCellHash, out _))
                {
                    error = $"Continue save references missing interior '{payload.ActiveInteriorCellId}'.";
                    return false;
                }

                interiorSections.DeactivateActiveInterior(transition);
                DestroyTransientInteriorEntities(entityManager, transitionEntity, ref logicalLookup);
                exteriorSections.HideExteriorVisibility(ref loaded);
                if (!interiorSections.LoadAndActivateByHash(activeInteriorCellHash, InteriorWorldOffset, ref sectionRegistry, ref logicalLookup, out FixedString128Bytes spawnedInteriorCellId))
                {
                    error = $"Continue save references unloaded interior '{payload.ActiveInteriorCellId}'.";
                    return false;
                }
                config.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = spawnedInteriorCellId.IsEmpty
                    ? RuntimeFixedStringUtility.ToFixed128OrDefault(payload.ActiveInteriorCellId)
                    : spawnedInteriorCellId;
                transition.ActiveInteriorCellHash = activeInteriorCellHash;
                transition.TransitionInProgress = 0;

                var interactionState = entityManager.GetComponentData<InteractionRuntimeState>(runtimeEntity);
                interactionState.PendingPickedItemPrune = 1;
                entityManager.SetComponentData(runtimeEntity, interactionState);
            }
            else
            {
                interiorSections.DeactivateActiveInterior(transition);
                DestroyTransientInteriorEntities(entityManager, transitionEntity, ref logicalLookup);
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
                transition.ActiveInteriorCellHash = 0UL;
                transition.TransitionInProgress = 0;
                config.ExteriorStreamingPaused = false;
                config.CameraCell = ComputeExteriorCell(payload.PlayerPosition);
                exteriorSections.SyncExteriorVisibility(config, available, ref loaded);
            }

            entityManager.SetComponentData(streamingEntity, config);
            entityManager.SetComponentData(streamingEntity, available);
            entityManager.SetComponentData(streamingEntity, sectionRegistry);
            entityManager.SetComponentData(streamingEntity, loaded);
            entityManager.SetComponentData(streamingEntity, logicalLookup);
            entityManager.SetComponentData(transitionEntity, transition);
            return true;
        }

        static void DestroyTransientInteriorEntities(EntityManager entityManager, Entity transitionEntity, ref LogicalRefLookup logicalLookup)
        {
            var spawnedBuffer = entityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            if (spawnedBuffer.Length == 0)
                return;

            var entitiesToDestroy = new NativeArray<Entity>(spawnedBuffer.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < spawnedBuffer.Length; i++)
                    entitiesToDestroy[i] = spawnedBuffer[i].Value;

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entitiesToDestroy.Length; i++)
                    {
                        Entity entity = entitiesToDestroy[i];
                        if (entityManager.Exists(entity)
                            && entityManager.HasComponent<RuntimeCellSectionMember>(entity))
                        {
                            continue;
                        }

                        if (entityManager.Exists(entity) && entityManager.HasComponent<LogicalRefTag>(entity))
                        {
                            LogicalRefDestroyUtility.QueueDestroyLogicalRef(
                                entityManager,
                                ref ecb,
                                entity,
                                ref logicalLookup,
                                preserveRuntimeSpawnRegistration: true);
                            continue;
                        }

                        if (entityManager.Exists(entity))
                            ecb.DestroyEntity(entity);
                    }

                    ecb.Playback(entityManager);
                }
                finally
                {
                    ecb.Dispose();
                }
            }
            finally
            {
                entitiesToDestroy.Dispose();
            }

            spawnedBuffer.Clear();
        }

        static bool TryGetRegistryEntity(EntityManager entityManager, out Entity registryEntity)
        {
            registryEntity = Entity.Null;
            EntityQuery query = RegistryQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
                return false;

            registryEntity = query.GetSingletonEntity();
            return true;
        }

        static BlobAssetReference<RuntimeContentBlob> RequireRuntimeContentBlob(EntityManager entityManager)
        {
            EntityQuery query = RuntimeContentBlobQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Runtime spawn projection requires runtime content blob.");

            var blob = entityManager.GetComponentData<RuntimeContentBlobReference>(query.GetSingletonEntity()).Blob;
            if (!blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Runtime spawn projection content blob is not created.");
            return blob;
        }

        static BlobAssetReference<RuntimeWorldCellBlob> RequireRuntimeWorldCellBlob(EntityManager entityManager)
        {
            EntityQuery query = RuntimeWorldCellBlobQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] Runtime spawn projection requires exactly one RuntimeWorldCellBlobReference singleton.");

            var blob = query.GetSingleton<RuntimeWorldCellBlobReference>().Blob;
            if (!blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] Runtime spawn projection requires runtime world cell blob.");
            return blob;
        }

        static bool IsExteriorCellActive(in StreamingConfig config, int2 exteriorCell)
        {
            if (config.ExteriorStreamingPaused)
                return false;

            int dx = math.abs(exteriorCell.x - config.CameraCell.x);
            int dy = math.abs(exteriorCell.y - config.CameraCell.y);
            return dx <= config.ViewRadius && dy <= config.ViewRadius;
        }

        static class RegistryQueryCache
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
                s_Query = entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<RuntimeSpawnState>(),
                    ComponentType.ReadWrite<RuntimeSpawnedRef>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class RuntimeContentBlobQueryCache
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
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeContentBlobReference>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class RuntimeWorldCellBlobQueryCache
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
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static int2 ComputeExteriorCell(float3 worldPosition)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            if (cellMeters <= 0f)
                return int2.zero;

            return new int2(
                (int)math.floor(worldPosition.x / cellMeters),
                (int)math.floor(worldPosition.z / cellMeters));
        }

        static void EnsureExteriorCapacity(ref AvailableCells available)
        {
            if (!available.Set.IsCreated)
                return;

            int count = available.Set.Count;
            if (count < available.Set.Capacity)
                return;

            available.Set.Capacity = math.max(available.Set.Capacity * 2, count + 1);
        }

        static InteriorSectionLifecycleSystem RequireInteriorSections(World world)
        {
            var system = world.GetExistingSystemManaged<InteriorSectionLifecycleSystem>();
            if (system == null)
                throw new InvalidOperationException("[VVardenfell][Streaming] InteriorSectionLifecycleSystem is unavailable.");
            return system;
        }

        static CellLoadWorkerSystem RequireExteriorSections(World world)
        {
            var system = world.GetExistingSystemManaged<CellLoadWorkerSystem>();
            if (system == null)
                throw new InvalidOperationException("[VVardenfell][Streaming] CellLoadWorkerSystem is unavailable.");
            return system;
        }
    }
}
