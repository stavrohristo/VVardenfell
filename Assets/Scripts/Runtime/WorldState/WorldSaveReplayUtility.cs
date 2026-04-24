using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldState
{
    static class WorldSaveReplayUtility
    {
        public static bool TryRestoreContinueSave(World world, EntityManager entityManager, ref GameInitializationSingleton init, out string error)
        {
            error = null;
            if (!WorldSaveStorage.TryLoadContinueSave(out WorldSavePayload payload, out error))
                return false;

            Entity journalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
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
            init.PlayerActorStats = payload.ActorStats;
            init.PlayerIdentity = payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer();
            PopulateInitializationSpellbook(entityManager, payload.KnownSpells);
            PopulateInitializationActiveEffects(entityManager, payload.ActiveMagicEffects);

            if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(world, entityManager, payload, out error))
                return false;

            RestoreAliveRefsForCurrentWorld(entityManager);
            return true;
        }

        public static bool TryRestoreSlotInPlace(World world, EntityManager entityManager, string slotId, out string error)
        {
            error = null;
            if (!WorldSaveStorage.TryLoadSlot(slotId, out WorldSavePayload payload, out error))
                return false;

            return TryRestorePayloadInPlace(world, entityManager, payload, out error);
        }

        public static bool TryRestorePayloadInPlace(World world, EntityManager entityManager, in WorldSavePayload payload, out string error)
        {
            error = null;

            if (!TryValidatePayload(payload, out error))
                return false;

            if (!TryGetReplayEntities(
                    entityManager,
                    out Entity journalEntity,
                    out Entity runtimeEntity,
                    out Entity spawnEntity,
                    out Entity playerEntity,
                    out Entity viewEntity,
                    out error))
                return false;

            if (!ValidateWorldLocation(payload, out error))
                return false;
            if (!TryValidateWorldRestorePrereqs(entityManager, out error))
                return false;

            ClearRuntimeState(entityManager, journalEntity, runtimeEntity, spawnEntity);
            ApplyPayload(entityManager, payload, journalEntity, runtimeEntity, spawnEntity);
            ApplyPlayerPayload(entityManager, playerEntity, viewEntity, payload);

            if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(world, entityManager, payload, out error))
                return false;

            RestoreAliveRefsForCurrentWorld(entityManager);
            if (!HasExactlyOne<PlayerTag>(entityManager) || !HasExactlyOne<PlayerViewComponent>(entityManager))
            {
                error = "Save replay produced an invalid player entity count.";
                return false;
            }

            return true;
        }

        static void RestoreAliveRefsForCurrentWorld(EntityManager entityManager)
        {
            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            if (!RuntimeSpawnProjectionUtility.TryQueueRestoreAliveRefsCreatePhase(
                    entityManager,
                    RuntimeContentDatabase.Active,
                    ref createEcb,
                    out var projection))
            {
                createEcb.Dispose();
                return;
            }

            PlaybackAndDispose(entityManager, ref createEcb);

            var materializeEcb = new EntityCommandBuffer(Allocator.Temp);
            RuntimeSpawnProjectionUtility.QueueRestoreAliveRefsMaterializePhase(
                entityManager,
                ref materializeEcb,
                ref projection);
            PlaybackAndDispose(entityManager, ref materializeEcb);
            RuntimeSpawnProjectionUtility.ApplyRestoreAliveRefsProjection(entityManager, projection);
        }

        public static void ResetRuntimeForInitialization(World world, EntityManager entityManager, bool preserveShell)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueDestroySingletonEntities<PlayerViewComponent>(entityManager, ref ecb);
            QueueDestroySingletonEntities<PlayerTag>(entityManager, ref ecb);
            QueueClearMapDiscovery(entityManager, ref ecb, ensureState: true);
            PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);

            Entity journalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (journalEntity != Entity.Null && runtimeEntity != Entity.Null && spawnEntity != Entity.Null)
                ClearRuntimeState(entityManager, journalEntity, runtimeEntity, spawnEntity);

            if (preserveShell && WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeShellState>(entityManager) is Entity shellEntity && shellEntity != Entity.Null)
            {
                var shell = entityManager.GetComponentData<RuntimeShellState>(shellEntity);
                shell.InventoryOpen = 0;
                shell.ContainerOpen = 0;
                shell.PauseMenuOpen = 0;
                shell.ModalOpen = 0;
                shell.SaveLoadBrowserOpen = 0;
                shell.SelectedAction = (byte)RuntimeShellMenuActionId.Resume;
                shell.ModalTitle = default;
                shell.ModalBody = default;
                entityManager.SetComponentData(shellEntity, shell);
            }
        }

        static void QueueDestroySingletonEntities<T>(EntityManager entityManager, ref EntityCommandBuffer ecb)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            if (query.IsEmptyIgnoreFilter)
                return;

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    ecb.DestroyEntity(entities[i]);
            }
        }

        static void ApplyPlayerPayload(EntityManager entityManager, Entity playerEntity, Entity viewEntity, in WorldSavePayload payload)
        {
            entityManager.SetComponentData(playerEntity, LocalTransform.FromPositionRotationScale(payload.PlayerPosition, payload.PlayerRotation, 1f));
            entityManager.SetComponentData(playerEntity, new LocalToWorld
            {
                Value = float4x4.TRS(payload.PlayerPosition, payload.PlayerRotation, new float3(1f)),
            });
            entityManager.SetComponentData(playerEntity, payload.ActorStats.Attributes);
            entityManager.SetComponentData(playerEntity, payload.ActorStats.Skills);
            var vitals = payload.ActorStats.Vitals;
            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, payload.ActorStats.Attributes, ref vitals, initializeMissingCurrents: true);
            entityManager.SetComponentData(playerEntity, vitals);
            entityManager.SetComponentData(playerEntity, payload.ActorStats.EffectModifiers);
            var derived = MorrowindActorMovementStats.BuildDerived(
                RuntimeContentDatabase.Active,
                payload.ActorStats.Attributes,
                payload.ActorStats.Skills,
                vitals,
                payload.ActorStats.EffectModifiers,
                0f);
            entityManager.SetComponentData(playerEntity, derived);
            entityManager.SetComponentData(playerEntity, payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer());
            entityManager.SetComponentData(playerEntity, new PlayerCharacterControl());
            entityManager.SetComponentData(playerEntity, new PlayerCharacterState());
            entityManager.SetComponentData(playerEntity, new MorrowindMovementIntent());
            entityManager.SetComponentData(playerEntity, new MorrowindActorKinematicState());
            entityManager.SetComponentData(playerEntity, new MorrowindMovementFrameTrace());

            if (entityManager.HasBuffer<PlayerKnownSpell>(playerEntity))
            {
                var spells = entityManager.GetBuffer<PlayerKnownSpell>(playerEntity);
                spells.Clear();
                if (payload.KnownSpells != null)
                {
                    for (int i = 0; i < payload.KnownSpells.Length; i++)
                    {
                        if (payload.KnownSpells[i].Spell.IsValid)
                            spells.Add(payload.KnownSpells[i]);
                    }
                }
            }
            if (!entityManager.HasBuffer<ActorActiveMagicEffect>(playerEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<ActorActiveMagicEffect>(playerEntity);
                PlaybackAndDispose(entityManager, ref ecb);
            }
            if (entityManager.HasBuffer<ActorActiveMagicEffect>(playerEntity))
            {
                var activeEffects = entityManager.GetBuffer<ActorActiveMagicEffect>(playerEntity);
                activeEffects.Clear();
                if (payload.ActiveMagicEffects != null)
                {
                    for (int i = 0; i < payload.ActiveMagicEffects.Length; i++)
                    {
                        if (payload.ActiveMagicEffects[i].Applied != 0)
                            activeEffects.Add(payload.ActiveMagicEffects[i]);
                    }
                }
            }

            var character = entityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var eyeOffset = new float3(0f, character.StandingEyeHeight, 0f);
            entityManager.SetComponentData(viewEntity, new Parent { Value = playerEntity });
            entityManager.SetComponentData(viewEntity, LocalTransform.FromPositionRotationScale(eyeOffset, quaternion.identity, 1f));
            entityManager.SetComponentData(viewEntity, new LocalToWorld
            {
                Value = float4x4.TRS(payload.PlayerPosition + math.rotate(payload.PlayerRotation, eyeOffset), payload.PlayerRotation, new float3(1f)),
            });
            entityManager.SetComponentData(viewEntity, new PlayerViewComponent
            {
                ControlledCharacter = playerEntity,
                LocalPitchDegrees = payload.PlayerPitchDegrees,
                LocalViewRotation = quaternion.identity,
                LocalEyeOffset = eyeOffset,
            });
            entityManager.SetComponentData(viewEntity, new PlayerPhysicsViewPose
            {
                Position = payload.PlayerPosition + math.rotate(payload.PlayerRotation, eyeOffset),
                Rotation = payload.PlayerRotation,
            });
        }

        static bool TryValidatePayload(in WorldSavePayload payload, out string error)
        {
            error = null;
            if (payload.Inventory == null)
            {
                error = "Save payload is missing inventory data.";
                return false;
            }

            if (payload.JournalEntries == null)
            {
                error = "Save payload is missing journal data.";
                return false;
            }

            if (payload.KnownSpells == null)
            {
                error = "Save payload is missing spellbook data.";
                return false;
            }

            if (payload.ActiveMagicEffects == null)
            {
                error = "Save payload is missing active magic effect data.";
                return false;
            }

            return true;
        }

        static bool ValidateWorldLocation(in WorldSavePayload payload, out string error)
        {
            error = null;
            if (payload.InteriorActive
                && !string.IsNullOrWhiteSpace(payload.ActiveInteriorCellId)
                && (!WorldResources.InteriorCells.TryGetValue(payload.ActiveInteriorCellId, out CellData cell) || cell == null))
            {
                error = $"Save references missing interior '{payload.ActiveInteriorCellId}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateWorldRestorePrereqs(EntityManager entityManager, out string error)
        {
            error = null;
            if (!TryGetExactlyOne<LogicalRefLookup>(entityManager, out Entity streamingEntity, out error, "streaming lookup"))
                return false;
            if (!TryGetExactlyOne<InteriorTransitionState>(entityManager, out _, out error, "interior transition"))
                return false;
            if (!TryGetExactlyOne<InteractionRuntimeState>(entityManager, out _, out error, "interaction runtime"))
                return false;

            if (!entityManager.HasComponent<StreamingConfig>(streamingEntity)
                || !entityManager.HasComponent<AvailableCells>(streamingEntity)
                || !entityManager.HasComponent<LoadedCellsMap>(streamingEntity))
            {
                error = "Required world streaming state is not ready for save replay.";
                return false;
            }

            return true;
        }

        static bool TryGetReplayEntities(
            EntityManager entityManager,
            out Entity journalEntity,
            out Entity runtimeEntity,
            out Entity spawnEntity,
            out Entity playerEntity,
            out Entity viewEntity,
            out string error)
        {
            journalEntity = Entity.Null;
            runtimeEntity = Entity.Null;
            spawnEntity = Entity.Null;
            playerEntity = Entity.Null;
            viewEntity = Entity.Null;
            error = null;

            if (!TryGetExactlyOne<WorldJournalState>(entityManager, out journalEntity, out error, "world journal"))
                return false;
            if (!TryGetExactlyOneBufferOwner<PlayerInventoryItem>(entityManager, out runtimeEntity, out error, "runtime inventory"))
                return false;
            if (!TryGetExactlyOne<RuntimeSpawnState>(entityManager, out spawnEntity, out error, "runtime spawn state"))
                return false;
            if (!TryGetExactlyOne<PlayerTag>(entityManager, out playerEntity, out error, "player"))
                return false;
            if (!TryGetExactlyOne<PlayerViewComponent>(entityManager, out viewEntity, out error, "player view"))
                return false;

            return true;
        }

        static bool HasExactlyOne<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.CalculateEntityCount() == 1;
        }

        static bool TryGetExactlyOne<T>(EntityManager entityManager, out Entity entity, out string error, string label)
            where T : unmanaged, IComponentData
        {
            entity = Entity.Null;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            int count = query.CalculateEntityCount();
            if (count != 1)
            {
                error = count == 0
                    ? $"Runtime {label} state is not ready for save replay."
                    : $"Runtime {label} state has {count} entities; expected exactly one.";
                return false;
            }

            entity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetExactlyOneBufferOwner<T>(EntityManager entityManager, out Entity entity, out string error, string label)
            where T : unmanaged, IBufferElementData
        {
            entity = Entity.Null;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            int count = query.CalculateEntityCount();
            if (count != 1)
            {
                error = count == 0
                    ? $"Runtime {label} state is not ready for save replay."
                    : $"Runtime {label} state has {count} entities; expected exactly one.";
                return false;
            }

            entity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static void PopulateInitializationSpellbook(EntityManager entityManager, PlayerKnownSpell[] knownSpells)
        {
            Entity initEntity = WorldStateEntityQueryUtility.GetSingletonEntity<GameInitializationSingleton>(entityManager);
            if (initEntity == Entity.Null)
                return;

            if (!entityManager.HasBuffer<PlayerKnownSpell>(initEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<PlayerKnownSpell>(initEntity);
                PlaybackAndDispose(entityManager, ref ecb);
            }

            DynamicBuffer<PlayerKnownSpell> buffer = entityManager.GetBuffer<PlayerKnownSpell>(initEntity);
            buffer.Clear();
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationActiveEffects(EntityManager entityManager, ActorActiveMagicEffect[] activeEffects)
        {
            Entity initEntity = WorldStateEntityQueryUtility.GetSingletonEntity<GameInitializationSingleton>(entityManager);
            if (initEntity == Entity.Null)
                return;

            if (!entityManager.HasBuffer<ActorActiveMagicEffect>(initEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<ActorActiveMagicEffect>(initEntity);
                PlaybackAndDispose(entityManager, ref ecb);
            }

            DynamicBuffer<ActorActiveMagicEffect> buffer = entityManager.GetBuffer<ActorActiveMagicEffect>(initEntity);
            buffer.Clear();
            if (activeEffects == null)
                return;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                if (activeEffects[i].Applied != 0)
                    buffer.Add(activeEffects[i]);
            }
        }

        public static void ApplyMapDiscoveryPayload(EntityManager entityManager, in WorldSavePayload payload)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueClearMapDiscovery(entityManager, ref ecb, ensureState: false);
            QueueRestoreMapDiscoveryPayload(entityManager, ref ecb, payload.ExteriorMapDiscovery);
            PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);
            GlobalMapPresentationCache.RestoreOverlayPayload(payload.GlobalMapOverlay);
        }

        static void QueueClearMapDiscovery(EntityManager entityManager, ref EntityCommandBuffer ecb, bool ensureState)
        {
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ExteriorMapDiscoveryTile>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                        ecb.DestroyEntity(entities[i]);
                }
            }

            if (ensureState)
                QueueEnsureMapDiscoveryState(entityManager, ref ecb);
            GlobalMapPresentationCache.ClearOverlay();
        }

        static void QueueRestoreMapDiscoveryPayload(EntityManager entityManager, ref EntityCommandBuffer ecb, LocalMapDiscoveryTilePayload[] tiles)
        {
            Entity stateEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LocalMapDiscoveryState>(entityManager);
            int fallbackResolution = 64;
            if (stateEntity != Entity.Null)
            {
                var state = entityManager.GetComponentData<LocalMapDiscoveryState>(stateEntity);
                fallbackResolution = state.MaskResolution > 0 ? state.MaskResolution : 64;
            }

            QueueEnsureMapDiscoveryState(entityManager, ref ecb);
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    var payload = tiles[i];
                    int resolution = payload.Resolution > 0 ? payload.Resolution : fallbackResolution;
                    int expected = resolution * resolution;
                    if (payload.Alpha == null || payload.Alpha.Length != expected)
                        continue;

                    var entity = ecb.CreateEntity();
                    ecb.SetName(entity, new FixedString64Bytes($"LocalMapDiscovery({payload.Cell.x},{payload.Cell.y})"));
                    ecb.AddComponent(entity, new ExteriorMapDiscoveryTile
                    {
                        Cell = payload.Cell,
                        Revision = 1,
                        Dirty = 1,
                    });
                    var buffer = ecb.AddBuffer<ExteriorMapDiscoverySample>(entity);
                    buffer.ResizeUninitialized(expected);
                    for (int s = 0; s < expected; s++)
                        buffer[s] = new ExteriorMapDiscoverySample { Alpha = payload.Alpha[s] };
                }
            }
        }

        static void RefreshMapDiscoveryState(EntityManager entityManager)
        {
            Entity stateEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LocalMapDiscoveryState>(entityManager);
            if (stateEntity == Entity.Null)
                return;

            var state = entityManager.GetComponentData<LocalMapDiscoveryState>(stateEntity);
            int fallbackResolution = state.MaskResolution > 0 ? state.MaskResolution : 64;
            if (state.MaskResolution <= 0)
                state.MaskResolution = fallbackResolution;
            if (state.RenderResolution <= 0)
                state.RenderResolution = 256;
            if (state.RevealRadiusFraction <= 0f)
                state.RevealRadiusFraction = 0.17f;
            state.Revision++;
            entityManager.SetComponentData(stateEntity, state);
        }

        static void QueueEnsureMapDiscoveryState(EntityManager entityManager, ref EntityCommandBuffer ecb)
        {
            Entity stateEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LocalMapDiscoveryState>(entityManager);
            if (stateEntity != Entity.Null)
                return;

            stateEntity = ecb.CreateEntity();
            ecb.SetName(stateEntity, new FixedString64Bytes("VVardenfell.LocalMapDiscovery"));
            ecb.AddComponent(stateEntity, new LocalMapDiscoveryState
            {
                MaskResolution = 64,
                RenderResolution = 256,
                RevealRadiusFraction = 0.17f,
            });
        }

        static void ClearRuntimeState(EntityManager entityManager, Entity journalEntity, Entity runtimeEntity, Entity spawnEntity)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueClearMapDiscovery(entityManager, ref ecb, ensureState: true);
            PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);
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
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueRestoreMapDiscoveryPayload(entityManager, ref ecb, payload.ExteriorMapDiscovery);
            PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);
            GlobalMapPresentationCache.RestoreOverlayPayload(payload.GlobalMapOverlay);

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

        static void PlaybackAndDispose(EntityManager entityManager, ref EntityCommandBuffer ecb)
        {
            ecb.Playback(entityManager);
            ecb.Dispose();
        }
    }
}
