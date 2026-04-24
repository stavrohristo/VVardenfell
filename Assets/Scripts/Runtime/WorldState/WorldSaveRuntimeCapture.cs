using System;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
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
            };
            return true;
        }
    }
}
