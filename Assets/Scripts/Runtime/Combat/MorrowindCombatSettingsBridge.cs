using System;
using Unity.Entities;
using VVardenfell.Core.Config;

namespace VVardenfell.Runtime.Combat
{
    public static class MorrowindCombatSettingsBridge
    {
        public static void PublishPersisted(EntityManager entityManager)
            => PublishDifficulty(entityManager, ResolvePersistedDifficulty());

        public static void PublishDifficultyInDefaultWorld(int difficulty)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Combat] Cannot publish combat difficulty because the default ECS world is not ready.");

            PublishDifficulty(world.EntityManager, difficulty);
        }

        public static void PublishDifficulty(EntityManager entityManager, int difficulty)
        {
            ValidateDifficulty(difficulty);

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<MorrowindCombatSettings>());
            if (query.IsEmptyIgnoreFilter)
            {
                Entity entity = entityManager.CreateEntity(typeof(MorrowindCombatSettings));
                entityManager.SetName(entity, "VVardenfell.MorrowindCombatSettings");
                entityManager.SetComponentData(entity, new MorrowindCombatSettings { Difficulty = difficulty });
                return;
            }

            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Combat] Multiple MorrowindCombatSettings singletons exist.");

            Entity settingsEntity = query.GetSingletonEntity();
            entityManager.SetComponentData(settingsEntity, new MorrowindCombatSettings { Difficulty = difficulty });
        }

        static int ResolvePersistedDifficulty()
        {
            if (ConfigStorage.TryLoad(out var config) && config != null)
            {
                ValidateDifficulty(config.Difficulty);
                return config.Difficulty;
            }

            return 0;
        }

        static void ValidateDifficulty(int difficulty)
        {
            if (difficulty < -100 || difficulty > 100)
                throw new InvalidOperationException($"[VVardenfell][Combat] Difficulty {difficulty} is outside Morrowind's -100..100 range.");
        }
    }
}
