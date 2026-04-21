using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Low-level binary reader for Morrowind-era NIF files (v4.0.0.2).
    /// Everything is little-endian. Strings are uint32-length-prefixed ASCII (no null terminator).
    /// Bools are stored as int32 (pre-4.1). Vec3/Matrix3/Quat follow NIF conventions.
    /// </summary>
    public sealed class NifStream : IDisposable
    {
        public const uint VersionMorrowind = 0x04000002;

        private readonly BinaryReader _reader;
        private readonly Stream _stream;

        public string FilePath { get; }
        public uint Version { get; private set; }
        public long Position => _stream.Position;
        public long Length => _stream.Length;

        public NifStream(string path) : this(path, File.OpenRead(path)) { }

        public NifStream(string path, Stream stream)
        {
            FilePath = path;
            _stream = stream;
            _reader = new BinaryReader(_stream, Encoding.ASCII, leaveOpen: false);
        }

        /// <summary>
        /// Reads the version-string line (terminated by '\n') and the 4-byte version code.
        /// Throws on a non-Morrowind NIF.
        /// </summary>
        public void ReadHeader()
        {
            string header = ReadLine();
            if (!header.StartsWith("NetImmerse File Format"))
                throw new InvalidDataException($"Not a NIF file ({FilePath}): header was '{header}'");

            Version = _reader.ReadUInt32();
            if (Version != VersionMorrowind)
                throw new InvalidDataException($"Unsupported NIF version 0x{Version:X8} in {FilePath}; expected 0x{VersionMorrowind:X8}");
        }

        public byte ReadByte() => _reader.ReadByte();
        public short ReadInt16() => _reader.ReadInt16();
        public ushort ReadUInt16() => _reader.ReadUInt16();
        public int ReadInt32() => _reader.ReadInt32();
        public uint ReadUInt32() => _reader.ReadUInt32();
        public float ReadFloat() => _reader.ReadSingle();
        public bool ReadBool32() => _reader.ReadInt32() != 0;

        /// <summary>Length-prefixed (uint32) ASCII string.</summary>
        public string ReadSizedString()
        {
            uint len = _reader.ReadUInt32();
            if (len > 1_000_000)
                throw new InvalidDataException($"Absurd string length {len} at 0x{Position - 4:X} in {FilePath}");
            if (len == 0) return string.Empty;
            var bytes = _reader.ReadBytes((int)len);
            if (bytes.Length != len)
                throw new EndOfStreamException($"Short read on sized string in {FilePath}");
            int end = Array.IndexOf<byte>(bytes, 0);
            if (end < 0) end = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        /// <summary>'\n'-terminated ASCII line (used for the version header line).</summary>
        public string ReadLine()
        {
            var sb = new StringBuilder(64);
            int b;
            while ((b = _stream.ReadByte()) != -1 && b != '\n')
                sb.Append((char)b);
            return sb.ToString();
        }

        public Vector3 ReadVec3() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());

        /// <summary>
        /// NIF 3x3 rotation read row-major into Unity's Matrix4x4. We do NOT transpose —
        /// OpenMW's <c>NiTransform::toMatrix</c> transposes because OSG uses row-vector
        /// (<c>v' = v * M</c>) semantics, but Unity uses column-vector (<c>v' = M * v</c>).
        /// Both conversions net out to the same visual rotation, so loading straight works.
        /// </summary>
        public Matrix4x4 ReadMatrix3()
        {
            float m00 = ReadFloat(), m01 = ReadFloat(), m02 = ReadFloat();
            float m10 = ReadFloat(), m11 = ReadFloat(), m12 = ReadFloat();
            float m20 = ReadFloat(), m21 = ReadFloat(), m22 = ReadFloat();
            var m = Matrix4x4.identity;
            m.m00 = m00; m.m01 = m01; m.m02 = m02;
            m.m10 = m10; m.m11 = m11; m.m12 = m12;
            m.m20 = m20; m.m21 = m21; m.m22 = m22;
            return m;
        }

        /// <summary>NIF quaternion order: (w, x, y, z).</summary>
        public Quaternion ReadQuat()
        {
            float w = ReadFloat();
            float x = ReadFloat();
            float y = ReadFloat();
            float z = ReadFloat();
            return new Quaternion(x, y, z, w);
        }

        public void Skip(long bytes) => _stream.Seek(bytes, SeekOrigin.Current);

        public void Dispose() => _reader.Dispose();
    }
}
