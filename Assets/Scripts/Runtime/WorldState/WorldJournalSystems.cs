using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeSpawnBootstrapSystem))]
    [UpdateAfter(typeof(ContainerLootBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial class WorldJournalBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<WorldJournalState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<WorldJournalState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.WorldJournal");
            }

            EnsureComponent(runtimeEntity, new WorldJournalState());
            EnsureBuffer<WorldJournalEntry>(runtimeEntity);
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }

        void EnsureBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity))
                EntityManager.AddBuffer<T>(entity);
        }
    }

    static class WorldJournalUtility
    {
        public static bool TryGetJournalEntity(EntityManager entityManager, out Entity journalEntity)
        {
            journalEntity = Entity.Null;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldJournalState>(),
                ComponentType.ReadWrite<WorldJournalEntry>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            journalEntity = query.GetSingletonEntity();
            return true;
        }

        public static uint AppendLooseItemRemoved(EntityManager entityManager, uint placedRefId, ContentReference content)
        {
            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.LooseItemRemoved,
                PlacedRefId = placedRefId,
                Content = content,
                DeltaCount = 1,
            });
        }

        public static uint AppendContainerDelta(EntityManager entityManager, uint placedRefId, ContentReference content, int deltaCount)
        {
            if (placedRefId == 0u || !content.IsValid || deltaCount == 0)
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.ContainerDelta,
                PlacedRefId = placedRefId,
                Content = content,
                DeltaCount = deltaCount,
            });
        }

        public static uint AppendRuntimeSpawn(EntityManager entityManager, in RuntimeSpawnedRef spawnedRef)
        {
            if (spawnedRef.RuntimeRefId == 0u || !spawnedRef.Content.IsValid)
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.RuntimeSpawned,
                RuntimeRefId = spawnedRef.RuntimeRefId,
                Content = spawnedRef.Content,
                Position = spawnedRef.Position,
                Rotation = spawnedRef.Rotation,
                Scale = spawnedRef.Scale,
                ExteriorCell = spawnedRef.ExteriorCell,
                InteriorCellId = spawnedRef.InteriorCellId,
                IsInterior = spawnedRef.IsInterior,
                PersistencePolicy = spawnedRef.PersistencePolicy,
            });
        }

        public static uint AppendRuntimeDestroyed(EntityManager entityManager, uint runtimeRefId)
        {
            if (!RuntimeSpawnRegistryUtility.IsRuntimeRefId(runtimeRefId))
                return 0u;

            return AppendEntry(entityManager, new WorldJournalEntry
            {
                Kind = (byte)WorldJournalEntryKind.RuntimeDestroyed,
                RuntimeRefId = runtimeRefId,
            });
        }

        public static void RebuildPickedItemProjection(DynamicBuffer<WorldJournalEntry> journal, DynamicBuffer<PickedItemRecord> pickedItems)
        {
            pickedItems.Clear();
            var seen = new HashSet<uint>();
            for (int i = 0; i < journal.Length; i++)
            {
                var entry = journal[i];
                if (entry.Kind != (byte)WorldJournalEntryKind.LooseItemRemoved
                    || entry.PlacedRefId == 0u
                    || entry.Content.Kind != ContentReferenceKind.Item
                    || !seen.Add(entry.PlacedRefId))
                {
                    continue;
                }

                pickedItems.Add(new PickedItemRecord
                {
                    PlacedRefId = entry.PlacedRefId,
                    Definition = new ItemDefHandle { Value = entry.Content.HandleValue },
                });
            }
        }

        public static void ApplyContainerDeltas(
            uint placedRefId,
            DynamicBuffer<WorldJournalEntry> journal,
            DynamicBuffer<ContainerSessionItem> items)
        {
            if (placedRefId == 0u)
                return;

            for (int i = 0; i < journal.Length; i++)
            {
                var entry = journal[i];
                if (entry.Kind != (byte)WorldJournalEntryKind.ContainerDelta || entry.PlacedRefId != placedRefId)
                    continue;

                ContainerLootUtility.ApplyContainerDelta(items, placedRefId, entry.Content, entry.DeltaCount);
            }
        }

        static uint AppendEntry(EntityManager entityManager, in WorldJournalEntry template)
        {
            if (!TryGetJournalEntity(entityManager, out Entity journalEntity))
                return 0u;

            var state = entityManager.GetComponentData<WorldJournalState>(journalEntity);
            uint sequence = state.NextSequence + 1u;
            state.NextSequence = sequence;
            entityManager.SetComponentData(journalEntity, state);

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var entry = template;
            entry.Sequence = sequence;
            journal.Add(entry);
            return sequence;
        }
    }

    static class RuntimeSpawnProjectionUtility
    {
        static readonly float3 InteriorWorldOffset = float3.zero;

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
            var rebuilt = new List<RuntimeSpawnedRef>();
            var indices = new Dictionary<uint, int>();

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
                        indices.Add(spawned.RuntimeRefId, rebuilt.Count);
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
            for (int i = 0; i < rebuilt.Count; i++)
                registry.Add(rebuilt[i]);
        }

        public static void RestoreAliveRefsForCurrentWorld(World world, EntityManager entityManager, RuntimeContentDatabase contentDb)
        {
            if (contentDb == null || !TryGetRegistryEntity(entityManager, out Entity registryEntity))
                return;

            Entity streamingEntity = SystemAPIHelper.GetSingletonEntity<LogicalRefLookup>(entityManager);
            Entity transitionEntity = SystemAPIHelper.GetSingletonEntity<InteriorTransitionState>(entityManager);
            if (streamingEntity == Entity.Null || transitionEntity == Entity.Null)
                return;

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
            for (int i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                if (entry.Alive == 0)
                {
                    entry.LogicalEntity = Entity.Null;
                    snapshot[i] = entry;
                    continue;
                }

                if (entry.IsInterior == 0)
                {
                    EnsureExteriorCapacity(ref available);
                    available.Set.Add(entry.ExteriorCell);
                    changedAvailable = true;
                }

                bool shouldSpawn = entry.IsInterior == 0
                    || (transition.InteriorActive != 0 && entry.InteriorCellId.Equals(transition.ActiveInteriorCellId));
                if (!shouldSpawn)
                {
                    entry.LogicalEntity = Entity.Null;
                    snapshot[i] = entry;
                    continue;
                }

                if (entry.LogicalEntity != Entity.Null && entityManager.Exists(entry.LogicalEntity))
                    continue;

                if (!WorldResources.TryGetRuntimeSpawnPrefab(entry.Content, out var descriptor))
                {
                    Debug.LogWarning($"[VVardenfell][Save] skipped runtime ref 0x{entry.RuntimeRefId:X8}: no spawnable prefab descriptor for {entry.Content.Kind}:{entry.Content.HandleValue}.");
                    continue;
                }

                bool exteriorActive = entry.IsInterior != 0 || IsExteriorCellActive(config, entry.ExteriorCell);
                Entity logicalEntity = RuntimeSpawnFactory.Spawn(
                    entityManager,
                    contentDb,
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
                entry.LogicalEntity = logicalEntity;
                snapshot[i] = entry;
            }

            if (changedAvailable)
                entityManager.SetComponentData(streamingEntity, available);

            entityManager.SetComponentData(streamingEntity, logicalLookup);

            registry = entityManager.GetBuffer<RuntimeSpawnedRef>(registryEntity);
            registry.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                registry.Add(snapshot[i]);
        }

        public static bool TryRestoreWorldLocation(
            World world,
            EntityManager entityManager,
            in WorldSavePayload payload,
            out string error)
        {
            error = null;
            Entity streamingEntity = SystemAPIHelper.GetSingletonEntity<LogicalRefLookup>(entityManager);
            Entity transitionEntity = SystemAPIHelper.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity runtimeEntity = SystemAPIHelper.GetSingletonEntity<InteractionRuntimeState>(entityManager);
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
                if (!WorldResources.InteriorCells.TryGetValue(payload.ActiveInteriorCellId, out CellData interiorCell) || interiorCell == null)
                {
                    error = $"Continue save references missing interior '{payload.ActiveInteriorCellId}'.";
                    return false;
                }

                WorldSpawner.HideExteriorVisibility(world, ref loaded);
                WorldSpawner.SpawnInteriorCell(world, interiorCell, InteriorWorldOffset, transitionEntity, ref logicalLookup);
                config.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = new FixedString128Bytes(payload.ActiveInteriorCellId);
                transition.TransitionInProgress = 0;

                var interactionState = entityManager.GetComponentData<InteractionRuntimeState>(runtimeEntity);
                interactionState.PendingPickedItemPrune = 1;
                entityManager.SetComponentData(runtimeEntity, interactionState);
            }
            else
            {
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
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

    static class WorldSaveStorage
    {
        const uint Magic = 0x53575656u; // VVWS
        const int Version = 1;
        const string FileName = "continue_save.bin";

        public static string ContinueSavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool TryGetContinueAvailability(out string error)
        {
            if (!File.Exists(ContinueSavePath))
            {
                error = "No serialized save payload is available.";
                return false;
            }

            return TryLoadContinueSave(out _, out error);
        }

        public static bool TryWriteContinueSave(EntityManager entityManager, out string error)
        {
            error = null;
            if (!TryBuildPayload(entityManager, out WorldSavePayload payload, out error))
                return false;

            try
            {
                string path = ContinueSavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
                using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var w = new BinaryWriter(fs);
                WritePayload(w, payload);
                Debug.Log($"[VVardenfell][Save] wrote continue payload: journal={payload.JournalEntries.Length}, inventory={payload.Inventory.Length}, path='{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed writing continue save: {ex.Message}";
                return false;
            }
        }

        public static bool TryLoadContinueSave(out WorldSavePayload payload, out string error)
        {
            payload = default;
            error = null;

            try
            {
                string path = ContinueSavePath;
                if (!File.Exists(path))
                {
                    error = "No serialized save payload is available.";
                    return false;
                }

                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var r = new BinaryReader(fs);
                payload = ReadPayload(r);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Continue save unreadable: {ex.Message}";
                return false;
            }
        }

        static bool TryBuildPayload(EntityManager entityManager, out WorldSavePayload payload, out string error)
        {
            payload = default;
            error = null;

            Entity playerEntity = SystemAPIHelper.GetSingletonEntity<PlayerTag>(entityManager);
            Entity viewEntity = SystemAPIHelper.GetSingletonEntity<PlayerViewComponent>(entityManager);
            Entity journalEntity = SystemAPIHelper.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity inventoryEntity = SystemAPIHelper.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity transitionEntity = SystemAPIHelper.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity spawnEntity = SystemAPIHelper.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (playerEntity == Entity.Null
                || viewEntity == Entity.Null
                || journalEntity == Entity.Null
                || inventoryEntity == Entity.Null
                || transitionEntity == Entity.Null
                || spawnEntity == Entity.Null)
            {
                error = "Runtime save state is not ready.";
                return false;
            }

            var playerTransform = entityManager.GetComponentData<LocalTransform>(playerEntity);
            var view = entityManager.GetComponentData<PlayerViewComponent>(viewEntity);
            var journalState = entityManager.GetComponentData<WorldJournalState>(journalEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var spawnState = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var journalEntries = new WorldJournalEntry[journal.Length];
            for (int i = 0; i < journal.Length; i++)
                journalEntries[i] = journal[i];

            var inventory = entityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            var inventoryEntries = new PlayerInventoryItem[inventory.Length];
            for (int i = 0; i < inventory.Length; i++)
                inventoryEntries[i] = inventory[i];

            payload = new WorldSavePayload
            {
                PlayerPosition = playerTransform.Position,
                PlayerRotation = playerTransform.Rotation,
                PlayerPitchDegrees = view.LocalPitchDegrees,
                InteriorActive = transition.InteriorActive != 0 && transition.ActiveInteriorCellId.Length > 0,
                ActiveInteriorCellId = transition.ActiveInteriorCellId.ToString(),
                NextJournalSequence = journalState.NextSequence,
                NextRuntimeRefId = spawnState.NextRuntimeRefId,
                JournalEntries = journalEntries,
                Inventory = inventoryEntries,
            };
            return true;
        }

        static void WritePayload(BinaryWriter w, in WorldSavePayload payload)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(payload.PlayerPosition.x);
            w.Write(payload.PlayerPosition.y);
            w.Write(payload.PlayerPosition.z);
            w.Write(payload.PlayerRotation.value.x);
            w.Write(payload.PlayerRotation.value.y);
            w.Write(payload.PlayerRotation.value.z);
            w.Write(payload.PlayerRotation.value.w);
            w.Write(payload.PlayerPitchDegrees);
            w.Write(payload.InteriorActive);
            w.Write(payload.ActiveInteriorCellId ?? string.Empty);
            w.Write(payload.NextJournalSequence);
            w.Write(payload.NextRuntimeRefId);

            w.Write(payload.Inventory?.Length ?? 0);
            if (payload.Inventory != null)
            {
                for (int i = 0; i < payload.Inventory.Length; i++)
                    WriteInventoryEntry(w, payload.Inventory[i]);
            }

            w.Write(payload.JournalEntries?.Length ?? 0);
            if (payload.JournalEntries != null)
            {
                for (int i = 0; i < payload.JournalEntries.Length; i++)
                    WriteJournalEntry(w, payload.JournalEntries[i]);
            }
        }

        static WorldSavePayload ReadPayload(BinaryReader r)
        {
            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException("unexpected save magic");

            int version = r.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"unsupported save version {version}");

            var payload = new WorldSavePayload
            {
                PlayerPosition = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerRotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerPitchDegrees = r.ReadSingle(),
                InteriorActive = r.ReadBoolean(),
                ActiveInteriorCellId = r.ReadString(),
                NextJournalSequence = r.ReadUInt32(),
                NextRuntimeRefId = r.ReadUInt32(),
            };

            int inventoryCount = ReadCount(r, "inventory");
            payload.Inventory = new PlayerInventoryItem[inventoryCount];
            for (int i = 0; i < inventoryCount; i++)
                payload.Inventory[i] = ReadInventoryEntry(r);

            int journalCount = ReadCount(r, "journal");
            payload.JournalEntries = new WorldJournalEntry[journalCount];
            for (int i = 0; i < journalCount; i++)
                payload.JournalEntries[i] = ReadJournalEntry(r);

            return payload;
        }

        static void WriteInventoryEntry(BinaryWriter w, in PlayerInventoryItem value)
        {
            WriteContentReference(w, value.Content);
            w.Write(value.Count);
        }

        static PlayerInventoryItem ReadInventoryEntry(BinaryReader r)
        {
            return new PlayerInventoryItem
            {
                Content = ReadContentReference(r),
                Count = r.ReadInt32(),
            };
        }

        static void WriteJournalEntry(BinaryWriter w, in WorldJournalEntry value)
        {
            w.Write(value.Sequence);
            w.Write(value.Kind);
            w.Write(value.PlacedRefId);
            w.Write(value.RuntimeRefId);
            WriteContentReference(w, value.Content);
            w.Write(value.DeltaCount);
            w.Write(value.Position.x);
            w.Write(value.Position.y);
            w.Write(value.Position.z);
            w.Write(value.Rotation.value.x);
            w.Write(value.Rotation.value.y);
            w.Write(value.Rotation.value.z);
            w.Write(value.Rotation.value.w);
            w.Write(value.Scale);
            w.Write(value.ExteriorCell.x);
            w.Write(value.ExteriorCell.y);
            w.Write(value.InteriorCellId.ToString());
            w.Write(value.IsInterior);
            w.Write(value.PersistencePolicy);
        }

        static WorldJournalEntry ReadJournalEntry(BinaryReader r)
        {
            return new WorldJournalEntry
            {
                Sequence = r.ReadUInt32(),
                Kind = r.ReadByte(),
                PlacedRefId = r.ReadUInt32(),
                RuntimeRefId = r.ReadUInt32(),
                Content = ReadContentReference(r),
                DeltaCount = r.ReadInt32(),
                Position = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Rotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Scale = r.ReadSingle(),
                ExteriorCell = new int2(r.ReadInt32(), r.ReadInt32()),
                InteriorCellId = new FixedString128Bytes(r.ReadString() ?? string.Empty),
                IsInterior = r.ReadByte(),
                PersistencePolicy = r.ReadByte(),
            };
        }

        static void WriteContentReference(BinaryWriter w, ContentReference value)
        {
            w.Write((byte)value.Kind);
            w.Write(value.HandleValue);
        }

        static ContentReference ReadContentReference(BinaryReader r)
        {
            return new ContentReference
            {
                Kind = (ContentReferenceKind)r.ReadByte(),
                HandleValue = r.ReadInt32(),
            };
        }

        static int ReadCount(BinaryReader r, string label)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"invalid {label} count {count}");
            return count;
        }
    }

    struct WorldSavePayload
    {
        public float3 PlayerPosition;
        public quaternion PlayerRotation;
        public float PlayerPitchDegrees;
        public bool InteriorActive;
        public string ActiveInteriorCellId;
        public uint NextJournalSequence;
        public uint NextRuntimeRefId;
        public PlayerInventoryItem[] Inventory;
        public WorldJournalEntry[] JournalEntries;
    }

    static class WorldSaveReplayUtility
    {
        public static bool TryRestoreContinueSave(World world, EntityManager entityManager, ref GameInitializationSingleton init, out string error)
        {
            error = null;
            if (!WorldSaveStorage.TryLoadContinueSave(out WorldSavePayload payload, out error))
                return false;

            Entity journalEntity = SystemAPIHelper.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity runtimeEntity = SystemAPIHelper.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity spawnEntity = SystemAPIHelper.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (journalEntity == Entity.Null || runtimeEntity == Entity.Null || spawnEntity == Entity.Null)
            {
                error = "Runtime journal state is not ready for continue load.";
                return false;
            }

            ClearRuntimeState(entityManager, journalEntity, runtimeEntity, spawnEntity);
            ApplyPayload(entityManager, payload, journalEntity, runtimeEntity, spawnEntity);

            init.PlayerPosition = payload.PlayerPosition;
            init.PlayerRotation = payload.PlayerRotation;
            init.PlayerPitchDegrees = payload.PlayerPitchDegrees;

            if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(world, entityManager, payload, out error))
                return false;

            RuntimeSpawnProjectionUtility.RestoreAliveRefsForCurrentWorld(world, entityManager, RuntimeContentDatabase.Active);
            Debug.Log($"[VVardenfell][Save] restored continue payload: journal={payload.JournalEntries.Length}, inventory={payload.Inventory.Length}, interior={payload.InteriorActive}.");
            return true;
        }

        static void ClearRuntimeState(EntityManager entityManager, Entity journalEntity, Entity runtimeEntity, Entity spawnEntity)
        {
            entityManager.GetBuffer<WorldJournalEntry>(journalEntity).Clear();
            entityManager.GetBuffer<PlayerInventoryItem>(runtimeEntity).Clear();
            entityManager.GetBuffer<PickedItemRecord>(runtimeEntity).Clear();
            entityManager.GetBuffer<ContainerSessionHeader>(runtimeEntity).Clear();
            entityManager.GetBuffer<ContainerSessionItem>(runtimeEntity).Clear();
            entityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity).Clear();
            entityManager.GetBuffer<RuntimeSpawnedRef>(spawnEntity).Clear();

            var spawnResult = entityManager.GetComponentData<RuntimeSpawnResult>(spawnEntity);
            spawnResult = new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            };
            entityManager.SetComponentData(spawnEntity, spawnResult);
        }

        static void ApplyPayload(
            EntityManager entityManager,
            in WorldSavePayload payload,
            Entity journalEntity,
            Entity runtimeEntity,
            Entity spawnEntity)
        {
            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            uint maxSequence = 0u;
            for (int i = 0; i < payload.JournalEntries.Length; i++)
            {
                var entry = payload.JournalEntries[i];
                journal.Add(entry);
                if (entry.Sequence > maxSequence)
                    maxSequence = entry.Sequence;
            }

            entityManager.SetComponentData(journalEntity, new WorldJournalState
            {
                NextSequence = math.max(payload.NextJournalSequence, maxSequence),
            });

            var inventory = entityManager.GetBuffer<PlayerInventoryItem>(runtimeEntity);
            for (int i = 0; i < payload.Inventory.Length; i++)
            {
                if (payload.Inventory[i].Count > 0 && payload.Inventory[i].Content.IsValid)
                    inventory.Add(payload.Inventory[i]);
            }

            var pickedItems = entityManager.GetBuffer<PickedItemRecord>(runtimeEntity);
            WorldJournalUtility.RebuildPickedItemProjection(journal, pickedItems);

            RuntimeSpawnProjectionUtility.RebuildRegistryFromJournal(entityManager);
            uint maxRuntimeOrdinal = RuntimeSpawnProjectionUtility.FindMaxRuntimeOrdinal(journal);
            var spawnState = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            spawnState.NextRuntimeRefId = math.max(payload.NextRuntimeRefId, maxRuntimeOrdinal);
            spawnState.NextRequestSequence = 0u;
            entityManager.SetComponentData(spawnEntity, spawnState);
        }
    }

    static class SystemAPIHelper
    {
        public static Entity GetSingletonEntity<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        public static Entity GetSingletonBufferOwner<T>(EntityManager entityManager)
            where T : unmanaged, IBufferElementData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }
    }
}
