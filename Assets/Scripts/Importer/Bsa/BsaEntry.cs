namespace VVardenfell.Importer.Bsa
{
    public readonly struct BsaEntry
    {
        public readonly string Name;
        public readonly uint Offset;
        public readonly uint Size;
        public readonly string ArchivePath;
        public readonly string LoosePath;

        public BsaEntry(string name, uint offset, uint size)
        {
            Name = name;
            Offset = offset;
            Size = size;
            ArchivePath = string.Empty;
            LoosePath = string.Empty;
        }

        public BsaEntry(string name, uint offset, uint size, string archivePath)
        {
            Name = name;
            Offset = offset;
            Size = size;
            ArchivePath = archivePath ?? string.Empty;
            LoosePath = string.Empty;
        }

        public BsaEntry(string name, string loosePath, long size)
        {
            Name = name;
            Offset = 0;
            Size = checked((uint)size);
            ArchivePath = string.Empty;
            LoosePath = loosePath ?? string.Empty;
        }
    }
}
