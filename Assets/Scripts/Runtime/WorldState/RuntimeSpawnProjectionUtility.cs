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
            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            if (!TryQueueRestoreAliveRefsCreatePhase(
                    entityManager,
                    ref content,
                    ref worldCells,
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

        public static void RebuildRegistryFromJournal(EntityManager entityManager)
        {
            if (!WorldJournalUtility.TryGetJournalEntity(entityManager, out Entity journalEntity)
                || !TryGetRegistryEntity(entityManager, out Entity registryEntity))
            {
                return;
            }

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var rebuilt = new NativeList<RuntimeSpawnedRef>(journal.Length, Allocator.Temp);
            var indices = new NativeParallelHashMap<uint, int>(journal.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < journal.Length; i++)
                {
                    var entry = journal[i];
                    if (entry.Kind == (byte)WorldJournalEntryKind.RuntimeSpawned)
                    {
                        var spawned = new RuntimeSpawnedRef
                        {
                            RuntimeRefId = entry.RuntimeRefId,
                            Content = entry.Content,
                            Position = entry.Position,
                            Rotation = entry.Rotation,
                            Scale = math.max(0.0001f, entry.Scale),
                            ExteriorCell = entry.ExteriorCell,
                            InteriorCellId = entry.InteriorCellId,
                            InteriorCellHash = entry.InteriorCellHash != 0UL
                                ? entry.InteriorCellHash
                                : InteriorCellIdHash.Hash(entry.InteriorCellId),
                            LogicalEntity = Entity.Null,
                            IsInterior = entry.IsInterior,
                            PersistencePolicy = entry.PersistencePolicy,
                            Alive = 1,
                        };

                        if (indices.TryGetValue(spawned.RuntimeRefId, out int existingIndex))
                        {
                            rebuilt[existingIndex] = spawned;
                        }
                        else
                        {
                            indices.Add(spawned.RuntimeRefId, rebuilt.Length);
                            rebuilt.Add(spawned);
                        }
                    }
                    else if (entry.Kind == (byte)WorldJournalEntryKind.RuntimeDestroyed
                             && indices.TryGetValue(entry.RuntimeRefId, out int existingIndex))
                    {
                        var destroyed = rebuilt[existingIndex];
                        destroyed.Alive = 0;
                        destroyed.LogicalEntity = Entity.Null;
                        rebuilt[existingIndex] = destroyed;
                    }
                }

                var registry = entityManager.GetBuffer<RuntimeSpawnedRef>(registryEntity);
                registry.Clear();
                for (int i = 0; i < rebuilt.Length; i++)
                    registry.Add(rebuilt[i]);
            }
            finally
            {
                if (indices.IsCreated)
                    indices.Dispose();
                if (rebuilt.IsCreated)
                    rebuilt.Dispose();
            }
        }

        public static bool TryQueueRestoreAliveRefsCreatePhase(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            ref RuntimeWorldCellBlob worldCells,
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
                WorldResources.RuntimeSpawnPrefabDescriptor descriptor = default;
                if (!actorSpawn && !WorldResources.TryGetRuntimeSpawnPrefab(entry.Content, out descriptor))
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
            var logicalLookup = entityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);

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

                WorldSpawner.HideExteriorVisibility(world, ref loaded);
                if (!WorldSpawner.TrySpawnInteriorCellByHash(world, activeInteriorCellHash, InteriorWorldOffset, transitionEntity, ref logicalLookup, out FixedString128Bytes spawnedInteriorCellId))
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
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
                transition.ActiveInteriorCellHash = 0UL;
                transition.TransitionInProgress = 0;
                config.ExteriorStreamingPaused = false;
                config.CameraCell = ComputeExteriorCell(payload.PlayerPosition);
                WorldSpawner.SyncExteriorVisibility(world, config, available, ref loaded);
            }

            entityManager.SetComponentData(streamingEntity, config);
            entityManager.SetComponentData(streamingEntity, available);
            entityManager.SetComponentData(streamingEntity, loaded);
            entityManager.SetComponentData(streamingEntity, logicalLookup);
            entityManager.SetComponentData(transitionEntity, transition);
            return true;
        }

        public static uint FindMaxRuntimeOrdinal(DynamicBuffer<WorldJournalEntry> journal)
        {
            uint maxOrdinal = 0u;
            for (int i = 0; i < journal.Length; i++)
            {
                var entry = journal[i];
                if (entry.Kind != (byte)WorldJournalEntryKind.RuntimeSpawned)
                    continue;

                uint ordinal = entry.RuntimeRefId & ~0x80000000u;
                if (ordinal > maxOrdinal)
                    maxOrdinal = ordinal;
            }

            return maxOrdinal;
        }

        static bool TryGetRegistryEntity(EntityManager entityManager, out Entity registryEntity)
        {
            registryEntity = Entity.Null;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnState>(),
                ComponentType.ReadWrite<RuntimeSpawnedRef>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            registryEntity = query.GetSingletonEntity();
            return true;
        }

        static BlobAssetReference<RuntimeContentBlob> RequireRuntimeContentBlob(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeContentBlobReference>());
            if (query.IsEmptyIgnoreFilter)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Runtime spawn projection requires runtime content blob.");

            var blob = entityManager.GetComponentData<RuntimeContentBlobReference>(query.GetSingletonEntity()).Blob;
            if (!blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Runtime spawn projection content blob is not created.");
            return blob;
        }

        static BlobAssetReference<RuntimeWorldCellBlob> RequireRuntimeWorldCellBlob(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
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
    }
}
