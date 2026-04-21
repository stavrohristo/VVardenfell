using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Terrain
{
    /// <summary>
    /// Builds a UnityEngine.Mesh from a decoded LAND record. Positions are in Unity space
    /// (Y-up, meters). The mesh is centered on the cell origin so we can place it with a
    /// LocalTransform at the cell's world-coord position.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public static Mesh Build(LandRecord land)
        {
            const int N = LandRecord.Size;
            const float spacingMw = LandRecord.CellUnits / (float)(N - 1); // 128 MW units
            float spacingU = spacingMw * WorldScale.MwUnitsToMeters;

            var verts = new Vector3[N * N];
            var normals = new Vector3[N * N];
            var uvs = new Vector2[N * N];
            var tris = new int[(N - 1) * (N - 1) * 6];

            // Morrowind stores verts row-major with (x, y) = (col, row). Convert to Unity:
            //   worldMW: (x * spacing, y * spacing, height)
            //   Unity:   (x, z=height, y) * MwToMeters, Y/Z swapped.
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float h = land.HasHeights ? land.Heights[i] : 0f;
                    verts[i] = new Vector3(
                        x * spacingU,
                        h * WorldScale.MwUnitsToMeters,
                        y * spacingU);
                    uvs[i] = new Vector2(x / (float)(N - 1), y / (float)(N - 1));

                    if (land.Normals != null)
                    {
                        // Morrowind stored normals are (nx, ny, nz) signed bytes, Z-up.
                        float nx = land.Normals[i * 3 + 0] / 127f;
                        float ny = land.Normals[i * 3 + 1] / 127f;
                        float nz = land.Normals[i * 3 + 2] / 127f;
                        // Swap Y and Z for Unity's Y-up.
                        normals[i] = new Vector3(nx, nz, ny).normalized;
                    }
                }
            }

            // Triangles — CCW after the coord mapping above.
            int t = 0;
            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    int v00 = y * N + x;
                    int v10 = y * N + x + 1;
                    int v01 = (y + 1) * N + x;
                    int v11 = (y + 1) * N + x + 1;

                    tris[t++] = v00; tris[t++] = v01; tris[t++] = v10;
                    tris[t++] = v10; tris[t++] = v01; tris[t++] = v11;
                }
            }

            var mesh = new Mesh { name = $"Terrain({land.GridX},{land.GridY})" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16; // 4225 < 65535
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            if (land.Normals != null) mesh.SetNormals(normals);
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
