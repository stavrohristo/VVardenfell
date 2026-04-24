using Unity.Mathematics;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldState
{
    public struct WorldSavePayload
    {
        public float3 PlayerPosition;
        public quaternion PlayerRotation;
        public float PlayerPitchDegrees;
        public ActorRuntimeStatSeed ActorStats;
        public ActorIdentitySet PlayerIdentity;
        public bool InteriorActive;
        public string ActiveInteriorCellId;
        public uint NextJournalSequence;
        public uint NextRuntimeRefId;
        public PlayerInventoryItem[] Inventory;
        public PlayerKnownSpell[] KnownSpells;
        public WorldJournalEntry[] JournalEntries;
    }
}
