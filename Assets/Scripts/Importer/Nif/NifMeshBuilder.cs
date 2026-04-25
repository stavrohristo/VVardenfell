using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Walks a parsed NIF and emits raw render geometry in Unity bake space.
    /// World baking consumes the raw payloads directly so it can stay off the Mesh API.
    /// </summary>
    public static class NifMeshBuilder
    {
        /// <summary>Case-insensitive substring match; if the NIF path contains it, verbose-dump to console.</summary>
        public static string DebugMeshPath = "";

        public readonly struct RawBuiltMesh
        {
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly Vector2[] Uvs;
            public readonly int[] Indices;
            public readonly string TexturePath;
            public readonly string Name;
            public readonly Bounds LocalBounds;
            public readonly ushort AlphaFlags;
            public readonly byte AlphaThreshold;

            public RawBuiltMesh(
                Vector3[] vertices,
                Vector3[] normals,
                Vector2[] uvs,
                int[] indices,
                string texturePath,
                string name,
                Bounds bounds,
                ushort alphaFlags,
                byte alphaThreshold)
            {
                Vertices = vertices;
                Normals = normals;
                Uvs = uvs;
                Indices = indices;
                TexturePath = texturePath;
                Name = name;
                LocalBounds = bounds;
                AlphaFlags = alphaFlags;
                AlphaThreshold = alphaThreshold;
            }

            public bool HasNormals => Normals != null && Normals.Length == Vertices.Length;
            public bool HasUvs => Uvs != null && Uvs.Length == Vertices.Length;
            public int VertexCount => Vertices?.Length ?? 0;
        }

        /// <summary>
        /// Compatibility wrapper kept for code paths that still want Unity meshes.
        /// World baking should prefer <see cref="BuildRaw"/>.
        /// </summary>
        public static List<BuiltMesh> Build(NifFile nif)
        {
            var raw = BuildRaw(nif);
            var result = new List<BuiltMesh>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var mesh = CreateMesh(raw[i], string.IsNullOrEmpty(raw[i].Name) ? $"NifMesh{i}" : raw[i].Name);
                result.Add(new BuiltMesh(
                    mesh,
                    raw[i].TexturePath,
                    raw[i].Name,
                    raw[i].LocalBounds,
                    raw[i].AlphaFlags,
                    raw[i].AlphaThreshold));
            }
            return result;
        }

        public static List<RawBuiltMesh> BuildRaw(NifFile nif)
        {
            bool debug = !string.IsNullOrEmpty(DebugMeshPath)
                         && nif.Path.IndexOf(DebugMeshPath, System.StringComparison.OrdinalIgnoreCase) >= 0;
            var result = new List<RawBuiltMesh>();
            foreach (int rootIndex in nif.Roots)
            {
                if (rootIndex < 0 || rootIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[rootIndex] is NiAVObject av)
                    Walk(nif, av, Matrix4x4.identity, result);
            }

            if (debug)
                Debug.Log($"[VVardenfell] Built {result.Count} raw meshes from {nif.Path}");

            return MergeCompatible(result);
        }

        public readonly struct BuiltMesh
        {
            public readonly Mesh Mesh;
            public readonly string TexturePath;
            public readonly string Name;
            public readonly Bounds LocalBounds;
            public readonly ushort AlphaFlags;
            public readonly byte AlphaThreshold;

            public BuiltMesh(Mesh mesh, string texturePath, string name, Bounds bounds, ushort alphaFlags, byte alphaThreshold)
            {
                Mesh = mesh;
                TexturePath = texturePath;
                Name = name;
                LocalBounds = bounds;
                AlphaFlags = alphaFlags;
                AlphaThreshold = alphaThreshold;
            }
        }

        private static void Walk(NifFile nif, NiAVObject obj, Matrix4x4 parent, List<RawBuiltMesh> result)
        {
            if ((obj.Flags & 0x0001) != 0)
                return;

            var r = obj.Rotation;
            float s = obj.Scale;
            var local = new Matrix4x4();
            local.m00 = r.m00 * s; local.m01 = r.m01 * s; local.m02 = r.m02 * s; local.m03 = obj.Translation.x;
            local.m10 = r.m10 * s; local.m11 = r.m11 * s; local.m12 = r.m12 * s; local.m13 = obj.Translation.y;
            local.m20 = r.m20 * s; local.m21 = r.m21 * s; local.m22 = r.m22 * s; local.m23 = obj.Translation.z;
            local.m30 = 0f;        local.m31 = 0f;        local.m32 = 0f;        local.m33 = 1f;
            var world = parent * local;

            if (obj is RootCollisionNode)
                return;

            if (obj is NiGeometry geometry && TryResolveRenderableGeometry(nif, geometry, out _))
            {
                FindAlpha(nif, geometry, out ushort alphaFlags, out byte alphaThreshold);
                var built = BuildRawMesh(nif, geometry, world, alphaFlags, alphaThreshold);
                if (built.HasValue)
                    result.Add(built.Value);
                return;
            }

            if (obj is NiNode node && node.Children != null)
            {
                foreach (int childIdx in node.Children)
                {
                    if (childIdx < 0 || childIdx >= nif.Records.Length)
                        continue;
                    if (nif.Records[childIdx] is NiAVObject child)
                        Walk(nif, child, world, result);
                }
            }
        }

        private static RawBuiltMesh? BuildRawMesh(
            NifFile nif,
            NiGeometry geometry,
            Matrix4x4 world,
            ushort alphaFlags,
            byte alphaThreshold)
        {
            if (!TryResolveRenderableGeometry(nif, geometry, out var data))
                return null;

            int vcount = data.NumVertices;
            var verts = new Vector3[vcount];
            var normals = data.Normals != null ? new Vector3[vcount] : null;
            string texturePath = FindTexture(nif, geometry, out int uvSet);
            var uvs = ResolveUvSet(data, uvSet, vcount);

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < vcount; i++)
            {
                var p = world.MultiplyPoint3x4(data.Vertices[i]);
                var pU = new Vector3(p.x, p.z, p.y) * WorldScale.MwUnitsToMeters;
                verts[i] = pU;
                if (pU.x < min.x) min.x = pU.x; if (pU.x > max.x) max.x = pU.x;
                if (pU.y < min.y) min.y = pU.y; if (pU.y > max.y) max.y = pU.y;
                if (pU.z < min.z) min.z = pU.z; if (pU.z > max.z) max.z = pU.z;

                if (normals != null)
                {
                    var n = world.MultiplyVector(data.Normals[i]);
                    normals[i] = new Vector3(n.x, n.z, n.y).normalized;
                }
            }

            int[] indices = BuildTriangleIndices(geometry, data);
            if (indices.Length == 0)
                return null;

            var bounds = new Bounds((min + max) * 0.5f, max - min);
            return new RawBuiltMesh(
                verts,
                normals,
                uvs,
                indices,
                texturePath,
                geometry.Name ?? "",
                bounds,
                alphaFlags,
                alphaThreshold);
        }

        private static Mesh CreateMesh(in RawBuiltMesh raw, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = raw.VertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(raw.Vertices);
            if (raw.HasNormals)
                mesh.SetNormals(raw.Normals);
            if (raw.HasUvs)
                mesh.SetUVs(0, raw.Uvs);
            mesh.SetTriangles(raw.Indices, 0);
            if (!raw.HasNormals)
                mesh.RecalculateNormals();
            mesh.bounds = raw.LocalBounds;
            return mesh;
        }

        private static void FindAlpha(NifFile nif, NiGeometry geometry, out ushort flags, out byte threshold)
        {
            flags = 0;
            threshold = 0;
            if (geometry.PropertyLinks == null)
                return;
            foreach (int pIdx in geometry.PropertyLinks)
            {
                if (pIdx < 0 || pIdx >= nif.Records.Length)
                    continue;
                if (nif.Records[pIdx] is NiAlphaProperty ap)
                {
                    flags = ap.Flags;
                    threshold = ap.Threshold;
                    return;
                }
            }
        }

        private static string FindTexture(NifFile nif, NiGeometry geometry, out int uvSet)
        {
            uvSet = 0;
            if (geometry.PropertyLinks == null)
                return null;
            foreach (int pIdx in geometry.PropertyLinks)
            {
                if (pIdx < 0 || pIdx >= nif.Records.Length)
                    continue;
                if (nif.Records[pIdx] is NiTexturingProperty texProp
                    && texProp.Textures != null
                    && texProp.Textures.Length > 0
                    && texProp.Textures[0] != null
                    && texProp.Textures[0].Enabled)
                {
                    var src = Resolve<NiSourceTexture>(nif, texProp.Textures[0].SourceTexture);
                    if (src != null && src.External)
                    {
                        uvSet = (int)texProp.Textures[0].UVSet;
                        return src.FileName;
                    }
                }
            }
            return null;
        }

        private static bool TryResolveRenderableGeometry(NifFile nif, NiGeometry geometry, out NiGeometryData data)
        {
            data = null;
            if (geometry == null)
                return false;

            data = geometry switch
            {
                NiTriShape => Resolve<NiTriShapeData>(nif, geometry.Data),
                NiTriStrips => Resolve<NiTriStripsData>(nif, geometry.Data),
                _ => null,
            };

            return data != null
                && data.Vertices != null
                && data.NumVertices > 0;
        }

        private static Vector2[] ResolveUvSet(NiGeometryData data, int uvSet, int vertexCount)
        {
            if (data?.UvSets == null || data.UvSets.Length == 0)
                return null;

            if (uvSet < 0 || uvSet >= data.UvSets.Length || data.UvSets[uvSet] == null || data.UvSets[uvSet].Length != vertexCount)
                uvSet = 0;

            return data.UvSets[uvSet] != null && data.UvSets[uvSet].Length == vertexCount
                ? (Vector2[])data.UvSets[uvSet].Clone()
                : null;
        }

        private static int[] BuildTriangleIndices(NiGeometry geometry, NiGeometryData data)
        {
            switch (geometry)
            {
                case NiTriShape when data is NiTriShapeData triShapeData:
                {
                    if (triShapeData.Triangles == null || triShapeData.Triangles.Length == 0)
                        return System.Array.Empty<int>();

                    int[] indices = new int[triShapeData.Triangles.Length];
                    for (int i = 0; i < triShapeData.Triangles.Length; i += 3)
                    {
                        indices[i + 0] = triShapeData.Triangles[i + 0];
                        indices[i + 1] = triShapeData.Triangles[i + 2];
                        indices[i + 2] = triShapeData.Triangles[i + 1];
                    }
                    return indices;
                }
                case NiTriStrips when data is NiTriStripsData triStripsData:
                    return ConvertTriangleStrips(triStripsData.Strips);
                default:
                    return System.Array.Empty<int>();
            }
        }

        private static int[] ConvertTriangleStrips(ushort[][] strips)
        {
            if (strips == null || strips.Length == 0)
                return System.Array.Empty<int>();

            var triangles = new List<int>();
            for (int s = 0; s < strips.Length; s++)
            {
                var strip = strips[s];
                if (strip == null || strip.Length < 3)
                    continue;

                ushort b = strip[0];
                ushort c = strip[1];
                for (int i = 2; i < strip.Length; i++)
                {
                    ushort a = b;
                    b = c;
                    c = strip[i];
                    if (a == b || b == c || a == c)
                        continue;

                    if ((i & 1) == 0)
                    {
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                    }
                }
            }

            return triangles.ToArray();
        }

        private static T Resolve<T>(NifFile nif, int link) where T : NifRecord
        {
            if (link < 0 || link >= nif.Records.Length)
                return null;
            return nif.Records[link] as T;
        }

        private static List<RawBuiltMesh> MergeCompatible(List<RawBuiltMesh> source)
        {
            if (source.Count <= 1)
                return source;

            var groups = new Dictionary<MergeKey, List<RawBuiltMesh>>(new MergeKeyComparer());
            for (int i = 0; i < source.Count; i++)
            {
                var built = source[i];
                var key = new MergeKey(
                    built.TexturePath,
                    built.AlphaFlags,
                    built.AlphaThreshold,
                    built.HasNormals,
                    built.HasUvs);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<RawBuiltMesh>();
                list.Add(built);
            }

            var merged = new List<RawBuiltMesh>(groups.Count);
            foreach (var kv in groups)
            {
                var group = kv.Value;
                if (group.Count == 1)
                {
                    merged.Add(group[0]);
                    continue;
                }

                merged.Add(MergeGroup(kv.Key, group));
            }

            return merged;
        }

        private static RawBuiltMesh MergeGroup(in MergeKey key, List<RawBuiltMesh> group)
        {
            int totalVerts = 0;
            int totalIndices = 0;
            for (int i = 0; i < group.Count; i++)
            {
                totalVerts += group[i].VertexCount;
                totalIndices += group[i].Indices.Length;
            }

            var verts = new Vector3[totalVerts];
            Vector3[] normals = key.HasNormals ? new Vector3[totalVerts] : null;
            Vector2[] uvs = key.HasUvs ? new Vector2[totalVerts] : null;
            var indices = new int[totalIndices];

            int vertexOffset = 0;
            int indexOffset = 0;
            var bounds = group[0].LocalBounds;

            for (int i = 0; i < group.Count; i++)
            {
                var raw = group[i];
                System.Array.Copy(raw.Vertices, 0, verts, vertexOffset, raw.Vertices.Length);
                if (normals != null)
                    System.Array.Copy(raw.Normals, 0, normals, vertexOffset, raw.Normals.Length);
                if (uvs != null)
                    System.Array.Copy(raw.Uvs, 0, uvs, vertexOffset, raw.Uvs.Length);

                for (int j = 0; j < raw.Indices.Length; j++)
                    indices[indexOffset + j] = raw.Indices[j] + vertexOffset;

                bounds.Encapsulate(raw.LocalBounds.min);
                bounds.Encapsulate(raw.LocalBounds.max);
                vertexOffset += raw.Vertices.Length;
                indexOffset += raw.Indices.Length;
            }

            return new RawBuiltMesh(
                verts,
                normals,
                uvs,
                indices,
                key.TexturePath,
                string.IsNullOrEmpty(group[0].Name) ? $"Merged({group.Count})" : $"{group[0].Name}[merged:{group.Count}]",
                bounds,
                key.AlphaFlags,
                key.AlphaThreshold);
        }

        private readonly struct MergeKey
        {
            public readonly string TexturePath;
            public readonly ushort AlphaFlags;
            public readonly byte AlphaThreshold;
            public readonly bool HasNormals;
            public readonly bool HasUvs;

            public MergeKey(string texturePath, ushort alphaFlags, byte alphaThreshold, bool hasNormals, bool hasUvs)
            {
                TexturePath = texturePath ?? "";
                AlphaFlags = alphaFlags;
                AlphaThreshold = alphaThreshold;
                HasNormals = hasNormals;
                HasUvs = hasUvs;
            }
        }

        private sealed class MergeKeyComparer : IEqualityComparer<MergeKey>
        {
            public bool Equals(MergeKey x, MergeKey y)
            {
                return x.AlphaFlags == y.AlphaFlags
                    && x.AlphaThreshold == y.AlphaThreshold
                    && x.HasNormals == y.HasNormals
                    && x.HasUvs == y.HasUvs
                    && string.Equals(x.TexturePath, y.TexturePath, System.StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(MergeKey obj)
            {
                int hash = obj.AlphaFlags;
                hash = (hash * 397) ^ obj.AlphaThreshold;
                hash = (hash * 397) ^ obj.HasNormals.GetHashCode();
                hash = (hash * 397) ^ obj.HasUvs.GetHashCode();
                hash = (hash * 397) ^ System.StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TexturePath ?? "");
                return hash;
            }
        }
    }
}
