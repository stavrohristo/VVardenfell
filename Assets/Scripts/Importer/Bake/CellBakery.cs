using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Shared cell-bake value helpers used before DOTS section serialization.
    /// </summary>
    public static class CellBakery
    {
        public readonly struct BakedRef
        {
            public readonly int SpawnModeRaw;
            public readonly int ModelPrefabIndex;
            public readonly int LocalMeshIndex;
            public readonly int LocalMaterialIndex;
            public readonly int SliceIndex;
            public readonly int CollisionIndex;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly int ContentHandleValue;
            public readonly int ContentKind;
            public readonly Vector3 PositionUnity;
            public readonly Quaternion RotationUnity;
            public readonly float Scale;

            public BakedRef(
                RefSpawnMode spawnMode,
                int modelPrefabIndex,
                int localMeshIndex,
                int localMaterialIndex,
                int slice,
                int collision,
                uint placedRefId,
                int doorMetaIndex,
                int contentHandleValue,
                int contentKind,
                Vector3 pos,
                Quaternion rot,
                float scale)
            {
                SpawnModeRaw = (int)spawnMode;
                ModelPrefabIndex = modelPrefabIndex;
                LocalMeshIndex = localMeshIndex;
                LocalMaterialIndex = localMaterialIndex;
                SliceIndex = slice;
                CollisionIndex = collision;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentHandleValue = contentHandleValue;
                ContentKind = contentKind;
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
