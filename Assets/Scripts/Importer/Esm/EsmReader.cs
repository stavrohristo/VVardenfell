using System;
using System.IO;
using System.Text;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Low-level reader for TES3 (Morrowind) ESM/ESP/ESS files.
    ///
    /// Format (reimplemented from OpenMW reference, not ported):
    ///   File = sequence of records.
    ///   Record = [4B tag][4B dataSize][4B unused=0][4B flags][dataSize bytes of subrecords]
    ///   Subrecord = [4B tag][4B size][size bytes]
    /// All integers are little-endian.
    /// </summary>
    public sealed class EsmReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;

        public string FilePath { get; }
        public long Length => _stream.Length;
        public long Position => _stream.Position;

        /// <summary>Bytes remaining in the current record body (subrecords region).</summary>
        public long RecordBytesLeft { get; private set; }

        /// <summary>Bytes remaining in the current subrecord body.</summary>
        public long SubrecordBytesLeft { get; private set; }

        public EsmReader(string path)
        {
            FilePath = path;
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            _reader = new BinaryReader(_stream, Encoding.ASCII, leaveOpen: false);
        }

        public bool Eof => _stream.Position >= _stream.Length;

        /// <summary>Seek to an absolute stream offset. Clears current record/subrecord state.</summary>
        public void Seek(long offset)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            RecordBytesLeft = 0;
            SubrecordBytesLeft = 0;
        }

        /// <summary>
        /// Read the next record header. Returns false at EOF.
        /// Leaves the stream positioned at the first subrecord of the record.
        /// </summary>
        public bool ReadRecordHeader(out RecordHeader header)
        {
            if (_stream.Position + 16 > _stream.Length)
            {
                header = default;
                RecordBytesLeft = 0;
                return false;
            }

            uint tag = _reader.ReadUInt32();
            uint size = _reader.ReadUInt32();
            _reader.ReadUInt32(); // unused / header1
            uint flags = _reader.ReadUInt32();

            header = new RecordHeader(tag, size, flags);
            RecordBytesLeft = size;
            SubrecordBytesLeft = 0;
            return true;
        }

        /// <summary>
        /// Read the next subrecord header inside the current record.
        /// Returns false when the current record is exhausted.
        /// </summary>
        public bool ReadSubrecordHeader(out SubrecordHeader header)
        {
            if (RecordBytesLeft < 8)
            {
                header = default;
                return false;
            }

            uint tag = _reader.ReadUInt32();
            uint size = _reader.ReadUInt32();
            RecordBytesLeft -= 8;

            if (size > RecordBytesLeft)
                throw new InvalidDataException(
                    $"Subrecord {EsmFourCC.ToAscii(tag)} size {size} exceeds remaining record bytes {RecordBytesLeft} at 0x{Position - 8:X} in {FilePath}");

            header = new SubrecordHeader(tag, size);
            SubrecordBytesLeft = size;
            return true;
        }

        /// <summary>Skip the body of the current subrecord without reading it.</summary>
        public void SkipSubrecord()
        {
            if (SubrecordBytesLeft == 0) return;
            _stream.Seek(SubrecordBytesLeft, SeekOrigin.Current);
            RecordBytesLeft -= SubrecordBytesLeft;
            SubrecordBytesLeft = 0;
        }

        /// <summary>Skip any remaining subrecords in the current record.</summary>
        public void SkipRecord()
        {
            if (RecordBytesLeft == 0) return;
            _stream.Seek(RecordBytesLeft, SeekOrigin.Current);
            RecordBytesLeft = 0;
            SubrecordBytesLeft = 0;
        }

        public byte[] ReadSubrecordBytes()
        {
            var buf = _reader.ReadBytes((int)SubrecordBytesLeft);
            if (buf.Length != SubrecordBytesLeft)
                throw new EndOfStreamException($"Short read in subrecord at 0x{Position:X}");
            RecordBytesLeft -= SubrecordBytesLeft;
            SubrecordBytesLeft = 0;
            return buf;
        }

        /// <summary>Read a null-terminated (or fixed) ASCII string filling the whole subrecord.</summary>
        public string ReadSubrecordString()
        {
            var bytes = ReadSubrecordBytes();
            int end = Array.IndexOf<byte>(bytes, 0);
            if (end < 0) end = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        public byte ReadByte()
        {
            var v = _reader.ReadByte();
            SubrecordBytesLeft -= 1;
            RecordBytesLeft -= 1;
            return v;
        }

        public ushort ReadUInt16()
        {
            var v = _reader.ReadUInt16();
            SubrecordBytesLeft -= 2;
            RecordBytesLeft -= 2;
            return v;
        }

        public int ReadInt32()
        {
            var v = _reader.ReadInt32();
            SubrecordBytesLeft -= 4;
            RecordBytesLeft -= 4;
            return v;
        }

        public uint ReadUInt32()
        {
            var v = _reader.ReadUInt32();
            SubrecordBytesLeft -= 4;
            RecordBytesLeft -= 4;
            return v;
        }

        public float ReadFloat()
        {
            var v = _reader.ReadSingle();
            SubrecordBytesLeft -= 4;
            RecordBytesLeft -= 4;
            return v;
        }

        public void Dispose() => _reader.Dispose();
    }

    public readonly struct RecordHeader
    {
        public readonly uint Tag;
        public readonly uint DataSize;
        public readonly uint Flags;

        public RecordHeader(uint tag, uint dataSize, uint flags)
        {
            Tag = tag;
            DataSize = dataSize;
            Flags = flags;
        }

        public string TagString => EsmFourCC.ToAscii(Tag);
    }

    public readonly struct SubrecordHeader
    {
        public readonly uint Tag;
        public readonly uint Size;

        public SubrecordHeader(uint tag, uint size)
        {
            Tag = tag;
            Size = size;
        }

        public string TagString => EsmFourCC.ToAscii(Tag);
    }
}
