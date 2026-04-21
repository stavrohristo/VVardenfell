using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Writes a single exterior cell's baked data to <c>cells/&lt;gx&gt;_&lt;gy&gt;.bin</c>.
    ///
    /// File layout:
    ///   u32 magic 'CELL'
    ///   i32 gridX, gridY
    ///   u32 flags              (CellFlagHasTerrain, CellFlagHasNormals, CellFlagHasVtex)
    ///   (if HasTerrain)
    ///     float[4225] heights  (Unity meters, Y-up already applied)
    ///     (if HasNormals)
    ///       sbyte[3 * 4225] normals
    ///   (if HasVtex)
    ///     u16[256] layerIndices (dense bakery layer index per 16x16 quadrant, row-major)
    ///   u32 refCount
    ///   RefEntry[refCount]     (44 bytes each)
    /// </summary>
    public static class CellBakery
    {
        public const uint MagicCell = 0x4C4C4543u; // 'CELL'

        public readonly struct BakedRef
        {
            public readonly int MeshIndex;
            public readonly int MaterialIndex; // blend-variant (0=Opaque,1=AlphaTest,2=AlphaBlend)
            public readonly int SliceIndex;    // slice in the shared ref Texture2DArray, -1 = no texture
            public readonly Vector3 PositionUnity;
            public readonly Quaternion RotationUnity;
            public readonly float Scale;

            public BakedRef(int mesh, int mat, int slice, Vector3 pos, Quaternion rot, float scale)
            { MeshIndex = mesh; MaterialIndex = mat; SliceIndex = slice; PositionUnity = pos; RotationUnity = rot; Scale = scale; }
        }

        public static void Write(string path, int gridX, int gridY, LandRecord land, ushort[] layerGrid, IReadOnlyList<BakedRef> refs)
        {
            uint flags = 0;
            bool hasTerrain = land != null && land.HasHeights;
            bool hasNormals = hasTerrain && land.Normals != null;
            bool hasVtex    = hasTerrain && layerGrid != null && layerGrid.Length == LandRecord.NumTextures;
            if (hasTerrain) flags |= CacheFormat.CellFlagHasTerrain;
            if (hasNormals) flags |= CacheFormat.CellFlagHasNormals;
            if (hasVtex)    flags |= CacheFormat.CellFlagHasVtex;

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCell);
            w.Write(gridX);
            w.Write(gridY);
            w.Write(flags);

            if (hasTerrain)
            {
                // Store heights in Unity meters (Y-up already), ready for mesh building with
                // the same conversion formula we use in TerrainMeshBuilder.
                const int N = LandRecord.Size;
                for (int i = 0; i < N * N; i++)
                    w.Write(land.Heights[i] * WorldScale.MwUnitsToMeters);
                if (hasNormals)
                {
                    for (int i = 0; i < land.Normals.Length; i++) w.Write(land.Normals[i]);
                }
                if (hasVtex)
                {
                    for (int i = 0; i < LandRecord.NumTextures; i++) w.Write(layerGrid[i]);
                }
            }

            w.Write((uint)refs.Count);
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                w.Write(r.MeshIndex);
                w.Write(r.MaterialIndex);
                w.Write(r.SliceIndex);
                w.Write(r.PositionUnity.x); w.Write(r.PositionUnity.y); w.Write(r.PositionUnity.z);
                w.Write(r.RotationUnity.x); w.Write(r.RotationUnity.y); w.Write(r.RotationUnity.z); w.Write(r.RotationUnity.w);
                w.Write(r.Scale);
            }
        }

        /// <summary>
        /// Convert a raw CellReference (MW coords, Euler radians) to a Unity-space transform.
        /// </summary>
        public static void ToUnityTransform(in CellReference r, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(
                r.PosX * WorldScale.MwUnitsToMeters,
                r.PosZ * WorldScale.MwUnitsToMeters,   // MW Z → Unity Y
                r.PosY * WorldScale.MwUnitsToMeters);  // MW Y → Unity Z

            // OpenMW (objectpaging.cpp) composes: Quat(rot[2],-Z) * Quat(rot[1],-Y) * Quat(rot[0],-X)
            // in OSG row-vector form — rot[2] applied first, rot[0] last. Converting to Unity
            // (column-vector, Y/Z axis swap) by conjugation with the swap matrix S:
            //   S·Rx_MW(θ)·S = Rx_Unity(-θ)      S·Ry_MW(θ)·S = Rz_Unity(-θ)      S·Rz_MW(θ)·S = Ry_Unity(-θ)
            // The axis-swap sign flip cancels OSG's negated axes, leaving positive Unity angles.
            var qx = Quaternion.AngleAxis(r.RotX * Mathf.Rad2Deg, Vector3.right);   // MW X → Unity X
            var qz = Quaternion.AngleAxis(r.RotY * Mathf.Rad2Deg, Vector3.forward); // MW Y → Unity Z
            var qy = Quaternion.AngleAxis(r.RotZ * Mathf.Rad2Deg, Vector3.up);       // MW Z → Unity Y
            rot = qx * qz * qy;
        }
    }
}
