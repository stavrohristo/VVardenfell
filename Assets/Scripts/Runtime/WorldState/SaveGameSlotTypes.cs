

namespace VVardenfell.Runtime.WorldState
{
    public struct SaveGameSlotMetadata
    {
        public string SlotId;
        public string DisplayName;
        public long CreatedUtcTicks;
        public long LastModifiedUtcTicks;
        public string CharacterName;
        public int PlayerLevel;
        public string LocationName;
        public string CellName;
        public int PayloadVersion;
    }

    public struct SaveGameSlotSummary
    {
        public string SlotId;
        public string DisplayName;
        public string FilePath;
        public long CreatedUtcTicks;
        public long LastModifiedUtcTicks;
        public string CharacterName;
        public int PlayerLevel;
        public string LocationName;
        public string CellName;
        public int PayloadVersion;
        public bool IsValid;
        public bool IsLegacy;
        public string Error;
    }
}
