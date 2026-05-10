using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VVardenfell.Importer.Bsa
{
    /// <summary>
    /// Reader for Morrowind-era (v0x100, uncompressed) BSA archives.
    /// Format reference: OpenMW components/bsa/bsa_file.cpp (reimplemented, not ported).
    /// </summary>
    public sealed class BsaArchive : IDisposable
    {
        private const uint MorrowindVersion = 0x100;
        private static readonly Dictionary<string, BsaArchive> s_OpenArchives = new(StringComparer.OrdinalIgnoreCase);

        private readonly FileStream _stream;
        public string FilePath { get; }
        public BsaEntry[] Entries { get; }

        private BsaArchive(string path, FileStream stream, BsaEntry[] entries)
        {
            FilePath = path;
            _stream = stream;
            Entries = entries;
            lock (s_OpenArchives)
                s_OpenArchives[path] = this;
        }

        public static BsaArchive Open(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var entries = ReadHeader(stream, path);
                return new BsaArchive(path, stream, entries);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static BsaEntry[] ReadHeader(FileStream stream, string path)
        {
            long fsize = stream.Length;
            if (fsize < 12) throw new InvalidDataException($"BSA too small: {path}");

            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            uint version = reader.ReadUInt32();
            if (version != MorrowindVersion)
                throw new InvalidDataException($"Unsupported BSA version 0x{version:X} in {path}");

            uint dirSize = reader.ReadUInt32();
            uint fileCount = reader.ReadUInt32();

            if (fileCount == 0) return Array.Empty<BsaEntry>();
            if (fileCount * 21L > fsize - 12 || dirSize + 8L * fileCount > fsize - 12)
                throw new InvalidDataException($"BSA directory larger than archive: {path}");

            // fileCount * (size, dataOffset) = 8 bytes each
            var fileSizes = new uint[fileCount];
            var dataOffsets = new uint[fileCount];
            for (int i = 0; i < fileCount; i++)
            {
                fileSizes[i] = reader.ReadUInt32();
                dataOffsets[i] = reader.ReadUInt32();
            }

            // fileCount * 4 bytes: name offsets into string buffer
            var nameOffsets = new uint[fileCount];
            for (int i = 0; i < fileCount; i++)
                nameOffsets[i] = reader.ReadUInt32();

            // String buffer: (dirSize - 12 * fileCount) bytes
            int stringBufLen = checked((int)(dirSize - 12 * fileCount));
            var stringBuf = reader.ReadBytes(stringBufLen);
            if (stringBuf.Length != stringBufLen)
                throw new EndOfStreamException($"Truncated BSA string table: {path}");

            // Skip hash table: fileCount * 8 bytes
            // (data offsets in the header are relative to end of hash table)
            long fileDataOffset = 12L + dirSize + 8L * fileCount;

            var entries = new BsaEntry[fileCount];
            for (int i = 0; i < fileCount; i++)
            {
                uint nameOff = nameOffsets[i];
                if (nameOff >= stringBuf.Length)
                    throw new InvalidDataException($"BSA name offset out of range: {path}");

                int end = Array.IndexOf<byte>(stringBuf, 0, (int)nameOff);
                if (end < 0)
                    throw new InvalidDataException($"BSA name not null-terminated: {path}");

                string name = Encoding.ASCII.GetString(stringBuf, (int)nameOff, end - (int)nameOff);
                uint absOffset = checked((uint)(dataOffsets[i] + fileDataOffset));

                if (absOffset + fileSizes[i] > fsize)
                    throw new InvalidDataException($"BSA entry '{name}' extends past end: {path}");

                entries[i] = new BsaEntry(name, absOffset, fileSizes[i], path);
            }

            return entries;
        }

        /// <summary>Read the full contents of an entry into a new byte array.</summary>
        public byte[] Read(in BsaEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.LoosePath))
                return File.ReadAllBytes(entry.LoosePath);

            if (!string.IsNullOrWhiteSpace(entry.ArchivePath)
                && !string.Equals(entry.ArchivePath, FilePath, StringComparison.OrdinalIgnoreCase))
            {
                lock (s_OpenArchives)
                {
                    if (s_OpenArchives.TryGetValue(entry.ArchivePath, out var targetArchive) && targetArchive != null)
                        return targetArchive.ReadOwn(entry);
                }

                using var temporaryArchive = Open(entry.ArchivePath);
                return temporaryArchive.ReadOwn(entry);
            }

            return ReadOwn(entry);
        }

        byte[] ReadOwn(in BsaEntry entry)
        {
            var buf = new byte[entry.Size];
            lock (_stream)
            {
                _stream.Seek(entry.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < buf.Length)
                {
                    int n = _stream.Read(buf, read, buf.Length - read);
                    if (n <= 0) throw new EndOfStreamException($"Short read on BSA entry '{entry.Name}'");
                    read += n;
                }
            }
            return buf;
        }

        public void Dispose()
        {
            lock (s_OpenArchives)
            {
                if (!string.IsNullOrWhiteSpace(FilePath)
                    && s_OpenArchives.TryGetValue(FilePath, out var archive)
                    && ReferenceEquals(archive, this))
                {
                    s_OpenArchives.Remove(FilePath);
                }
            }

            _stream?.Dispose();
        }
    }
}
