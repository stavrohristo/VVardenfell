using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;
using MeshCollider = Unity.Physics.MeshCollider;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Writes a single baked cell file. Exterior cells use grid-keyed filenames under
    /// <c>cells/</c>; interior cells use hashed filenames under <c>interiors/</c>.
    ///
    /// FormatVersion >= 18: per-cell static-collision and terrain colliders are now pre-built
    /// <c>Unity.Physics</c> blobs (<c>MeshCollider</c> / <c>TerrainCollider</c>) with the BVH
    /// baked in - runtime load becomes a byte-for-byte deserialization instead of a
    /// multi-hundred-millisecond BVH rebuild per cell.
    ///
    /// File layout:
    ///   u32 magic 'CELL'
    ///   i32 gridX, gridY
    ///   u32 flags              (CellFlagHasTerrain, CellFlagHasNormals, CellFlagHasVtex, CellFlagHasStaticCollision)
    ///   (if HasTerrain)
    ///     float[4225] heights
    ///     (if HasNormals)
    ///       sbyte[3 * 4225] normals
    ///     u32 terrainBlobLen
    ///     byte[terrainBlobLen] pre-built TerrainCollider blob
    ///   (if HasVtex)
    ///     u16[256] layerIndices
    ///   (if HasStaticCollision)
    ///     u32 staticBlobLen
    ///     byte[staticBlobLen] pre-built MeshCollider blob
    ///   u32 refCount
    ///   RefEntry[refCount]     (56 bytes each)
    ///   u32 doorCount
    ///   DoorRefEntry[doorCount]
    /// </summary>
    public static class CellBakery
    {
        public const uint MagicCell = 0x4C4C4543u; // 'CELL'

        public readonly struct BakedRef
        {
            public readonly int MeshIndex;
            public readonly int MaterialIndex;
            public readonly int SliceIndex;
            public readonly int CollisionIndex;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly Vector3 PositionUnity;
            public readonly Quaternion RotationUnity;
            public readonly float Scale;

            public BakedRef(
                int mesh,
                int mat,
                int slice,
                int collision,
                uint placedRefId,
                int doorMetaIndex,
                Vector3 pos,
                Quaternion rot,
                float scale)
            {
                MeshIndex = mesh;
                MaterialIndex = mat;
                SliceIndex = slice;
                CollisionIndex = collision;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                PositionUnity = pos;
                RotationUnity = rot;
                Scale = scale;
            }
        }

        public readonly struct StaticCollision
        {
            public readonly Vector3[] Vertices;
            public readonly int[] Indices;

            public StaticCollision(Vector3[] vertices, int[] indices)
            {
                Vertices = vertices;
                Indices = indices;
            }

            public bool IsEmpty => Vertices == null || Vertices.Length == 0 || Indices == null || Indices.Length == 0;
        }

        public static void Write(
            string path,
            int gridX,
            int gridY,
            LandRecord land,
            ushort[] layerGrid,
            in StaticCollision staticCollision,
            IReadOnlyList<BakedRef> refs,
            IReadOnlyList<DoorRefEntry> doors,
            float cellMeters,
            NativeList<float3> scratchV,
            NativeList<int3> scratchI,
            NativeArray<float> scratchHeights)
        {
            uint flags = 0;
            bool hasTerrain = land != null && land.HasHeights;
            bool hasNormals = hasTerrain && land.Normals != null;
            bool hasVtex = hasTerrain && layerGrid != null && layerGrid.Length == LandRecord.NumTextures;
            bool hasStaticCollision = !staticCollision.IsEmpty;
            if (hasTerrain) flags |= CacheFormat.CellFlagHasTerrain;
            if (hasNormals) flags |= CacheFormat.CellFlagHasNormals;
            if (hasVtex) flags |= CacheFormat.CellFlagHasVtex;
            if (hasStaticCollision) flags |= CacheFormat.CellFlagHasStaticCollision;

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(MagicCell);
            w.Write(gridX);
            w.Write(gridY);
            w.Write(flags);

            if (hasTerrain)
            {
                const int N = LandRecord.Size;
                for (int i = 0; i < N * N; i++)
                {
                    float h = land.Heights[i] * WorldScale.MwUnitsToMeters;
                    scratchHeights[i] = h;
                    w.Write(h);
                }

                if (hasNormals)
                {
                    for (int i = 0; i < land.Normals.Length; i++)
                        w.Write(land.Normals[i]);
                }

                float spacing = cellMeters / (N - 1);
                var terrainScale = new float3(spacing, 1f, spacing);
                var terrainBlob = TerrainCollider.Create(
                    scratchHeights,
                    new int2(N, N),
                    terrainScale,
                    TerrainCollider.CollisionMethod.VertexSamples);
                BlobStreamIO.WriteLengthPrefixed(w, terrainBlob, CacheFormat.PhysicsBlobVersion);
                if (terrainBlob.IsCreated)
                    terrainBlob.Dispose();

                if (hasVtex)
                {
                    for (int i = 0; i < LandRecord.NumTextures; i++)
                        w.Write(layerGrid[i]);
                }
            }

            if (hasStaticCollision)
            {
                int vertexCount = staticCollision.Vertices.Length;
                scratchV.Clear();
                scratchV.ResizeUninitialized(vertexCount);
                var vertexSpan = scratchV.AsArray();
                for (int i = 0; i < vertexCount; i++)
                {
                    var vertex = staticCollision.Vertices[i];
                    vertexSpan[i] = new float3(vertex.x, vertex.y, vertex.z);
                }

                int triangleCount = staticCollision.Indices.Length / 3;
                scratchI.Clear();
                scratchI.ResizeUninitialized(triangleCount);
                var triangleSpan = scratchI.AsArray();
                for (int t = 0; t < triangleCount; t++)
                {
                    triangleSpan[t] = new int3(
                        staticCollision.Indices[t * 3 + 0],
                        staticCollision.Indices[t * 3 + 1],
                        staticCollision.Indices[t * 3 + 2]);
                }

                var statBlob = MeshCollider.Create(vertexSpan, triangleSpan, CollisionFilter.Default);
                BlobStreamIO.WriteLengthPrefixed(w, statBlob, CacheFormat.PhysicsBlobVersion);
                if (statBlob.IsCreated)
                    statBlob.Dispose();
            }

            w.Write((uint)refs.Count);
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                w.Write(r.MeshIndex);
                w.Write(r.MaterialIndex);
                w.Write(r.SliceIndex);
                w.Write(r.CollisionIndex);
                w.Write(r.PlacedRefId);
                w.Write(r.DoorMetaIndex);
                w.Write(r.PositionUnity.x);
                w.Write(r.PositionUnity.y);
                w.Write(r.PositionUnity.z);
                w.Write(r.RotationUnity.x);
                w.Write(r.RotationUnity.y);
                w.Write(r.RotationUnity.z);
                w.Write(r.RotationUnity.w);
                w.Write(r.Scale);
            }

            int doorCount = doors?.Count ?? 0;
            w.Write((uint)doorCount);
            for (int i = 0; i < doorCount; i++)
            {
                var door = doors[i];
                w.Write(door.PlacedRefId);
                w.Write(door.Flags);
                w.Write(door.DestPosX);
                w.Write(door.DestPosY);
                w.Write(door.DestPosZ);
                w.Write(door.DestRotX);
                w.Write(door.DestRotY);
                w.Write(door.DestRotZ);
                w.Write(door.DestRotW);
                w.Write(door.DestinationCellId ?? string.Empty);
            }
        }

        public static void ToUnityTransform(in CellReference r, out Vector3 pos, out Quaternion rot)
        {
            ToUnityTransformRaw(r.PosX, r.PosY, r.PosZ, r.RotX, r.RotY, r.RotZ, out pos, out rot);
        }

        public static void ToUnityTransformRaw(
            float posX,
            float posY,
            float posZ,
            float rotX,
            float rotY,
            float rotZ,
            out Vector3 pos,
            out Quaternion rot)
        {
            pos = new Vector3(
                posX * WorldScale.MwUnitsToMeters,
                posZ * WorldScale.MwUnitsToMeters,
                posY * WorldScale.MwUnitsToMeters);

            var qx = Quaternion.AngleAxis(rotX * Mathf.Rad2Deg, Vector3.right);
            var qz = Quaternion.AngleAxis(rotY * Mathf.Rad2Deg, Vector3.forward);
            var qy = Quaternion.AngleAxis(rotZ * Mathf.Rad2Deg, Vector3.up);
            rot = qx * qz * qy;
        }
    }
}
