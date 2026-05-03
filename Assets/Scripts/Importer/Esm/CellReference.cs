namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// A single object reference inside a cell — placement of a base object (STAT/ACTI/NPC_/etc).
    /// Coordinates are in Morrowind world units, rotations in radians.
    /// </summary>
    public readonly struct CellReference
    {
        public readonly uint FormId;
        public readonly string BaseId;      // refId of the base record (e.g. "ex_common_house_01")
        public readonly float PosX, PosY, PosZ;
        public readonly float RotX, RotY, RotZ;
        public readonly float Scale;
        public readonly bool Deleted;
        public readonly bool IsDoor;
        public readonly string SoulId;
        public readonly int LockLevel;
        public readonly string KeyId;
        public readonly string TrapId;
        public readonly string DoorDestCell; // non-empty only for teleport doors
        public readonly float DoorDestX, DoorDestY, DoorDestZ;
        public readonly float DoorDestRotX, DoorDestRotY, DoorDestRotZ;

        public CellReference(
            uint formId, string baseId,
            float px, float py, float pz, float rx, float ry, float rz,
            float scale, bool deleted,
            string soulId, int lockLevel, string keyId, string trapId,
            bool isDoor, string doorDestCell,
            float ddx, float ddy, float ddz, float ddrx, float ddry, float ddrz)
        {
            FormId = formId;
            BaseId = baseId;
            PosX = px; PosY = py; PosZ = pz;
            RotX = rx; RotY = ry; RotZ = rz;
            Scale = scale;
            Deleted = deleted;
            SoulId = soulId ?? string.Empty;
            LockLevel = lockLevel;
            KeyId = keyId ?? string.Empty;
            TrapId = trapId ?? string.Empty;
            IsDoor = isDoor;
            DoorDestCell = doorDestCell;
            DoorDestX = ddx; DoorDestY = ddy; DoorDestZ = ddz;
            DoorDestRotX = ddrx; DoorDestRotY = ddry; DoorDestRotZ = ddrz;
        }
    }
}
