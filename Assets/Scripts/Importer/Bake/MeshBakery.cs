using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
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
        private const uint CatalogVersion = 3u;
        [ThreadStatic] private static SHA256 s_threadSha256;

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _indicesByPayloadHash =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly List<byte[]> _payloads = new List<byte[]>();
        private readonly List<string> _payloadHashes = new List<string>();
        private readonly List<string> _names = new List<string>();

        public int Count => _payloads.Count;
        public bool Modified { get; private set; }
        public long TotalPayloadBytes
        {
            get
            {
                lock (_gate)
                {
                    long total = 0;
                    for (int i = 0; i < _payloads.Count; i++)
                        total += _payloads[i]?.Length ?? 0;
                    return total;
                }
            }
        }

        public string GetSourceLabel(int index)
        {
            lock (_gate)
            {
                return (uint)index < (uint)_names.Count ? (_names[index] ?? string.Empty) : string.Empty;
            }
        }

        public int GetPayloadLength(int index)
        {
            lock (_gate)
            {
                return (uint)index < (uint)_payloads.Count ? (_payloads[index]?.Length ?? 0) : 0;
            }
        }

        public void TryLoadExisting(string catalogPath, string meshesPath)
        {
            if (!File.Exists(catalogPath) || !File.Exists(meshesPath))
                return;

            try
            {
                Clear();

                using var catalogFs = File.OpenRead(catalogPath);
                using var catalogReader = new BinaryReader(catalogFs);
                if (catalogReader.ReadUInt32() != MagicCatalog)
                    return;

                uint version = catalogReader.ReadUInt32();
                if (version != CatalogVersion)
                    return;

                uint catalogCount = catalogReader.ReadUInt32();
                var hashes = new string[catalogCount];
                var names = new string[catalogCount];
                var lengths = new int[catalogCount];
                for (int i = 0; i < catalogCount; i++)
                {
                    hashes[i] = catalogReader.ReadString();
                    names[i] = catalogReader.ReadString();
                    lengths[i] = catalogReader.ReadInt32();
                    if (lengths[i] < 0)
                        throw new InvalidDataException($"Negative mesh payload length {lengths[i]} in '{catalogPath}'.");
                }

                using var meshFs = File.OpenRead(meshesPath);
                using var meshReader = new BinaryReader(meshFs);
                if (meshReader.ReadUInt32() != MagicMesh)
                    throw new InvalidDataException($"Bad mesh magic in '{meshesPath}'.");

                uint meshCount = meshReader.ReadUInt32();
                if (meshCount != catalogCount)
                    throw new InvalidDataException($"Mesh catalog count {catalogCount} does not match meshes.bin count {meshCount}.");

                var offsets = new ulong[meshCount];
                for (int i = 0; i < meshCount; i++)
                    offsets[i] = meshReader.ReadUInt64();

                ulong fileLength = (ulong)meshFs.Length;
                for (int i = 0; i < meshCount; i++)
                {
                    ulong start = offsets[i];
                    ulong end = i + 1 < meshCount ? offsets[i + 1] : fileLength;
                    if (start > end || end > fileLength)
                        throw new InvalidDataException($"Invalid mesh offset range [{start}, {end}) in '{meshesPath}' for mesh {i}.");

                    ulong actualLength = end - start;
                    if (actualLength > int.MaxValue)
                        throw new InvalidDataException($"Mesh payload {i} in '{meshesPath}' exceeds supported size.");
                    if ((int)actualLength != lengths[i])
                        throw new InvalidDataException($"Mesh payload {i} length mismatch: catalog={lengths[i]}, meshes.bin={(int)actualLength}.");

                    meshFs.Position = (long)start;
                    byte[] payload = meshReader.ReadBytes((int)actualLength);
                    if (payload.Length != (int)actualLength)
                        throw new InvalidDataException($"Mesh payload {i} in '{meshesPath}' truncated.");

                    int index = _payloads.Count;
                    _payloads.Add(payload);
                    _payloadHashes.Add(hashes[i] ?? string.Empty);
                    _names.Add(names[i] ?? string.Empty);
                    _indicesByPayloadHash[hashes[i] ?? string.Empty] = index;
                }
            }
            catch
            {
                Clear();
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

        public int AddOrGetRaw(
            string sourceLabel,
            Vector3[] vertices,
            Vector3[] sourceNormals,
            Vector2[] uv0,
            Vector2[] uv1,
            int[] indices,
            Bounds bounds)
        {
            byte[] payload = EncodePayload(sourceLabel, vertices, sourceNormals, uv0, uv1, indices, bounds);
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
            w.Write(CatalogVersion);
            w.Write((uint)_payloads.Count);
            for (int i = 0; i < _payloads.Count; i++)
            {
                w.Write(_payloadHashes[i]);
                w.Write(_names[i] ?? string.Empty);
                w.Write(_payloads[i].Length);
            }
            Modified = false;
        }

        public static byte[] EncodePayload(in NifMeshBuilder.RawBuiltMesh bm)
            => EncodePayload(bm.Name, bm.Vertices, bm.Normals, bm.HasUvs ? bm.Uvs : null, null, bm.Indices, bm.LocalBounds);

        public static byte[] EncodePayload(
            string name,
            Vector3[] vertices,
            Vector3[] sourceNormals,
            Vector2[] uv0,
            Vector2[] uv1,
            int[] indices,
            Bounds bounds)
        {
            int vertexCount = vertices?.Length ?? 0;
            if (vertexCount == 0)
                throw new InvalidDataException($"Baked mesh '{name}' has no vertices.");
            if (indices == null || indices.Length == 0)
                throw new InvalidDataException($"Baked mesh '{name}' has no indices.");

            bool hasNormals = sourceNormals != null && sourceNormals.Length == vertexCount;
            bool hasUVs = uv0 != null && uv0.Length == vertexCount;
            bool hasUV1 = uv1 != null && uv1.Length == vertexCount;
            bool index32 = vertexCount > 65535;
            Vector3[] finalNormals = hasNormals
                ? sourceNormals
                : BuildFallbackNormals(vertices, indices);
            hasNormals = finalNormals != null && finalNormals.Length == vertexCount;

            if (!hasNormals)
                throw new InvalidDataException($"Baked mesh '{name}' is missing normals.");

            uint flags = 0;
            if (hasNormals) flags |= CacheFormat.MeshFlagHasNormals;
            if (hasUVs) flags |= CacheFormat.MeshFlagHasUVs;
            if (hasUV1) flags |= CacheFormat.MeshFlagHasUV1;
            if (index32) flags |= CacheFormat.MeshFlagIndex32;

            int vertexStride = 12;
            if (hasNormals) vertexStride += 12;
            if (hasUVs) vertexStride += 8;
            if (hasUV1) vertexStride += 8;

            int vertexDataBytes = checked(vertexCount * vertexStride);
            int indexStride = index32 ? 4 : 2;
            int indexDataBytes = checked(indices.Length * indexStride);
            int totalBytes = checked(44 + vertexDataBytes + indexDataBytes);
            byte[] payload = new byte[totalBytes];
            int offset = 0;

            WriteUInt32(payload, ref offset, (uint)vertexCount);
            WriteUInt32(payload, ref offset, (uint)indices.Length);
            WriteUInt32(payload, ref offset, flags);

            var b = bounds;
            WriteSingle(payload, ref offset, b.center.x);
            WriteSingle(payload, ref offset, b.center.y);
            WriteSingle(payload, ref offset, b.center.z);
            WriteSingle(payload, ref offset, b.extents.x);
            WriteSingle(payload, ref offset, b.extents.y);
            WriteSingle(payload, ref offset, b.extents.z);
            WriteUInt32(payload, ref offset, (uint)vertexDataBytes);
            WriteUInt32(payload, ref offset, (uint)indexDataBytes);

            for (int i = 0; i < vertexCount; i++)
            {
                WriteSingle(payload, ref offset, vertices[i].x);
                WriteSingle(payload, ref offset, vertices[i].y);
                WriteSingle(payload, ref offset, vertices[i].z);
                if (hasNormals)
                {
                    WriteSingle(payload, ref offset, finalNormals[i].x);
                    WriteSingle(payload, ref offset, finalNormals[i].y);
                    WriteSingle(payload, ref offset, finalNormals[i].z);
                }
                if (hasUVs)
                {
                    WriteSingle(payload, ref offset, uv0[i].x);
                    WriteSingle(payload, ref offset, uv0[i].y);
                }
                if (hasUV1)
                {
                    WriteSingle(payload, ref offset, uv1[i].x);
                    WriteSingle(payload, ref offset, uv1[i].y);
                }
            }

            if (index32)
            {
                for (int i = 0; i < indices.Length; i++)
                    WriteUInt32(payload, ref offset, (uint)indices[i]);
            }
            else
            {
                for (int i = 0; i < indices.Length; i++)
                    WriteUInt16(payload, ref offset, (ushort)indices[i]);
            }

            return payload;
        }

        static Vector3[] BuildFallbackNormals(Vector3[] vertices, int[] indices)
        {
            if (vertices == null || vertices.Length == 0 || indices == null || indices.Length < 3)
                return null;

            var normals = new Vector3[vertices.Length];
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i + 0];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                if ((uint)i0 >= (uint)vertices.Length
                    || (uint)i1 >= (uint)vertices.Length
                    || (uint)i2 >= (uint)vertices.Length)
                {
                    continue;
                }

                Vector3 edgeA = vertices[i1] - vertices[i0];
                Vector3 edgeB = vertices[i2] - vertices[i0];
                Vector3 normal = Vector3.Cross(edgeA, edgeB);
                if (normal.sqrMagnitude <= 0.00000001f)
                    continue;

                normals[i0] += normal;
                normals[i1] += normal;
                normals[i2] += normal;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                if (normals[i].sqrMagnitude <= 0.00000001f)
                    normals[i] = Vector3.up;
                else
                    normals[i].Normalize();
            }

            return normals;
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

        private void Clear()
        {
            _indicesByPayloadHash.Clear();
            _payloads.Clear();
            _payloadHashes.Clear();
            _names.Clear();
        }
    }
}
