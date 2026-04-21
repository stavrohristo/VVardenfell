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
    /// Collects baked meshes during the bake phase, dedupes by encoded mesh payload,
    /// and writes them to a single <c>meshes.bin</c>.
    ///
    /// File layout:
    ///   u32 magic 'MESH'
    ///   u32 count
    ///   u64 offset[count]             absolute file offset to each payload
    ///   payload[]                     contiguous
    /// Each payload:
    ///   u32 vertexCount, indexCount, flags
    ///   float[6] bounds (centerXYZ + extentsXYZ)
    ///   float[vertexCount*3] positions
    ///   float[vertexCount*3] normals  (if flag HasNormals)
    ///   float[vertexCount*2] uv0      (if flag HasUVs)
    ///   u16 or u32 [indexCount] indices  (Index32 flag selects width)
    /// </summary>
    public sealed class MeshBakery
    {
        public const uint MagicMesh = 0x4853454Du; // 'MESH'

        private readonly Dictionary<string, List<int>> _indicesByPayloadHash =
            new Dictionary<string, List<int>>(System.StringComparer.Ordinal);
        private readonly List<byte[]> _payloads = new List<byte[]>();
        private readonly List<string> _names = new List<string>();

        public int Count => _payloads.Count;

        /// <summary>
        /// Returns the mesh index for the given built mesh, reusing an existing index when
        /// the encoded geometry payload is byte-identical even if it came from a different
        /// NIF path/submesh.
        /// </summary>
        public int AddOrGet(string nifPath, int submeshIndex, in NifMeshBuilder.BuiltMesh bm)
        {
            string sourceLabel = $"{nifPath}#{submeshIndex}";
            byte[] payload = EncodePayload(bm);
            string payloadHash = ComputePayloadHash(payload);

            if (_indicesByPayloadHash.TryGetValue(payloadHash, out var existing))
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    int idx = existing[i];
                    if (PayloadEquals(_payloads[idx], payload))
                        return idx;
                }
            }
            else
            {
                existing = new List<int>(1);
                _indicesByPayloadHash[payloadHash] = existing;
            }

            int newIdx = _payloads.Count;
            existing.Add(newIdx);
            _payloads.Add(payload);
            _names.Add(sourceLabel);
            return newIdx;
        }

        public void WriteNames(string path)
        {
            using var fs = System.IO.File.Create(path);
            using var w = new System.IO.BinaryWriter(fs);
            w.Write((uint)_names.Count);
            foreach (var n in _names)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(n);
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
            }
        }

        public static string[] ReadNames(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            using var fs = System.IO.File.OpenRead(path);
            using var r = new System.IO.BinaryReader(fs);
            uint n = r.ReadUInt32();
            var result = new string[n];
            for (int i = 0; i < n; i++)
            {
                int len = r.ReadUInt16();
                result[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
            }
            return result;
        }

        private static byte[] EncodePayload(in NifMeshBuilder.BuiltMesh bm)
        {
            var mesh = bm.Mesh;
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var uvs = mesh.uv;
            var tris = mesh.triangles; // Unity returns int[]
            bool hasNormals = norms != null && norms.Length == verts.Length;
            bool hasUVs = uvs != null && uvs.Length == verts.Length;
            bool index32 = verts.Length > 65535;

            uint flags = 0;
            if (hasNormals) flags |= CacheFormat.MeshFlagHasNormals;
            if (hasUVs) flags |= CacheFormat.MeshFlagHasUVs;
            if (index32) flags |= CacheFormat.MeshFlagIndex32;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)verts.Length);
            w.Write((uint)tris.Length);
            w.Write(flags);

            var b = bm.LocalBounds;
            w.Write(b.center.x); w.Write(b.center.y); w.Write(b.center.z);
            w.Write(b.extents.x); w.Write(b.extents.y); w.Write(b.extents.z);

            for (int i = 0; i < verts.Length; i++)
            {
                w.Write(verts[i].x); w.Write(verts[i].y); w.Write(verts[i].z);
            }
            if (hasNormals)
                for (int i = 0; i < verts.Length; i++)
                { w.Write(norms[i].x); w.Write(norms[i].y); w.Write(norms[i].z); }
            if (hasUVs)
                for (int i = 0; i < verts.Length; i++)
                { w.Write(uvs[i].x); w.Write(uvs[i].y); }

            if (index32)
                for (int i = 0; i < tris.Length; i++) w.Write((uint)tris[i]);
            else
                for (int i = 0; i < tris.Length; i++) w.Write((ushort)tris[i]);

            return ms.ToArray();
        }

        private static string ComputePayloadHash(byte[] payload)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(payload);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static bool PayloadEquals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public void WriteTo(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicMesh);
            w.Write((uint)_payloads.Count);
            long tableStart = fs.Position;
            // Reserve offsets
            for (int i = 0; i < _payloads.Count; i++) w.Write((ulong)0);
            // Payloads + record offsets
            var offsets = new ulong[_payloads.Count];
            for (int i = 0; i < _payloads.Count; i++)
            {
                offsets[i] = (ulong)fs.Position;
                w.Write(_payloads[i]);
            }
            // Backfill offsets
            fs.Position = tableStart;
            for (int i = 0; i < offsets.Length; i++) w.Write(offsets[i]);
        }
    }
}
