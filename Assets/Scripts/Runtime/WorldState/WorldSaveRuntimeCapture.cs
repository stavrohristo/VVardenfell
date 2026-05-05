using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Combat;
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
            Entity journalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<WorldJournalState>(entityManager);
            Entity questJournalEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindQuestJournalState>(entityManager);
            Entity dialogueEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindDialogueState>(entityManager);
            Entity transitionEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
            if (playerEntity == Entity.Null
                || viewEntity == Entity.Null
                || journalEntity == Entity.Null
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
                Skills = entityManager.GetComponentData<ActorSkillSet>(playerEntity),
                Vitals = entityManager.GetComponentData<ActorVitalSet>(playerEntity),
                EffectModifiers = entityManager.GetComponentData<ActorEffectStatModifiers>(playerEntity),
            };
            var identity = entityManager.HasComponent<ActorIdentitySet>(playerEntity)
                ? entityManager.GetComponentData<ActorIdentitySet>(playerEntity)
                : ActorIdentitySet.DefaultPlayer();
            var playerCrime = entityManager.HasComponent<PlayerCrimeState>(playerEntity)
                ? entityManager.GetComponentData<PlayerCrimeState>(playerEntity)
                : PlayerCrimeState.Default;
            var playerFactions = CapturePlayerFactions(entityManager, playerEntity);
            var journalState = entityManager.GetComponentData<WorldJournalState>(journalEntity);
            var questJournalPayload = CaptureQuestJournalPayload(entityManager, questJournalEntity);
            var dialoguePayload = CaptureDialoguePayload(entityManager, dialogueEntity);
            var actorDeathCounts = CaptureActorDeathCounts(entityManager, questJournalEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var spawnState = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            var timePayload = CaptureTimePayload(entityManager);
            var weatherPayload = CaptureWeatherPayload(entityManager);
            var combatPayload = CaptureCombatPayload(entityManager);

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var journalEntries = new WorldJournalEntry[journal.Length];
            for (int i = 0; i < journal.Length; i++)
                journalEntries[i] = journal[i];

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

            payload = new WorldSavePayload
            {
                PlayerPosition = playerTransform.Position,
                PlayerRotation = playerTransform.Rotation,
                PlayerPitchDegrees = view.LocalPitchDegrees,
                ActorStats = actorStats,
                PlayerIdentity = identity,
                PlayerCrime = playerCrime,
                PlayerFactions = playerFactions,
                InteriorActive = transition.InteriorActive != 0 && transition.ActiveInteriorCellId.Length > 0,
                ActiveInteriorCellId = transition.ActiveInteriorCellId.ToString(),
                NextJournalSequence = journalState.NextSequence,
                NextRuntimeRefId = spawnState.NextRuntimeRefId,
                JournalEntries = journalEntries,
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
            };
            return true;
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
                TimeScale = time.TimeScale,
                SimulationTimeScale = time.SimulationTimeScale,
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
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ExteriorMapDiscoveryTile>(),
                ComponentType.ReadOnly<ExteriorMapDiscoverySample>());
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
    }
}
