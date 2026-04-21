namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Minimal indexed record: record type, refId ("NAME"), model path ("MODL"),
    /// and optional display name ("FNAM"). Enough to resolve a cell reference to a mesh.
    /// </summary>
    public readonly struct BaseRecord
    {
        public readonly uint Tag;
        public readonly string Id;
        public readonly string Model;
        public readonly string DisplayName;

        public BaseRecord(uint tag, string id, string model, string displayName)
        {
            Tag = tag;
            Id = id;
            Model = model;
            DisplayName = displayName;
        }

        public string TagString => EsmFourCC.ToAscii(Tag);
    }
}
