using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldState
{
    public static partial class WorldSaveStorage
    {
        static bool TryBuildPayload(EntityManager entityManager, out WorldSavePayload payload, out string error)
        {
            payload = default;
            error = null;

            Entity playerEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlayerTag>(entityManager);
            Entity viewEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlayerViewComponent>(entityManager);
            Entity questJournalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindQuestJournalState>(entityManager);
            Entity dialogueEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindDialogueState>(entityManager);
            Entity transitionEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (playerEntity == Entity.Null
                || viewEntity == Entity.Null
                || questJournalEntity == Entity.Null
                || dialogueEntity == Entity.Null
                || !entityManager.HasBuffer<PlayerInventoryItem>(playerEntity)
                || transitionEntity == Entity.Null
                || spawnEntity == Entity.Null)
            {
                error = "Runtime save state is not ready.";
                return false;
            }

            var playerTransform = entityManager.GetComponentData<LocalTransform>(playerEntity);
            var view = entityManager.GetComponentData<PlayerViewComponent>(viewEntity);
            var actorStats = new ActorRuntimeStatSeed
            {
                Attributes = entityManager.GetComponentData<ActorAttributeSet>(playerEntity),
                AttributeBase = entityManager.GetComponentData<ActorAttributeBaseSet>(playerEntity).Value,
                AttributeDamage = entityManager.GetComponentData<ActorAttributeDamageSet>(playerEntity).Value,
                AttributeModifiers = entityManager.GetComponentData<ActorAttributeModifierSet>(playerEntity).Value,
                Skills = entityManager.GetComponentData<ActorSkillSet>(playerEntity),
                SkillBase = entityManager.GetComponentData<ActorSkillBaseSet>(playerEntity).Value,
                SkillDamage = entityManager.GetComponentData<ActorSkillDamageSet>(playerEntity).Value,
                SkillModifiers = entityManager.GetComponentData<ActorSkillModifierSet>(playerEntity).Value,
                Vitals = entityManager.GetComponentData<ActorVitalSet>(playerEntity),
                VitalBase = entityManager.GetComponentData<ActorVitalBaseSet>(playerEntity),
                VitalModifiers = entityManager.GetComponentData<ActorVitalModifierSet>(playerEntity),
                EffectModifiers = entityManager.GetComponentData<ActorEffectStatModifiers>(playerEntity),
            };
            var identity = entityManager.HasComponent<ActorIdentitySet>(playerEntity)
                ? entityManager.GetComponentData<ActorIdentitySet>(playerEntity)
                : ActorIdentitySet.DefaultPlayer();
            var playerCrime = entityManager.HasComponent<PlayerCrimeState>(playerEntity)
                ? entityManager.GetComponentData<PlayerCrimeState>(playerEntity)
                : PlayerCrimeState.Default;
            var playerAppearance = entityManager.HasComponent<PlayerRaceAppearance>(playerEntity)
                ? entityManager.GetComponentData<PlayerRaceAppearance>(playerEntity)
                : new PlayerRaceAppearance { RaceId = identity.RaceName, Male = 1 };
            var playerCustomClass = entityManager.HasComponent<PlayerCustomClass>(playerEntity)
                ? entityManager.GetComponentData<PlayerCustomClass>(playerEntity)
                : default;
            Entity charGenEntity = WorldStateEntityQueryUtility.GetSingletonEntity<CharacterGenerationState>(entityManager);
            var characterGeneration = charGenEntity != Entity.Null
                ? entityManager.GetComponentData<CharacterGenerationState>(charGenEntity)
                : new CharacterGenerationState { Initialized = 1, Finalized = 1 };
            var playerFactions = CapturePlayerFactions(entityManager, playerEntity);
            var questJournalPayload = CaptureQuestJournalPayload(entityManager, questJournalEntity);
            var dialoguePayload = CaptureDialoguePayload(entityManager, dialogueEntity);
            var actorDeathCounts = CaptureActorDeathCounts(entityManager, questJournalEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var spawnState = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            var timePayload = CaptureTimePayload(entityManager);
            var weatherPayload = CaptureWeatherPayload(entityManager);
            var combatPayload = CaptureCombatPayload(entityManager);
            var magicPayload = CaptureMagicPayload(entityManager);
            var scriptPayload = CaptureScriptPayload(entityManager, questJournalEntity);
            var placedRefPayload = CapturePlacedRefStatePayload(entityManager, questJournalEntity);
            var runtimeSpawns = CaptureRuntimeSpawns(entityManager, spawnEntity);

            var inventory = entityManager.GetBuffer<PlayerInventoryItem>(playerEntity);
            var inventoryEntries = new PlayerInventoryItem[inventory.Length];
            for (int i = 0; i < inventory.Length; i++)
                inventoryEntries[i] = inventory[i];

            ActorEquipmentSlot[] playerEquipment = Array.Empty<ActorEquipmentSlot>();
            if (entityManager.HasBuffer<ActorEquipmentSlot>(playerEntity))
            {
                var equipmentBuffer = entityManager.GetBuffer<ActorEquipmentSlot>(playerEntity);
                playerEquipment = new ActorEquipmentSlot[equipmentBuffer.Length];
                for (int i = 0; i < equipmentBuffer.Length; i++)
                    playerEquipment[i] = equipmentBuffer[i];
            }

            ActorKnownSpell[] knownSpells = Array.Empty<ActorKnownSpell>();
            if (entityManager.HasBuffer<ActorKnownSpell>(playerEntity))
            {
                var spellBuffer = entityManager.GetBuffer<ActorKnownSpell>(playerEntity);
                knownSpells = new ActorKnownSpell[spellBuffer.Length];
                for (int i = 0; i < spellBuffer.Length; i++)
                    knownSpells[i] = spellBuffer[i];
            }
            ActorActiveMagicEffect[] activeMagicEffects = Array.Empty<ActorActiveMagicEffect>();
            if (entityManager.HasBuffer<ActorActiveMagicEffect>(playerEntity))
            {
                var effectBuffer = entityManager.GetBuffer<ActorActiveMagicEffect>(playerEntity);
                activeMagicEffects = new ActorActiveMagicEffect[effectBuffer.Length];
                for (int i = 0; i < effectBuffer.Length; i++)
                    activeMagicEffects[i] = effectBuffer[i];
            }
            ActorActiveSpell[] activeSpells = Array.Empty<ActorActiveSpell>();
            if (entityManager.HasBuffer<ActorActiveSpell>(playerEntity))
            {
                var activeSpellBuffer = entityManager.GetBuffer<ActorActiveSpell>(playerEntity);
                activeSpells = new ActorActiveSpell[activeSpellBuffer.Length];
                for (int i = 0; i < activeSpellBuffer.Length; i++)
                    activeSpells[i] = activeSpellBuffer[i];
            }
            ActorUsedPower[] usedPowers = Array.Empty<ActorUsedPower>();
            if (entityManager.HasBuffer<ActorUsedPower>(playerEntity))
            {
                var usedPowerBuffer = entityManager.GetBuffer<ActorUsedPower>(playerEntity);
                usedPowers = new ActorUsedPower[usedPowerBuffer.Length];
                for (int i = 0; i < usedPowerBuffer.Length; i++)
                    usedPowers[i] = usedPowerBuffer[i];
            }
            var exteriorMapDiscovery = CaptureExteriorMapDiscovery(entityManager);
            var globalMapOverlay = GlobalMapPresentationCache.CaptureOverlayPayload();
            var bookReadHistory = CaptureBookReadHistory(entityManager);

            payload = new WorldSavePayload
            {
                PlayerPosition = playerTransform.Position,
                PlayerRotation = playerTransform.Rotation,
                PlayerPitchDegrees = view.LocalPitchDegrees,
                ActorStats = actorStats,
                PlayerIdentity = identity,
                PlayerAppearance = playerAppearance,
                PlayerCustomClass = playerCustomClass,
                CharacterGeneration = characterGeneration,
                PlayerCrime = playerCrime,
                PlayerFactions = playerFactions,
                InteriorActive = transition.InteriorActive != 0 && transition.ActiveInteriorCellId.Length > 0,
                ActiveInteriorCellId = transition.ActiveInteriorCellId.ToString(),
                NextRuntimeRefId = spawnState.NextRuntimeRefId,
                BookReadHistory = bookReadHistory,
                QuestJournal = questJournalPayload,
                Dialogue = dialoguePayload,
                ActorDeathCounts = actorDeathCounts,
                Inventory = inventoryEntries,
                PlayerEquipment = playerEquipment,
                KnownSpells = knownSpells,
                ActiveSpells = activeSpells,
                ActiveMagicEffects = activeMagicEffects,
                UsedPowers = usedPowers,
                ExteriorMapDiscovery = exteriorMapDiscovery,
                GlobalMapOverlay = globalMapOverlay,
                Time = timePayload,
                Weather = weatherPayload,
                Combat = combatPayload,
                Magic = magicPayload,
                Script = scriptPayload,
                PlacedRefs = placedRefPayload,
                RuntimeSpawns = runtimeSpawns,
            };
            return true;
        }

        static RuntimeSpawnedRef[] CaptureRuntimeSpawns(EntityManager entityManager, Entity spawnEntity)
        {
            if (!entityManager.HasBuffer<RuntimeSpawnedRef>(spawnEntity))
                return Array.Empty<RuntimeSpawnedRef>();

            var buffer = entityManager.GetBuffer<RuntimeSpawnedRef>(spawnEntity);
            var result = new RuntimeSpawnedRef[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                result[i] = buffer[i];
            return result;
        }

        static MorrowindScriptSavePayload CaptureScriptPayload(EntityManager entityManager, Entity scriptRuntimeEntity)
        {
            var runtimeState = entityManager.HasComponent<MorrowindScriptRuntimeState>(scriptRuntimeEntity)
                ? entityManager.GetComponentData<MorrowindScriptRuntimeState>(scriptRuntimeEntity)
                : default;

            MorrowindScriptGlobalValue[] globals = Array.Empty<MorrowindScriptGlobalValue>();
            if (entityManager.HasBuffer<MorrowindScriptGlobalValue>(scriptRuntimeEntity))
            {
                var buffer = entityManager.GetBuffer<MorrowindScriptGlobalValue>(scriptRuntimeEntity);
                globals = new MorrowindScriptGlobalValue[buffer.Length];
                for (int i = 0; i < buffer.Length; i++)
                    globals[i] = buffer[i];
            }

            var globalScripts = CaptureGlobalScripts(entityManager);
            var objectScripts = CaptureObjectScripts(entityManager, scriptRuntimeEntity);
            return new MorrowindScriptSavePayload
            {
                NextAudioRequestSequence = runtimeState.NextAudioRequestSequence,
                RandomState = runtimeState.RandomState,
                Globals = globals,
                GlobalScripts = globalScripts,
                ObjectScripts = objectScripts,
            };
        }

        static MorrowindGlobalScriptSavePayload[] CaptureGlobalScripts(EntityManager entityManager)
        {
            EntityQuery query = GlobalScriptQueryCache.Get(entityManager);
            int count = query.CalculateEntityCount();
            if (count == 0)
                return Array.Empty<MorrowindGlobalScriptSavePayload>();

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var globals = query.ToComponentDataArray<MorrowindGlobalScriptInstance>(Unity.Collections.Allocator.Temp);
            using var instances = query.ToComponentDataArray<MorrowindScriptInstance>(Unity.Collections.Allocator.Temp);
            var result = new MorrowindGlobalScriptSavePayload[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = new MorrowindGlobalScriptSavePayload
                {
                    ProgramIndex = instances[i].ProgramIndex,
                    ProgramCounter = instances[i].ProgramCounter,
                    Status = instances[i].Status,
                    SuppressActivation = instances[i].SuppressActivation,
                    DisabledReason = instances[i].DisabledReason.ToString(),
                    TargetPlacedRefId = globals[i].TargetPlacedRefId,
                    Locals = CaptureScriptLocals(entityManager, entities[i]),
                };
            }

            return result;
        }

        static MorrowindObjectScriptSavePayload[] CaptureObjectScripts(EntityManager entityManager, Entity scriptRuntimeEntity)
        {
            var results = new List<MorrowindObjectScriptSavePayload>();
            if (entityManager.HasBuffer<PlacedRefOverlayScriptInstance>(scriptRuntimeEntity))
            {
                var instances = entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(scriptRuntimeEntity);
                var locals = entityManager.HasBuffer<PlacedRefOverlayScriptLocalValue>(scriptRuntimeEntity)
                    ? entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(scriptRuntimeEntity)
                    : default;
                for (int i = 0; i < instances.Length; i++)
                {
                    var instance = instances[i];
                    if (instance.PlacedRefId == 0u)
                        continue;

                    UpsertObjectScriptPayload(results, new MorrowindObjectScriptSavePayload
                    {
                        PlacedRefId = instance.PlacedRefId,
                        ProgramIndex = instance.ProgramIndex,
                        ProgramCounter = instance.ProgramCounter,
                        Status = instance.Status,
                        SuppressActivation = instance.SuppressActivation,
                        DisabledReason = instance.DisabledReason.ToString(),
                        Locals = CaptureSavedScriptLocals(locals, instance.PlacedRefId, instance.ProgramIndex),
                    });
                }
            }

            EntityQuery query = ObjectScriptQueryCache.Get(entityManager);
            int count = query.CalculateEntityCount();
            if (count == 0)
                return results.ToArray();

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Unity.Collections.Allocator.Temp);
            using var instancesLive = query.ToComponentDataArray<MorrowindScriptInstance>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u)
                    continue;

                var instance = instancesLive[i];
                UpsertObjectScriptPayload(results, new MorrowindObjectScriptSavePayload
                {
                    PlacedRefId = placedRefId,
                    ProgramIndex = instance.ProgramIndex,
                    ProgramCounter = instance.ProgramCounter,
                    Status = instance.Status,
                    SuppressActivation = instance.SuppressActivation,
                    DisabledReason = instance.DisabledReason.ToString(),
                    Locals = CaptureScriptLocals(entityManager, entities[i]),
                });
            }

            return results.ToArray();
        }

        static MorrowindScriptLocalValue[] CaptureScriptLocals(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasBuffer<MorrowindScriptLocalValue>(entity))
                return Array.Empty<MorrowindScriptLocalValue>();

            var buffer = entityManager.GetBuffer<MorrowindScriptLocalValue>(entity);
            var result = new MorrowindScriptLocalValue[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                result[i] = buffer[i];
            return result;
        }

        static MorrowindScriptLocalValue[] CaptureSavedScriptLocals(
            DynamicBuffer<PlacedRefOverlayScriptLocalValue> locals,
            uint placedRefId,
            int programIndex)
        {
            if (!locals.IsCreated)
                return Array.Empty<MorrowindScriptLocalValue>();

            int count = 0;
            for (int i = 0; i < locals.Length; i++)
            {
                if (locals[i].PlacedRefId == placedRefId && locals[i].ProgramIndex == programIndex)
                    count++;
            }

            if (count == 0)
                return Array.Empty<MorrowindScriptLocalValue>();

            var result = new MorrowindScriptLocalValue[count];
            for (int i = 0; i < locals.Length; i++)
            {
                var local = locals[i];
                if (local.PlacedRefId != placedRefId || local.ProgramIndex != programIndex)
                    continue;

                if ((uint)local.LocalIndex >= (uint)result.Length)
                    throw new InvalidOperationException($"[VVardenfell][Save] saved local index {local.LocalIndex} is outside compact local count {result.Length} for ref 0x{placedRefId:X8}.");
                result[local.LocalIndex] = local.Value;
            }

            return result;
        }

        static void UpsertObjectScriptPayload(List<MorrowindObjectScriptSavePayload> results, in MorrowindObjectScriptSavePayload payload)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].PlacedRefId == payload.PlacedRefId && results[i].ProgramIndex == payload.ProgramIndex)
                {
                    results[i] = payload;
                    return;
                }
            }

            results.Add(payload);
        }

        static PlacedRefOverlaySavePayload CapturePlacedRefStatePayload(EntityManager entityManager, Entity scriptRuntimeEntity)
        {
            var entries = new List<PlacedRefOverlayEntrySavePayload>();
            var inventories = new List<PlacedRefOverlayActorInventorySavePayload>();
            var containers = new List<PlacedRefOverlayContainerSavePayload>();

            if (entityManager.HasBuffer<PlacedRefOverlayState>(scriptRuntimeEntity))
            {
                var states = entityManager.GetBuffer<PlacedRefOverlayState>(scriptRuntimeEntity);
                for (int i = 0; i < states.Length; i++)
                    UpsertPlacedRefState(entries, ToPayload(states[i]));
            }

            CaptureLivePlacedRefState(entityManager, entries);
            CaptureActorInventoryPayload(entityManager, scriptRuntimeEntity, inventories);
            CaptureContainerPayload(entityManager, scriptRuntimeEntity, containers);
            return new PlacedRefOverlaySavePayload
            {
                Entries = entries.ToArray(),
                ActorInventories = inventories.ToArray(),
                Containers = containers.ToArray(),
            };
        }

        static void CaptureLivePlacedRefState(EntityManager entityManager, List<PlacedRefOverlayEntrySavePayload> entries)
        {
            EntityQuery query = PlacedRefStateQueryCache.Get(entityManager);
            int count = query.CalculateEntityCount();
            if (count == 0)
                return;

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                Entity entity = entities[i];
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u)
                    continue;

                var payload = FindPlacedRefState(entries, placedRefId);
                payload.PlacedRefId = placedRefId;

                if (entityManager.HasComponent<PlacedRefRuntimeState>(entity))
                {
                    var state = entityManager.GetComponentData<PlacedRefRuntimeState>(entity);
                    if (state.Disabled != 0)
                    {
                        payload.HasDisabled = 1;
                        payload.Disabled = state.Disabled;
                    }
                }

                if (entityManager.HasComponent<PlacedRefLockState>(entity))
                {
                    var lockState = entityManager.GetComponentData<PlacedRefLockState>(entity);
                    payload.HasLock = 1;
                    payload.LockLevel = lockState.LockLevel;
                    payload.Locked = lockState.Locked;
                    payload.KeyId = lockState.KeyId.ToString();
                    payload.TrapId = lockState.TrapId.ToString();
                }

                if (ScriptVisibleSaveStateUtility.TransformDiffersFromInitial(entityManager, entity)
                    && entityManager.HasComponent<LocalTransform>(entity)
                    && entityManager.HasComponent<LogicalRefLocation>(entity))
                {
                    var transform = entityManager.GetComponentData<LocalTransform>(entity);
                    var location = entityManager.GetComponentData<LogicalRefLocation>(entity);
                    payload.HasTransform = 1;
                    payload.Position = transform.Position;
                    payload.Rotation = transform.Rotation;
                    payload.Scale = transform.Scale;
                    payload.ExteriorCell = location.ExteriorCell;
                    payload.InteriorCellId = location.InteriorCellId.ToString();
                    payload.InteriorCellHash = location.InteriorCellHash;
                    payload.IsInterior = location.IsInterior;
                }

                if (payload.HasRemoved != 0 || payload.HasDisabled != 0 || payload.HasLock != 0 || payload.HasTransform != 0 || payload.HasContainer != 0)
                    UpsertPlacedRefState(entries, payload);
            }
        }

        static void CaptureActorInventoryPayload(
            EntityManager entityManager,
            Entity scriptRuntimeEntity,
            List<PlacedRefOverlayActorInventorySavePayload> inventories)
        {
            if (entityManager.HasBuffer<PlacedRefOverlayActorInventoryItem>(scriptRuntimeEntity))
            {
                var saved = entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(scriptRuntimeEntity);
                for (int i = 0; i < saved.Length; i++)
                {
                    uint placedRefId = saved[i].PlacedRefId;
                    if (placedRefId == 0u || ContainsActorInventory(inventories, placedRefId))
                        continue;

                    inventories.Add(new PlacedRefOverlayActorInventorySavePayload
                    {
                        PlacedRefId = placedRefId,
                        Items = CaptureSavedActorInventory(saved, placedRefId),
                    });
                }
            }

            EntityQuery query = ActorInventoryQueryCache.Get(entityManager);
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u || !ContainsActorInventory(inventories, placedRefId))
                    continue;

                UpsertActorInventory(inventories, placedRefId, CaptureActorInventory(entityManager, entities[i]));
            }
        }

        static ActorInventoryItem[] CaptureSavedActorInventory(DynamicBuffer<PlacedRefOverlayActorInventoryItem> saved, uint placedRefId)
        {
            int count = 0;
            for (int i = 0; i < saved.Length; i++)
            {
                if (saved[i].PlacedRefId == placedRefId)
                    count++;
            }

            var items = new ActorInventoryItem[count];
            int index = 0;
            for (int i = 0; i < saved.Length; i++)
            {
                if (saved[i].PlacedRefId == placedRefId)
                    items[index++] = saved[i].Item;
            }

            return items;
        }

        static ActorInventoryItem[] CaptureActorInventory(EntityManager entityManager, Entity entity)
        {
            var buffer = entityManager.GetBuffer<ActorInventoryItem>(entity);
            var items = new ActorInventoryItem[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                items[i] = buffer[i];
            return items;
        }

        static void CaptureContainerPayload(
            EntityManager entityManager,
            Entity scriptRuntimeEntity,
            List<PlacedRefOverlayContainerSavePayload> containers)
        {
            if (!entityManager.HasBuffer<PlacedRefOverlayState>(scriptRuntimeEntity)
                || !entityManager.HasBuffer<PlacedRefOverlayContainerItem>(scriptRuntimeEntity))
            {
                return;
            }

            var states = entityManager.GetBuffer<PlacedRefOverlayState>(scriptRuntimeEntity);
            var overlayItems = entityManager.GetBuffer<PlacedRefOverlayContainerItem>(scriptRuntimeEntity);
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].HasContainer == 0 || states[i].PlacedRefId == 0u || ContainsContainer(containers, states[i].PlacedRefId))
                    continue;

                containers.Add(new PlacedRefOverlayContainerSavePayload
                {
                    PlacedRefId = states[i].PlacedRefId,
                    Items = CaptureContainerItems(overlayItems, states[i].PlacedRefId),
                });
            }
        }

        static ContainerSessionItem[] CaptureContainerItems(DynamicBuffer<PlacedRefOverlayContainerItem> overlayItems, uint placedRefId)
        {
            int count = 0;
            for (int i = 0; i < overlayItems.Length; i++)
            {
                if (overlayItems[i].PlacedRefId == placedRefId)
                    count++;
            }

            var items = new ContainerSessionItem[count];
            int index = 0;
            for (int i = 0; i < overlayItems.Length; i++)
            {
                if (overlayItems[i].PlacedRefId == placedRefId)
                    items[index++] = overlayItems[i].Item;
            }

            return items;
        }

        static PlacedRefOverlayEntrySavePayload ToPayload(in PlacedRefOverlayState state)
        {
            return new PlacedRefOverlayEntrySavePayload
            {
                PlacedRefId = state.PlacedRefId,
                HasRemoved = state.HasRemoved,
                Removed = state.Removed,
                RemovedContent = state.RemovedContent,
                HasDisabled = state.HasDisabled,
                Disabled = state.Disabled,
                HasLock = state.HasLock,
                LockLevel = state.LockLevel,
                Locked = state.Locked,
                KeyId = state.KeyId.ToString(),
                TrapId = state.TrapId.ToString(),
                HasTransform = state.HasTransform,
                Position = state.Position,
                Rotation = state.Rotation,
                Scale = state.Scale,
                ExteriorCell = state.ExteriorCell,
                InteriorCellId = state.InteriorCellId.ToString(),
                InteriorCellHash = state.InteriorCellHash,
                IsInterior = state.IsInterior,
                HasContainer = state.HasContainer,
            };
        }

        static PlacedRefOverlayEntrySavePayload FindPlacedRefState(List<PlacedRefOverlayEntrySavePayload> entries, uint placedRefId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlacedRefId == placedRefId)
                    return entries[i];
            }

            return default;
        }

        static void UpsertPlacedRefState(List<PlacedRefOverlayEntrySavePayload> entries, in PlacedRefOverlayEntrySavePayload payload)
        {
            if (payload.PlacedRefId == 0u)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlacedRefId == payload.PlacedRefId)
                {
                    entries[i] = payload;
                    return;
                }
            }

            entries.Add(payload);
        }

        static bool ContainsActorInventory(List<PlacedRefOverlayActorInventorySavePayload> inventories, uint placedRefId)
        {
            for (int i = 0; i < inventories.Count; i++)
            {
                if (inventories[i].PlacedRefId == placedRefId)
                    return true;
            }

            return false;
        }

        static bool ContainsContainer(List<PlacedRefOverlayContainerSavePayload> containers, uint placedRefId)
        {
            for (int i = 0; i < containers.Count; i++)
            {
                if (containers[i].PlacedRefId == placedRefId)
                    return true;
            }

            return false;
        }

        static void UpsertActorInventory(List<PlacedRefOverlayActorInventorySavePayload> inventories, uint placedRefId, ActorInventoryItem[] items)
        {
            for (int i = 0; i < inventories.Count; i++)
            {
                if (inventories[i].PlacedRefId == placedRefId)
                {
                    inventories[i] = new PlacedRefOverlayActorInventorySavePayload
                    {
                        PlacedRefId = placedRefId,
                        Items = items,
                    };
                    return;
                }
            }

            inventories.Add(new PlacedRefOverlayActorInventorySavePayload
            {
                PlacedRefId = placedRefId,
                Items = items,
            });
        }

        static BookReadHistoryEntry[] CaptureBookReadHistory(EntityManager entityManager)
        {
            Entity entity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<BookReadHistoryEntry>(entityManager);
            if (entity == Entity.Null || !entityManager.HasBuffer<BookReadHistoryEntry>(entity))
                return Array.Empty<BookReadHistoryEntry>();

            var buffer = entityManager.GetBuffer<BookReadHistoryEntry>(entity);
            var result = new BookReadHistoryEntry[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                result[i] = buffer[i];
            return result;
        }

        static MorrowindCombatSavePayload CaptureCombatPayload(EntityManager entityManager)
        {
            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindCombatRuntimeState>(entityManager);
            if (entity == Entity.Null)
                return default;

            var state = entityManager.GetComponentData<MorrowindCombatRuntimeState>(entity);
            return new MorrowindCombatSavePayload
            {
                RandomState = state.RandomState,
                Initialized = 1,
            };
        }

        static MorrowindMagicSavePayload CaptureMagicPayload(EntityManager entityManager)
        {
            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindMagicRuntimeState>(entityManager);
            if (entity == Entity.Null)
                return default;

            var state = entityManager.GetComponentData<MorrowindMagicRuntimeState>(entity);
            var payload = new MorrowindMagicSavePayload
            {
                RandomState = state.RandomState,
                NextActiveSpellId = state.NextActiveSpellId,
                Initialized = 1,
            };
            Entity spellWindowEntity = WorldStateEntityQueryUtility.GetSingletonEntity<SpellWindowState>(entityManager);
            if (spellWindowEntity != Entity.Null)
            {
                var spellWindow = entityManager.GetComponentData<SpellWindowState>(spellWindowEntity);
                payload.SelectedSourceKind = spellWindow.SelectedSourceKind;
                payload.SelectedSpell = spellWindow.SelectedSpell;
                payload.SelectedInventoryIndex = spellWindow.SelectedInventoryIndex;
                payload.SelectedItemContent = spellWindow.SelectedItemContent;
                payload.SelectedEnchantment = spellWindow.SelectedEnchantment;
            }

            return payload;
        }

        static int[] CaptureActorDeathCounts(EntityManager entityManager, Entity scriptRuntimeEntity)
        {
            if (!entityManager.HasBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity))
                return Array.Empty<int>();

            var counts = entityManager.GetBuffer<MorrowindActorDeathCount>(scriptRuntimeEntity);
            var result = new int[counts.Length];
            for (int i = 0; i < counts.Length; i++)
                result[i] = counts[i].Count;
            return result;
        }

        static PlayerFactionMembership[] CapturePlayerFactions(EntityManager entityManager, Entity playerEntity)
        {
            if (!entityManager.HasBuffer<PlayerFactionMembership>(playerEntity))
                return Array.Empty<PlayerFactionMembership>();

            var buffer = entityManager.GetBuffer<PlayerFactionMembership>(playerEntity);
            var result = new PlayerFactionMembership[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                result[i] = buffer[i];
            return result;
        }

        static MorrowindDialogueSavePayload CaptureDialoguePayload(EntityManager entityManager, Entity dialogueEntity)
        {
            var state = entityManager.GetComponentData<MorrowindDialogueState>(dialogueEntity);
            var knownTopics = entityManager.GetBuffer<MorrowindKnownDialogueTopic>(dialogueEntity);
            var known = new List<int>();
            for (int i = 0; i < knownTopics.Length; i++)
            {
                if (knownTopics[i].Known != 0)
                    known.Add(i);
            }

            var topicEntries = entityManager.GetBuffer<MorrowindTopicJournalEntry>(dialogueEntity);
            var entryPayloads = new MorrowindTopicJournalEntrySavePayload[topicEntries.Length];
            for (int i = 0; i < topicEntries.Length; i++)
            {
                entryPayloads[i] = new MorrowindTopicJournalEntrySavePayload
                {
                    Sequence = topicEntries[i].Sequence,
                    DialogueIndex = topicEntries[i].DialogueIndex,
                    InfoIndex = topicEntries[i].InfoIndex,
                    ActorPlacedRefId = topicEntries[i].ActorPlacedRefId,
                    ActorId = topicEntries[i].ActorId.ToString(),
                    Day = topicEntries[i].Day,
                    Month = topicEntries[i].Month,
                    DayOfMonth = topicEntries[i].DayOfMonth,
                };
            }

            var factionReactions = entityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueEntity);
            var factionReactionPayloads = new MorrowindFactionReactionSavePayload[factionReactions.Length];
            for (int i = 0; i < factionReactions.Length; i++)
            {
                factionReactionPayloads[i] = new MorrowindFactionReactionSavePayload
                {
                    SourceFactionIndex = factionReactions[i].SourceFactionIndex,
                    TargetFactionIndex = factionReactions[i].TargetFactionIndex,
                    Reaction = factionReactions[i].Reaction,
                };
            }

            return new MorrowindDialogueSavePayload
            {
                NextTopicEntrySequence = state.NextTopicEntrySequence,
                KnownTopicDialogueIndices = known.ToArray(),
                TopicEntries = entryPayloads,
                FactionReactions = factionReactionPayloads,
            };
        }

        static MorrowindQuestJournalSavePayload CaptureQuestJournalPayload(EntityManager entityManager, Entity questJournalEntity)
        {
            var state = entityManager.GetComponentData<MorrowindQuestJournalState>(questJournalEntity);
            var questStates = entityManager.GetBuffer<MorrowindQuestJournalIndex>(questJournalEntity);
            var nonDefaultStates = new List<MorrowindQuestJournalStateSavePayload>();
            for (int i = 0; i < questStates.Length; i++)
            {
                var quest = questStates[i];
                if (quest.Index == 0 && quest.Started == 0 && quest.Finished == 0)
                    continue;

                nonDefaultStates.Add(new MorrowindQuestJournalStateSavePayload
                {
                    DialogueIndex = i,
                    Index = quest.Index,
                    Started = quest.Started,
                    Finished = quest.Finished,
                });
            }

            var entries = entityManager.GetBuffer<MorrowindQuestJournalEntry>(questJournalEntity);
            var entryPayloads = new MorrowindQuestJournalEntrySavePayload[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                entryPayloads[i] = new MorrowindQuestJournalEntrySavePayload
                {
                    Sequence = entries[i].Sequence,
                    DialogueIndex = entries[i].DialogueIndex,
                    InfoIndex = entries[i].InfoIndex,
                    JournalIndex = entries[i].JournalIndex,
                    Day = entries[i].Day,
                    Month = entries[i].Month,
                    DayOfMonth = entries[i].DayOfMonth,
                    QuestStatus = entries[i].QuestStatus,
                };
            }

            return new MorrowindQuestJournalSavePayload
            {
                NextEntrySequence = state.NextEntrySequence,
                States = nonDefaultStates.ToArray(),
                Entries = entryPayloads,
            };
        }

        static MorrowindTimeSavePayload CaptureTimePayload(EntityManager entityManager)
        {
            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindTimeState>(entityManager);
            if (entity == Entity.Null)
            {
                var fallback = MorrowindTimeBootstrapSystem.CreateDefaultTime();
                return ToPayload(fallback);
            }

            return ToPayload(entityManager.GetComponentData<MorrowindTimeState>(entity));
        }

        static MorrowindWeatherSavePayload CaptureWeatherPayload(EntityManager entityManager)
        {
            Entity entity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindWeatherState>(entityManager);
            if (entity == Entity.Null)
                return ToPayload(MorrowindTimeBootstrapSystem.CreateDefaultWeather());

            var payload = ToPayload(entityManager.GetComponentData<MorrowindWeatherState>(entity));
            if (entityManager.HasBuffer<MorrowindRegionWeatherCacheEntry>(entity))
            {
                var cache = entityManager.GetBuffer<MorrowindRegionWeatherCacheEntry>(entity);
                payload.RegionWeather = new MorrowindRegionWeatherCacheSavePayload[cache.Length];
                for (int i = 0; i < cache.Length; i++)
                {
                    payload.RegionWeather[i] = new MorrowindRegionWeatherCacheSavePayload
                    {
                        RegionHandleValue = cache[i].RegionHandleValue,
                        Weather = cache[i].Weather,
                    };
                }
            }
            if (entityManager.HasBuffer<MorrowindRegionWeatherOverrideEntry>(entity))
            {
                var overrides = entityManager.GetBuffer<MorrowindRegionWeatherOverrideEntry>(entity);
                payload.RegionWeatherOverrides = new MorrowindRegionWeatherOverrideSavePayload[overrides.Length];
                for (int i = 0; i < overrides.Length; i++)
                {
                    payload.RegionWeatherOverrides[i] = new MorrowindRegionWeatherOverrideSavePayload
                    {
                        RegionHandleValue = overrides[i].RegionHandleValue,
                        ClearChance = overrides[i].ClearChance,
                        CloudyChance = overrides[i].CloudyChance,
                        FoggyChance = overrides[i].FoggyChance,
                        OvercastChance = overrides[i].OvercastChance,
                        RainChance = overrides[i].RainChance,
                        ThunderChance = overrides[i].ThunderChance,
                        AshChance = overrides[i].AshChance,
                        BlightChance = overrides[i].BlightChance,
                        SnowChance = overrides[i].SnowChance,
                        BlizzardChance = overrides[i].BlizzardChance,
                    };
                }
            }
            return payload;
        }

        static MorrowindTimeSavePayload ToPayload(MorrowindTimeState time)
        {
            return new MorrowindTimeSavePayload
            {
                GameHour = time.GameHour,
                DaysPassed = time.DaysPassed,
                Day = time.Day,
                Month = time.Month,
                Year = time.Year,
                TimeScale = time.TimeScale
            };
        }

        static MorrowindWeatherSavePayload ToPayload(MorrowindWeatherState weather)
        {
            return new MorrowindWeatherSavePayload
            {
                CurrentWeather = weather.CurrentWeather,
                NextWeather = weather.NextWeather,
                QueuedWeather = weather.QueuedWeather,
                Transition = weather.Transition,
                TransitionFactor = weather.TransitionFactor,
                TransitionDelta = weather.TransitionDelta,
                HoursUntilNextChange = weather.HoursUntilNextChange,
                WeatherUpdateHoursRemaining = weather.WeatherUpdateHoursRemaining,
                RegionHandleValue = weather.RegionHandleValue,
                RandomState = weather.RandomState,
                ForcedWeather = weather.ForcedWeather,
                SecondsUntilThunder = weather.SecondsUntilThunder,
                LightningBrightness = weather.LightningBrightness,
                ThunderSequence = weather.ThunderSequence,
                LastThunderSoundIndex = weather.LastThunderSoundIndex,
                Initialized = weather.Initialized,
                Transitioning = weather.Transitioning,
                RegionWeather = Array.Empty<MorrowindRegionWeatherCacheSavePayload>(),
            };
        }

        static LocalMapDiscoveryTilePayload[] CaptureExteriorMapDiscovery(EntityManager entityManager)
        {
            EntityQuery query = ExteriorMapDiscoveryQueryCache.Get(entityManager);
            int count = query.CalculateEntityCount();
            if (count == 0)
                return Array.Empty<LocalMapDiscoveryTilePayload>();

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var tiles = query.ToComponentDataArray<ExteriorMapDiscoveryTile>(Unity.Collections.Allocator.Temp);
            var payloads = new LocalMapDiscoveryTilePayload[entities.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                var samples = entityManager.GetBuffer<ExteriorMapDiscoverySample>(entities[i]);
                var alpha = new byte[samples.Length];
                for (int s = 0; s < samples.Length; s++)
                    alpha[s] = samples[s].Alpha;

                int resolution = samples.Length > 0 ? (int)Math.Round(Math.Sqrt(samples.Length)) : 0;
                payloads[i] = new LocalMapDiscoveryTilePayload
                {
                    Cell = tiles[i].Cell,
                    Resolution = resolution,
                    Alpha = alpha,
                };
            }

            return payloads;
        }

        static class ExteriorMapDiscoveryQueryCache
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
                    ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                    ComponentType.ReadOnly<ExteriorMapDiscoverySample>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class GlobalScriptQueryCache
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
                    ComponentType.ReadOnly<MorrowindGlobalScriptInstance>(),
                    ComponentType.ReadOnly<MorrowindScriptInstance>(),
                    ComponentType.ReadOnly<MorrowindScriptLocalValue>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class ObjectScriptQueryCache
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
                    ComponentType.ReadOnly<PlacedRefIdentity>(),
                    ComponentType.ReadOnly<MorrowindScriptInstance>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class PlacedRefStateQueryCache
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
                    ComponentType.ReadOnly<LogicalRefTag>(),
                    ComponentType.ReadOnly<PlacedRefIdentity>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class ActorInventoryQueryCache
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
                    ComponentType.ReadOnly<PlacedRefIdentity>(),
                    ComponentType.ReadOnly<ActorInventoryItem>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
