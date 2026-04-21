namespace VVardenfell.Importer.Bsa
{
    public readonly struct BsaEntry
    {
        public readonly string Name;
        public readonly uint Offset;
        public readonly uint Size;

        public BsaEntry(string name, uint offset, uint size)
        {
            Name = name;
            Offset = offset;
            Size = size;
        }
    }
}
