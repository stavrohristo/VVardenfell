using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;
using MeshCollider = Unity.Physics.MeshCollider;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Global table of deduped collision payloads used for interactable refs.
    /// Raw payloads live in the sidecar catalog so indices stay stable across runs.
    /// </summary>
    public sealed class CollisionBakery
    {
        public const uint MagicCollision = 0x4C4C4F43u; // 'COLL'
        private const uint MagicCatalog = 0x54414343u; // 'CCAT'
        [ThreadStatic] private static SHA256 s_threadSha256;

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _indicesByPayloadHash =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly List<CollisionPayload> _payloads = new List<CollisionPayload>();
        private readonly List<byte[]> _encoded = new List<byte[]>();
        private readonly List<string> _payloadHashes = new List<string>();

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
                    int payloadLength = r.ReadInt32();
                    byte[] encoded = r.ReadBytes(payloadLength);
                    if (encoded.Length != payloadLength)
                        return;

                    int index = _payloads.Count;
                    _payloads.Add(DecodePayload(encoded));
                    _encoded.Add(encoded);
                    _payloadHashes.Add(hash);
                    _indicesByPayloadHash[hash] = index;
                }
            }
            catch
            {
                _indicesByPayloadHash.Clear();
                _payloads.Clear();
                _encoded.Clear();
                _payloadHashes.Clear();
            }
        }

        public int AddOrGet(in CollisionPayload payload)
        {
            if (payload.IsEmpty)
                return -1;

            byte[] encoded = EncodePayload(payload);
            string hash = ComputePayloadHash(encoded);
            return AddOrGetEncoded(payload, encoded, hash);
        }

        public int AddOrGetInteractionPick(in CollisionPayload payload)
        {
            if (payload.IsEmpty)
                return -1;

            byte[] encoded = EncodePayload(payload);
            string hash = "interaction-pick:" + ComputePayloadHash(encoded);
            return AddOrGetEncoded(payload, encoded, hash);
        }

        public int AddOrGetEncoded(in CollisionPayload payload, byte[] encoded, string hash)
        {
            lock (_gate)
            {
                if (_indicesByPayloadHash.TryGetValue(hash, out int existing))
                    return existing;

                int newIdx = _payloads.Count;
                _indicesByPayloadHash[hash] = newIdx;
                _payloads.Add(payload);
                _encoded.Add(encoded);
                _payloadHashes.Add(hash);
                Modified = true;
                return newIdx;
            }
        }

        public void WriteCatalog(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCatalog);
            w.Write((uint)_encoded.Count);
            for (int i = 0; i < _encoded.Count; i++)
            {
                w.Write(_payloadHashes[i]);
                w.Write(_encoded[i].Length);
                w.Write(_encoded[i]);
            }
            Modified = false;
        }

        public static byte[] EncodePayload(in CollisionPayload payload)
        {
            int totalBytes = checked(4 + (payload.Vertices.Length * 12) + 4 + (payload.Indices.Length * 4));
            byte[] encoded = new byte[totalBytes];
            int offset = 0;
            WriteUInt32(encoded, ref offset, (uint)payload.Vertices.Length);
            for (int i = 0; i < payload.Vertices.Length; i++)
            {
                var v = payload.Vertices[i];
                WriteSingle(encoded, ref offset, v.x);
                WriteSingle(encoded, ref offset, v.y);
                WriteSingle(encoded, ref offset, v.z);
            }
            WriteUInt32(encoded, ref offset, (uint)payload.Indices.Length);
            for (int i = 0; i < payload.Indices.Length; i++)
                WriteUInt32(encoded, ref offset, unchecked((uint)payload.Indices[i]));
            return encoded;
        }

        private static CollisionPayload DecodePayload(byte[] encoded)
        {
            using var ms = new MemoryStream(encoded, writable: false);
            using var r = new BinaryReader(ms);
            int vertexCount = (int)r.ReadUInt32();
            var verts = new UnityEngine.Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                verts[i] = new UnityEngine.Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            int indexCount = (int)r.ReadUInt32();
            var indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
                indices[i] = r.ReadInt32();
            return new CollisionPayload(verts, indices);
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
            w.Write(MagicCollision);
            w.Write((uint)_payloads.Count);
            long tableStart = fs.Position;
            for (int i = 0; i < _payloads.Count; i++)
                w.Write((ulong)0);

            var verts = new NativeList<float3>(4096, Allocator.Persistent);
            var tris = new NativeList<int3>(4096, Allocator.Persistent);

            var offsets = new ulong[_payloads.Count];
            for (int i = 0; i < _payloads.Count; i++)
            {
                offsets[i] = (ulong)fs.Position;
                var payload = _payloads[i];

                verts.Clear();
                verts.ResizeUninitialized(payload.Vertices.Length);
                var vSpan = verts.AsArray();
                for (int k = 0; k < payload.Vertices.Length; k++)
                {
                    var v = payload.Vertices[k];
                    vSpan[k] = new float3(v.x, v.y, v.z);
                }

                int triCount = payload.Indices.Length / 3;
                tris.Clear();
                tris.ResizeUninitialized(triCount);
                var iSpan = tris.AsArray();
                for (int t = 0; t < triCount; t++)
                {
                    iSpan[t] = new int3(
                        payload.Indices[t * 3 + 0],
                        payload.Indices[t * 3 + 1],
                        payload.Indices[t * 3 + 2]);
                }

                var blob = MeshCollider.Create(vSpan, iSpan, CollisionFilter.Default);
                BlobStreamIO.WriteLengthPrefixed(w, blob, CacheFormat.PhysicsBlobVersion);
                blob.Dispose();
            }

            verts.Dispose();
            tris.Dispose();

            fs.Position = tableStart;
            for (int i = 0; i < offsets.Length; i++)
                w.Write(offsets[i]);
        }
    }
}
