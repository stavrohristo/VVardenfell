using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Walks a parsed <see cref="NifFile"/> and collects the triangle-soup geometry
    /// authored under every <see cref="RootCollisionNode"/> subtree — that's how
    /// Morrowind ships collision (it predates Havok; no <c>bhk*</c> records exist
    /// in MW NIFs). <see cref="NifMeshBuilder"/> already prunes these subtrees
    /// when building render meshes; this class does the mirror walk and keeps
    /// them.
    ///
    /// Output: a single concatenated mesh in NIF-root-local space, already
    /// converted to Unity coordinates (Y-up, left-handed, meters) using the same
    /// rules as the render path:
    ///   - swap Y and Z on positions
    ///   - reverse triangle winding
    ///   - scale by <see cref="WorldScale.MwUnitsToMeters"/>
    /// </summary>
    public static class NifCollisionExtractor
    {
        /// <summary>
        /// Returns <c>true</c> if the NIF contained any RootCollisionNode geometry.
        /// <paramref name="payload"/> is left default when the function returns false
        /// (e.g. vegetation, ghosts, and other props MW intentionally ships without
        /// collision).
        /// </summary>
        public static bool TryExtract(NifFile nif, out CollisionPayload payload)
        {
            var verts = new List<Vector3>();
            var indices = new List<int>();

            foreach (int rootIndex in nif.Roots)
            {
                if (rootIndex < 0 || rootIndex >= nif.Records.Length) continue;
                if (nif.Records[rootIndex] is NiAVObject av)
                    Walk(nif, av, Matrix4x4.identity, inCollision: false, verts, indices);
            }

            if (verts.Count == 0 || indices.Count == 0)
            {
                payload = default;
                return false;
            }

            payload = new CollisionPayload(verts.ToArray(), indices.ToArray());
            return true;
        }

        private static void Walk(NifFile nif, NiAVObject obj, Matrix4x4 parent, bool inCollision,
                                 List<Vector3> verts, List<int> indices)
        {
            // Mirror NifMeshBuilder.Walk: build local matrix preserving non-uniform
            // or reflected scale instead of decomposing to (quat + uniform scale).
            var r = obj.Rotation;
            float s = obj.Scale;
            var local = new Matrix4x4();
            local.m00 = r.m00 * s; local.m01 = r.m01 * s; local.m02 = r.m02 * s; local.m03 = obj.Translation.x;
            local.m10 = r.m10 * s; local.m11 = r.m11 * s; local.m12 = r.m12 * s; local.m13 = obj.Translation.y;
            local.m20 = r.m20 * s; local.m21 = r.m21 * s; local.m22 = r.m22 * s; local.m23 = obj.Translation.z;
            local.m30 = 0f;        local.m31 = 0f;        local.m32 = 0f;        local.m33 = 1f;
            var world = parent * local;

            // Hidden nodes *are* walked for collision — MW uses hidden RootCollisionNode
            // children routinely. Only the render path honors the hidden flag.

            // Entering a collision subtree flips the flag for all descendants.
            bool childInCollision = inCollision || obj is RootCollisionNode;

            if (childInCollision && obj is NiTriShape tri)
            {
                var data = Resolve<NiTriShapeData>(nif, tri.Data);
                if (data != null && data.Vertices != null && data.NumVertices > 0 && data.Triangles != null)
                    AppendTriShape(data, world, verts, indices);
                // NiTriShape has no children; fall through to return.
                return;
            }

            if (obj is NiNode node && node.Children != null)
            {
                foreach (int childIdx in node.Children)
                {
                    if (childIdx < 0 || childIdx >= nif.Records.Length) continue;
                    if (nif.Records[childIdx] is NiAVObject child)
                        Walk(nif, child, world, childInCollision, verts, indices);
                }
            }
        }

        private static void AppendTriShape(NiTriShapeData data, Matrix4x4 world,
                                           List<Vector3> verts, List<int> indices)
        {
            int baseVert = verts.Count;
            int vcount = data.NumVertices;
            for (int i = 0; i < vcount; i++)
            {
                var p = world.MultiplyPoint3x4(data.Vertices[i]);
                // MW → Unity: swap Y/Z, scale to meters. Same as the render path.
                var pU = new Vector3(p.x, p.z, p.y) * WorldScale.MwUnitsToMeters;
                verts.Add(pU);
            }

            int itri = data.Triangles.Length;
            for (int i = 0; i < itri; i += 3)
            {
                // Winding reversed (0,2,1) to match the Y/Z swap — same convention
                // as NifMeshBuilder.BuildMesh. Unity Physics MeshCollider expects
                // CCW triangles in its left-handed system.
                indices.Add(baseVert + data.Triangles[i + 0]);
                indices.Add(baseVert + data.Triangles[i + 2]);
                indices.Add(baseVert + data.Triangles[i + 1]);
            }
        }

        private static T Resolve<T>(NifFile nif, int link) where T : NifRecord
        {
            if (link < 0 || link >= nif.Records.Length) return null;
            return nif.Records[link] as T;
        }
    }

    /// <summary>
    /// Extracted collision geometry from a single NIF, already in NIF-root-local
    /// Unity coordinates (meters, Y-up, left-handed, CCW winding). Consumers
    /// hand this directly to <c>Unity.Physics.MeshCollider.Create</c>.
    /// </summary>
    public readonly struct CollisionPayload
    {
        public readonly Vector3[] Vertices;
        public readonly int[] Indices;  // triangle list; int32 since some NIFs exceed 65535 verts

        public CollisionPayload(Vector3[] vertices, int[] indices)
        {
            Vertices = vertices;
            Indices = indices;
        }

        public int TriangleCount => Indices != null ? Indices.Length / 3 : 0;
        public bool IsEmpty => Vertices == null || Vertices.Length == 0 || Indices == null || Indices.Length == 0;
    }
}
