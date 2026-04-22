using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Collects baked mesh payloads, keeps indices stable across runs via a sidecar catalog,
    /// and writes the runtime mesh payload table.
    /// </summary>
    public sealed class MeshBakery
    {
        public const uint MagicMesh = 0x4853454Du; // 'MESH'
        private const uint MagicCatalog = 0x5441434Du; // 'MCAT'
        [ThreadStatic] private static SHA256 s_threadSha256;

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _indicesByPayloadHash =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly List<byte[]> _payloads = new List<byte[]>();
        private readonly List<string> _payloadHashes = new List<string>();
        private readonly List<string> _names = new List<string>();

        public int Count => _payloads.Count;
        public bool Modified { get; private set; }

        public void TryLoadExisting(string catalogPath)
        {
            if (!File.Exists(catalogPath))
                return;

            try
            {
                using var fs = File.OpenRead(catalogPath);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != MagicCatalog)
                    return;

                uint count = r.ReadUInt32();
                for (int i = 0; i < count; i++)
                {
                    string hash = r.ReadString();
                    string sourceLabel = r.ReadString();
                    int payloadLength = r.ReadInt32();
                    byte[] payload = r.ReadBytes(payloadLength);
                    if (payload.Length != payloadLength)
                        return;

                    int index = _payloads.Count;
                    _payloads.Add(payload);
                    _payloadHashes.Add(hash);
                    _names.Add(sourceLabel);
                    _indicesByPayloadHash[hash] = index;
                }
            }
            catch
            {
                _indicesByPayloadHash.Clear();
                _payloads.Clear();
                _payloadHashes.Clear();
                _names.Clear();
            }
        }

        public int AddOrGet(string nifPath, int submeshIndex, in NifMeshBuilder.RawBuiltMesh bm)
        {
            string sourceLabel = $"{nifPath}#{submeshIndex}";
            return AddOrGet(sourceLabel, bm);
        }

        public int AddOrGet(string sourceLabel, in NifMeshBuilder.RawBuiltMesh bm)
        {
            byte[] payload = EncodePayload(bm);
            string payloadHash = ComputePayloadHash(payload);
            return AddOrGetEncoded(sourceLabel, payload, payloadHash);
        }

        public int AddOrGetEncoded(string sourceLabel, byte[] payload, string payloadHash)
        {
            sourceLabel ??= string.Empty;

            lock (_gate)
            {
                if (_indicesByPayloadHash.TryGetValue(payloadHash, out int existing))
                    return existing;

                int newIdx = _payloads.Count;
                _indicesByPayloadHash[payloadHash] = newIdx;
                _payloads.Add(payload);
                _payloadHashes.Add(payloadHash);
                _names.Add(sourceLabel);
                Modified = true;
                return newIdx;
            }
        }

        public void WriteNames(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((uint)_names.Count);
            foreach (var n in _names)
            {
                var bytes = Encoding.UTF8.GetBytes(n);
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
            }
        }

        public static string[] ReadNames(string path)
        {
            if (!File.Exists(path))
                return null;
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            uint n = r.ReadUInt32();
            var result = new string[n];
            for (int i = 0; i < n; i++)
            {
                int len = r.ReadUInt16();
                result[i] = Encoding.UTF8.GetString(r.ReadBytes(len));
            }
            return result;
        }

        public void WriteCatalog(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCatalog);
            w.Write((uint)_payloads.Count);
            for (int i = 0; i < _payloads.Count; i++)
            {
                w.Write(_payloadHashes[i]);
                w.Write(_names[i] ?? string.Empty);
                w.Write(_payloads[i].Length);
                w.Write(_payloads[i]);
            }
            Modified = false;
        }

        public static byte[] EncodePayload(in NifMeshBuilder.RawBuiltMesh bm)
        {
            bool hasNormals = bm.HasNormals;
            bool hasUVs = bm.HasUvs;
            bool index32 = bm.VertexCount > 65535;

            if (!hasNormals)
                throw new InvalidDataException($"Baked mesh '{bm.Name}' is missing normals.");

            uint flags = 0;
            if (hasNormals) flags |= CacheFormat.MeshFlagHasNormals;
            if (hasUVs) flags |= CacheFormat.MeshFlagHasUVs;
            if (index32) flags |= CacheFormat.MeshFlagIndex32;

            int vertexStride = 12;
            if (hasNormals) vertexStride += 12;
            if (hasUVs) vertexStride += 8;

            int vertexDataBytes = checked(bm.VertexCount * vertexStride);
            int indexStride = index32 ? 4 : 2;
            int indexDataBytes = checked(bm.Indices.Length * indexStride);
            int totalBytes = checked(44 + vertexDataBytes + indexDataBytes);
            byte[] payload = new byte[totalBytes];
            int offset = 0;

            WriteUInt32(payload, ref offset, (uint)bm.VertexCount);
            WriteUInt32(payload, ref offset, (uint)bm.Indices.Length);
            WriteUInt32(payload, ref offset, flags);

            var b = bm.LocalBounds;
            WriteSingle(payload, ref offset, b.center.x);
            WriteSingle(payload, ref offset, b.center.y);
            WriteSingle(payload, ref offset, b.center.z);
            WriteSingle(payload, ref offset, b.extents.x);
            WriteSingle(payload, ref offset, b.extents.y);
            WriteSingle(payload, ref offset, b.extents.z);
            WriteUInt32(payload, ref offset, (uint)vertexDataBytes);
            WriteUInt32(payload, ref offset, (uint)indexDataBytes);

            for (int i = 0; i < bm.VertexCount; i++)
            {
                WriteSingle(payload, ref offset, bm.Vertices[i].x);
                WriteSingle(payload, ref offset, bm.Vertices[i].y);
                WriteSingle(payload, ref offset, bm.Vertices[i].z);
                if (hasNormals)
                {
                    WriteSingle(payload, ref offset, bm.Normals[i].x);
                    WriteSingle(payload, ref offset, bm.Normals[i].y);
                    WriteSingle(payload, ref offset, bm.Normals[i].z);
                }
                if (hasUVs)
                {
                    WriteSingle(payload, ref offset, bm.Uvs[i].x);
                    WriteSingle(payload, ref offset, bm.Uvs[i].y);
                }
            }

            if (index32)
            {
                for (int i = 0; i < bm.Indices.Length; i++)
                    WriteUInt32(payload, ref offset, (uint)bm.Indices[i]);
            }
            else
            {
                for (int i = 0; i < bm.Indices.Length; i++)
                    WriteUInt16(payload, ref offset, (ushort)bm.Indices[i]);
            }

            return payload;
        }

        public static string ComputePayloadHash(byte[] payload)
        {
            Span<byte> hash = stackalloc byte[32];
            var sha = GetThreadSha256();
            if (sha.TryComputeHash(payload, hash, out int written) && written == hash.Length)
                return ToLowerHex(hash);

            byte[] fallback = sha.ComputeHash(payload);
            return ToLowerHex(fallback);
        }

        private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }

        private static void WriteSingle(byte[] buffer, ref int offset, float value)
        {
            WriteUInt32(buffer, ref offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static SHA256 GetThreadSha256()
            => s_threadSha256 ??= SHA256.Create();

        private static string ToLowerHex(ReadOnlySpan<byte> bytes)
        {
            const string hex = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                chars[i * 2] = hex[value >> 4];
                chars[i * 2 + 1] = hex[value & 0xF];
            }
            return new string(chars);
        }

        public void WriteTo(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicMesh);
            w.Write((uint)_payloads.Count);
            long tableStart = fs.Position;
            for (int i = 0; i < _payloads.Count; i++)
                w.Write((ulong)0);

            var offsets = new ulong[_payloads.Count];
            for (int i = 0; i < _payloads.Count; i++)
            {
                offsets[i] = (ulong)fs.Position;
                w.Write(_payloads[i]);
            }

            fs.Position = tableStart;
            for (int i = 0; i < offsets.Length; i++)
                w.Write(offsets[i]);
        }
    }
}
