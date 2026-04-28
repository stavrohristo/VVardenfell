using System;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
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
            Entity inventoryEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
            Entity transitionEntity = WorldStateEntityQueryUtility.GetSingletonEntity<InteriorTransitionState>(entityManager);
            Entity spawnEntity = WorldStateEntityQueryUtility.GetSingletonEntity<RuntimeSpawnState>(entityManager);
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
            var journalState = entityManager.GetComponentData<WorldJournalState>(journalEntity);
            var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionEntity);
            var spawnState = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            var timePayload = CaptureTimePayload(entityManager);
            var weatherPayload = CaptureWeatherPayload(entityManager);

            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var journalEntries = new WorldJournalEntry[journal.Length];
            for (int i = 0; i < journal.Length; i++)
                journalEntries[i] = journal[i];

            var inventory = entityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            var inventoryEntries = new PlayerInventoryItem[inventory.Length];
            for (int i = 0; i < inventory.Length; i++)
                inventoryEntries[i] = inventory[i];

            PlayerKnownSpell[] knownSpells = Array.Empty<PlayerKnownSpell>();
            if (entityManager.HasBuffer<PlayerKnownSpell>(playerEntity))
            {
                var spellBuffer = entityManager.GetBuffer<PlayerKnownSpell>(playerEntity);
                knownSpells = new PlayerKnownSpell[spellBuffer.Length];
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
            var exteriorMapDiscovery = CaptureExteriorMapDiscovery(entityManager);
            var globalMapOverlay = GlobalMapPresentationCache.CaptureOverlayPayload();

            payload = new WorldSavePayload
            {
                PlayerPosition = playerTransform.Position,
                PlayerRotation = playerTransform.Rotation,
                PlayerPitchDegrees = view.LocalPitchDegrees,
                ActorStats = actorStats,
                PlayerIdentity = identity,
                InteriorActive = transition.InteriorActive != 0 && transition.ActiveInteriorCellId.Length > 0,
                ActiveInteriorCellId = transition.ActiveInteriorCellId.ToString(),
                NextJournalSequence = journalState.NextSequence,
                NextRuntimeRefId = spawnState.NextRuntimeRefId,
                JournalEntries = journalEntries,
                Inventory = inventoryEntries,
                KnownSpells = knownSpells,
                ActiveMagicEffects = activeMagicEffects,
                ExteriorMapDiscovery = exteriorMapDiscovery,
                GlobalMapOverlay = globalMapOverlay,
                Time = timePayload,
                Weather = weatherPayload,
            };
            return true;
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

            return ToPayload(entityManager.GetComponentData<MorrowindWeatherState>(entity));
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
                Transition = weather.Transition,
                TransitionDelta = weather.TransitionDelta,
                HoursUntilNextChange = weather.HoursUntilNextChange,
                RegionHandleValue = weather.RegionHandleValue,
                RandomState = weather.RandomState,
                ForcedWeather = weather.ForcedWeather,
                SecondsUntilThunder = weather.SecondsUntilThunder,
                LightningBrightness = weather.LightningBrightness,
                ThunderSequence = weather.ThunderSequence,
                LastThunderSoundIndex = weather.LastThunderSoundIndex,
                Initialized = weather.Initialized,
                Transitioning = weather.Transitioning,
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
