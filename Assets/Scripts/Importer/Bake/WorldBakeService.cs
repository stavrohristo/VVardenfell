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
        private static readonly ProfilerMarker k_PrepareCellWritePayload = new("VV.Bake.PrepareCellWritePayload");
        private static readonly ProfilerMarker k_BuildCellColliderBlobs = new("VV.Bake.BuildCellColliderBlobs");
        private static readonly ProfilerMarker k_CreateTerrainColliderBlob = new("VV.Bake.CreateTerrainColliderBlob");
        private static readonly ProfilerMarker k_CreateStaticColliderBlob = new("VV.Bake.CreateStaticColliderBlob");
        private static readonly ProfilerMarker k_SerializeCellBlob = new("VV.Bake.SerializeCellBlob");
        private static readonly ProfilerMarker k_AssembleFinalCellWriteBuffer = new("VV.Bake.AssembleFinalCellWriteBuffer");
        private static readonly ProfilerMarker k_FlushCellFile = new("VV.Bake.FlushCellFile");

        private readonly struct CellBakeWorkItem
        {
            public readonly CellHeader Cell;
            public readonly bool IsInterior;
            public readonly long LandOffset;
            public readonly string Key;
            public readonly string OutputPath;
            public readonly Vector3 CellOrigin;

            public CellBakeWorkItem(CellHeader cell, bool isInterior, long landOffset, string key, string outputPath, Vector3 cellOrigin)
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
            public readonly NifMeshBuilder.RawBuiltMesh[] Meshes;
            public readonly CollisionPayload Collision;

            public ModelSource(NifMeshBuilder.RawBuiltMesh[] meshes, CollisionPayload collision)
            {
                Meshes = meshes;
                Collision = collision;
            }
        }

        private readonly struct StagedRefData
        {
            public readonly NifMeshBuilder.RawBuiltMesh Built;
            public readonly CollisionPayload Collision;
            public readonly uint MaterialFlags;
            public readonly string TexturePath;
            public readonly bool IsInteractable;
            public readonly uint PlacedRefId;
            public readonly int DoorMetaIndex;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public StagedRefData(
                in NifMeshBuilder.RawBuiltMesh built,
                in CollisionPayload collision,
                uint materialFlags,
                string texturePath,
                bool isInteractable,
                uint placedRefId,
                int doorMetaIndex,
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                Built = built;
                Collision = collision;
                MaterialFlags = materialFlags;
                TexturePath = texturePath;
                IsInteractable = isInteractable;
                PlacedRefId = placedRefId;
                DoorMetaIndex = doorMetaIndex;
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
            public LandRecord Land;
            public string[] TerrainTexturePaths;
            public ushort[] LayerGrid;
            public List<CellBakery.BakedRef> BakedRefs;
            public List<DoorRefEntry> DoorEntries;
            public CellBakery.StaticCollision StaticCollision;
            public List<StagedRefData> PendingRefs;
            public List<PreparedRefData> PreparedRefs;
            public string Fingerprint;
            public bool NeedsWrite;
            public double BuildPreparedCellRefsMs;
            public int PendingRefCount;
            public int PreparedWorkItemCount;
            public int StaticGroupCount;
            public int CombinedGroupCount;
            public int InteractableWorkItemCount;
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
            public LandRecord Land;
            public CellBakery.StaticCollision StaticCollision;
            public byte[] TerrainHeightBytes;
            public byte[] TerrainNormalBytes;
            public byte[] LayerGridBytes;
            public byte[] RefBytes;
            public byte[] DoorBytes;
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

        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress)
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
            bakeryMeshes.TryLoadExisting(CachePaths.MeshCatalog);
            var bakeryMaterials = new MaterialBakery();
            bakeryMaterials.TryLoadExisting(CachePaths.MaterialCatalog);
            var bakeryTextures = new TextureBakery(sharedBsa, textureResolver);
            bakeryTextures.TryLoadExisting(CachePaths.TextureCatalog);
            int defaultTexIdx = bakeryTextures.AddOrGet(LtexIndex.DefaultTexturePath);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);
            bakeryLayers.TryLoadExisting(CachePaths.TerrainLayers);
            var bakeryCollisions = new CollisionBakery();
            bakeryCollisions.TryLoadExisting(CachePaths.CollisionCatalog);

            progress.Current = 1;
            yield return null;

            var bsaByName = new Dictionary<string, BsaEntry>(sharedBsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in sharedBsa.Entries)
                bsaByName[entry.Name] = entry;

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
                var modelCache = new ConcurrentDictionary<string, Lazy<ModelSource>>(StringComparer.OrdinalIgnoreCase);
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
                if (staged.NeedsWrite)
                    dirtyCells.Add(staged);

                cellStates[i] = BuildCellState(staged);
                progress.Current = i + 1;
                if (((i + 1) & 7) == 0)
                    yield return null;
            }

            yield return PrepareDirtyCellsIncremental(dirtyCells, progress, ltexMap);
            yield return ResolveDirtyCellIndicesIncremental(dirtyCells, progress, bakeryMeshes, bakeryMaterials, bakeryTextures, bakeryLayers, bakeryCollisions);

            for (int i = 0; i < stagedCells.Length; i++)
                cellStates[i] = BuildCellState(stagedCells[i]);

            yield return WriteDirtyCellsIncremental(dirtyCells, progress, cellMeters);

            progress.Stage = "Writing";
            progress.Current = 0;
            progress.Total = 8;

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

            progress.Label = "textures.bin";
            progress.Current = 3;
            yield return null;
            if (bakeryTextures.Modified || !File.Exists(CachePaths.TexturesIndex) || !File.Exists(CachePaths.TextureCatalog))
            {
                bakeryTextures.WriteIndex(CachePaths.TexturesIndex);
                bakeryTextures.WriteCatalog(CachePaths.TextureCatalog);
            }

            progress.Label = "terrain_layers.bin";
            progress.Current = 4;
            yield return null;
            if (bakeryLayers.Modified || !File.Exists(CachePaths.TerrainLayers))
                bakeryLayers.WriteTo(CachePaths.TerrainLayers);

            progress.Label = "collisions.bin";
            progress.Current = 5;
            yield return null;
            if (bakeryCollisions.Modified || !File.Exists(CachePaths.Collisions) || !File.Exists(CachePaths.CollisionCatalog))
            {
                bakeryCollisions.WriteTo(CachePaths.Collisions);
                bakeryCollisions.WriteCatalog(CachePaths.CollisionCatalog);
            }

            progress.Label = "Pruning stale cells";
            progress.Current = 6;
            yield return null;
            PruneOrphans(CachePaths.CellsDir, expectedOutputs);
            PruneOrphans(CachePaths.InteriorCellsDir, expectedOutputs);

            progress.Label = "ui.bin";
            progress.Current = 7;
            yield return null;
            UiAssetBakery.Bake(config, sharedBsa, progress);

            progress.Label = "manifest.bin";
            progress.Current = 8;
            yield return null;
            var manifest = BakeManifest.FromCurrentSources(esmPath, bsaPath);
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
                && TryValidateCellFile(workItem.OutputPath, workItem.IsInterior, workItem.Cell.Name, out _);

            var staged = new StagedCellData
            {
                WorkItem = workItem,
                PreviousState = hasPrevious ? previousState : null,
                Fingerprint = fingerprint,
                Land = land,
                NeedsWrite = !canReuse,
                PendingRefs = canReuse ? null : new List<StagedRefData>(refs.Count),
                DoorEntries = canReuse ? null : new List<DoorRefEntry>(),
            };

            if (canReuse)
                return staged;

            var staticVerts = new List<Vector3>();
            var staticIndices = new List<int>();
            for (int i = 0; i < refs.Count; i++)
            {
                var reference = refs[i];
                if (reference.Deleted)
                    continue;
                if (!recordIndex.TryGet(reference.BaseId, out var rec))
                    continue;

                var model = EnsureModelSource(rec, sharedBsa, bsaByName, modelCache);
                if (model == null || model.Meshes.Length == 0)
                    continue;

                CellBakery.ToUnityTransform(reference, out var pos, out var rot);
                bool isStat = rec.Tag == StatTag;
                bool isInteractable = !isStat;

                int doorMetaIndex = -1;
                if (rec.Tag == DoorTag)
                {
                    doorMetaIndex = staged.DoorEntries.Count;
                    BuildDoorEntry(reference, out var doorEntry);
                    staged.DoorEntries.Add(doorEntry);
                }

                if (!model.Collision.IsEmpty)
                {
                    if (isStat)
                    {
                        AppendTransformed(model.Collision, pos, rot, reference.Scale, workItem.CellOrigin, staticVerts, staticIndices);
                    }
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
                        meshIndex == 0 ? model.Collision : default,
                        matFlags,
                        built.TexturePath,
                        isInteractable,
                        reference.FormId,
                        meshIndex == 0 ? doorMetaIndex : -1,
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

        private static ModelSource EnsureModelSource(
            BaseRecord rec,
            BsaArchive sharedBsa,
            Dictionary<string, BsaEntry> bsaByName,
            ConcurrentDictionary<string, Lazy<ModelSource>> modelCache)
        {
            if (string.IsNullOrEmpty(rec.Model))
                return null;

            string nifPath = "meshes\\" + rec.Model;
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

                    CollisionPayload collision = default;
                    NifCollisionExtractor.TryExtract(nif, out collision);
                    return new ModelSource(built.ToArray(), collision);
                }, LazyThreadSafetyMode.ExecutionAndPublication));

            return lazy.Value;
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
            CollisionBakery collisions)
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

        private static void ResolveDirtyCellIndices(
            StagedCellData staged,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            TerrainLayerBakery terrainLayers,
            CollisionBakery collisions,
            Dictionary<uint, int> materialIndexCache,
            Dictionary<string, int> textureIndexCache,
            Dictionary<int, ushort> terrainLayerCache)
        {
            k_ResolveDirtyCellIndices.Begin();
            try
            {
            if (staged.PreparedRefs == null)
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

            var result = new List<CellBakery.BakedRef>(staged.PreparedRefs.Count);
            for (int i = 0; i < staged.PreparedRefs.Count; i++)
            {
                var prepared = staged.PreparedRefs[i];
                int meshIndex = meshes.AddOrGet(prepared.MeshSourceLabel, prepared.Built);
                int materialIndex = GetMaterialIndex(prepared.MaterialFlags, materials, materialIndexCache);
                int sliceIndex = GetTextureIndex(prepared.TexturePath, textures, textureIndexCache);
                int collisionIndex = prepared.Collision.IsEmpty
                    ? -1
                    : collisions.AddOrGet(prepared.Collision);
                result.Add(new CellBakery.BakedRef(
                    meshIndex,
                    materialIndex,
                    sliceIndex,
                    collisionIndex,
                    prepared.PlacedRefId,
                    prepared.DoorMetaIndex,
                    prepared.Position,
                    prepared.Rotation,
                    prepared.Scale));
            }

            staged.BakedRefs = result;
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
                staged.StaticGroupCount = 0;
                staged.CombinedGroupCount = 0;
                staged.InteractableWorkItemCount = 0;
                return result;
            }

            var groups = new Dictionary<BatchKey, List<StagedRefData>>(new BatchKeyComparer());
            var workItems = new List<PreparedWorkItem>(staged.PendingRefs.Count);
            int interactableWorkItems = 0;
            k_BuildPreparedCellRefsGroup.Begin();
            try
            {
                for (int i = 0; i < staged.PendingRefs.Count; i++)
                {
                    var pending = staged.PendingRefs[i];
                    if (pending.IsInteractable)
                    {
                        int preparedIndex = workItems.Count;
                        workItems.Add(new PreparedWorkItem(
                            true,
                            pending.Built,
                            pending.Collision,
                            pending.MaterialFlags,
                            pending.TexturePath,
                            pending.PlacedRefId,
                            pending.DoorMetaIndex,
                            pending.Position,
                            pending.Rotation,
                            pending.Scale,
                            BuildPreparedMeshSourceLabel(staged.WorkItem.Key, preparedIndex)));
                        interactableWorkItems++;
                        continue;
                    }

                    var key = new BatchKey(pending.MaterialFlags, pending.TexturePath, pending.Built.HasNormals, pending.Built.HasUvs);
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<StagedRefData>();
                    list.Add(pending);
                }
            }
            finally
            {
                k_BuildPreparedCellRefsGroup.End();
            }

            int batchIndex = 0;
            int combinedGroupCount = 0;
            k_BuildPreparedCellRefsCombine.Begin();
            try
            {
                foreach (var kv in groups)
                {
                    var group = kv.Value;
                    if (group.Count == 1)
                    {
                        var pending = group[0];
                        int preparedIndex = workItems.Count;
                        workItems.Add(new PreparedWorkItem(
                            false,
                            pending.Built,
                            default,
                            pending.MaterialFlags,
                            pending.TexturePath,
                            0u,
                            -1,
                            pending.Position,
                            pending.Rotation,
                            pending.Scale,
                            BuildPreparedMeshSourceLabel(staged.WorkItem.Key, preparedIndex)));
                        batchIndex++;
                        continue;
                    }

                    var combined = CombineGroupMesh(group, staged.WorkItem.CellOrigin, $"{staged.WorkItem.Key}[{batchIndex}]");
                    int combinedIndex = workItems.Count;
                    workItems.Add(new PreparedWorkItem(
                        false,
                        combined,
                        default,
                        kv.Key.MaterialFlags,
                        kv.Key.TexturePath,
                        0u,
                        -1,
                        staged.WorkItem.CellOrigin,
                        Quaternion.identity,
                        1f,
                        BuildPreparedMeshSourceLabel(staged.WorkItem.Key, combinedIndex)));
                    combinedGroupCount++;
                    batchIndex++;
                }
            }
            finally
            {
                k_BuildPreparedCellRefsCombine.End();
            }

            if (workItems.Count == 0)
            {
                staged.BuildPreparedCellRefsMs = 0d;
                staged.PreparedWorkItemCount = 0;
                staged.StaticGroupCount = groups.Count;
                staged.CombinedGroupCount = combinedGroupCount;
                staged.InteractableWorkItemCount = interactableWorkItems;
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
            staged.StaticGroupCount = groups.Count;
            staged.CombinedGroupCount = combinedGroupCount;
            staged.InteractableWorkItemCount = interactableWorkItems;
            return result;
            }
            finally
            {
                k_BuildPreparedCellRefs.End();
            }
        }

        private static string BuildPreparedMeshSourceLabel(string cellKey, int preparedIndex)
            => $"{cellKey}#{preparedIndex}";

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
                bool hasStaticCollision = !staged.StaticCollision.IsEmpty;
                if (hasTerrain) flags |= CacheFormat.CellFlagHasTerrain;
                if (hasNormals) flags |= CacheFormat.CellFlagHasNormals;
                if (hasVtex) flags |= CacheFormat.CellFlagHasVtex;
                if (hasStaticCollision) flags |= CacheFormat.CellFlagHasStaticCollision;

                return new PreparedCellWriteData
                {
                    Key = staged.WorkItem.Key,
                    OutputPath = staged.WorkItem.OutputPath,
                    IsInterior = staged.WorkItem.IsInterior,
                    CellId = staged.WorkItem.Cell.Name ?? string.Empty,
                    GridX = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridY,
                    Flags = flags,
                    Land = staged.Land,
                    StaticCollision = staged.StaticCollision,
                    TerrainHeightBytes = hasTerrain ? BuildTerrainHeightBytes(staged.Land) : null,
                    TerrainNormalBytes = hasNormals ? BuildTerrainNormalBytes(staged.Land) : null,
                    LayerGridBytes = hasVtex ? BuildLayerGridBytes(staged.LayerGrid) : null,
                    RefBytes = BuildRefBytes(staged.BakedRefs),
                    DoorBytes = BuildDoorBytes(staged.DoorEntries),
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
                WriteSegment(fs, preparedWrite.FinalBuffer.StaticCollisionChunkBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.RefBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorCountBytes);
                WriteSegment(fs, preparedWrite.FinalBuffer.DoorBytes);

                fs.Flush(flushToDisk: true);
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

        private static byte[] BuildRefBytes(IReadOnlyList<CellBakery.BakedRef> refs)
        {
            int count = refs?.Count ?? 0;
            if (count == 0)
                return null;

            using var ms = new MemoryStream(count * 56);
            using var w = new BinaryWriter(ms);
            for (int i = 0; i < count; i++)
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

        private static byte[] BuildCellHeaderBytes(PreparedCellWriteData preparedWrite)
        {
            var bytes = new byte[16];
            int offset = 0;
            WriteUInt32(bytes, ref offset, CellBakery.MagicCell);
            WriteInt32(bytes, ref offset, preparedWrite.GridX);
            WriteInt32(bytes, ref offset, preparedWrite.GridY);
            WriteUInt32(bytes, ref offset, preparedWrite.Flags);
            return bytes;
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
                SkipExact(r, checked((long)refCount * 56L), "ref table");

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

            var meshIndices = new HashSet<int>();
            var materialIndices = new HashSet<int>();
            var textureIndices = new HashSet<int>();
            var collisionIndices = new HashSet<int>();
            var terrainLayerIndices = new HashSet<int>();

            if (staged.BakedRefs != null)
            {
                for (int i = 0; i < staged.BakedRefs.Count; i++)
                {
                    var baked = staged.BakedRefs[i];
                    meshIndices.Add(baked.MeshIndex);
                    materialIndices.Add(baked.MaterialIndex);
                    if (baked.SliceIndex >= 0)
                        textureIndices.Add(baked.SliceIndex);
                    if (baked.CollisionIndex >= 0)
                        collisionIndices.Add(baked.CollisionIndex);
                }
            }

            if (staged.LayerGrid != null)
            {
                for (int i = 0; i < staged.LayerGrid.Length; i++)
                    terrainLayerIndices.Add(staged.LayerGrid[i]);
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
                MeshIndices = ToSortedArray(meshIndices),
                MaterialIndices = ToSortedArray(materialIndices),
                TextureIndices = ToSortedArray(textureIndices),
                CollisionIndices = ToSortedArray(collisionIndices),
                TerrainLayerIndices = ToSortedArray(terrainLayerIndices),
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
