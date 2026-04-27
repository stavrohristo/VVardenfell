using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    internal static partial class WorldBakeService
    {
        [Unity.Burst.BurstCompile]
        private struct CreateStaticColliderBlobJob : Unity.Jobs.IJob
        {
            [Unity.Collections.ReadOnly]
            public Unity.Collections.NativeArray<Unity.Mathematics.float3> Points;
            [Unity.Collections.ReadOnly]
            public Unity.Collections.NativeArray<Unity.Mathematics.int3> Triangles;
            public Unity.Collections.NativeArray<Unity.Entities.BlobAssetReference<Unity.Physics.Collider>> Output;

            public void Execute()
            {
                Output[0] = Unity.Physics.MeshCollider.Create(Points, Triangles, Unity.Physics.CollisionFilter.Default);
            }
        }


        private static readonly uint StatTag = EsmFourCC.Make('S', 'T', 'A', 'T');
        private static readonly uint DoorTag = EsmFourCC.Make('D', 'O', 'O', 'R');


        private const bool EnableModelPrefabWorldRefs = false;


        private readonly struct CellBakeWorkItem
        {
            public readonly CellHeader Cell;
            public readonly bool IsInterior;
            public readonly long LandOffset;
            public readonly string Key;
            public readonly string OutputPath;
            public readonly Vector3 CellOrigin;

            public CellBakeWorkItem(
                CellHeader cell,
                bool isInterior,
                long landOffset,
                string key,
                string outputPath,
                Vector3 cellOrigin)
            {
                Cell = cell;
                IsInterior = isInterior;
                LandOffset = landOffset;
                Key = key;
                OutputPath = outputPath;
                CellOrigin = cellOrigin;
            }
        }


        private sealed class WorkerContext : IDisposable
        {
            public readonly EsmReader RefsReader;
            public readonly EsmReader LandReader;

            public WorkerContext(string esmPath)
            {
                RefsReader = new EsmReader(esmPath);
                LandReader = new EsmReader(esmPath);
            }

            public void Dispose()
            {
                RefsReader.Dispose();
                LandReader.Dispose();
            }
        }


        private sealed class ModelSource
        {
            public readonly string ModelPath;
            public readonly NifFile Nif;
            public readonly NifMeshBuilder.RawBuiltMesh[] Meshes;
            public readonly CollisionPayload Collision;
            public readonly CollisionPayload AutoVisualStaticCollision;
            public readonly CollisionExtractionSource CollisionSource;
            public readonly ModelPrefabSource Prefab;

            public ModelSource(
                string modelPath,
                NifFile nif,
                NifMeshBuilder.RawBuiltMesh[] meshes,
                CollisionPayload collision,
                CollisionPayload autoVisualStaticCollision,
                CollisionExtractionSource collisionSource,
                ModelPrefabSource prefab)
            {
                ModelPath = modelPath;
                Nif = nif;
                Meshes = meshes;
                Collision = collision;
                AutoVisualStaticCollision = autoVisualStaticCollision;
                CollisionSource = collisionSource;
                Prefab = prefab;
            }
        }


        private enum ActorSkinPartReferenceType : byte
        {
            Head = 0,
            Hair = 1,
            Neck = 2,
            Cuirass = 3,
            Groin = 4,
            Skirt = 5,
            RightHand = 6,
            LeftHand = 7,
            RightWrist = 8,
            LeftWrist = 9,
            Shield = 10,
            RightForearm = 11,
            LeftForearm = 12,
            RightUpperarm = 13,
            LeftUpperarm = 14,
            RightFoot = 15,
            LeftFoot = 16,
            RightAnkle = 17,
            LeftAnkle = 18,
            RightKnee = 19,
            LeftKnee = 20,
            RightLeg = 21,
            LeftLeg = 22,
            RightPauldron = 23,
            LeftPauldron = 24,
            Weapon = 25,
            Tail = 26,
            Count = 27,
        }


        private readonly struct StagedPlacedRefData
        {
            public readonly string ModelPath;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly ContentReference ContentReference;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public StagedPlacedRefData(
                string modelPath,
                uint placedRefId,
                int doorMetaIndex,
                ContentReference contentReference,
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                ModelPath = modelPath ?? string.Empty;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentReference = contentReference;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }


        private readonly struct StagedRefData
        {
            public readonly NifMeshBuilder.RawBuiltMesh Built;
            public readonly CollisionPayload Collision;
            public readonly string MeshSourceLabel;
            public readonly uint MaterialFlags;
            public readonly string TexturePath;
            public readonly bool IsInteractable;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly ContentReference ContentReference;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public StagedRefData(
                in NifMeshBuilder.RawBuiltMesh built,
                in CollisionPayload collision,
                string meshSourceLabel,
                uint materialFlags,
                string texturePath,
                bool isInteractable,
                uint placedRefId,
                int doorMetaIndex,
                ContentReference contentReference,
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                Built = built;
                Collision = collision;
                MeshSourceLabel = meshSourceLabel;
                MaterialFlags = materialFlags;
                TexturePath = texturePath;
                IsInteractable = isInteractable;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentReference = contentReference;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }


        private readonly struct PreparedRefData
        {
            public readonly NifMeshBuilder.RawBuiltMesh Built;
            public readonly CollisionPayload Collision;
            public readonly string MeshSourceLabel;
            public readonly uint MaterialFlags;
            public readonly string TexturePath;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly ContentReference ContentReference;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public PreparedRefData(
                in NifMeshBuilder.RawBuiltMesh built,
                in CollisionPayload collision,
                string meshSourceLabel,
                uint materialFlags,
                string texturePath,
                uint placedRefId,
                int doorMetaIndex,
                ContentReference contentReference,
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                Built = built;
                Collision = collision;
                MeshSourceLabel = meshSourceLabel;
                MaterialFlags = materialFlags;
                TexturePath = texturePath;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentReference = contentReference;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }


        private readonly struct PreparedWorkItem
        {
            public readonly bool IsInteractable;
            public readonly NifMeshBuilder.RawBuiltMesh Built;
            public readonly CollisionPayload Collision;
            public readonly uint MaterialFlags;
            public readonly string TexturePath;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly ContentReference ContentReference;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;
            public readonly string MeshSourceLabel;

            public PreparedWorkItem(
                bool isInteractable,
                in NifMeshBuilder.RawBuiltMesh built,
                in CollisionPayload collision,
                uint materialFlags,
                string texturePath,
                uint placedRefId,
                int doorMetaIndex,
                ContentReference contentReference,
                Vector3 position,
                Quaternion rotation,
                float scale,
                string meshSourceLabel)
            {
                IsInteractable = isInteractable;
                Built = built;
                Collision = collision;
                MaterialFlags = materialFlags;
                TexturePath = texturePath;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentReference = contentReference;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                MeshSourceLabel = meshSourceLabel;
            }
        }


        private sealed class StagedCellData
        {
            public CellBakeWorkItem WorkItem;
            public BakeManifest.BakedCellState PreviousState;
            public CellEnvironmentData Environment;
            public LandRecord Land;
            public string[] TerrainTexturePaths;
            public ushort[] LayerGrid;
            public List<CellBakery.BakedRef> BakedRefs;
            public List<DoorRefEntry> DoorEntries;
            public CellBakery.StaticCollision StaticCollision;
            public List<StagedPlacedRefData> PlacedRefs;
            public List<StagedRefData> PendingRefs;
            public List<PreparedRefData> PreparedRefs;
            public string Fingerprint;
            public int[] GlobalMeshIndices;
            public int[] GlobalMaterialIndices;
            public int[] GlobalTextureIndices;
            public int[] GlobalCollisionIndices;
            public int[] GlobalTerrainLayerIndices;
            public bool NeedsWrite;
            public int PendingRefCount;
            public int PreparedWorkItemCount;
            public int SourcePreparedMeshCount;
            public int StaticGroupCount;
            public int CombinedGroupCount;
            public int InteractableWorkItemCount;
            public long CombinedPreparedPayloadBytes;
            public int CollisionStaticCandidateCount;
            public int CollisionStaticAggregateCount;
            public int CollisionStaticAggregateTriangleCount;
            public int CollisionPerPlacedRefCount;
            public int CollisionMissingPayloadCount;
            public int CollisionAuthoredRootCount;
            public int CollisionAutoVisualStaticCount;
            public int CollisionExplicitNoCollisionCount;
            public int CollisionNoColliderCount;
            public List<string> CollisionMissingPayloadSamples;
        }


        private enum PlacedRefCollisionAssignment : byte
        {
            NoCollider,
            CellStaticAggregate,
            PerPlacedRef,
        }


        private readonly struct RenderShardRequestKey : System.IEquatable<RenderShardRequestKey>
        {
            public readonly int BucketKey;
            public readonly string FamilyKey;
            public readonly int GlobalMeshIndex;

            public RenderShardRequestKey(int bucketKey, string familyKey, int globalMeshIndex)
            {
                BucketKey = bucketKey;
                FamilyKey = familyKey ?? string.Empty;
                GlobalMeshIndex = globalMeshIndex;
            }

            public bool Equals(RenderShardRequestKey other)
                => BucketKey == other.BucketKey
                   && GlobalMeshIndex == other.GlobalMeshIndex
                   && string.Equals(FamilyKey, other.FamilyKey, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is RenderShardRequestKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(BucketKey, GlobalMeshIndex, StringComparer.Ordinal.GetHashCode(FamilyKey ?? string.Empty));
        }


        private readonly struct RenderShardResolvedAssignment
        {
            public readonly int RenderShardIndex;
            public readonly int LocalMeshIndex;

            public RenderShardResolvedAssignment(int renderShardIndex, int localMeshIndex)
            {
                RenderShardIndex = renderShardIndex;
                LocalMeshIndex = localMeshIndex;
            }
        }


        private sealed class RenderShardRequestKeyComparer : IComparer<RenderShardRequestKey>
        {
            public static readonly RenderShardRequestKeyComparer Instance = new();

            public int Compare(RenderShardRequestKey x, RenderShardRequestKey y)
            {
                int bucketCompare = x.BucketKey.CompareTo(y.BucketKey);
                if (bucketCompare != 0)
                    return bucketCompare;

                int familyCompare = string.CompareOrdinal(x.FamilyKey, y.FamilyKey);
                if (familyCompare != 0)
                    return familyCompare;

                return x.GlobalMeshIndex.CompareTo(y.GlobalMeshIndex);
            }
        }


        private sealed class PreparedCellWriteData
        {
            public string Key;
            public string OutputPath;
            public bool IsInterior;
            public string CellId;
            public int GridX;
            public int GridY;
            public uint Flags;
            public CellEnvironmentData Environment;
            public LandRecord Land;
            public CellBakery.StaticCollision StaticCollision;
            public byte[] TerrainHeightBytes;
            public byte[] TerrainNormalBytes;
            public byte[] LayerGridBytes;
            public byte[] WorldMapBytes;
            public byte[] RefBytes;
            public byte[] DoorBytes;
            public int RefCount;
            public int DoorCount;
            public BuiltCellBlobData BlobData;
            public FinalCellWriteBuffer FinalBuffer;
        }


        private sealed class BuiltCellBlobData
        {
            public byte[] TerrainColliderBlobBytes;
            public byte[] StaticCollisionBlobBytes;
        }


        private sealed class FinalCellWriteBuffer
        {
            public byte[] HeaderBytes;
            public byte[] TerrainHeightBytes;
            public byte[] TerrainNormalBytes;
            public byte[] TerrainColliderChunkBytes;
            public byte[] LayerGridBytes;
            public byte[] WorldMapBytes;
            public byte[] StaticCollisionChunkBytes;
            public byte[] RefCountBytes;
            public byte[] RefBytes;
            public byte[] DoorCountBytes;
            public byte[] DoorBytes;
        }


        private sealed class CellWriteProgressInfo
        {
            public string Subphase;
            public int ClaimedCount;
            public int CompletedCount;
            public string LastCompletedCellKey;
        }


        private sealed class CellWriteScratch : IDisposable
        {
            public Unity.Collections.NativeArray<float> TerrainHeights;

            public CellWriteScratch()
            {
                TerrainHeights = new Unity.Collections.NativeArray<float>(
                    LandRecord.Size * LandRecord.Size,
                    Unity.Collections.Allocator.Persistent);
            }

            public void Dispose()
            {
                if (TerrainHeights.IsCreated)
                    TerrainHeights.Dispose();
            }
        }


        private sealed class PendingStaticColliderJob : IDisposable
        {
            public PreparedCellWriteData PreparedWrite;
            public Unity.Collections.NativeArray<Unity.Mathematics.float3> Points;
            public Unity.Collections.NativeArray<Unity.Mathematics.int3> Triangles;
            public Unity.Collections.NativeArray<Unity.Entities.BlobAssetReference<Unity.Physics.Collider>> Output;
            public Unity.Jobs.JobHandle Handle;

            public void Dispose()
            {
                if (Output.IsCreated)
                {
                    if (Output[0].IsCreated)
                        Output[0].Dispose();
                    Output.Dispose();
                }

                if (Triangles.IsCreated)
                    Triangles.Dispose();
                if (Points.IsCreated)
                    Points.Dispose();
            }
        }


        private readonly struct BatchKey
        {
            public readonly uint MaterialFlags;
            public readonly string TexturePath;
            public readonly bool HasNormals;
            public readonly bool HasUvs;

            public BatchKey(uint materialFlags, string texturePath, bool hasNormals, bool hasUvs)
            {
                MaterialFlags = materialFlags;
                TexturePath = texturePath ?? string.Empty;
                HasNormals = hasNormals;
                HasUvs = hasUvs;
            }
        }


        private sealed class BatchKeyComparer : IEqualityComparer<BatchKey>
        {
            public bool Equals(BatchKey x, BatchKey y)
            {
                return x.MaterialFlags == y.MaterialFlags
                    && x.HasNormals == y.HasNormals
                    && x.HasUvs == y.HasUvs
                    && string.Equals(x.TexturePath, y.TexturePath, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(BatchKey obj)
            {
                int hash = (int)obj.MaterialFlags;
                hash = (hash * 397) ^ obj.HasNormals.GetHashCode();
                hash = (hash * 397) ^ obj.HasUvs.GetHashCode();
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TexturePath ?? string.Empty);
                return hash;
            }
        }


    }
}
