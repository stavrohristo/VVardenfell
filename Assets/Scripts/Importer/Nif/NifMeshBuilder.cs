using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Walks a parsed NifFile and emits one <see cref="UnityEngine.Mesh"/> per renderable
    /// NiTriShape, with its local-within-file world transform baked in.
    ///
    /// Conversions applied:
    ///   - Morrowind units → meters via <see cref="WorldScale.MwUnitsToMeters"/>.
    ///   - Handedness: Morrowind is right-handed Z-up, Unity is left-handed Y-up.
    ///     We swap Y and Z on positions/normals and reverse triangle winding.
    ///
    /// The RootCollisionNode subtree is skipped — it is collision geometry, not visual.
    /// </summary>
    public static class NifMeshBuilder
    {
        /// <summary>Case-insensitive substring match; if the NIF path contains it, verbose-dump to console.</summary>
        public static string DebugMeshPath = "";

        public static List<BuiltMesh> Build(NifFile nif)
        {
            bool debug = !string.IsNullOrEmpty(DebugMeshPath)
                         && nif.Path.IndexOf(DebugMeshPath, System.StringComparison.OrdinalIgnoreCase) >= 0;
            var result = new List<BuiltMesh>();
            foreach (int rootIndex in nif.Roots)
            {
                if (rootIndex < 0 || rootIndex >= nif.Records.Length) continue;
                if (nif.Records[rootIndex] is NiAVObject av)
                    Walk(nif, av, Matrix4x4.identity, result);
            }
            return result;
        }

        public readonly struct BuiltMesh
        {
            public readonly Mesh Mesh;
            public readonly string TexturePath;  // may be null/empty
            public readonly string Name;         // NiTriShape name, for debugging
            public readonly Bounds LocalBounds;
            public readonly ushort AlphaFlags;   // 0 when no NiAlphaProperty; see property.hpp Flag_Blending/Testing
            public readonly byte AlphaThreshold; // cutoff byte for alpha testing

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

        private static void Walk(NifFile nif, NiAVObject obj, Matrix4x4 parent, List<BuiltMesh> result)
        {
            if ((obj.Flags & 0x0001) != 0) return; // hidden

            // The NIF "rotation" matrix can carry non-uniform scale and negative (reflection) scale
            // per OpenMW's NiTransform::toMatrix comment. Build the local matrix directly instead
            // of decomposing to (quat + uniform scale), which was losing that information.
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

            if (obj is NiTriShape tri)
            {
                var data = Resolve<NiTriShapeData>(nif, tri.Data);
                if (data == null || data.Vertices == null || data.NumVertices == 0 || data.Triangles == null)
                    return;
                FindAlpha(nif, tri, out ushort alphaFlags, out byte alphaThreshold);
                var built = BuildMesh(tri, data, world, FindTexture(nif, tri), alphaFlags, alphaThreshold);
                if (built.HasValue) result.Add(built.Value);
                return;
            }

            if (obj is NiNode node && node.Children != null)
            {
                foreach (int childIdx in node.Children)
                {
                    if (childIdx < 0 || childIdx >= nif.Records.Length) continue;
                    if (nif.Records[childIdx] is NiAVObject child)
                        Walk(nif, child, world, result);
                }
            }
        }


        private static BuiltMesh? BuildMesh(NiTriShape tri, NiTriShapeData data, Matrix4x4 world, string texturePath, ushort alphaFlags, byte alphaThreshold)
        {
            int vcount = data.NumVertices;
            var verts = new Vector3[vcount];
            var normals = new Vector3[vcount];
            bool hasNormals = data.Normals != null;
            var uvs = (data.UvSets != null && data.UvSets.Length > 0) ? data.UvSets[0] : null;

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < vcount; i++)
            {
                var p = world.MultiplyPoint3x4(data.Vertices[i]);
                // Morrowind Z-up → Unity Y-up: swap Y and Z
                var pU = new Vector3(p.x, p.z, p.y) * WorldScale.MwUnitsToMeters;
                verts[i] = pU;
                if (pU.x < min.x) min.x = pU.x; if (pU.x > max.x) max.x = pU.x;
                if (pU.y < min.y) min.y = pU.y; if (pU.y > max.y) max.y = pU.y;
                if (pU.z < min.z) min.z = pU.z; if (pU.z > max.z) max.z = pU.z;

                if (hasNormals)
                {
                    var n = world.MultiplyVector(data.Normals[i]);
                    normals[i] = new Vector3(n.x, n.z, n.y).normalized;
                }
            }

            // Reverse winding: (a, b, c) → (a, c, b) to flip faces after Y/Z swap
            int itri = data.Triangles.Length;
            var indices = new int[itri];
            for (int i = 0; i < itri; i += 3)
            {
                indices[i + 0] = data.Triangles[i + 0];
                indices[i + 1] = data.Triangles[i + 2];
                indices[i + 2] = data.Triangles[i + 1];
            }

            var mesh = new Mesh { name = string.IsNullOrEmpty(tri.Name) ? "NiTriShape" : tri.Name };
            mesh.indexFormat = vcount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            if (hasNormals) mesh.SetNormals(normals);
            if (uvs != null && uvs.Length == vcount) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            if (!hasNormals) mesh.RecalculateNormals();

            var bounds = new Bounds((min + max) * 0.5f, max - min);
            mesh.bounds = bounds;

            return new BuiltMesh(mesh, texturePath, tri.Name ?? "", bounds, alphaFlags, alphaThreshold);
        }

        private static void FindAlpha(NifFile nif, NiTriShape tri, out ushort flags, out byte threshold)
        {
            flags = 0; threshold = 0;
            if (tri.PropertyLinks == null) return;
            foreach (int pIdx in tri.PropertyLinks)
            {
                if (pIdx < 0 || pIdx >= nif.Records.Length) continue;
                if (nif.Records[pIdx] is NiAlphaProperty ap)
                {
                    flags = ap.Flags;
                    threshold = ap.Threshold;
                    return;
                }
            }
        }

        private static string FindTexture(NifFile nif, NiTriShape tri)
        {
            if (tri.PropertyLinks == null) return null;
            foreach (int pIdx in tri.PropertyLinks)
            {
                if (pIdx < 0 || pIdx >= nif.Records.Length) continue;
                if (nif.Records[pIdx] is NiTexturingProperty texProp
                    && texProp.Textures != null
                    && texProp.Textures.Length > 0
                    && texProp.Textures[0] != null
                    && texProp.Textures[0].Enabled)
                {
                    var src = Resolve<NiSourceTexture>(nif, texProp.Textures[0].SourceTexture);
                    if (src != null && src.External) return src.FileName;
                }
            }
            return null;
        }

        private static T Resolve<T>(NifFile nif, int link) where T : NifRecord
        {
            if (link < 0 || link >= nif.Records.Length) return null;
            return nif.Records[link] as T;
        }

        // Matrix4x4Rotation removed — we used to decompose to a quaternion here, which silently
        // discarded any non-uniform/negative scale baked into the NIF rotation matrix.
    }
}
