using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
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
            Entity questJournalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindQuestJournalState>(entityManager);
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (journalEntity == Entity.Null || questJournalEntity == Entity.Null || runtimeEntity == Entity.Null || spawnEntity == Entity.Null)
            {
                error = "Runtime journal state is not ready for continue load.";
                return false;
            }

            ClearRuntimeState(entityManager, journalEntity, questJournalEntity, runtimeEntity, spawnEntity);
            if (!ApplyPayload(entityManager, payload, journalEntity, questJournalEntity, runtimeEntity, spawnEntity, out error))
                return false;

            init.PlayerPosition = payload.PlayerPosition;
            init.PlayerRotation = payload.PlayerRotation;
            init.PlayerPitchDegrees = payload.PlayerPitchDegrees;
            init.PlayerActorStats = payload.ActorStats;
            init.PlayerIdentity = payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer();
            init.PlayerCrime = payload.PlayerCrime;
            if (payload.PlayerFactions != null)
                PopulateInitializationFactions(entityManager, payload.PlayerFactions);
            PopulateInitializationSpellbook(entityManager, payload.KnownSpells);
            PopulateInitializationActiveEffects(entityManager, payload.ActiveMagicEffects);

            if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(world, entityManager, payload, out error))
                return false;

            RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(entityManager, RuntimeContentDatabase.Active);
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
                    out Entity questJournalEntity,
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

            ClearRuntimeState(entityManager, journalEntity, questJournalEntity, runtimeEntity, spawnEntity);
            if (!ApplyPayload(entityManager, payload, journalEntity, questJournalEntity, runtimeEntity, spawnEntity, out error))
                return false;
            ApplyPlayerPayload(entityManager, playerEntity, viewEntity, payload);

            if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(world, entityManager, payload, out error))
                return false;

            RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(entityManager, RuntimeContentDatabase.Active);
            if (!WorldStateEntityQueryUtility.HasExactlyOne<PlayerTag>(entityManager)
                || !WorldStateEntityQueryUtility.HasExactlyOne<PlayerViewComponent>(entityManager))
            {
                error = "Save replay produced an invalid player entity count.";
                return false;
            }

            return true;
        }

        public static void ResetRuntimeForInitialization(World world, EntityManager entityManager, bool preserveShell)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueDestroySingletonEntities<PlayerViewComponent>(entityManager, ref ecb);
            QueueDestroySingletonEntities<PlayerTag>(entityManager, ref ecb);
            QueueClearMapDiscovery(entityManager, ref ecb, ensureState: true);
            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);

            Entity journalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity questJournalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindQuestJournalState>(entityManager);
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (journalEntity != Entity.Null && questJournalEntity != Entity.Null && runtimeEntity != Entity.Null && spawnEntity != Entity.Null)
                ClearRuntimeState(entityManager, journalEntity, questJournalEntity, runtimeEntity, spawnEntity);

            if (preserveShell && WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeShellState>(entityManager) is Entity shellEntity && shellEntity != Entity.Null)
            {
                var shell = entityManager.GetComponentData<RuntimeShellState>(shellEntity);
                shell.InventoryOpen = 0;
                shell.ContainerOpen = 0;
                shell.PauseMenuOpen = 0;
                shell.ModalOpen = 0;
                shell.SaveLoadBrowserOpen = 0;
                shell.JournalOpen = 0;
                shell.DialogueOpen = 0;
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
            entityManager.SetComponentData(
                playerEntity,
                MorrowindActorMovementStats.BuildPlayerMovementSpeed(
                    RuntimeContentDatabase.Active,
                    payload.ActorStats.Attributes,
                    payload.ActorStats.Skills,
                    vitals,
                    payload.ActorStats.EffectModifiers,
                    derived));
            entityManager.SetComponentData(playerEntity, payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer());
            if (entityManager.HasComponent<PlayerCrimeState>(playerEntity))
                entityManager.SetComponentData(playerEntity, payload.PlayerCrime);
            ApplyPlayerFactionPayload(entityManager, playerEntity, payload.PlayerFactions);
            entityManager.SetComponentData(playerEntity, new PlayerCharacterControl());
            entityManager.SetComponentData(playerEntity, new PlayerCharacterState());
            entityManager.SetComponentData(playerEntity, new MorrowindMovementInput());
            entityManager.SetComponentData(playerEntity, new MorrowindMovementState
            {
                GroundNormal = math.up(),
            });

            if (entityManager.HasBuffer<ActorKnownSpell>(playerEntity))
            {
                var spells = entityManager.GetBuffer<ActorKnownSpell>(playerEntity);
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
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
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

            if (payload.QuestJournal.States == null || payload.QuestJournal.Entries == null)
            {
                error = "Save payload is missing quest journal data.";
                return false;
            }

            if (payload.Dialogue.KnownTopicDialogueIndices == null
                || payload.Dialogue.TopicEntries == null
                || payload.Dialogue.FactionReactions == null)
            {
                error = "Save payload is missing dialogue data.";
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

            if (payload.ActorDeathCounts == null)
            {
                error = "Save payload is missing actor death count data.";
                return false;
            }

            return true;
        }

        static bool ValidateWorldLocation(in WorldSavePayload payload, out string error)
        {
            error = null;
            if (payload.InteriorActive
                && !string.IsNullOrWhiteSpace(payload.ActiveInteriorCellId)
                && !WorldResources.TryGetInteriorCell(InteriorCellIdHash.Hash(payload.ActiveInteriorCellId), out CellData cell))
            {
                error = $"Save references missing interior '{payload.ActiveInteriorCellId}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateWorldRestorePrereqs(EntityManager entityManager, out string error)
        {
            error = null;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<LogicalRefLookup>(
                    entityManager,
                    out Entity streamingEntity,
                    out error,
                    "streaming lookup",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<InteriorTransitionState>(
                    entityManager,
                    out _,
                    out error,
                    "interior transition",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<InteractionRuntimeState>(
                    entityManager,
                    out _,
                    out error,
                    "interaction runtime",
                    "for save replay"))
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
            out Entity questJournalEntity,
            out Entity runtimeEntity,
            out Entity spawnEntity,
            out Entity playerEntity,
            out Entity viewEntity,
            out string error)
        {
            journalEntity = Entity.Null;
            questJournalEntity = Entity.Null;
            runtimeEntity = Entity.Null;
            spawnEntity = Entity.Null;
            playerEntity = Entity.Null;
            viewEntity = Entity.Null;
            error = null;

            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<WorldJournalState>(
                    entityManager,
                    out journalEntity,
                    out error,
                    "world journal",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<MorrowindQuestJournalState>(
                    entityManager,
                    out questJournalEntity,
                    out error,
                    "quest journal",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOneBufferOwner<PlayerInventoryItem>(
                    entityManager,
                    out runtimeEntity,
                    out error,
                    "runtime inventory",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<RuntimeSpawnState>(
                    entityManager,
                    out spawnEntity,
                    out error,
                    "runtime spawn state",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<PlayerTag>(
                    entityManager,
                    out playerEntity,
                    out error,
                    "player",
                    "for save replay"))
                return false;
            if (!WorldStateEntityQueryUtility.TryGetExactlyOne<PlayerViewComponent>(
                    entityManager,
                    out viewEntity,
                    out error,
                    "player view",
                    "for save replay"))
                return false;

            return true;
        }

        static void PopulateInitializationSpellbook(EntityManager entityManager, ActorKnownSpell[] knownSpells)
        {
            Entity initEntity = WorldStateEntityQueryUtility.GetSingletonEntity<GameInitializationSingleton>(entityManager);
            if (initEntity == Entity.Null)
                return;

            if (!entityManager.HasBuffer<ActorKnownSpell>(initEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<ActorKnownSpell>(initEntity);
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
            }

            DynamicBuffer<ActorKnownSpell> buffer = entityManager.GetBuffer<ActorKnownSpell>(initEntity);
            buffer.Clear();
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationFactions(EntityManager entityManager, PlayerFactionMembership[] factions)
        {
            Entity initEntity = WorldStateEntityQueryUtility.GetSingletonEntity<GameInitializationSingleton>(entityManager);
            if (initEntity == Entity.Null || factions == null)
                return;

            if (!entityManager.HasBuffer<PlayerFactionMembership>(initEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<PlayerFactionMembership>(initEntity);
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
            }

            DynamicBuffer<PlayerFactionMembership> buffer = entityManager.GetBuffer<PlayerFactionMembership>(initEntity);
            buffer.Clear();
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex >= 0)
                    buffer.Add(factions[i]);
            }
        }

        static void ApplyPlayerFactionPayload(EntityManager entityManager, Entity playerEntity, PlayerFactionMembership[] factions)
        {
            if (factions == null)
                return;

            if (!entityManager.HasBuffer<PlayerFactionMembership>(playerEntity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddBuffer<PlayerFactionMembership>(playerEntity);
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
            }

            var buffer = entityManager.GetBuffer<PlayerFactionMembership>(playerEntity);
            buffer.Clear();
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex >= 0)
                    buffer.Add(factions[i]);
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
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
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
            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
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

        static void ClearRuntimeState(EntityManager entityManager, Entity journalEntity, Entity questJournalEntity, Entity runtimeEntity, Entity spawnEntity)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueClearMapDiscovery(entityManager, ref ecb, ensureState: true);
            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
            RefreshMapDiscoveryState(entityManager);
            entityManager.GetBuffer<WorldJournalEntry>(journalEntity).Clear();
            ClearQuestJournal(entityManager, questJournalEntity);
            ClearDialogue(entityManager, questJournalEntity);
            ClearActorDeathCounts(entityManager, questJournalEntity);
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

        static bool ApplyPayload(
            EntityManager entityManager,
            in WorldSavePayload payload,
            Entity journalEntity,
            Entity questJournalEntity,
            Entity runtimeEntity,
            Entity spawnEntity,
            out string error)
        {
            error = null;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            QueueRestoreMapDiscoveryPayload(entityManager, ref ecb, payload.ExteriorMapDiscovery);
            WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
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

            if (!ApplyQuestJournalPayload(entityManager, questJournalEntity, payload.QuestJournal, out error))
                return false;
            if (!ApplyDialoguePayload(entityManager, questJournalEntity, payload.Dialogue, out error))
                return false;
            if (!ApplyActorDeathCounts(entityManager, questJournalEntity, payload.ActorDeathCounts, out error))
                return false;

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

            ApplyTimePayload(entityManager, payload.Time);
            ApplyWeatherPayload(entityManager, payload.Weather);
            return true;
        }

        static void ClearQuestJournal(EntityManager entityManager, Entity questJournalEntity)
        {
            var state = entityManager.GetComponentData<MorrowindQuestJournalState>(questJournalEntity);
            state.NextEntrySequence = 1u;
            entityManager.SetComponentData(questJournalEntity, state);

            var questStates = entityManager.GetBuffer<MorrowindQuestJournalIndex>(questJournalEntity);
            for (int i = 0; i < questStates.Length; i++)
                questStates[i] = default;

            entityManager.GetBuffer<MorrowindQuestJournalEntry>(questJournalEntity).Clear();
            entityManager.GetBuffer<MorrowindQuestJournalRequest>(questJournalEntity).Clear();
        }

        static void ClearDialogue(EntityManager entityManager, Entity dialogueEntity)
        {
            if (!entityManager.HasComponent<MorrowindDialogueState>(dialogueEntity))
                return;

            var state = entityManager.GetComponentData<MorrowindDialogueState>(dialogueEntity);
            state.NextTopicEntrySequence = 1u;
            entityManager.SetComponentData(dialogueEntity, state);

            var knownTopics = entityManager.GetBuffer<MorrowindKnownDialogueTopic>(dialogueEntity);
            for (int i = 0; i < knownTopics.Length; i++)
                knownTopics[i] = default;

            entityManager.GetBuffer<MorrowindTopicJournalEntry>(dialogueEntity).Clear();
            entityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueEntity).Clear();
            entityManager.GetBuffer<MorrowindDialogueRequest>(dialogueEntity).Clear();
        }

        static void ClearActorDeathCounts(EntityManager entityManager, Entity scriptRuntimeEntity)
        {
            if (!entityManager.HasBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity))
                return;

            var deathCounts = entityManager.GetBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity);
            for (int i = 0; i < deathCounts.Length; i++)
                deathCounts[i] = default;
        }

        static bool ApplyActorDeathCounts(EntityManager entityManager, Entity scriptRuntimeEntity, int[] payload, out string error)
        {
            error = null;
            if (payload == null || payload.Length == 0)
                return true;

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
            {
                error = "Runtime content database is not ready for actor death count replay.";
                return false;
            }

            if (!entityManager.HasBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity))
            {
                error = "Morrowind script runtime is missing actor death count state.";
                return false;
            }

            var deathCounts = entityManager.GetBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity);
            if (payload.Length != contentDb.ActorCount || deathCounts.Length != contentDb.ActorCount)
            {
                error = $"Actor death count replay count mismatch: save={payload.Length} buffer={deathCounts.Length} content={contentDb.ActorCount}.";
                return false;
            }

            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] < 0)
                {
                    error = $"Actor death count save has negative count at actor index {i}.";
                    return false;
                }

                deathCounts[i] = new MorrowindActorDeathCount { Count = payload[i] };
            }

            return true;
        }

        static bool ApplyQuestJournalPayload(
            EntityManager entityManager,
            Entity questJournalEntity,
            in MorrowindQuestJournalSavePayload payload,
            out string error)
        {
            error = null;
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
            {
                error = "Runtime content database is not ready for quest journal replay.";
                return false;
            }

            var state = entityManager.GetComponentData<MorrowindQuestJournalState>(questJournalEntity);
            var questStates = entityManager.GetBuffer<MorrowindQuestJournalIndex>(questJournalEntity);
            var entries = entityManager.GetBuffer<MorrowindQuestJournalEntry>(questJournalEntity);
            if (state.QuestCount != questStates.Length || questStates.Length != contentDb.DialogueCount)
            {
                error = $"Quest journal replay count mismatch: state={state.QuestCount} buffer={questStates.Length} content={contentDb.DialogueCount}.";
                return false;
            }

            for (int i = 0; i < payload.States.Length; i++)
            {
                var saved = payload.States[i];
                if ((uint)saved.DialogueIndex >= (uint)questStates.Length)
                {
                    error = $"Quest journal save references invalid dialogue index {saved.DialogueIndex}.";
                    return false;
                }

                questStates[saved.DialogueIndex] = new MorrowindQuestJournalIndex
                {
                    Index = saved.Index,
                    Started = saved.Started,
                    Finished = saved.Finished,
                };
            }

            uint maxSequence = 0u;
            int dialogueInfoCount = contentDb.Data.DialogueInfos?.Length ?? 0;
            for (int i = 0; i < payload.Entries.Length; i++)
            {
                var saved = payload.Entries[i];
                if ((uint)saved.DialogueIndex >= (uint)questStates.Length)
                {
                    error = $"Quest journal entry references invalid dialogue index {saved.DialogueIndex}.";
                    return false;
                }

                if ((uint)saved.InfoIndex >= (uint)dialogueInfoCount)
                {
                    error = $"Quest journal entry references invalid info index {saved.InfoIndex}.";
                    return false;
                }

                entries.Add(new MorrowindQuestJournalEntry
                {
                    Sequence = saved.Sequence,
                    DialogueIndex = saved.DialogueIndex,
                    InfoIndex = saved.InfoIndex,
                    JournalIndex = saved.JournalIndex,
                    Day = saved.Day,
                    Month = saved.Month,
                    DayOfMonth = saved.DayOfMonth,
                    QuestStatus = saved.QuestStatus,
                });
                if (saved.Sequence > maxSequence)
                    maxSequence = saved.Sequence;
            }

            state.NextEntrySequence = math.max(math.max(1u, payload.NextEntrySequence), maxSequence + 1u);
            entityManager.SetComponentData(questJournalEntity, state);
            return true;
        }

        static bool ApplyDialoguePayload(
            EntityManager entityManager,
            Entity dialogueEntity,
            in MorrowindDialogueSavePayload payload,
            out string error)
        {
            error = null;
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
            {
                error = "Runtime content database is not ready for dialogue replay.";
                return false;
            }

            if (!entityManager.HasComponent<MorrowindDialogueState>(dialogueEntity))
            {
                error = "Runtime dialogue state is not ready for save replay.";
                return false;
            }

            var state = entityManager.GetComponentData<MorrowindDialogueState>(dialogueEntity);
            var knownTopics = entityManager.GetBuffer<MorrowindKnownDialogueTopic>(dialogueEntity);
            var entries = entityManager.GetBuffer<MorrowindTopicJournalEntry>(dialogueEntity);
            var factionReactions = entityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueEntity);
            if (state.DialogueCount != knownTopics.Length || knownTopics.Length != contentDb.DialogueCount)
            {
                error = $"Dialogue replay count mismatch: state={state.DialogueCount} buffer={knownTopics.Length} content={contentDb.DialogueCount}.";
                return false;
            }

            for (int i = 0; i < payload.KnownTopicDialogueIndices.Length; i++)
            {
                int dialogueIndex = payload.KnownTopicDialogueIndices[i];
                if ((uint)dialogueIndex >= (uint)knownTopics.Length)
                {
                    error = $"Dialogue save references invalid known topic index {dialogueIndex}.";
                    return false;
                }

                if (contentDb.Data.Dialogues[dialogueIndex].Type != DialogueDefType.Topic)
                {
                    error = $"Dialogue save references non-topic known dialogue index {dialogueIndex}.";
                    return false;
                }

                knownTopics[dialogueIndex] = new MorrowindKnownDialogueTopic { Known = 1 };
            }

            uint maxSequence = 0u;
            int infoCount = contentDb.Data.DialogueInfos?.Length ?? 0;
            for (int i = 0; i < payload.TopicEntries.Length; i++)
            {
                var saved = payload.TopicEntries[i];
                if ((uint)saved.DialogueIndex >= (uint)knownTopics.Length)
                {
                    error = $"Topic journal entry references invalid dialogue index {saved.DialogueIndex}.";
                    return false;
                }

                if (contentDb.Data.Dialogues[saved.DialogueIndex].Type != DialogueDefType.Topic)
                {
                    error = $"Topic journal entry references non-topic dialogue index {saved.DialogueIndex}.";
                    return false;
                }

                if ((uint)saved.InfoIndex >= (uint)infoCount)
                {
                    error = $"Topic journal entry references invalid info index {saved.InfoIndex}.";
                    return false;
                }

                entries.Add(new MorrowindTopicJournalEntry
                {
                    Sequence = saved.Sequence,
                    DialogueIndex = saved.DialogueIndex,
                    InfoIndex = saved.InfoIndex,
                    ActorPlacedRefId = saved.ActorPlacedRefId,
                    ActorId = RuntimeFixedStringUtility.ToFixed128OrDefault(saved.ActorId),
                    Day = saved.Day,
                    Month = saved.Month,
                    DayOfMonth = saved.DayOfMonth,
                });
                if (saved.Sequence > maxSequence)
                    maxSequence = saved.Sequence;
            }

            int factionCount = contentDb.Data.Factions?.Length ?? 0;
            for (int i = 0; i < payload.FactionReactions.Length; i++)
            {
                var saved = payload.FactionReactions[i];
                if ((uint)saved.SourceFactionIndex >= (uint)factionCount)
                {
                    error = $"Dialogue faction reaction references invalid source faction index {saved.SourceFactionIndex}.";
                    return false;
                }

                if ((uint)saved.TargetFactionIndex >= (uint)factionCount)
                {
                    error = $"Dialogue faction reaction references invalid target faction index {saved.TargetFactionIndex}.";
                    return false;
                }

                factionReactions.Add(new MorrowindFactionReactionOverride
                {
                    SourceFactionIndex = saved.SourceFactionIndex,
                    TargetFactionIndex = saved.TargetFactionIndex,
                    Reaction = saved.Reaction,
                });
            }

            state.NextTopicEntrySequence = math.max(math.max(1u, payload.NextTopicEntrySequence), maxSequence + 1u);
            entityManager.SetComponentData(dialogueEntity, state);
            return true;
        }

        static void ApplyTimePayload(EntityManager entityManager, MorrowindTimeSavePayload payload)
        {
            MorrowindTimeState time = payload.TimeScale > 0f
                ? new MorrowindTimeState
                {
                    GameHour = payload.GameHour,
                    DaysPassed = payload.DaysPassed,
                    Day = payload.Day,
                    Month = payload.Month,
                    Year = payload.Year,
                    TimeScale = payload.TimeScale,
                    SimulationTimeScale = payload.SimulationTimeScale > 0f ? payload.SimulationTimeScale : 1f,
                }
                : MorrowindTimeBootstrapSystem.CreateDefaultTime();

            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindTimeState>(entityManager);
            if (entity == Entity.Null)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                entity = ecb.CreateEntity();
                ecb.SetName(entity, new FixedString64Bytes("VVardenfell.TimeState"));
                ecb.AddComponent(entity, time);
                ecb.AddBuffer<MorrowindTimeAdvanceRequest>(entity);
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
                return;
            }

            entityManager.SetComponentData(entity, time);
            if (entityManager.HasBuffer<MorrowindTimeAdvanceRequest>(entity))
                entityManager.GetBuffer<MorrowindTimeAdvanceRequest>(entity).Clear();
        }

        static void ApplyWeatherPayload(EntityManager entityManager, MorrowindWeatherSavePayload payload)
        {
            MorrowindWeatherState weather = payload.Initialized != 0
                ? new MorrowindWeatherState
                {
                    CurrentWeather = payload.CurrentWeather,
                    NextWeather = payload.NextWeather,
                    QueuedWeather = payload.QueuedWeather,
                    Transition = payload.Transition,
                    TransitionFactor = payload.TransitionFactor,
                    TransitionDelta = payload.TransitionDelta,
                    HoursUntilNextChange = payload.HoursUntilNextChange,
                    WeatherUpdateHoursRemaining = payload.WeatherUpdateHoursRemaining > 0f ? payload.WeatherUpdateHoursRemaining : payload.HoursUntilNextChange,
                    RegionHandleValue = payload.RegionHandleValue,
                    RandomState = payload.RandomState,
                    ForcedWeather = payload.ForcedWeather,
                    SecondsUntilThunder = payload.SecondsUntilThunder,
                    LightningBrightness = payload.LightningBrightness,
                    ThunderSequence = payload.ThunderSequence,
                    LastThunderSoundIndex = payload.LastThunderSoundIndex,
                    Initialized = payload.Initialized,
                    Transitioning = payload.Transitioning,
                }
                : MorrowindTimeBootstrapSystem.CreateDefaultWeather();

            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindWeatherState>(entityManager);
            if (entity == Entity.Null)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                entity = ecb.CreateEntity();
                ecb.SetName(entity, new FixedString64Bytes("VVardenfell.WeatherState"));
                ecb.AddComponent(entity, weather);
                ecb.AddBuffer<MorrowindWeatherChangeRequest>(entity);
                ecb.AddBuffer<MorrowindWeatherForceRequest>(entity);
                ecb.AddBuffer<MorrowindRegionWeatherCacheEntry>(entity);
                WorldStateStructuralUtility.PlaybackAndDispose(entityManager, ref ecb);
                entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindWeatherState>(entityManager);
                RestoreRegionWeatherCache(entityManager, entity, payload.RegionWeather);
                return;
            }

            entityManager.SetComponentData(entity, weather);
            EnsureWeatherBuffers(entityManager, entity);
            RestoreRegionWeatherCache(entityManager, entity, payload.RegionWeather);
        }

        static void EnsureWeatherBuffers(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasBuffer<MorrowindWeatherChangeRequest>(entity))
                entityManager.AddBuffer<MorrowindWeatherChangeRequest>(entity);
            if (!entityManager.HasBuffer<MorrowindWeatherForceRequest>(entity))
                entityManager.AddBuffer<MorrowindWeatherForceRequest>(entity);
            if (!entityManager.HasBuffer<MorrowindRegionWeatherCacheEntry>(entity))
                entityManager.AddBuffer<MorrowindRegionWeatherCacheEntry>(entity);

            entityManager.GetBuffer<MorrowindWeatherChangeRequest>(entity).Clear();
            entityManager.GetBuffer<MorrowindWeatherForceRequest>(entity).Clear();
        }

        static void RestoreRegionWeatherCache(EntityManager entityManager, Entity entity, MorrowindRegionWeatherCacheSavePayload[] payload)
        {
            var buffer = entityManager.GetBuffer<MorrowindRegionWeatherCacheEntry>(entity);
            buffer.Clear();
            if (payload == null)
                return;

            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i].RegionHandleValue <= 0 || payload[i].Weather < 0)
                    continue;

                buffer.Add(new MorrowindRegionWeatherCacheEntry
                {
                    RegionHandleValue = payload[i].RegionHandleValue,
                    Weather = payload[i].Weather,
                });
            }
        }
    }
}
