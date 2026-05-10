using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
        private static readonly ConcurrentDictionary<string, int> s_AnimatedStaticRefCounts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, int> s_UnsupportedObjectControllerRefCounts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> s_DroppedBakeRefWarnings = new(StringComparer.OrdinalIgnoreCase);


        private readonly struct CellBakeWorkItem
        {
            public readonly CellHeader Cell;
            public readonly bool IsInterior;
            public readonly long LandOffset;
            public readonly string LandSourcePath;
            public readonly CellHeader[] CellRecords;
            public readonly string Key;
            public readonly Vector3 CellOrigin;

            public CellBakeWorkItem(
                CellHeader cell,
                bool isInterior,
                long landOffset,
                string landSourcePath,
                CellHeader[] cellRecords,
                string key,
                Vector3 cellOrigin)
            {
                Cell = cell;
                IsInterior = isInterior;
                LandOffset = landOffset;
                LandSourcePath = landSourcePath ?? cell.SourcePath;
                CellRecords = cellRecords ?? new[] { cell };
                Key = key;
                CellOrigin = cellOrigin;
            }
        }


        private sealed class WorkerContext : IDisposable
        {
            readonly Dictionary<string, EsmReader> _readers = new(StringComparer.OrdinalIgnoreCase);

            public EsmReader GetReader(string esmPath)
            {
                if (string.IsNullOrWhiteSpace(esmPath))
                    throw new InvalidOperationException("Cannot read ESM data without a source path.");

                if (!_readers.TryGetValue(esmPath, out var reader))
                {
                    reader = new EsmReader(esmPath);
                    _readers.Add(esmPath, reader);
                }

                return reader;
            }

            public void Dispose()
            {
                foreach (var pair in _readers)
                    pair.Value.Dispose();
                _readers.Clear();
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
            public readonly bool HasObjectAnimation;
            public readonly bool HasUnsupportedObjectControllers;
            public readonly float EffectControllerStopTime;

            public ModelSource(
                string modelPath,
                NifFile nif,
                NifMeshBuilder.RawBuiltMesh[] meshes,
                CollisionPayload collision,
                CollisionPayload autoVisualStaticCollision,
                CollisionExtractionSource collisionSource,
                ModelPrefabSource prefab,
                bool hasObjectAnimation,
                bool hasUnsupportedObjectControllers,
                float effectControllerStopTime)
            {
                ModelPath = modelPath;
                Nif = nif;
                Meshes = meshes;
                Collision = collision;
                AutoVisualStaticCollision = autoVisualStaticCollision;
                CollisionSource = collisionSource;
                Prefab = prefab;
                HasObjectAnimation = hasObjectAnimation;
                HasUnsupportedObjectControllers = hasUnsupportedObjectControllers;
                EffectControllerStopTime = effectControllerStopTime;
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
            public readonly ModelSource Model;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly ContentReference ContentReference;
            public readonly bool AttachModelCollision;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public StagedPlacedRefData(
                string modelPath,
                ModelSource model,
                uint placedRefId,
                int doorMetaIndex,
                ContentReference contentReference,
                Vector3 position,
                Quaternion rotation,
                float scale,
                bool attachModelCollision = false)
            {
                ModelPath = modelPath ?? string.Empty;
                Model = model;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
                ContentReference = contentReference;
                AttachModelCollision = attachModelCollision;
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
            public List<PlacedRefSoulEntry> CapturedSouls;
            public List<PlacedRefLockEntry> LockStates;
            public List<CombinedCellRenderChunkDef> CombinedRenderChunks;
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
            public bool BakeCombinedCellRenderChunks;
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
            public int CombinedRenderCandidateRefCount;
            public int CombinedRenderCandidateLeafCount;
            public int CombinedRenderRejectedNonStaticRefCount;
            public int CombinedRenderRejectedOversizedLeafCount;
            public int CombinedRenderRejectedWeakGroupCount;
            public int CombinedRenderBucketGroupCount;
            public int CombinedRenderRescuedWeakLeafCount;
            public int CombinedRenderCellWideChunkCount;
            public int CombinedRenderCoalescedSourceChunkCount;
            public int CombinedRenderCoalescedChunkCount;
            public int CombinedRenderEmittedChunkCount;
            public int CombinedRenderEmittedMemberLeafCount;
            public int CombinedRenderEmittedMemberRefCount;
            public int CombinedRenderEmittedVertexCount;
            public int CombinedRenderEmittedIndexCount;
            public int CombinedRenderEmittedMultiTextureChunkCount;
            public int CombinedRenderEmittedUniqueTextureCount;
            public List<string> CollisionMissingPayloadSamples;
        }


        private enum PlacedRefCollisionAssignment : byte
        {
            NoCollider,
            CellStaticAggregate,
            PerPlacedRef,
        }


        private sealed class PreparedCellWriteData
        {
            public string Key;
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
            public byte[] CapturedSoulBytes;
            public byte[] LockStateBytes;
            public byte[] CombinedRenderChunkBytes;
            public int RefCount;
            public int DoorCount;
            public int CapturedSoulCount;
            public int LockStateCount;
            public int CombinedRenderChunkCount;
            public BuiltCellBlobData BlobData;
        }


        private sealed class BuiltCellBlobData
        {
            public byte[] TerrainColliderBlobBytes;
            public byte[] StaticCollisionBlobBytes;
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
