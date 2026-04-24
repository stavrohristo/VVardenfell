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
        public ActorActiveMagicEffect[] ActiveMagicEffects;
        public LocalMapDiscoveryTilePayload[] ExteriorMapDiscovery;
        public GlobalMapOverlayPayload GlobalMapOverlay;
        public WorldJournalEntry[] JournalEntries;
    }

    public struct LocalMapDiscoveryTilePayload
    {
        public int2 Cell;
        public int Resolution;
        public byte[] Alpha;
    }

    public struct GlobalMapOverlayPayload
    {
        public int2 MinCell;
        public int2 MaxCell;
        public int CellPixelSize;
        public int Width;
        public int Height;
        public int2[] VisitedCells;
        public byte[] PngBytes;
    }
}
