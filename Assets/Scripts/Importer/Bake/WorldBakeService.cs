using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    internal static class WorldBakeService
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
        private static readonly ProfilerMarker k_PrepareDirtyCells = new("VV.Bake.PrepareDirtyCells");
        private static readonly ProfilerMarker k_PrepareDirtyCellsWorkerLoop = new("VV.Bake.PrepareDirtyCells.WorkerLoop");
        private static readonly ProfilerMarker k_PrepareDirtyCell = new("VV.Bake.PrepareDirtyCell");
        private static readonly ProfilerMarker k_PrepareTerrainTexturePaths = new("VV.Bake.PrepareTerrainTexturePaths");
        private static readonly ProfilerMarker k_BuildPreparedCellRefs = new("VV.Bake.BuildPreparedCellRefs");
        private static readonly ProfilerMarker k_BuildPreparedCellRefsGroup = new("VV.Bake.BuildPreparedCellRefs.Group");
        private static readonly ProfilerMarker k_BuildPreparedCellRefsCombine = new("VV.Bake.BuildPreparedCellRefs.Combine");
        private static readonly ProfilerMarker k_BuildPreparedCellRefsEncode = new("VV.Bake.BuildPreparedCellRefs.Encode");
        private static readonly ProfilerMarker k_BuildPreparedRefData = new("VV.Bake.BuildPreparedRefData");
        private static readonly ProfilerMarker k_CombineGroupMesh = new("VV.Bake.CombineGroupMesh");
        private static readonly ProfilerMarker k_ResolveDirtyCellIndices = new("VV.Bake.ResolveDirtyCellIndices");
        private static readonly ProfilerMarker k_AssignRenderShards = new("VV.Bake.AssignRenderShards");
        private static readonly ProfilerMarker k_AssignRenderShardsFamilyPrecompute = new("VV.Bake.AssignRenderShards.FamilyPrecompute");
        private static readonly ProfilerMarker k_AssignRenderShardsBuildRequests = new("VV.Bake.AssignRenderShards.BuildRequests");
        private static readonly ProfilerMarker k_AssignRenderShardsReduce = new("VV.Bake.AssignRenderShards.ReduceUnique");
        private static readonly ProfilerMarker k_AssignRenderShardsResolve = new("VV.Bake.AssignRenderShards.Resolve");
        private static readonly ProfilerMarker k_AssignRenderShardsApply = new("VV.Bake.AssignRenderShards.Apply");
        private static readonly ProfilerMarker k_PrepareCellWritePayload = new("VV.Bake.PrepareCellWritePayload");
        private static readonly ProfilerMarker k_BuildCellColliderBlobs = new("VV.Bake.BuildCellColliderBlobs");
        private static readonly ProfilerMarker k_CreateTerrainColliderBlob = new("VV.Bake.CreateTerrainColliderBlob");
        private static readonly ProfilerMarker k_CreateStaticColliderBlob = new("VV.Bake.CreateStaticColliderBlob");
        private static readonly ProfilerMarker k_SerializeCellBlob = new("VV.Bake.SerializeCellBlob");
        private static readonly ProfilerMarker k_AssembleFinalCellWriteBuffer = new("VV.Bake.AssembleFinalCellWriteBuffer");
        private static readonly ProfilerMarker k_FlushCellFile = new("VV.Bake.FlushCellFile");

        private const bool EnableModelPrefabWorldRefs = false;

        private readonly struct CellBakeWorkItem
        {
            public readonly CellHeader Cell;
            public readonly bool IsInterior;
            public readonly long LandOffset;
            public readonly string Key;
            public readonly string OutputPath;
            public readonly string AuditOutputPath;
            public readonly Vector3 CellOrigin;

            public CellBakeWorkItem(
                CellHeader cell,
                bool isInterior,
                long landOffset,
                string key,
                string outputPath,
                string auditOutputPath,
                Vector3 cellOrigin)
            {
                Cell = cell;
                IsInterior = isInterior;
                LandOffset = landOffset;
                Key = key;
                OutputPath = outputPath;
                AuditOutputPath = auditOutputPath;
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
            public readonly NifMeshBuilder.RawBuiltMesh[] Meshes;
            public readonly CollisionPayload Collision;
            public readonly CollisionPayload AutoVisualStaticCollision;
            public readonly CollisionExtractionSource CollisionSource;
            public readonly ModelPrefabSource Prefab;

            public ModelSource(
                string modelPath,
                NifMeshBuilder.RawBuiltMesh[] meshes,
                CollisionPayload collision,
                CollisionPayload autoVisualStaticCollision,
                CollisionExtractionSource collisionSource,
                ModelPrefabSource prefab)
            {
                ModelPath = modelPath;
                Meshes = meshes;
                Collision = collision;
                AutoVisualStaticCollision = autoVisualStaticCollision;
                CollisionSource = collisionSource;
                Prefab = prefab;
            }
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
            public List<RefPlacementAuditEntry> PlacementAuditEntries;
            public string Fingerprint;
            public int[] GlobalMeshIndices;
            public int[] GlobalMaterialIndices;
            public int[] GlobalTextureIndices;
            public int[] GlobalCollisionIndices;
            public int[] GlobalTerrainLayerIndices;
            public bool NeedsWrite;
            public double BuildPreparedCellRefsMs;
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
            public string AuditOutputPath;
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
            public byte[] PlacementAuditBytes;
            public int RefCount;
            public int DoorCount;
            public BuiltCellBlobData BlobData;
            public FinalCellWriteBuffer FinalBuffer;
            public double BlobBuildMs;
            public double FlushMs;
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
            public long StartTicks;

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

        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress, GameplayContentData gameplayContent = null)
        {
            string esmPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(esmPath) || !File.Exists(bsaPath))
            {
                progress.Error = "Morrowind.esm or Morrowind.bsa missing under the configured install path.";
                progress.Done = true;
                yield break;
            }

            CachePaths.Warmup();
            CachePaths.EnsureExists();
            var gameplayContentLookup = BuildGameplayContentLookup(gameplayContent);

            progress.Stage = "Source Indexing";
            progress.Label = "Opening archives";
            progress.Current = 0;
            progress.Total = 5;
            yield return null;

            using var sharedBsa = BsaArchive.Open(bsaPath);

            progress.Label = "Building record index";
            progress.Current = 1;
            yield return null;
            RecordIndex recordIndex;
            using (var esm = new EsmReader(esmPath))
                recordIndex = RecordIndex.Build(esm);

            progress.Label = "Enumerating cells";
            progress.Current = 2;
            yield return null;
            var exteriorCells = new List<CellHeader>(2048);
            var interiorCells = new List<CellHeader>(2048);
            using (var esm = new EsmReader(esmPath))
            {
                foreach (var cell in CellIndex.Enumerate(esm))
                {
                    if (cell.IsInterior)
                        interiorCells.Add(cell);
                    else
                        exteriorCells.Add(cell);
                }
            }

            progress.Label = "Indexing terrain";
            progress.Current = 3;
            yield return null;
            Dictionary<(int, int), long> landOffsets;
            using (var esm = new EsmReader(esmPath))
                landOffsets = LandIndex.BuildOffsetMap(esm);

            progress.Label = "Indexing land textures";
            progress.Current = 4;
            yield return null;
            Dictionary<int, string> ltexMap;
            using (var esm = new EsmReader(esmPath))
                ltexMap = LtexIndex.Build(esm);

            progress.Stage = "Dependency Snapshot";
            progress.Label = "Loading previous cache state";
            progress.Current = 0;
            progress.Total = 1;
            yield return null;

            BakeManifest.TryRead(CachePaths.Manifest, out var existingManifest);
            var previousStateByKey = new Dictionary<string, BakeManifest.BakedCellState>(StringComparer.OrdinalIgnoreCase);
            if (existingManifest?.CellStates != null)
            {
                for (int i = 0; i < existingManifest.CellStates.Length; i++)
                {
                    var state = existingManifest.CellStates[i];
                    if (state != null && !string.IsNullOrEmpty(state.Key))
                        previousStateByKey[state.Key] = state;
                }
            }

            var textureResolver = new TexturePathResolver(sharedBsa);
            var bakeryMeshes = new MeshBakery();
            bakeryMeshes.TryLoadExisting(CachePaths.MeshCatalog, CachePaths.Meshes);
            var bakeryMaterials = new MaterialBakery();
            bakeryMaterials.TryLoadExisting(CachePaths.MaterialCatalog);
            var bakeryTextures = new TextureBakery(sharedBsa, textureResolver);
            bakeryTextures.TryLoadExisting(CachePaths.TextureCatalog);
            int defaultTexIdx = bakeryTextures.AddOrGet(LtexIndex.DefaultTexturePath);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);
            bakeryLayers.TryLoadExisting(CachePaths.TerrainLayers);
            var bakeryCollisions = new CollisionBakery();
            bakeryCollisions.TryLoadExisting(CachePaths.CollisionCatalog);
            var bakeryRenderShards = new RenderShardBakery();
            bakeryRenderShards.TryLoadExisting(CachePaths.RenderShards);
            var bakeryModelPrefabs = new ModelPrefabBakery();
            var modelCache = new ConcurrentDictionary<string, Lazy<ModelSource>>(StringComparer.OrdinalIgnoreCase);

            progress.Current = 1;
            yield return null;

            var bsaByName = new Dictionary<string, BsaEntry>(sharedBsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in sharedBsa.Entries)
                bsaByName[entry.Name] = entry;

            SeedRuntimeSpawnableModels(gameplayContent, sharedBsa, bsaByName, modelCache);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var workItems = new List<CellBakeWorkItem>(exteriorCells.Count + interiorCells.Count);
            for (int i = 0; i < exteriorCells.Count; i++)
            {
                var cell = exteriorCells[i];
                landOffsets.TryGetValue((cell.GridX, cell.GridY), out var landOffset);
                var cellOrigin = new Vector3(cell.GridX * cellMeters, 0f, cell.GridY * cellMeters);
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    false,
                    landOffset,
                    BuildExteriorKey(cell.GridX, cell.GridY),
                    CachePaths.CellFile(cell.GridX, cell.GridY),
                    CachePaths.CellPlacementAuditFile(cell.GridX, cell.GridY),
                    cellOrigin));
            }

            for (int i = 0; i < interiorCells.Count; i++)
            {
                var cell = interiorCells[i];
                string interiorId = cell.Name ?? string.Empty;
                workItems.Add(new CellBakeWorkItem(
                    cell,
                    true,
                    0,
                    BuildInteriorKey(interiorId),
                    CachePaths.InteriorCellFile(interiorId),
                    CachePaths.InteriorCellPlacementAuditFile(interiorId),
                    Vector3.zero));
            }

            progress.Stage = "Per-Cell Planning";
            progress.Label = "Staging cells";
            progress.Current = 0;
            progress.Total = workItems.Count;
            yield return null;

            var stagedCells = new StagedCellData[workItems.Count];
            int plannedCount = 0;
            Exception stageFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            var stageTask = Task.Run(() =>
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, workItems.Count),
                        options,
                        () => new WorkerContext(esmPath),
                        (range, _, worker) =>
                        {
                            for (int i = range.Item1; i < range.Item2; i++)
                            {
                                stagedCells[i] = StageCell(
                                    worker,
                                    workItems[i],
                                recordIndex,
                                    gameplayContentLookup,
                                    sharedBsa,
                                    bsaByName,
                                    ltexMap,
                                    previousStateByKey,
                                    modelCache);
                                Interlocked.Increment(ref plannedCount);
                            }
                            return worker;
                        },
                        worker => worker.Dispose());
                }
                catch (Exception ex)
                {
                    stageFailure = ex;
                }
            });

            while (!stageTask.IsCompleted)
            {
                progress.Current = plannedCount;
                yield return null;
            }

            if (stageFailure != null)
                throw stageFailure;

            progress.Current = workItems.Count;
            yield return null;

            progress.Stage = "Finalizing";
            progress.Label = "Collecting dirty cells";
            progress.Current = 0;
            progress.Total = workItems.Count;
            yield return null;

            var cellStates = new BakeManifest.BakedCellState[stagedCells.Length];
            var expectedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirtyCells = new List<StagedCellData>();

            for (int i = 0; i < stagedCells.Length; i++)
            {
                var staged = stagedCells[i];
                expectedOutputs.Add(staged.WorkItem.OutputPath);
                expectedOutputs.Add(staged.WorkItem.AuditOutputPath);
                if (staged.NeedsWrite)
                    dirtyCells.Add(staged);

                cellStates[i] = BuildCellState(staged);
                progress.Current = i + 1;
                if (((i + 1) & 7) == 0)
                    yield return null;
            }

            yield return PrepareDirtyCellsIncremental(dirtyCells, progress, ltexMap);
            yield return BuildModelPrefabsIncremental(modelCache, progress, bakeryModelPrefabs, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryCollisions);
            yield return ResolveDirtyCellIndicesIncremental(dirtyCells, progress, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryLayers, bakeryCollisions, bakeryModelPrefabs);
            yield return AssignRenderShardIndicesIncremental(dirtyCells, progress, bakeryMeshes, bakeryTextures, bakeryRenderShards);

            for (int i = 0; i < stagedCells.Length; i++)
                cellStates[i] = BuildCellState(stagedCells[i]);

            yield return WriteDirtyCellsIncremental(dirtyCells, progress, cellMeters);

            progress.Stage = "Writing";
            progress.Current = 0;
            progress.Total = 12;

            progress.Label = "meshes.bin";
            progress.Current = 1;
            yield return null;
            if (bakeryMeshes.Modified || !File.Exists(CachePaths.Meshes) || !File.Exists(CachePaths.MeshCatalog) || !File.Exists(CachePaths.MeshNames))
            {
                bakeryMeshes.WriteTo(CachePaths.Meshes);
                bakeryMeshes.WriteCatalog(CachePaths.MeshCatalog);
                bakeryMeshes.WriteNames(CachePaths.MeshNames);
            }

            progress.Label = "materials.bin";
            progress.Current = 2;
            yield return null;
            if (bakeryMaterials.Modified || !File.Exists(CachePaths.Materials) || !File.Exists(CachePaths.MaterialCatalog))
            {
                bakeryMaterials.WriteTo(CachePaths.Materials);
                bakeryMaterials.WriteCatalog(CachePaths.MaterialCatalog);
            }

            progress.Label = "render_shards.bin";
            progress.Current = 3;
            yield return null;
            if (bakeryRenderShards.Modified || !File.Exists(CachePaths.RenderShards))
                RenderShardFile.Write(CachePaths.RenderShards, bakeryRenderShards.BuildCatalog());

            progress.Label = "model_prefabs.bin";
            progress.Current = 4;
            yield return null;
            if (bakeryModelPrefabs.Modified || !File.Exists(CachePaths.ModelPrefabs))
                ModelPrefabFile.Write(CachePaths.ModelPrefabs, bakeryModelPrefabs.BuildCatalog());

            progress.Label = "textures.bin";
            progress.Current = 5;
            yield return null;
            if (bakeryTextures.Modified || !File.Exists(CachePaths.TexturesIndex) || !File.Exists(CachePaths.TextureCatalog))
            {
                bakeryTextures.WriteIndex(CachePaths.TexturesIndex);
                bakeryTextures.WriteCatalog(CachePaths.TextureCatalog);
            }

            progress.Label = "terrain_layers.bin";
            progress.Current = 6;
            yield return null;
            if (bakeryLayers.Modified || !File.Exists(CachePaths.TerrainLayers))
                bakeryLayers.WriteTo(CachePaths.TerrainLayers);

            progress.Label = "collisions.bin";
            progress.Current = 7;
            yield return null;
            if (bakeryCollisions.Modified || !File.Exists(CachePaths.Collisions) || !File.Exists(CachePaths.CollisionCatalog))
            {
                bakeryCollisions.WriteTo(CachePaths.Collisions);
                bakeryCollisions.WriteCatalog(CachePaths.CollisionCatalog);
            }

            progress.Label = "Pruning stale cells";
            progress.Current = 8;
            yield return null;
            PruneOrphans(CachePaths.CellsDir, expectedOutputs);
            PruneOrphans(CachePaths.InteriorCellsDir, expectedOutputs);

            progress.Label = "mesh_cache_report.txt";
            progress.Current = 9;
            yield return null;
            WriteMeshCacheReport(stagedCells, bakeryMeshes);

            progress.Label = "world_collision_validation.txt";
            progress.Current = 10;
            yield return null;
            WriteWorldCollisionValidationReport(stagedCells);

            progress.Label = "ui.bin";
            progress.Current = 11;
            yield return null;
            UiAssetBakery.Bake(config, sharedBsa, progress);

            progress.Label = "manifest.bin";
            progress.Current = 12;
            yield return null;
            var manifest = BakeManifest.FromCurrentSources(
                esmPath,
                bsaPath,
                InstalledContentSources.ResolveGameplayRecordSources(config.InstallPath));
            manifest.MeshCount = bakeryMeshes.Count;
            manifest.MaterialCount = bakeryMaterials.Count;
            manifest.TextureCount = bakeryTextures.Count;
            manifest.CollisionCount = bakeryCollisions.Count;
            manifest.CellCount = exteriorCells.Count;
            manifest.CellGrid = new (int, int)[exteriorCells.Count];
            for (int i = 0; i < exteriorCells.Count; i++)
                manifest.CellGrid[i] = (exteriorCells[i].GridX, exteriorCells[i].GridY);
            manifest.InteriorCellCount = interiorCells.Count;
            manifest.InteriorCellIds = new string[interiorCells.Count];
            for (int i = 0; i < interiorCells.Count; i++)
                manifest.InteriorCellIds[i] = interiorCells[i].Name ?? string.Empty;
            manifest.CellStates = cellStates;
            manifest.Write(CachePaths.Manifest);

            progress.Stage = "Done";
            int dirtyCount = 0;
            for (int i = 0; i < stagedCells.Length; i++)
            {
                if (stagedCells[i].NeedsWrite)
                    dirtyCount++;
            }
            progress.Label = $"{dirtyCount}/{stagedCells.Length} cells rebuilt, {bakeryMeshes.Count} meshes, {bakeryMaterials.Count} mats, {bakeryTextures.Count} textures, {bakeryLayers.Count} terrain layers, {bakeryCollisions.Count} collisions";
            progress.Done = true;
        }

        private static StagedCellData StageCell(
            WorkerContext worker,
            CellBakeWorkItem workItem,
            RecordIndex recordIndex,
            Dictionary<string, ContentReference> gameplayContentLookup,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            Dictionary<int, string> ltexMap,
            Dictionary<string, BakeManifest.BakedCellState> previousStateByKey,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            List<CellReference> refs;
            try
            {
                refs = CellReader.ReadReferences(worker.RefsReader, workItem.Cell);
            }
            catch
            {
                refs = new List<CellReference>();
            }

            LandRecord land = null;
            if (!workItem.IsInterior && workItem.LandOffset != 0)
            {
                try
                {
                    land = LandIndex.ReadAt(worker.LandReader, workItem.LandOffset);
                }
                catch
                {
                    land = null;
                }
            }

            string fingerprint = ComputeFingerprint(workItem, refs, land, recordIndex, ltexMap);
            bool hasPrevious = previousStateByKey.TryGetValue(workItem.Key, out var previousState);
            bool canReuse = hasPrevious
                && previousState.PipelineVersion == CacheFormat.WorldBakePipelineVersion
                && string.Equals(previousState.Fingerprint, fingerprint, StringComparison.Ordinal)
                && File.Exists(workItem.OutputPath)
                && File.Exists(workItem.AuditOutputPath)
                && TryValidateCellFile(workItem.OutputPath, workItem.IsInterior, workItem.Cell.Name, out _);

            var staged = new StagedCellData
            {
                WorkItem = workItem,
                PreviousState = hasPrevious ? previousState : null,
                Fingerprint = fingerprint,
                Environment = workItem.Cell.Environment,
                Land = land,
                NeedsWrite = !canReuse,
                PlacedRefs = canReuse ? null : new List<StagedPlacedRefData>(refs.Count),
                PendingRefs = canReuse ? null : new List<StagedRefData>(refs.Count),
                DoorEntries = canReuse ? null : new List<DoorRefEntry>(),
                PlacementAuditEntries = canReuse ? null : new List<RefPlacementAuditEntry>(refs.Count),
                CollisionMissingPayloadSamples = canReuse ? null : new List<string>(4),
            };

            if (canReuse)
                return staged;

            var duplicatePlacedRefCounts = new Dictionary<uint, int>();
            for (int i = 0; i < refs.Count; i++)
            {
                var reference = refs[i];
                if (reference.Deleted || reference.FormId == 0u)
                    continue;

                duplicatePlacedRefCounts.TryGetValue(reference.FormId, out int duplicateCount);
                duplicatePlacedRefCounts[reference.FormId] = duplicateCount + 1;
            }

            var staticVerts = new List<Vector3>();
            var staticIndices = new List<int>();
            for (int i = 0; i < refs.Count; i++)
            {
                var reference = refs[i];
                if (reference.Deleted)
                    continue;

                CellBakery.ToUnityTransform(reference, out var pos, out var rot);
                bool hasBaseRecord = recordIndex.TryGet(reference.BaseId, out var rec);
                bool isDoorRecord = hasBaseRecord && rec.Tag == DoorTag;
                bool isStat = hasBaseRecord && rec.Tag == StatTag;
                bool isInteractable = !isStat;
                var contentReference = ResolveGameplayContentReference(gameplayContentLookup, reference.BaseId);
                var model = hasBaseRecord ? EnsureModelSource(rec, sharedBsa, bsaByName, modelCache) : null;
                int duplicateCount = 1;
                if (reference.FormId != 0u && duplicatePlacedRefCounts.TryGetValue(reference.FormId, out int countedDuplicates))
                    duplicateCount = countedDuplicates;

                var auditFlags = RefPlacementAuditFlags.None;
                if (isDoorRecord)
                    auditFlags |= RefPlacementAuditFlags.IsDoor;
                if (reference.IsDoor)
                    auditFlags |= RefPlacementAuditFlags.IsTeleportDoor;
                if (duplicateCount > 1)
                    auditFlags |= RefPlacementAuditFlags.HasDuplicatePlacedRefId;
                if (!hasBaseRecord)
                    auditFlags |= RefPlacementAuditFlags.MissingBaseRecord;
                if (hasBaseRecord && !contentReference.IsValid)
                    auditFlags |= RefPlacementAuditFlags.MissingGameplayContentReference;
                if (hasBaseRecord && (string.IsNullOrEmpty(rec.Model) || model == null || model.Meshes.Length == 0))
                    auditFlags |= RefPlacementAuditFlags.MissingModel;
                if (model != null && model.Meshes.Length > 0)
                    auditFlags |= RefPlacementAuditFlags.WasBaked;

                Bounds aggregateWorldBounds = default;
                if (TryComputeAggregateWorldBounds(model, pos, rot, reference.Scale, out aggregateWorldBounds))
                    auditFlags |= RefPlacementAuditFlags.HasWorldBounds;

                staged.PlacementAuditEntries.Add(new RefPlacementAuditEntry
                {
                    PlacedRefId = reference.FormId,
                    BaseId = reference.BaseId ?? string.Empty,
                    SourcePosX = reference.PosX,
                    SourcePosY = reference.PosY,
                    SourcePosZ = reference.PosZ,
                    SourceRotX = reference.RotX,
                    SourceRotY = reference.RotY,
                    SourceRotZ = reference.RotZ,
                    SourceScale = reference.Scale,
                    UnityPosX = pos.x,
                    UnityPosY = pos.y,
                    UnityPosZ = pos.z,
                    UnityRotX = rot.x,
                    UnityRotY = rot.y,
                    UnityRotZ = rot.z,
                    UnityRotW = rot.w,
                    UnityScale = reference.Scale,
                    BoundsCenterX = aggregateWorldBounds.center.x,
                    BoundsCenterY = aggregateWorldBounds.center.y,
                    BoundsCenterZ = aggregateWorldBounds.center.z,
                    BoundsExtentsX = aggregateWorldBounds.extents.x,
                    BoundsExtentsY = aggregateWorldBounds.extents.y,
                    BoundsExtentsZ = aggregateWorldBounds.extents.z,
                    SpawnedSubmeshCount = model?.Meshes?.Length ?? 0,
                    DuplicatePlacedRefCount = duplicateCount,
                    Flags = auditFlags,
                });

                if (model == null || model.Meshes.Length == 0)
                {
                    staged.CollisionNoColliderCount++;
                    continue;
                }

                int doorMetaIndex = -1;
                if (isDoorRecord)
                {
                    doorMetaIndex = staged.DoorEntries.Count;
                    BuildDoorEntry(reference, out var doorEntry);
                    staged.DoorEntries.Add(doorEntry);
                }

                CollisionPayload collisionPayload = model.Collision;
                bool usedAutoVisualStaticCollision = false;
                if (isStat && collisionPayload.IsEmpty && !model.AutoVisualStaticCollision.IsEmpty)
                {
                    collisionPayload = model.AutoVisualStaticCollision;
                    usedAutoVisualStaticCollision = true;
                }

                PlacedRefCollisionAssignment collisionAssignment = ClassifyPlacedRefCollision(isStat, collisionPayload);
                if (isStat)
                    staged.CollisionStaticCandidateCount++;

                switch (collisionAssignment)
                {
                    case PlacedRefCollisionAssignment.CellStaticAggregate:
                        AppendTransformed(collisionPayload, pos, rot, reference.Scale, workItem.CellOrigin, staticVerts, staticIndices);
                        staged.CollisionStaticAggregateCount++;
                        staged.CollisionStaticAggregateTriangleCount += collisionPayload.TriangleCount;
                        if (usedAutoVisualStaticCollision)
                            staged.CollisionAutoVisualStaticCount++;
                        else
                            staged.CollisionAuthoredRootCount++;
                        break;
                    case PlacedRefCollisionAssignment.PerPlacedRef:
                        staged.CollisionAuthoredRootCount++;
                        staged.CollisionPerPlacedRefCount++;
                        break;
                    default:
                        if (model.CollisionSource == CollisionExtractionSource.ExplicitNoCollision)
                            staged.CollisionExplicitNoCollisionCount++;
                        else if (isStat && collisionPayload.IsEmpty)
                        {
                            staged.CollisionMissingPayloadCount++;
                            AddMissingCollisionSample(staged, model.ModelPath);
                        }

                        staged.CollisionNoColliderCount++;
                        break;
                }

                bool useModelPrefab = EnableModelPrefabWorldRefs && contentReference.Kind == ContentReferenceKind.Actor;
                if (useModelPrefab)
                {
                    staged.PlacedRefs.Add(new StagedPlacedRefData(
                        model.ModelPath,
                        reference.FormId,
                        doorMetaIndex,
                        contentReference,
                        pos,
                        rot,
                        reference.Scale));
                    continue;
                }

                for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
                {
                    var built = model.Meshes[meshIndex];
                    ushort apFlags = built.AlphaFlags;
                    uint matFlags = 0;
                    if ((apFlags & 0x0001) != 0)
                        matFlags |= CacheFormat.MatFlagAlphaBlend;
                    if ((apFlags & 0x0200) != 0)
                        matFlags |= CacheFormat.MatFlagAlphaClip;
                    matFlags = CacheFormat.PackAlphaThreshold(matFlags, built.AlphaThreshold);

                    staged.PendingRefs.Add(new StagedRefData(
                        built,
                        meshIndex == 0 && collisionAssignment == PlacedRefCollisionAssignment.PerPlacedRef ? model.Collision : default,
                        $"{rec.Model}#{meshIndex}",
                        matFlags,
                        built.TexturePath,
                        isInteractable,
                        reference.FormId,
                        meshIndex == 0 ? doorMetaIndex : -1,
                        contentReference,
                        pos,
                        rot,
                        reference.Scale));
                }
            }

            staged.StaticCollision = staticVerts.Count > 0
                ? new CellBakery.StaticCollision(staticVerts.ToArray(), staticIndices.ToArray())
                : default;

            return staged;
        }

        private static PlacedRefCollisionAssignment ClassifyPlacedRefCollision(bool isStat, in CollisionPayload collision)
        {
            if (collision.IsEmpty)
                return PlacedRefCollisionAssignment.NoCollider;

            return isStat
                ? PlacedRefCollisionAssignment.CellStaticAggregate
                : PlacedRefCollisionAssignment.PerPlacedRef;
        }

        private static void AddMissingCollisionSample(StagedCellData staged, string modelPath)
        {
            if (staged.CollisionMissingPayloadSamples == null || staged.CollisionMissingPayloadSamples.Count >= 5)
                return;

            if (!string.IsNullOrWhiteSpace(modelPath) && !staged.CollisionMissingPayloadSamples.Contains(modelPath))
                staged.CollisionMissingPayloadSamples.Add(modelPath);
        }

        private static bool TryComputeAggregateWorldBounds(
            ModelSource model,
            Vector3 position,
            Quaternion rotation,
            float scale,
            out Bounds aggregateBounds)
        {
            aggregateBounds = default;
            if (model == null || model.Meshes == null || model.Meshes.Length == 0)
                return false;

            bool hasBounds = false;
            for (int i = 0; i < model.Meshes.Length; i++)
            {
                Bounds worldBounds = TransformBounds(model.Meshes[i].LocalBounds, position, rotation, scale);
                if (!hasBounds)
                {
                    aggregateBounds = worldBounds;
                    hasBounds = true;
                }
                else
                {
                    aggregateBounds.Encapsulate(worldBounds.min);
                    aggregateBounds.Encapsulate(worldBounds.max);
                }
            }

            return hasBounds;
        }

        private static Bounds TransformBounds(Bounds localBounds, Vector3 position, Quaternion rotation, float scale)
        {
            float absoluteScale = Mathf.Abs(scale);
            Vector3 scaledCenter = localBounds.center * scale;
            Vector3 scaledExtents = localBounds.extents * absoluteScale;
            Vector3 worldCenter = position + rotation * scaledCenter;

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 worldExtents = new(
                Mathf.Abs(right.x) * scaledExtents.x + Mathf.Abs(up.x) * scaledExtents.y + Mathf.Abs(forward.x) * scaledExtents.z,
                Mathf.Abs(right.y) * scaledExtents.x + Mathf.Abs(up.y) * scaledExtents.y + Mathf.Abs(forward.y) * scaledExtents.z,
                Mathf.Abs(right.z) * scaledExtents.x + Mathf.Abs(up.z) * scaledExtents.y + Mathf.Abs(forward.z) * scaledExtents.z);

            return new Bounds(worldCenter, worldExtents * 2f);
        }

        private static ModelSource EnsureModelSource(
            BaseRecord rec,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            return EnsureModelSource(rec.Model, sharedBsa, bsaByName, modelCache);
        }

        private static ModelSource EnsureModelSource(
            string modelPath,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            if (string.IsNullOrEmpty(modelPath))
                return null;

            string nifPath = "meshes\\" + modelPath;
            var lazy = modelCache.GetOrAdd(
                nifPath,
                path => new Lazy<ModelSource>(() =>
                {
                    if (!bsaByName.TryGetValue(path, out var entry))
                        return null;

                    NifFile nif;
                    try
                    {
                        nif = NifFile.Parse(path, sharedBsa.Read(entry));
                    }
                    catch
                    {
                        return null;
                    }

                    var built = NifMeshBuilder.BuildRaw(nif);
                    if (built.Count == 0)
                        return null;

                    var prefab = NifModelPrefabBuilder.Build(nif);
                    var collisionResult = NifCollisionExtractor.Extract(nif);
                    CollisionPayload authoredCollision = collisionResult.Source == CollisionExtractionSource.AuthoredRootCollision
                        ? collisionResult.Payload
                        : default;
                    CollisionPayload autoVisualStaticCollision = collisionResult.Source == CollisionExtractionSource.AutoVisualStatic
                        ? collisionResult.Payload
                        : default;
                    return new ModelSource(path, built.ToArray(), authoredCollision, autoVisualStaticCollision, collisionResult.Source, prefab);
                }, LazyThreadSafetyMode.ExecutionAndPublication));

            return lazy.Value;
        }

        private static void SeedRuntimeSpawnableModels(
            GameplayContentData gameplayContent,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            if (gameplayContent == null)
                return;

            var actors = gameplayContent.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Kind != ActorDefKind.Creature)
                    continue;

                EnsureModelSource(actors[i].Model, sharedBsa, bsaByName, modelCache);
            }

            var items = gameplayContent.Items ?? Array.Empty<BaseDef>();
            for (int i = 0; i < items.Length; i++)
                EnsureModelSource(items[i].Model, sharedBsa, bsaByName, modelCache);

            var lights = gameplayContent.Lights ?? Array.Empty<LightDef>();
            for (int i = 0; i < lights.Length; i++)
                EnsureModelSource(lights[i].Model, sharedBsa, bsaByName, modelCache);
        }

        private static IEnumerator PrepareDirtyCellsIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            Dictionary<int, string> ltexMap)
        {
            k_PrepareDirtyCells.Begin();
            progress.Stage = "Preparing dirty cells";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to prepare" : $"Preparing dirty cells 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
            {
                k_PrepareDirtyCells.End();
                yield break;
            }

            int completed = 0;
            Exception prepareFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var task = Task.Run(() =>
            {
                k_PrepareDirtyCellsWorkerLoop.Begin();
                try
                {
                    int nextIndex = 0;
                    int failureSignaled = 0;
                    var workers = new Task[maxWorkers];
                    for (int worker = 0; worker < maxWorkers; worker++)
                    {
                        workers[worker] = Task.Factory.StartNew(() =>
                        {
                            while (Volatile.Read(ref failureSignaled) == 0)
                            {
                                int index = Interlocked.Increment(ref nextIndex) - 1;
                                if (index >= dirtyCells.Count)
                                    break;

                                try
                                {
                                    PrepareDirtyCell(dirtyCells[index], ltexMap);
                                    Interlocked.Increment(ref completed);
                                }
                                catch (Exception ex)
                                {
                                    if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                        prepareFailure = ex;
                                    break;
                                }
                            }
                        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }

                    Task.WaitAll(workers);
                }
                catch (Exception ex)
                {
                    prepareFailure ??= ex;
                }
                finally
                {
                    k_PrepareDirtyCellsWorkerLoop.End();
                }
            });

            while (!task.IsCompleted)
            {
                int current = completed;
                progress.Current = current;
                progress.Label = $"Preparing dirty cells {current}/{dirtyCells.Count}";
                yield return null;
            }

            if (prepareFailure != null)
            {
                k_PrepareDirtyCells.End();
                throw prepareFailure;
            }

            progress.Current = dirtyCells.Count;
            progress.Label = $"Preparing dirty cells {dirtyCells.Count}/{dirtyCells.Count}";
            LogSlowPreparedCellRefs(dirtyCells);
            k_PrepareDirtyCells.End();
            yield return null;
        }

        private static IEnumerator BuildModelPrefabsIncremental(
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache,
            BakeProgress progress,
            ModelPrefabBakery modelPrefabs,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            CollisionBakery collisions)
        {
            var keys = new List<string>(modelCache.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            progress.Stage = "Model Prefabs";
            progress.Total = Math.Max(1, keys.Count);
            progress.Current = 0;
            progress.Label = keys.Count == 0 ? "No model prefabs to bake" : $"Building model prefabs 0/{keys.Count}";
            yield return null;

            if (keys.Count == 0)
                yield break;

            for (int i = 0; i < keys.Count; i++)
            {
                if (modelCache.TryGetValue(keys[i], out var lazy))
                {
                    var source = lazy.Value;
                    if (source?.Prefab != null)
                    {
                        modelPrefabs.GetOrAdd(
                            source.ModelPath,
                            source.Prefab,
                            meshes,
                            materials,
                            textures,
                            collisions);
                    }
                }

                progress.Current = i + 1;
                progress.Label = $"Building model prefabs {i + 1}/{keys.Count}";
                if (((i + 1) & 31) == 0 || i + 1 == keys.Count)
                    yield return null;
            }
        }

        private static void PrepareDirtyCell(StagedCellData staged, Dictionary<int, string> ltexMap)
        {
            k_PrepareDirtyCell.Begin();
            try
            {
            if (staged.PendingRefs == null)
                return;

            if (staged.Land != null && staged.Land.VtexIndices != null)
            {
                k_PrepareTerrainTexturePaths.Begin();
                try
                {
                    var texturePaths = new string[LandRecord.NumTextures];
                    for (int i = 0; i < LandRecord.NumTextures; i++)
                        texturePaths[i] = LtexIndex.ResolveVtex(staged.Land.VtexIndices[i], ltexMap);
                    staged.TerrainTexturePaths = texturePaths;
                }
                finally
                {
                    k_PrepareTerrainTexturePaths.End();
                }
            }

            staged.PreparedRefs = BuildPreparedCellRefs(staged);
            staged.DoorEntries ??= new List<DoorRefEntry>();
            }
            finally
            {
                k_PrepareDirtyCell.End();
            }
        }

        private static IEnumerator ResolveDirtyCellIndicesIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            CollisionBakery collisions,
            ModelPrefabBakery modelPrefabs)
        {
            progress.Stage = "Assigning global indices";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to resolve" : $"Assigning global indices 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            int completed = 0;
            Exception resolveFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var resolveTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWorkers];
                for (int worker = 0; worker < maxWorkers; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        var materialIndexCache = new Dictionary<uint, int>();
                        var textureIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var terrainLayerCache = new Dictionary<int, ushort>();

                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= dirtyCells.Count)
                                break;

                            try
                            {
                                ResolveDirtyCellIndices(
                                    dirtyCells[index],
                                    meshes,
                                    materials,
                                    textures,
                                    terrainLayers,
                                    collisions,
                                    modelPrefabs,
                                    materialIndexCache,
                                    textureIndexCache,
                                    terrainLayerCache);
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    resolveFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!resolveTask.IsCompleted)
            {
                int current = completed;
                progress.Current = current;
                progress.Label = $"Assigning global indices {current}/{dirtyCells.Count}";
                yield return null;
            }

            if (resolveFailure != null)
                throw resolveFailure;

            progress.Current = dirtyCells.Count;
            progress.Label = $"Assigning global indices {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;
        }

        private static IEnumerator AssignRenderShardIndicesIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            MeshBakery meshes,
            TextureBakery textures,
            RenderShardBakery renderShards)
        {
            using var _ = k_AssignRenderShards.Auto();

            progress.Stage = "Assigning render shards";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to shard" : $"Assigning render shards 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            int maxWorkers = Math.Max(1, Environment.ProcessorCount);
            int fallbackBucketKey = RenderShardBakery.PackBucketKey(1, 1);

            string[] familyKeysByMeshIndex = Array.Empty<string>();
            if (meshes.Count > 0)
            {
                familyKeysByMeshIndex = new string[meshes.Count];
                int familyCompleted = 0;
                var familyTask = Task.Run(() =>
                {
                    k_AssignRenderShardsFamilyPrecompute.Begin();
                    try
                    {
                        Parallel.For(
                            0,
                            familyKeysByMeshIndex.Length,
                            new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                            meshIndex =>
                            {
                                string sourceLabel = meshes.GetSourceLabel(meshIndex);
                                familyKeysByMeshIndex[meshIndex] = RenderShardBakery.NormalizeFamilyKey(sourceLabel);
                                Interlocked.Increment(ref familyCompleted);
                            });
                    }
                    finally
                    {
                        k_AssignRenderShardsFamilyPrecompute.End();
                    }
                });

                while (!familyTask.IsCompleted)
                {
                    progress.Total = familyKeysByMeshIndex.Length;
                    progress.Current = familyCompleted;
                    progress.Label = $"Precomputing shard families {familyCompleted}/{familyKeysByMeshIndex.Length}";
                    yield return null;
                }

                familyTask.GetAwaiter().GetResult();

                progress.Total = familyKeysByMeshIndex.Length;
                progress.Current = familyKeysByMeshIndex.Length;
                progress.Label = $"Precomputing shard families {familyKeysByMeshIndex.Length}/{familyKeysByMeshIndex.Length}";
                yield return null;
            }

            int[] bucketKeysByTextureIndex = Array.Empty<int>();
            if (textures.Count > 0)
            {
                bucketKeysByTextureIndex = new int[textures.Count];
                for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
                    bucketKeysByTextureIndex[textureIndex] = GetTextureBucketKey(textures, textureIndex);
            }

            var localRequestSets = new ConcurrentBag<HashSet<RenderShardRequestKey>>();
            int requestBuildCompleted = 0;
            var requestBuildTask = Task.Run(() =>
            {
                k_AssignRenderShardsBuildRequests.Begin();
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, dirtyCells.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                        () => new HashSet<RenderShardRequestKey>(),
                        (range, _, localSet) =>
                        {
                            for (int cellIndex = range.Item1; cellIndex < range.Item2; cellIndex++)
                            {
                                var staged = dirtyCells[cellIndex];
                                var bakedRefs = staged.BakedRefs;
                                if (bakedRefs != null)
                                {
                                    for (int r = 0; r < bakedRefs.Count; r++)
                                    {
                                        var baked = bakedRefs[r];
                                        if ((RefSpawnMode)baked.SpawnModeRaw != RefSpawnMode.RenderShard)
                                            continue;
                                        int globalMeshIndex = baked.LocalMeshIndex;
                                        int textureIndex = baked.SliceIndex;
                                        int bucketKey = (uint)textureIndex < (uint)bucketKeysByTextureIndex.Length
                                            ? bucketKeysByTextureIndex[textureIndex]
                                            : fallbackBucketKey;
                                        string familyKey = (uint)globalMeshIndex < (uint)familyKeysByMeshIndex.Length
                                            ? familyKeysByMeshIndex[globalMeshIndex]
                                            : "__root__";
                                        localSet.Add(new RenderShardRequestKey(bucketKey, familyKey, globalMeshIndex));
                                    }
                                }

                                Interlocked.Increment(ref requestBuildCompleted);
                            }

                            return localSet;
                        },
                        localSet => localRequestSets.Add(localSet));
                }
                finally
                {
                    k_AssignRenderShardsBuildRequests.End();
                }
            });

            while (!requestBuildTask.IsCompleted)
            {
                progress.Total = dirtyCells.Count;
                progress.Current = requestBuildCompleted;
                progress.Label = $"Building shard requests {requestBuildCompleted}/{dirtyCells.Count}";
                yield return null;
            }

            requestBuildTask.GetAwaiter().GetResult();

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Building shard requests {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            List<RenderShardRequestKey> sortedRequests;
            using (k_AssignRenderShardsReduce.Auto())
            {
                var uniqueRequests = new HashSet<RenderShardRequestKey>();
                foreach (var localSet in localRequestSets)
                {
                    if (localSet == null)
                        continue;

                    foreach (var request in localSet)
                        uniqueRequests.Add(request);
                }

                sortedRequests = new List<RenderShardRequestKey>(uniqueRequests);
                sortedRequests.Sort(RenderShardRequestKeyComparer.Instance);
            }

            progress.Total = Math.Max(1, sortedRequests.Count);
            progress.Current = 0;
            progress.Label = sortedRequests.Count == 0
                ? "Resolving shard assignments 0/0"
                : $"Resolving shard assignments 0/{sortedRequests.Count}";
            yield return null;

            int suspiciousShardCount = 0;
            var resolvedAssignments = new Dictionary<RenderShardRequestKey, RenderShardResolvedAssignment>(sortedRequests.Count);
            using (k_AssignRenderShardsResolve.Auto())
            {
                for (int i = 0; i < sortedRequests.Count; i++)
                {
                    var request = sortedRequests[i];
                    var assignment = renderShards.GetOrAddAssignment(request.BucketKey, request.FamilyKey, request.GlobalMeshIndex);
                    if (assignment.LocalMeshIndex == 0 && assignment.RenderShardIndex >= renderShards.Count - 1)
                        suspiciousShardCount++;

                    resolvedAssignments[request] = new RenderShardResolvedAssignment(
                        assignment.RenderShardIndex,
                        assignment.LocalMeshIndex);

                    if (((i + 1) & 255) == 0 || i + 1 == sortedRequests.Count)
                    {
                        progress.Total = Math.Max(1, sortedRequests.Count);
                        progress.Current = i + 1;
                        progress.Label = $"Resolving shard assignments {i + 1}/{sortedRequests.Count}";
                        yield return null;
                    }
                }
            }

            int applyCompleted = 0;
            var applyTask = Task.Run(() =>
            {
                k_AssignRenderShardsApply.Begin();
                try
                {
                    Parallel.ForEach(
                        Partitioner.Create(0, dirtyCells.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = maxWorkers },
                        range =>
                        {
                            for (int cellIndex = range.Item1; cellIndex < range.Item2; cellIndex++)
                            {
                                var staged = dirtyCells[cellIndex];
                                var bakedRefs = staged.BakedRefs;
                                if (bakedRefs != null)
                                {
                                    for (int r = 0; r < bakedRefs.Count; r++)
                                    {
                                        var baked = bakedRefs[r];
                                        if ((RefSpawnMode)baked.SpawnModeRaw != RefSpawnMode.RenderShard)
                                            continue;
                                        int globalMeshIndex = baked.LocalMeshIndex;
                                        int textureIndex = baked.SliceIndex;
                                        int bucketKey = (uint)textureIndex < (uint)bucketKeysByTextureIndex.Length
                                            ? bucketKeysByTextureIndex[textureIndex]
                                            : fallbackBucketKey;
                                        string familyKey = (uint)globalMeshIndex < (uint)familyKeysByMeshIndex.Length
                                            ? familyKeysByMeshIndex[globalMeshIndex]
                                            : "__root__";
                                        var request = new RenderShardRequestKey(bucketKey, familyKey, globalMeshIndex);
                                        var resolved = resolvedAssignments[request];

                                        bakedRefs[r] = new CellBakery.BakedRef(
                                            RefSpawnMode.RenderShard,
                                            resolved.RenderShardIndex,
                                            resolved.LocalMeshIndex,
                                            baked.LocalMaterialIndex,
                                            baked.SliceIndex,
                                            baked.CollisionIndex,
                                            baked.PlacedRefId,
                                            baked.DoorMetaIndex,
                                            baked.ContentHandleValue,
                                            baked.ContentKind,
                                            baked.PositionUnity,
                                            baked.RotationUnity,
                                            baked.Scale);
                                    }
                                }

                                Interlocked.Increment(ref applyCompleted);
                            }
                        });
                }
                finally
                {
                    k_AssignRenderShardsApply.End();
                }
            });

            while (!applyTask.IsCompleted)
            {
                progress.Total = dirtyCells.Count;
                progress.Current = applyCompleted;
                progress.Label = $"Applying shard assignments {applyCompleted}/{dirtyCells.Count}";
                yield return null;
            }

            applyTask.GetAwaiter().GetResult();

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Applying shard assignments {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            progress.Total = dirtyCells.Count;
            progress.Current = dirtyCells.Count;
            progress.Label = $"Assigning render shards {dirtyCells.Count}/{dirtyCells.Count}";
            yield return null;

            var catalog = renderShards.BuildCatalog();
            RenderShardFile.LogShardStats(catalog, "bake");
            if (suspiciousShardCount > 0)
            {
                UnityEngine.Debug.Log($"[VVardenfell] render shard assignment created or extended {suspiciousShardCount} shard-local mesh routes during this bake pass.");
            }
        }

        private static int GetTextureBucketKey(TextureBakery textures, int textureIndex)
        {
            if (textureIndex < 0)
                return RenderShardBakery.PackBucketKey(1, 1);

            int2 dims = textures.GetBucketDimensions(textureIndex);
            return RenderShardBakery.PackBucketKey(dims.x, dims.y);
        }

        private static void ResolveDirtyCellIndices(
            StagedCellData staged,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            CollisionBakery collisions,
            ModelPrefabBakery modelPrefabs,
            Dictionary<uint, int> materialIndexCache,
            Dictionary<string, int> textureIndexCache,
            Dictionary<int, ushort> terrainLayerCache)
        {
            k_ResolveDirtyCellIndices.Begin();
            try
            {
            if (staged.PreparedRefs == null && staged.PlacedRefs == null)
                return;

            if (staged.TerrainTexturePaths != null)
            {
                var layerGrid = new ushort[staged.TerrainTexturePaths.Length];
                var layerByPath = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < staged.TerrainTexturePaths.Length; i++)
                {
                    string path = staged.TerrainTexturePaths[i];
                    if (!layerByPath.TryGetValue(path, out ushort layer))
                    {
                        int texIdx = GetTextureIndex(path, textures, textureIndexCache);
                        layer = GetTerrainLayerIndex(texIdx, terrainLayers, terrainLayerCache);
                        layerByPath[path] = layer;
                    }
                    layerGrid[i] = layer;
                }
                staged.LayerGrid = layerGrid;
            }

            int preparedCount = staged.PreparedRefs?.Count ?? 0;
            int placedCount = staged.PlacedRefs?.Count ?? 0;
            var result = new List<CellBakery.BakedRef>(preparedCount + placedCount);
            var meshIndices = new HashSet<int>();
            var materialIndices = new HashSet<int>();
            var textureIndices = new HashSet<int>();
            var collisionIndices = new HashSet<int>();
            for (int i = 0; i < preparedCount; i++)
            {
                var prepared = staged.PreparedRefs[i];
                int meshIndex = meshes.AddOrGet(prepared.MeshSourceLabel, prepared.Built);
                int materialIndex = GetMaterialIndex(prepared.MaterialFlags, materials, materialIndexCache);
                int sliceIndex = GetTextureIndex(prepared.TexturePath, textures, textureIndexCache);
                int collisionIndex = prepared.Collision.IsEmpty
                    ? -1
                    : collisions.AddOrGet(prepared.Collision);
                meshIndices.Add(meshIndex);
                materialIndices.Add(materialIndex);
                if (sliceIndex >= 0)
                    textureIndices.Add(sliceIndex);
                if (collisionIndex >= 0)
                    collisionIndices.Add(collisionIndex);
                result.Add(new CellBakery.BakedRef(
                    RefSpawnMode.RenderShard,
                    -1,
                    meshIndex,
                    materialIndex,
                    sliceIndex,
                    collisionIndex,
                    prepared.PlacedRefId,
                    prepared.DoorMetaIndex,
                    prepared.ContentReference.HandleValue,
                    (int)prepared.ContentReference.Kind,
                    prepared.Position,
                    prepared.Rotation,
                    prepared.Scale));
            }

            for (int i = 0; i < placedCount; i++)
            {
                var placed = staged.PlacedRefs[i];
                if (!modelPrefabs.TryGetAssignment(placed.ModelPath, out var assignment))
                    continue;

                if (assignment.CollisionIndex >= 0)
                    collisionIndices.Add(assignment.CollisionIndex);

                result.Add(new CellBakery.BakedRef(
                    RefSpawnMode.ModelPrefab,
                    assignment.ModelPrefabIndex,
                    -1,
                    -1,
                    -1,
                    assignment.CollisionIndex,
                    placed.PlacedRefId,
                    placed.DoorMetaIndex,
                    placed.ContentReference.HandleValue,
                    (int)placed.ContentReference.Kind,
                    placed.Position,
                    placed.Rotation,
                    placed.Scale));
            }

            staged.BakedRefs = result;
            staged.GlobalMeshIndices = ToSortedArray(meshIndices);
            staged.GlobalMaterialIndices = ToSortedArray(materialIndices);
            staged.GlobalTextureIndices = ToSortedArray(textureIndices);
            staged.GlobalCollisionIndices = ToSortedArray(collisionIndices);
            if (staged.LayerGrid != null)
            {
                var terrainLayerIndices = new HashSet<int>();
                for (int i = 0; i < staged.LayerGrid.Length; i++)
                    terrainLayerIndices.Add(staged.LayerGrid[i]);
                staged.GlobalTerrainLayerIndices = ToSortedArray(terrainLayerIndices);
            }
            else
            {
                staged.GlobalTerrainLayerIndices = Array.Empty<int>();
            }
            }
            finally
            {
                k_ResolveDirtyCellIndices.End();
            }
        }

        private static int GetMaterialIndex(uint flags, MaterialBakery materials, Dictionary<uint, int> materialIndexCache)
        {
            if (!materialIndexCache.TryGetValue(flags, out int materialIndex))
            {
                materialIndex = materials.AddOrGet(flags);
                materialIndexCache[flags] = materialIndex;
            }
            return materialIndex;
        }

        private static int GetTextureIndex(string texturePath, TextureBakery textures, Dictionary<string, int> textureIndexCache)
        {
            texturePath ??= string.Empty;
            if (!textureIndexCache.TryGetValue(texturePath, out int textureIndex))
            {
                textureIndex = textures.AddOrGet(texturePath);
                textureIndexCache[texturePath] = textureIndex;
            }
            return textureIndex;
        }

        private static ushort GetTerrainLayerIndex(int textureIndex, TerrainLayerBakery terrainLayers, Dictionary<int, ushort> terrainLayerCache)
        {
            if (!terrainLayerCache.TryGetValue(textureIndex, out ushort layerIndex))
            {
                layerIndex = terrainLayers.AddOrGet(textureIndex);
                terrainLayerCache[textureIndex] = layerIndex;
            }
            return layerIndex;
        }

        private static List<PreparedRefData> BuildPreparedCellRefs(StagedCellData staged)
        {
            k_BuildPreparedCellRefs.Begin();
            long startTicks = Stopwatch.GetTimestamp();
            try
            {
            var result = new List<PreparedRefData>(staged.PendingRefs.Count);
            staged.PendingRefCount = staged.PendingRefs.Count;
            if (staged.PendingRefs.Count == 0)
            {
                staged.BuildPreparedCellRefsMs = 0d;
                staged.PreparedWorkItemCount = 0;
                staged.SourcePreparedMeshCount = 0;
                staged.StaticGroupCount = 0;
                staged.CombinedGroupCount = 0;
                staged.InteractableWorkItemCount = 0;
                staged.CombinedPreparedPayloadBytes = 0L;
                return result;
            }

            var workItems = new List<PreparedWorkItem>(staged.PendingRefs.Count);
            int interactableWorkItems = 0;
            int sourcePreparedMeshCount = 0;
            k_BuildPreparedCellRefsGroup.Begin();
            try
            {
                for (int i = 0; i < staged.PendingRefs.Count; i++)
                {
                    var pending = staged.PendingRefs[i];
                    if (pending.IsInteractable)
                    {
                        workItems.Add(new PreparedWorkItem(
                            true,
                            pending.Built,
                            pending.Collision,
                            pending.MaterialFlags,
                            pending.TexturePath,
                            pending.PlacedRefId,
                            pending.DoorMetaIndex,
                            pending.ContentReference,
                            pending.Position,
                            pending.Rotation,
                            pending.Scale,
                            pending.MeshSourceLabel));
                        interactableWorkItems++;
                        continue;
                    }

                    workItems.Add(new PreparedWorkItem(
                        false,
                        pending.Built,
                        default,
                        pending.MaterialFlags,
                        pending.TexturePath,
                        0u,
                        -1,
                        default,
                        pending.Position,
                        pending.Rotation,
                        pending.Scale,
                        pending.MeshSourceLabel));
                    sourcePreparedMeshCount++;
                }
            }
            finally
            {
                k_BuildPreparedCellRefsGroup.End();
            }

            int combinedGroupCount = 0;
            k_BuildPreparedCellRefsCombine.Begin();
            try
            {
                // Storage-first pass: keep non-interactable refs as reusable source meshes
                // instead of baking cell-specific combined meshes.
            }
            finally
            {
                k_BuildPreparedCellRefsCombine.End();
            }

            if (workItems.Count == 0)
            {
                staged.BuildPreparedCellRefsMs = 0d;
                staged.PreparedWorkItemCount = 0;
                staged.SourcePreparedMeshCount = 0;
                staged.StaticGroupCount = 0;
                staged.CombinedGroupCount = combinedGroupCount;
                staged.InteractableWorkItemCount = interactableWorkItems;
                staged.CombinedPreparedPayloadBytes = 0L;
                return result;
            }

            var prepared = new PreparedRefData[workItems.Count];
            k_BuildPreparedCellRefsEncode.Begin();
            try
            {
                for (int i = 0; i < workItems.Count; i++)
                    prepared[i] = BuildPreparedRefData(workItems[i]);
            }
            finally
            {
                k_BuildPreparedCellRefsEncode.End();
            }

            result.AddRange(prepared);
            staged.BuildPreparedCellRefsMs = ElapsedMilliseconds(startTicks);
            staged.PreparedWorkItemCount = workItems.Count;
            staged.SourcePreparedMeshCount = sourcePreparedMeshCount;
            staged.StaticGroupCount = sourcePreparedMeshCount;
            staged.CombinedGroupCount = combinedGroupCount;
            staged.InteractableWorkItemCount = interactableWorkItems;
            staged.CombinedPreparedPayloadBytes = 0L;
            return result;
            }
            finally
            {
                k_BuildPreparedCellRefs.End();
            }
        }

        private static PreparedRefData BuildPreparedRefData(in PreparedWorkItem workItem)
        {
            k_BuildPreparedRefData.Begin();
            try
            {
            return new PreparedRefData(
                workItem.Built,
                workItem.Collision,
                workItem.MeshSourceLabel,
                workItem.MaterialFlags,
                workItem.TexturePath,
                workItem.PlacedRefId,
                workItem.DoorMetaIndex,
                workItem.ContentReference,
                workItem.Position,
                workItem.Rotation,
                workItem.Scale);
            }
            finally
            {
                k_BuildPreparedRefData.End();
            }
        }

        private static NifMeshBuilder.RawBuiltMesh CombineGroupMesh(List<StagedRefData> group, Vector3 cellOrigin, string name)
        {
            k_CombineGroupMesh.Begin();
            try
            {
            int totalVerts = 0;
            int totalIndices = 0;
            bool hasNormals = true;
            bool hasUvs = true;

            for (int i = 0; i < group.Count; i++)
            {
                totalVerts += group[i].Built.VertexCount;
                totalIndices += group[i].Built.Indices.Length;
                hasNormals &= group[i].Built.HasNormals;
                hasUvs &= group[i].Built.HasUvs;
            }

            var verts = new Vector3[totalVerts];
            Vector3[] normals = hasNormals ? new Vector3[totalVerts] : null;
            Vector2[] uvs = hasUvs ? new Vector2[totalVerts] : null;
            var indices = new int[totalIndices];

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            int vertexOffset = 0;
            int indexOffset = 0;
            for (int i = 0; i < group.Count; i++)
            {
                var pending = group[i];
                for (int v = 0; v < pending.Built.Vertices.Length; v++)
                {
                    var transformed = pending.Position + pending.Rotation * (pending.Built.Vertices[v] * pending.Scale) - cellOrigin;
                    verts[vertexOffset + v] = transformed;

                    if (transformed.x < min.x) min.x = transformed.x;
                    if (transformed.y < min.y) min.y = transformed.y;
                    if (transformed.z < min.z) min.z = transformed.z;
                    if (transformed.x > max.x) max.x = transformed.x;
                    if (transformed.y > max.y) max.y = transformed.y;
                    if (transformed.z > max.z) max.z = transformed.z;

                    if (normals != null)
                        normals[vertexOffset + v] = pending.Rotation * pending.Built.Normals[v];
                    if (uvs != null)
                        uvs[vertexOffset + v] = pending.Built.Uvs[v];
                }

                for (int t = 0; t < pending.Built.Indices.Length; t++)
                    indices[indexOffset + t] = pending.Built.Indices[t] + vertexOffset;

                vertexOffset += pending.Built.Vertices.Length;
                indexOffset += pending.Built.Indices.Length;
            }

            return new NifMeshBuilder.RawBuiltMesh(
                verts,
                normals,
                uvs,
                indices,
                group[0].TexturePath,
                name,
                new Bounds((min + max) * 0.5f, max - min),
                group[0].Built.AlphaFlags,
                group[0].Built.AlphaThreshold);
            }
            finally
            {
                k_CombineGroupMesh.End();
            }
        }

        private static IEnumerator WriteDirtyCellsIncremental(List<StagedCellData> dirtyCells, BakeProgress progress, float cellMeters)
        {
            progress.Stage = "Cell Writes";
            progress.Total = dirtyCells.Count;
            progress.Current = 0;
            progress.Label = dirtyCells.Count == 0 ? "No dirty cells to write" : $"Preparing cell write payloads 0/{dirtyCells.Count}";
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            var preparedWrites = new PreparedCellWriteData[dirtyCells.Count];
            yield return PrepareCellWritePayloadsIncremental(dirtyCells, preparedWrites, progress);
            yield return BuildCellColliderBlobsIncremental(preparedWrites, progress, cellMeters);
            yield return FlushCellFilesIncremental(preparedWrites, progress);
        }

        private static IEnumerator PrepareCellWritePayloadsIncremental(
            List<StagedCellData> dirtyCells,
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress)
        {
            var info = new CellWriteProgressInfo { Subphase = "Preparing cell write payloads" };
            UpdateCellWriteProgress(progress, dirtyCells.Count, info);
            yield return null;

            int claimed = 0;
            int completed = 0;
            string lastCompletedKey = null;
            Exception prepareFailure = null;
            int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, dirtyCells.Count));

            var prepareTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWorkers];
                for (int worker = 0; worker < maxWorkers; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= dirtyCells.Count)
                                break;

                            Interlocked.Increment(ref claimed);
                            try
                            {
                                preparedWrites[index] = PrepareCellWritePayload(dirtyCells[index]);
                                lastCompletedKey = dirtyCells[index].WorkItem.Key;
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    prepareFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!prepareTask.IsCompleted)
            {
                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                info.LastCompletedCellKey = lastCompletedKey;
                UpdateCellWriteProgress(progress, dirtyCells.Count, info);
                yield return null;
            }

            if (prepareFailure != null)
                throw prepareFailure;

            info.ClaimedCount = dirtyCells.Count;
            info.CompletedCount = dirtyCells.Count;
            info.LastCompletedCellKey = lastCompletedKey;
            UpdateCellWriteProgress(progress, dirtyCells.Count, info);
            yield return null;
        }

        private static PreparedCellWriteData PrepareCellWritePayload(StagedCellData staged)
        {
            k_PrepareCellWritePayload.Begin();
            try
            {
                uint flags = 0;
                bool hasTerrain = staged.Land != null && staged.Land.HasHeights;
                bool hasNormals = hasTerrain && staged.Land.Normals != null;
                bool hasVtex = hasTerrain && staged.LayerGrid != null && staged.LayerGrid.Length == LandRecord.NumTextures;
                bool hasWorldMap = hasTerrain && staged.Land.WorldMap != null && staged.Land.WorldMap.Length == 81;
                bool hasStaticCollision = !staged.StaticCollision.IsEmpty;
                bool hasEnvironment = staged.Environment.HasAnyData;
                if (hasTerrain) flags |= CacheFormat.CellFlagHasTerrain;
                if (hasNormals) flags |= CacheFormat.CellFlagHasNormals;
                if (hasVtex) flags |= CacheFormat.CellFlagHasVtex;
                if (hasStaticCollision) flags |= CacheFormat.CellFlagHasStaticCollision;
                if (hasEnvironment) flags |= CacheFormat.CellFlagHasEnvironment;
                if (hasWorldMap) flags |= CacheFormat.CellFlagHasWorldMap;

                return new PreparedCellWriteData
                {
                    Key = staged.WorkItem.Key,
                    OutputPath = staged.WorkItem.OutputPath,
                    AuditOutputPath = staged.WorkItem.AuditOutputPath,
                    IsInterior = staged.WorkItem.IsInterior,
                    CellId = staged.WorkItem.Cell.Name ?? string.Empty,
                    GridX = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridY,
                    Flags = flags,
                    Environment = staged.Environment,
                    Land = staged.Land,
                    StaticCollision = staged.StaticCollision,
                    TerrainHeightBytes = hasTerrain ? BuildTerrainHeightBytes(staged.Land) : null,
                    TerrainNormalBytes = hasNormals ? BuildTerrainNormalBytes(staged.Land) : null,
                    LayerGridBytes = hasVtex ? BuildLayerGridBytes(staged.LayerGrid) : null,
                    WorldMapBytes = hasWorldMap ? BuildWorldMapBytes(staged.Land) : null,
                    RefBytes = BuildRefBytes(staged.BakedRefs),
                    DoorBytes = BuildDoorBytes(staged.DoorEntries),
                    PlacementAuditBytes = BuildPlacementAuditBytes(staged),
                    RefCount = staged.BakedRefs?.Count ?? 0,
                    DoorCount = staged.DoorEntries?.Count ?? 0,
                };
            }
            finally
            {
                k_PrepareCellWritePayload.End();
            }
        }

        private static IEnumerator BuildCellColliderBlobsIncremental(
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress,
            float cellMeters)
        {
            var info = new CellWriteProgressInfo { Subphase = "Building collider blobs" };
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;

            using var scratch = new CellWriteScratch();
            var pendingStaticJobs = new List<PendingStaticColliderJob>();
            int nextIndex = 0;
            int claimed = 0;
            int completed = 0;
            int maxInFlightStaticJobs = Math.Max(1, Math.Min(Math.Max(1, Environment.ProcessorCount - 1), preparedWrites.Length));

            while (completed < preparedWrites.Length)
            {
                while (nextIndex < preparedWrites.Length && pendingStaticJobs.Count < maxInFlightStaticJobs)
                {
                    var preparedWrite = preparedWrites[nextIndex++];
                    PrepareTerrainColliderBlob(preparedWrite, cellMeters, scratch);

                    if ((preparedWrite.Flags & CacheFormat.CellFlagHasStaticCollision) != 0)
                    {
                        pendingStaticJobs.Add(ScheduleStaticColliderBlobJob(preparedWrite));
                    }
                    else
                    {
                        AssembleFinalCellWriteBuffer(preparedWrite);
                        claimed++;
                        completed++;
                        info.LastCompletedCellKey = preparedWrite.Key;
                    }
                }

                for (int i = pendingStaticJobs.Count - 1; i >= 0; i--)
                {
                    var pending = pendingStaticJobs[i];
                    if (!pending.Handle.IsCompleted)
                        continue;

                    FinalizeStaticColliderBlobJob(pending);
                    pendingStaticJobs.RemoveAt(i);
                    claimed++;
                    completed++;
                    info.LastCompletedCellKey = pending.PreparedWrite.Key;
                }

                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
                yield return null;
            }

            for (int i = 0; i < pendingStaticJobs.Count; i++)
                pendingStaticJobs[i].Dispose();

            if (pendingStaticJobs.Count == 0 && nextIndex > 0)
            {
                info.ClaimedCount = preparedWrites.Length;
                info.CompletedCount = preparedWrites.Length;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            }

            LogSlowCellWritePhase(preparedWrites, "blob build", cell => cell.BlobBuildMs);
        }

        private static void PrepareTerrainColliderBlob(
            PreparedCellWriteData preparedWrite,
            float cellMeters,
            CellWriteScratch scratch)
        {
            if ((preparedWrite.Flags & CacheFormat.CellFlagHasTerrain) == 0)
                return;

            k_CreateTerrainColliderBlob.Begin();
            try
            {
                preparedWrite.BlobData ??= new BuiltCellBlobData();

                const int n = LandRecord.Size;
                for (int i = 0; i < n * n; i++)
                    scratch.TerrainHeights[i] = preparedWrite.Land.Heights[i] * WorldScale.MwUnitsToMeters;

                float spacing = cellMeters / (n - 1);
                var terrainScale = new Unity.Mathematics.float3(spacing, 1f, spacing);
                var terrainBlob = Unity.Physics.TerrainCollider.Create(
                    scratch.TerrainHeights,
                    new Unity.Mathematics.int2(n, n),
                    terrainScale,
                    Unity.Physics.TerrainCollider.CollisionMethod.VertexSamples);
                try
                {
                    preparedWrite.BlobData.TerrainColliderBlobBytes = SerializeCellBlob(terrainBlob);
                }
                finally
                {
                    if (terrainBlob.IsCreated)
                        terrainBlob.Dispose();
                }
            }
            finally
            {
                k_CreateTerrainColliderBlob.End();
            }
        }

        private static PendingStaticColliderJob ScheduleStaticColliderBlobJob(PreparedCellWriteData preparedWrite)
        {
            k_CreateStaticColliderBlob.Begin();
            try
            {
                int vertexCount = preparedWrite.StaticCollision.Vertices.Length;
                int triangleCount = preparedWrite.StaticCollision.Indices.Length / 3;

                var points = new Unity.Collections.NativeArray<Unity.Mathematics.float3>(vertexCount, Unity.Collections.Allocator.Persistent);
                var triangles = new Unity.Collections.NativeArray<Unity.Mathematics.int3>(triangleCount, Unity.Collections.Allocator.Persistent);
                var output = new Unity.Collections.NativeArray<Unity.Entities.BlobAssetReference<Unity.Physics.Collider>>(1, Unity.Collections.Allocator.Persistent);

                for (int i = 0; i < vertexCount; i++)
                {
                    var vertex = preparedWrite.StaticCollision.Vertices[i];
                    points[i] = new Unity.Mathematics.float3(vertex.x, vertex.y, vertex.z);
                }

                for (int t = 0; t < triangleCount; t++)
                {
                    triangles[t] = new Unity.Mathematics.int3(
                        preparedWrite.StaticCollision.Indices[t * 3 + 0],
                        preparedWrite.StaticCollision.Indices[t * 3 + 1],
                        preparedWrite.StaticCollision.Indices[t * 3 + 2]);
                }

                var pending = new PendingStaticColliderJob
                {
                    PreparedWrite = preparedWrite,
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                    StartTicks = Stopwatch.GetTimestamp(),
                };

                pending.Handle = new CreateStaticColliderBlobJob
                {
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                }.Schedule();

                Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                return pending;
            }
            finally
            {
                k_CreateStaticColliderBlob.End();
            }
        }

        private static void FinalizeStaticColliderBlobJob(PendingStaticColliderJob pending)
        {
            pending.Handle.Complete();
            try
            {
                pending.PreparedWrite.BlobData ??= new BuiltCellBlobData();
                pending.PreparedWrite.BlobData.StaticCollisionBlobBytes = SerializeCellBlob(pending.Output[0]);
                AssembleFinalCellWriteBuffer(pending.PreparedWrite);
                pending.PreparedWrite.BlobBuildMs = ElapsedMilliseconds(pending.StartTicks);
            }
            finally
            {
                pending.Dispose();
            }
        }

        private static void AssembleFinalCellWriteBuffer(PreparedCellWriteData preparedWrite)
        {
            k_AssembleFinalCellWriteBuffer.Begin();
            try
            {
                preparedWrite.FinalBuffer = BuildFinalCellWriteBuffer(preparedWrite);
            }
            finally
            {
                k_AssembleFinalCellWriteBuffer.End();
            }
        }

        private static byte[] SerializeCellBlob<T>(Unity.Entities.BlobAssetReference<T> blob)
            where T : unmanaged
        {
            k_SerializeCellBlob.Begin();
            try
            {
                return BlobStreamIO.SerializeBlob(blob, CacheFormat.PhysicsBlobVersion);
            }
            finally
            {
                k_SerializeCellBlob.End();
            }
        }

        private static FinalCellWriteBuffer BuildFinalCellWriteBuffer(PreparedCellWriteData preparedWrite)
        {
            return new FinalCellWriteBuffer
            {
                HeaderBytes = BuildCellHeaderBytes(preparedWrite),
                TerrainHeightBytes = preparedWrite.TerrainHeightBytes,
                TerrainNormalBytes = preparedWrite.TerrainNormalBytes,
                TerrainColliderChunkBytes = ((preparedWrite.Flags & CacheFormat.CellFlagHasTerrain) != 0)
                    ? BuildLengthPrefixedBytes(preparedWrite.BlobData?.TerrainColliderBlobBytes)
                    : null,
                LayerGridBytes = preparedWrite.LayerGridBytes,
                WorldMapBytes = preparedWrite.WorldMapBytes,
                StaticCollisionChunkBytes = ((preparedWrite.Flags & CacheFormat.CellFlagHasStaticCollision) != 0)
                    ? BuildLengthPrefixedBytes(preparedWrite.BlobData?.StaticCollisionBlobBytes)
                    : null,
                RefCountBytes = BuildUInt32Bytes((uint)preparedWrite.RefCount),
                RefBytes = preparedWrite.RefBytes,
                DoorCountBytes = BuildUInt32Bytes((uint)preparedWrite.DoorCount),
                DoorBytes = preparedWrite.DoorBytes,
            };
        }

        private static IEnumerator FlushCellFilesIncremental(
            PreparedCellWriteData[] preparedWrites,
            BakeProgress progress)
        {
            var info = new CellWriteProgressInfo { Subphase = "Flushing cell files" };
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            yield return null;

            int claimed = 0;
            int completed = 0;
            string lastCompletedKey = null;
            Exception flushFailure = null;
            int maxWriters = Math.Max(1, Math.Min(Environment.ProcessorCount, preparedWrites.Length));

            var flushTask = Task.Run(() =>
            {
                int nextIndex = 0;
                int failureSignaled = 0;
                var workers = new Task[maxWriters];
                for (int worker = 0; worker < maxWriters; worker++)
                {
                    workers[worker] = Task.Factory.StartNew(() =>
                    {
                        while (Volatile.Read(ref failureSignaled) == 0)
                        {
                            int index = Interlocked.Increment(ref nextIndex) - 1;
                            if (index >= preparedWrites.Length)
                                break;

                            Interlocked.Increment(ref claimed);
                            try
                            {
                                FlushCellFile(preparedWrites[index]);
                                lastCompletedKey = preparedWrites[index].Key;
                                Interlocked.Increment(ref completed);
                            }
                            catch (Exception ex)
                            {
                                if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                                    flushFailure = ex;
                                break;
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Task.WaitAll(workers);
            });

            while (!flushTask.IsCompleted)
            {
                info.ClaimedCount = claimed;
                info.CompletedCount = completed;
                info.LastCompletedCellKey = lastCompletedKey;
                UpdateCellWriteProgress(progress, preparedWrites.Length, info);
                yield return null;
            }

            if (flushFailure != null)
                throw flushFailure;

            info.ClaimedCount = preparedWrites.Length;
            info.CompletedCount = preparedWrites.Length;
            info.LastCompletedCellKey = lastCompletedKey;
            UpdateCellWriteProgress(progress, preparedWrites.Length, info);
            LogSlowCellWritePhase(preparedWrites, "file flush", cell => cell.FlushMs);
            yield return null;
        }

        private static void FlushCellFile(PreparedCellWriteData preparedWrite)
        {
            k_FlushCellFile.Begin();
            long startTicks = Stopwatch.GetTimestamp();
            try
            {
                using var fs = File.Create(preparedWrite.OutputPath);
                WriteSegment(fs, preparedWrite.FinalBuffer.HeaderBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainHeightBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainNormalBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.TerrainColliderChunkBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.LayerGridBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.WorldMapBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.StaticCollisionChunkBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorBytes);

                fs.Flush(flushToDisk: true);

                if (!string.IsNullOrEmpty(preparedWrite.AuditOutputPath))
                    File.WriteAllBytes(preparedWrite.AuditOutputPath, preparedWrite.PlacementAuditBytes ?? Array.Empty<byte>());
            }
            finally
            {
                preparedWrite.FlushMs = ElapsedMilliseconds(startTicks);
                k_FlushCellFile.End();
            }

            if (!TryValidateCellFile(preparedWrite.OutputPath, preparedWrite.IsInterior, preparedWrite.CellId, out string validationError))
            {
                throw new InvalidDataException(
                    $"Wrote invalid cell file '{preparedWrite.OutputPath}' for '{preparedWrite.Key}': {validationError}");
            }
        }

        private static void UpdateCellWriteProgress(BakeProgress progress, int total, CellWriteProgressInfo info)
        {
            progress.Stage = "Cell Writes";
            progress.Total = total;
            progress.Current = Math.Min(total, info.ClaimedCount);
            progress.Label = total == 0
                ? $"No cells for {info.Subphase?.ToLowerInvariant() ?? "cell writes"}"
                : $"{info.Subphase} {info.CompletedCount}/{total}";
        }

        private static byte[] BuildTerrainHeightBytes(LandRecord land)
        {
            const int n = LandRecord.Size;
            var bytes = new byte[n * n * sizeof(float)];
            int offset = 0;
            for (int i = 0; i < n * n; i++)
            {
                float height = land.Heights[i] * WorldScale.MwUnitsToMeters;
                WriteSingle(bytes, ref offset, height);
            }
            return bytes;
        }

        private static Dictionary<string, ContentReference> BuildGameplayContentLookup(GameplayContentData gameplayContent)
        {
            return GameplayContentReferenceIndex.BuildPlaceableIndex(gameplayContent);
        }

        private static ContentReference ResolveGameplayContentReference(Dictionary<string, ContentReference> lookup, string baseId)
        {
            return GameplayContentReferenceIndex.TryResolvePlaceable(lookup, baseId, out var contentReference)
                ? contentReference
                : default;
        }

        private static byte[] BuildTerrainNormalBytes(LandRecord land)
        {
            var bytes = new byte[land.Normals.Length];
            Buffer.BlockCopy(land.Normals, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] BuildLayerGridBytes(ushort[] layerGrid)
        {
            var bytes = new byte[layerGrid.Length * sizeof(ushort)];
            int offset = 0;
            for (int i = 0; i < layerGrid.Length; i++)
                WriteUInt16(bytes, ref offset, layerGrid[i]);
            return bytes;
        }

        private static byte[] BuildWorldMapBytes(LandRecord land)
        {
            var bytes = new byte[land.WorldMap.Length];
            for (int i = 0; i < land.WorldMap.Length; i++)
                bytes[i] = unchecked((byte)land.WorldMap[i]);
            return bytes;
        }

        private static byte[] BuildRefBytes(IReadOnlyList<CellBakery.BakedRef> refs)
        {
            int count = refs?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream(count * 72);
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
            {
                var r = refs[i];
                w.Write(r.SpawnModeRaw);
                w.Write(r.RenderShardIndex);
                w.Write(r.LocalMeshIndex);
                w.Write(r.LocalMaterialIndex);
                w.Write(r.SliceIndex);
                w.Write(r.CollisionIndex);
                w.Write(r.PlacedRefId);
                w.Write(r.DoorMetaIndex);
                w.Write(r.ContentHandleValue);
                w.Write(r.ContentKind);
                w.Write(r.PositionUnity.x);
                w.Write(r.PositionUnity.y);
                w.Write(r.PositionUnity.z);
                w.Write(r.RotationUnity.x);
                w.Write(r.RotationUnity.y);
                w.Write(r.RotationUnity.z);
                w.Write(r.RotationUnity.w);
                w.Write(r.Scale);
            }
            return ms.ToArray();
        }

        private static byte[] BuildDoorBytes(IReadOnlyList<DoorRefEntry> doors)
        {
            int count = doors?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
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
            return ms.ToArray();
        }

        private static byte[] BuildPlacementAuditBytes(StagedCellData staged)
        {
            var audit = new CellPlacementAuditData
            {
                IsInterior = staged.WorkItem.IsInterior,
                CellId = staged.WorkItem.Cell.Name ?? string.Empty,
                GridX = staged.WorkItem.Cell.GridX,
                GridY = staged.WorkItem.Cell.GridY,
                Entries = staged.PlacementAuditEntries?.ToArray() ?? Array.Empty<RefPlacementAuditEntry>(),
            };

            return RefPlacementAuditFile.Serialize(audit);
        }

        private static byte[] BuildCellHeaderBytes(PreparedCellWriteData preparedWrite)
        {
            using var ms = new MemoryStream(64);
            using var w = new BinaryWriter(ms);
            w.Write(CellBakery.MagicCell);
            w.Write(preparedWrite.GridX);
            w.Write(preparedWrite.GridY);
            w.Write(preparedWrite.Flags);
            if ((preparedWrite.Flags & CacheFormat.CellFlagHasEnvironment) != 0)
            {
                var environment = preparedWrite.Environment;
                w.Write(environment.HasMood);
                w.Write(environment.HasWater);
                w.Write(environment.AmbientColorRgba);
                w.Write(environment.DirectionalColorRgba);
                w.Write(environment.FogColorRgba);
                w.Write(environment.FogDensity);
                w.Write(environment.WaterHeight);
                w.Write(environment.RegionId ?? string.Empty);
            }
            return ms.ToArray();
        }

        private static byte[] BuildLengthPrefixedBytes(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return BuildInt32Bytes(0);

            var bytes = new byte[sizeof(int) + payload.Length];
            int offset = 0;
            WriteInt32(bytes, ref offset, payload.Length);
            Buffer.BlockCopy(payload, 0, bytes, offset, payload.Length);
            return bytes;
        }

        private static byte[] BuildUInt32Bytes(uint value)
        {
            var bytes = new byte[sizeof(uint)];
            int offset = 0;
            WriteUInt32(bytes, ref offset, value);
            return bytes;
        }

        private static byte[] BuildInt32Bytes(int value)
        {
            var bytes = new byte[sizeof(int)];
            int offset = 0;
            WriteInt32(bytes, ref offset, value);
            return bytes;
        }

        private static void WriteSegment(Stream stream, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;
            stream.Write(bytes, 0, bytes.Length);
        }

        private static bool TryValidateCellFile(string path, bool isInterior, string cellId, out string error)
        {
            error = null;

            try
            {
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);

                if (r.ReadUInt32() != CellBakery.MagicCell)
                {
                    error = "bad cell magic";
                    return false;
                }

                r.ReadInt32();
                r.ReadInt32();
                uint flags = r.ReadUInt32();

                bool hasTerrain = (flags & CacheFormat.CellFlagHasTerrain) != 0;
                bool hasNormals = (flags & CacheFormat.CellFlagHasNormals) != 0;
                bool hasVtex = (flags & CacheFormat.CellFlagHasVtex) != 0;
                bool hasStaticCollision = (flags & CacheFormat.CellFlagHasStaticCollision) != 0;
                bool hasEnvironment = (flags & CacheFormat.CellFlagHasEnvironment) != 0;
                bool hasWorldMap = (flags & CacheFormat.CellFlagHasWorldMap) != 0;

                if (hasEnvironment)
                {
                    SkipExact(r, 2L, "cell environment flags");
                    SkipExact(r, 4L * 3L, "cell environment colors");
                    SkipExact(r, sizeof(float) * 2L, "cell environment scalars");
                    r.ReadString();
                }

                if (hasTerrain)
                {
                    SkipExact(r, 65L * 65L * sizeof(float), "terrain heights");
                    if (hasNormals)
                        SkipExact(r, 3L * 65L * 65L, "terrain normals");

                    var terrainBlob = BlobStreamIO.ReadLengthPrefixed<Unity.Physics.Collider>(
                        r,
                        CacheFormat.PhysicsBlobVersion,
                        $"cell file '{path}' terrain collider");
                    if (terrainBlob.IsCreated)
                        terrainBlob.Dispose();

                    if (hasVtex)
                        SkipExact(r, 16L * 16L * sizeof(ushort), "layer grid");
                    if (hasWorldMap)
                        SkipExact(r, 81L, "world map");
                }

                if (hasStaticCollision)
                {
                    var staticBlob = BlobStreamIO.ReadLengthPrefixed<Unity.Physics.Collider>(
                        r,
                        CacheFormat.PhysicsBlobVersion,
                        $"cell file '{path}' static collider");
                    if (staticBlob.IsCreated)
                        staticBlob.Dispose();
                }

                uint refCount = r.ReadUInt32();
                SkipExact(r, checked((long)refCount * 72L), "ref table");

                uint doorCount = r.ReadUInt32();
                for (int i = 0; i < doorCount; i++)
                {
                    SkipExact(r, 36L, "door table entry");
                    r.ReadString();
                }

                if (fs.Position != fs.Length)
                {
                    error = $"unexpected trailing data at offset {fs.Position}/{fs.Length}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                string cellLabel = isInterior
                    ? $"interior '{cellId ?? string.Empty}'"
                    : $"cell file '{path}'";
                error = $"{cellLabel}: {ex.Message}";
                return false;
            }
        }

        private static void SkipExact(BinaryReader reader, long byteCount, string section)
        {
            if (byteCount < 0)
                throw new InvalidDataException($"Negative byte count while validating {section}.");

            var stream = reader.BaseStream;
            long remaining = stream.Length - stream.Position;
            if (remaining < byteCount)
            {
                throw new InvalidDataException(
                    $"Truncated {section}: expected {byteCount} bytes, only {remaining} remain.");
            }

            stream.Seek(byteCount, SeekOrigin.Current);
        }

        private static void LogSlowCellWritePhase(
            IReadOnlyList<PreparedCellWriteData> preparedWrites,
            string phaseName,
            Func<PreparedCellWriteData, double> durationSelector)
        {
            const double thresholdMs = 50d;
            const int maxEntries = 10;

            var slowCells = new List<PreparedCellWriteData>();
            for (int i = 0; i < preparedWrites.Count; i++)
            {
                if (durationSelector(preparedWrites[i]) >= thresholdMs)
                    slowCells.Add(preparedWrites[i]);
            }

            if (slowCells.Count == 0)
                return;

            slowCells.Sort((a, b) => durationSelector(b).CompareTo(durationSelector(a)));

            var sb = new StringBuilder(512);
            sb.AppendLine($"[WorldBakeService] Slow {phaseName} cells ({slowCells.Count} over {thresholdMs:0} ms):");
            int count = Math.Min(maxEntries, slowCells.Count);
            for (int i = 0; i < count; i++)
            {
                var cell = slowCells[i];
                sb.Append(" - ")
                    .Append(cell.Key)
                    .Append(": ")
                    .Append(durationSelector(cell).ToString("0.0"))
                    .Append(" ms")
                    .AppendLine();
            }

            if (slowCells.Count > count)
                sb.Append(" ... ").Append(slowCells.Count - count).AppendLine(" more");

            UnityEngine.Debug.Log(sb.ToString());
        }

        private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        private static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            WriteUInt32(buffer, ref offset, unchecked((uint)value));
        }

        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }

        private static void WriteSingle(byte[] buffer, ref int offset, float value)
        {
            WriteUInt32(buffer, ref offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static BakeManifest.BakedCellState BuildCellState(StagedCellData staged)
        {
            if (staged.BakedRefs == null && staged.PreviousState != null)
            {
                return new BakeManifest.BakedCellState
                {
                    Key = staged.WorkItem.Key,
                    OutputPath = staged.WorkItem.OutputPath,
                    Fingerprint = staged.Fingerprint,
                    PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                    IsInterior = staged.WorkItem.IsInterior,
                    GridX = staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.Cell.GridY,
                    InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                    MeshIndices = staged.PreviousState.MeshIndices ?? Array.Empty<int>(),
                    MaterialIndices = staged.PreviousState.MaterialIndices ?? Array.Empty<int>(),
                    TextureIndices = staged.PreviousState.TextureIndices ?? Array.Empty<int>(),
                    CollisionIndices = staged.PreviousState.CollisionIndices ?? Array.Empty<int>(),
                    TerrainLayerIndices = staged.PreviousState.TerrainLayerIndices ?? Array.Empty<int>(),
                };
            }

            return new BakeManifest.BakedCellState
            {
                Key = staged.WorkItem.Key,
                OutputPath = staged.WorkItem.OutputPath,
                Fingerprint = staged.Fingerprint,
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                IsInterior = staged.WorkItem.IsInterior,
                GridX = staged.WorkItem.Cell.GridX,
                GridY = staged.WorkItem.Cell.GridY,
                InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                MeshIndices = staged.GlobalMeshIndices ?? Array.Empty<int>(),
                MaterialIndices = staged.GlobalMaterialIndices ?? Array.Empty<int>(),
                TextureIndices = staged.GlobalTextureIndices ?? Array.Empty<int>(),
                CollisionIndices = staged.GlobalCollisionIndices ?? Array.Empty<int>(),
                TerrainLayerIndices = staged.GlobalTerrainLayerIndices ?? Array.Empty<int>(),
            };
        }

        private static double ElapsedMilliseconds(long startTicks)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            return (elapsedTicks * 1000d) / Stopwatch.Frequency;
        }

        private static void LogSlowPreparedCellRefs(List<StagedCellData> dirtyCells)
        {
            const double thresholdMs = 50d;
            const int maxEntries = 10;

            var slowCells = new List<StagedCellData>();
            for (int i = 0; i < dirtyCells.Count; i++)
            {
                if (dirtyCells[i].BuildPreparedCellRefsMs >= thresholdMs)
                    slowCells.Add(dirtyCells[i]);
            }

            if (slowCells.Count == 0)
                return;

            slowCells.Sort((a, b) => b.BuildPreparedCellRefsMs.CompareTo(a.BuildPreparedCellRefsMs));

            var sb = new StringBuilder(512);
            sb.AppendLine($"[WorldBakeService] Slow BuildPreparedCellRefs cells ({slowCells.Count} over {thresholdMs:0} ms):");
            int count = Math.Min(maxEntries, slowCells.Count);
            for (int i = 0; i < count; i++)
            {
                var cell = slowCells[i];
                sb.Append(" - ")
                    .Append(cell.WorkItem.Key)
                    .Append(": ")
                    .Append(cell.BuildPreparedCellRefsMs.ToString("0.0"))
                    .Append(" ms, pending=")
                    .Append(cell.PendingRefCount)
                    .Append(", workItems=")
                    .Append(cell.PreparedWorkItemCount)
                    .Append(", sourceMeshes=")
                    .Append(cell.SourcePreparedMeshCount)
                    .Append(", staticGroups=")
                    .Append(cell.StaticGroupCount)
                    .Append(", combinedGroups=")
                    .Append(cell.CombinedGroupCount)
                    .Append(", interactables=")
                    .Append(cell.InteractableWorkItemCount)
                    .AppendLine();
            }

            if (slowCells.Count > count)
                sb.Append(" ... ").Append(slowCells.Count - count).AppendLine(" more");

            UnityEngine.Debug.Log(sb.ToString());
        }

        private static void WriteMeshCacheReport(StagedCellData[] stagedCells, MeshBakery meshes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePaths.MeshCacheReport) ?? string.Empty);

            long meshesFileBytes = File.Exists(CachePaths.Meshes) ? new FileInfo(CachePaths.Meshes).Length : 0L;
            long catalogFileBytes = File.Exists(CachePaths.MeshCatalog) ? new FileInfo(CachePaths.MeshCatalog).Length : 0L;

            int sourcePreparedMeshCount = 0;
            int combinedPreparedMeshCount = 0;
            long combinedPreparedPayloadBytes = 0L;
            var topCells = new List<(string Key, long Bytes, int MeshCount)>(stagedCells.Length);
            for (int i = 0; i < stagedCells.Length; i++)
            {
                var cell = stagedCells[i];
                sourcePreparedMeshCount += cell.SourcePreparedMeshCount;
                combinedPreparedMeshCount += cell.CombinedGroupCount;
                combinedPreparedPayloadBytes += cell.CombinedPreparedPayloadBytes;

                long cellBytes = 0L;
                var meshIndices = cell.GlobalMeshIndices ?? Array.Empty<int>();
                for (int m = 0; m < meshIndices.Length; m++)
                    cellBytes += meshes.GetPayloadLength(meshIndices[m]);

                topCells.Add((cell.WorkItem.Key, cellBytes, meshIndices.Length));
            }

            topCells.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

            var topMeshes = new List<(int Index, int Bytes, string Label)>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
                topMeshes.Add((i, meshes.GetPayloadLength(i), meshes.GetSourceLabel(i)));
            topMeshes.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

            var sb = new StringBuilder(2048);
            sb.AppendLine("[VVardenfell] Mesh Cache Report");
            sb.Append("meshCount=").Append(meshes.Count)
                .AppendLine();
            sb.Append("totalPayloadBytes=").Append(meshes.TotalPayloadBytes)
                .AppendLine();
            sb.Append("meshes.bin=").Append(meshesFileBytes)
                .AppendLine();
            sb.Append("mesh_catalog.bin=").Append(catalogFileBytes)
                .AppendLine();
            sb.Append("sourcePreparedMeshes=").Append(sourcePreparedMeshCount)
                .AppendLine();
            sb.Append("combinedPreparedMeshes=").Append(combinedPreparedMeshCount)
                .AppendLine();
            sb.Append("combinedPreparedPayloadBytes=").Append(combinedPreparedPayloadBytes)
                .AppendLine();

            sb.AppendLine();
            sb.AppendLine("Top mesh payloads:");
            int topMeshCount = Math.Min(10, topMeshes.Count);
            for (int i = 0; i < topMeshCount; i++)
            {
                var mesh = topMeshes[i];
                sb.Append(" - #").Append(mesh.Index)
                    .Append(" bytes=").Append(mesh.Bytes)
                    .Append(" label=").Append(mesh.Label)
                    .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Top cells by mesh payload contribution:");
            int topCellCount = Math.Min(10, topCells.Count);
            for (int i = 0; i < topCellCount; i++)
            {
                var cell = topCells[i];
                sb.Append(" - ").Append(cell.Key)
                    .Append(": bytes=").Append(cell.Bytes)
                    .Append(", meshRefs=").Append(cell.MeshCount)
                    .AppendLine();
            }

            File.WriteAllText(CachePaths.MeshCacheReport, sb.ToString(), Encoding.UTF8);

            UnityEngine.Debug.Log(
                $"[VVardenfell] mesh cache: meshCount={meshes.Count}, meshes.bin={meshesFileBytes} bytes, mesh_catalog.bin={catalogFileBytes} bytes, sourcePreparedMeshes={sourcePreparedMeshCount}, combinedPreparedMeshes={combinedPreparedMeshCount}, combinedPreparedPayloadBytes={combinedPreparedPayloadBytes}");
        }

        private static void WriteWorldCollisionValidationReport(StagedCellData[] stagedCells)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePaths.WorldCollisionValidationReport) ?? string.Empty);

            int staticCandidates = 0;
            int staticAggregates = 0;
            int staticTriangles = 0;
            int perPlacedRef = 0;
            int missingPayloads = 0;
            int authoredRootCollision = 0;
            int autoVisualStaticCollision = 0;
            int explicitNoCollisionMarkers = 0;
            int noCollider = 0;
            var topMissing = new List<(string Key, bool IsInterior, int Missing, int StaticCandidates, string[] Samples)>(stagedCells.Length);

            for (int i = 0; i < stagedCells.Length; i++)
            {
                var cell = stagedCells[i];
                staticCandidates += cell.CollisionStaticCandidateCount;
                staticAggregates += cell.CollisionStaticAggregateCount;
                staticTriangles += cell.CollisionStaticAggregateTriangleCount;
                perPlacedRef += cell.CollisionPerPlacedRefCount;
                missingPayloads += cell.CollisionMissingPayloadCount;
                authoredRootCollision += cell.CollisionAuthoredRootCount;
                autoVisualStaticCollision += cell.CollisionAutoVisualStaticCount;
                explicitNoCollisionMarkers += cell.CollisionExplicitNoCollisionCount;
                noCollider += cell.CollisionNoColliderCount;
                if (cell.CollisionMissingPayloadCount > 0 || cell.CollisionStaticCandidateCount > 0)
                {
                    var samples = cell.CollisionMissingPayloadSamples != null
                        ? cell.CollisionMissingPayloadSamples.ToArray()
                        : Array.Empty<string>();
                    topMissing.Add((cell.WorkItem.Key, cell.WorkItem.IsInterior, cell.CollisionMissingPayloadCount, cell.CollisionStaticCandidateCount, samples));
                }
            }

            topMissing.Sort((a, b) => b.Missing != a.Missing
                ? b.Missing.CompareTo(a.Missing)
                : b.StaticCandidates.CompareTo(a.StaticCandidates));

            var sb = new StringBuilder(2048);
            sb.AppendLine("[VVardenfell] World Collision Validation");
            sb.Append("cells=").Append(stagedCells.Length).AppendLine();
            sb.Append("staticCandidates=").Append(staticCandidates).AppendLine();
            sb.Append("staticAggregateRefs=").Append(staticAggregates).AppendLine();
            sb.Append("staticAggregateTriangles=").Append(staticTriangles).AppendLine();
            sb.Append("perPlacedRefColliders=").Append(perPlacedRef).AppendLine();
            sb.Append("authoredRootCollisionRefs=").Append(authoredRootCollision).AppendLine();
            sb.Append("autoVisualStaticRefs=").Append(autoVisualStaticCollision).AppendLine();
            sb.Append("explicitNoCollisionMarkerRefs=").Append(explicitNoCollisionMarkers).AppendLine();
            sb.Append("missingCollisionPayloads=").Append(missingPayloads).AppendLine();
            sb.Append("noColliderRefs=").Append(noCollider).AppendLine();
            sb.AppendLine();
            sb.AppendLine("Top cells by missing collision payloads:");
            int count = Math.Min(12, topMissing.Count);
            for (int i = 0; i < count; i++)
            {
                var cell = topMissing[i];
                sb.Append(" - ")
                    .Append(cell.Key)
                    .Append(" (")
                    .Append(cell.IsInterior ? "interior" : "exterior")
                    .Append(")")
                    .Append(": missingPayloads=")
                    .Append(cell.Missing)
                    .Append(", staticCandidates=")
                    .Append(cell.StaticCandidates);
                if (cell.Samples.Length > 0)
                {
                    sb.Append(", samples=");
                    for (int sampleIndex = 0; sampleIndex < cell.Samples.Length; sampleIndex++)
                    {
                        if (sampleIndex > 0)
                            sb.Append(" | ");
                        sb.Append(cell.Samples[sampleIndex]);
                    }
                }

                sb.AppendLine();
            }

            File.WriteAllText(CachePaths.WorldCollisionValidationReport, sb.ToString(), Encoding.UTF8);
            UnityEngine.Debug.Log(
                $"[VVardenfell] world collision validation: staticCandidates={staticCandidates}, staticAggregateRefs={staticAggregates}, autoVisualStaticRefs={autoVisualStaticCollision}, perPlacedRefColliders={perPlacedRef}, missingPayloads={missingPayloads}, noColliderRefs={noCollider}");
        }


        private static int[] ToSortedArray(HashSet<int> set)
        {
            var values = new int[set.Count];
            set.CopyTo(values);
            Array.Sort(values);
            return values;
        }

        private static string ComputeFingerprint(
            CellBakeWorkItem workItem,
            List<CellReference> refs,
            LandRecord land,
            RecordIndex recordIndex,
            Dictionary<int, string> ltexMap)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(workItem.IsInterior);
                w.Write(workItem.Cell.Name ?? string.Empty);
                w.Write(workItem.Cell.GridX);
                w.Write(workItem.Cell.GridY);
                w.Write(workItem.Cell.Environment.HasMood);
                w.Write(workItem.Cell.Environment.HasWater);
                w.Write(workItem.Cell.Environment.AmbientColorRgba);
                w.Write(workItem.Cell.Environment.DirectionalColorRgba);
                w.Write(workItem.Cell.Environment.FogColorRgba);
                w.Write(workItem.Cell.Environment.FogDensity);
                w.Write(workItem.Cell.Environment.WaterHeight);
                w.Write(workItem.Cell.Environment.RegionId ?? string.Empty);
                w.Write(refs.Count);
                for (int i = 0; i < refs.Count; i++)
                {
                    var reference = refs[i];
                    w.Write(reference.FormId);
                    w.Write(reference.BaseId ?? string.Empty);
                    w.Write(reference.PosX);
                    w.Write(reference.PosY);
                    w.Write(reference.PosZ);
                    w.Write(reference.RotX);
                    w.Write(reference.RotY);
                    w.Write(reference.RotZ);
                    w.Write(reference.Scale);
                    w.Write(reference.Deleted);
                    w.Write(reference.IsDoor);
                    w.Write(reference.DoorDestCell ?? string.Empty);
                    w.Write(reference.DoorDestX);
                    w.Write(reference.DoorDestY);
                    w.Write(reference.DoorDestZ);
                    w.Write(reference.DoorDestRotX);
                    w.Write(reference.DoorDestRotY);
                    w.Write(reference.DoorDestRotZ);
                    if (recordIndex.TryGet(reference.BaseId, out var rec))
                    {
                        w.Write(rec.Tag);
                        w.Write(rec.Model ?? string.Empty);
                    }
                    else
                    {
                        w.Write(0u);
                        w.Write(string.Empty);
                    }
                }

                bool hasLand = land != null && land.HasHeights;
                w.Write(hasLand);
                if (hasLand)
                {
                    w.Write(land.GridX);
                    w.Write(land.GridY);
                    w.Write(land.Heights.Length);
                    for (int i = 0; i < land.Heights.Length; i++)
                        w.Write(land.Heights[i]);

                    bool hasNormals = land.Normals != null;
                    w.Write(hasNormals);
                    if (hasNormals)
                    {
                        w.Write(land.Normals.Length);
                        for (int i = 0; i < land.Normals.Length; i++)
                            w.Write(land.Normals[i]);
                    }

                    bool hasVtex = land.VtexIndices != null;
                    w.Write(hasVtex);
                    if (hasVtex)
                    {
                        w.Write(land.VtexIndices.Length);
                        for (int i = 0; i < land.VtexIndices.Length; i++)
                        {
                            ushort vtex = land.VtexIndices[i];
                            w.Write(vtex);
                            w.Write(LtexIndex.ResolveVtex(vtex, ltexMap) ?? string.Empty);
                        }
                    }

                    bool hasWorldMap = land.WorldMap != null;
                    w.Write(hasWorldMap);
                    if (hasWorldMap)
                    {
                        w.Write(land.WorldMap.Length);
                        for (int i = 0; i < land.WorldMap.Length; i++)
                            w.Write(land.WorldMap[i]);
                    }
                }
            }

            ms.Position = 0;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(ms.ToArray());
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static void AppendTransformed(
            CollisionPayload src,
            Vector3 pos,
            Quaternion rot,
            float scale,
            Vector3 cellOrigin,
            List<Vector3> outVerts,
            List<int> outIndices)
        {
            int baseVert = outVerts.Count;
            for (int i = 0; i < src.Vertices.Length; i++)
                outVerts.Add(pos + rot * (src.Vertices[i] * scale) - cellOrigin);

            for (int i = 0; i < src.Indices.Length; i++)
                outIndices.Add(baseVert + src.Indices[i]);
        }

        private static void BuildDoorEntry(in CellReference reference, out DoorRefEntry doorEntry)
        {
            CellBakery.ToUnityTransformRaw(
                reference.DoorDestX,
                reference.DoorDestY,
                reference.DoorDestZ,
                reference.DoorDestRotX,
                reference.DoorDestRotY,
                reference.DoorDestRotZ,
                out var destPos,
                out var destRot);

            uint flags = 0u;
            string destinationCellId = string.Empty;
            if (reference.IsDoor)
            {
                flags |= DoorRefEntry.FlagTeleport;
                destinationCellId = reference.DoorDestCell ?? string.Empty;
            }

            doorEntry = new DoorRefEntry
            {
                PlacedRefId = reference.FormId,
                Flags = flags,
                DestPosX = destPos.x,
                DestPosY = destPos.y,
                DestPosZ = destPos.z,
                DestRotX = destRot.x,
                DestRotY = destRot.y,
                DestRotZ = destRot.z,
                DestRotW = destRot.w,
                DestinationCellId = destinationCellId,
            };
        }

        private static string BuildExteriorKey(int gridX, int gridY) => $"ext:{gridX},{gridY}";

        private static string BuildInteriorKey(string interiorId) => $"int:{(interiorId ?? string.Empty).Trim().ToLowerInvariant()}";

        private static void PruneOrphans(string directory, HashSet<string> expectedOutputs)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!expectedOutputs.Contains(file))
                    File.Delete(file);
            }
        }
    }
}
