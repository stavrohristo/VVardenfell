using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Runs the one-shot bake as a coroutine so the host MonoBehaviour can
    /// repaint its UI between chunks. Writes a self-contained cache under
    /// <see cref="CachePaths.Root"/>.
    /// </summary>
    public static class BakeCoordinator
    {
        private readonly struct PendingModel
        {
            public readonly NifMeshBuilder.BuiltMesh[] Built;
            public readonly int[] Mesh;
            public readonly int[] Mat;
            public readonly int[] Slice;

            public PendingModel(NifMeshBuilder.BuiltMesh[] built, int[] mesh, int[] mat, int[] slice)
            {
                Built = built;
                Mesh = mesh;
                Mat = mat;
                Slice = slice;
            }
        }

        private readonly struct PendingRef
        {
            public readonly NifMeshBuilder.BuiltMesh Built;
            public readonly int MeshIndex;
            public readonly int MaterialIndex;
            public readonly int SliceIndex;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Scale;

            public PendingRef(
                NifMeshBuilder.BuiltMesh built,
                int meshIndex,
                int materialIndex,
                int sliceIndex,
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                Built = built;
                MeshIndex = meshIndex;
                MaterialIndex = materialIndex;
                SliceIndex = sliceIndex;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        private readonly struct BatchKey
        {
            public readonly int MaterialIndex;
            public readonly int SliceIndex;
            public readonly bool HasNormals;
            public readonly bool HasUvs;

            public BatchKey(int materialIndex, int sliceIndex, bool hasNormals, bool hasUvs)
            {
                MaterialIndex = materialIndex;
                SliceIndex = sliceIndex;
                HasNormals = hasNormals;
                HasUvs = hasUvs;
            }
        }

        private sealed class BatchKeyComparer : IEqualityComparer<BatchKey>
        {
            public bool Equals(BatchKey x, BatchKey y)
            {
                return x.MaterialIndex == y.MaterialIndex
                    && x.SliceIndex == y.SliceIndex
                    && x.HasNormals == y.HasNormals
                    && x.HasUvs == y.HasUvs;
            }

            public int GetHashCode(BatchKey obj)
            {
                int hash = obj.MaterialIndex;
                hash = (hash * 397) ^ obj.SliceIndex;
                hash = (hash * 397) ^ obj.HasNormals.GetHashCode();
                hash = (hash * 397) ^ obj.HasUvs.GetHashCode();
                return hash;
            }
        }

        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress)
        {
            var esmPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.esm");
            var bsaPath = Path.Combine(config.InstallPath, "Data Files", "Morrowind.bsa");
            if (!File.Exists(esmPath) || !File.Exists(bsaPath))
            {
                progress.Error = "Morrowind.esm or Morrowind.bsa missing under the configured install path.";
                progress.Done = true;
                yield break;
            }

            CachePaths.EnsureExists();
            ClearDirectory(CachePaths.CellsDir);

            BsaArchive bsa = null;
            RecordIndex recordIndex = null;
            List<CellHeader> exteriorCells = null;
            Dictionary<(int, int), long> landOffsets = null;

            progress.Stage = "ESM";
            progress.Label = "Opening archives";
            progress.Current = 0;
            progress.Total = 5;
            yield return null;

            try
            {
                bsa = BsaArchive.Open(bsaPath);
            }
            catch (Exception ex)
            {
                progress.Error = $"Failed to open BSA: {ex.Message}";
                progress.Done = true;
                yield break;
            }

            progress.Label = "Building record index";
            progress.Current = 1;
            yield return null;
            using (var esm = new EsmReader(esmPath))
                recordIndex = RecordIndex.Build(esm);

            progress.Label = "Enumerating cells";
            progress.Current = 2;
            yield return null;
            exteriorCells = new List<CellHeader>(2048);
            using (var esm = new EsmReader(esmPath))
            {
                foreach (var c in CellIndex.Enumerate(esm))
                    if (!c.IsInterior) exteriorCells.Add(c);
            }

            progress.Label = "Indexing terrain";
            progress.Current = 3;
            yield return null;
            using (var esm = new EsmReader(esmPath))
                landOffsets = LandIndex.BuildOffsetMap(esm);

            progress.Label = "Indexing land textures";
            progress.Current = 4;
            yield return null;
            Dictionary<int, string> ltexMap;
            using (var esm = new EsmReader(esmPath))
                ltexMap = LtexIndex.Build(esm);

            var bakeryMeshes = new MeshBakery();
            var bakeryMaterials = new MaterialBakery();
            var textureResolver = new TexturePathResolver(bsa);
            var bakeryTextures = new TextureBakery(bsa, textureResolver);
            int defaultTexIdx = bakeryTextures.AddOrGet(LtexIndex.DefaultTexturePath);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);

            var bsaByName = new Dictionary<string, BsaEntry>(bsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var e in bsa.Entries) bsaByName[e.Name] = e;

            var modelCache = new Dictionary<string, PendingModel>(StringComparer.OrdinalIgnoreCase);
            var failedNifs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            NifMeshBuilder.DebugMeshPath = "Ex_De_Docks_Center";

            progress.Stage = "Cells";
            progress.Label = "";
            progress.Current = 0;
            progress.Total = exteriorCells.Count;
            yield return null;

            using (bsa)
            using (var esmForRefs = new EsmReader(esmPath))
            using (var esmForLand = new EsmReader(esmPath))
            {
                for (int ci = 0; ci < exteriorCells.Count; ci++)
                {
                    var cell = exteriorCells[ci];
                    progress.Current = ci + 1;
                    progress.Label = $"({cell.GridX},{cell.GridY})";

                    List<CellReference> refs;
                    try
                    {
                        refs = CellReader.ReadReferences(esmForRefs, cell);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[VVardenfell] cell {cell.GridX},{cell.GridY} refs failed: {ex.Message}");
                        refs = new List<CellReference>();
                    }

                    LandRecord land = null;
                    if (landOffsets.TryGetValue((cell.GridX, cell.GridY), out var landOff))
                    {
                        try
                        {
                            land = LandIndex.ReadAt(esmForLand, landOff);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[VVardenfell] cell {cell.GridX},{cell.GridY} land failed: {ex.Message}");
                        }
                    }

                    ushort[] layerGrid = null;
                    if (land != null && land.VtexIndices != null)
                    {
                        var vtexToLayer = new Dictionary<ushort, ushort>(8);
                        layerGrid = bakeryLayers.BuildCellGrid(
                            land.VtexIndices, ltexMap, bakeryTextures, vtexToLayer);
                    }

                    var pendingRefs = new List<PendingRef>(refs.Count);
                    foreach (var r in refs)
                    {
                        if (r.Deleted) continue;
                        var model = EnsureModel(r.BaseId, recordIndex, bsaByName, bsa, bakeryMeshes, bakeryMaterials, bakeryTextures, modelCache, failedNifs);
                        if (model == null) continue;

                        CellBakery.ToUnityTransform(r, out var pos, out var rot);
                        for (int i = 0; i < model.Value.Mesh.Length; i++)
                        {
                            pendingRefs.Add(new PendingRef(
                                model.Value.Built[i],
                                model.Value.Mesh[i],
                                model.Value.Mat[i],
                                model.Value.Slice[i],
                                pos,
                                rot,
                                r.Scale));
                        }
                    }

                    var bakedRefs = BuildCellBatches(cell.GridX, cell.GridY, pendingRefs, bakeryMeshes);

                    CellBakery.Write(
                        CachePaths.CellFile(cell.GridX, cell.GridY),
                        cell.GridX,
                        cell.GridY,
                        land,
                        layerGrid,
                        bakedRefs);

                    if ((ci & 7) == 0) yield return null;
                }
            }

            progress.Stage = "Writing";
            progress.Total = 5;

            progress.Label = "meshes.bin";
            progress.Current = 1;
            yield return null;
            bakeryMeshes.WriteTo(CachePaths.Meshes);

            progress.Label = "materials.bin";
            progress.Current = 2;
            yield return null;
            bakeryMaterials.WriteTo(CachePaths.Materials);
            bakeryMeshes.WriteNames(CachePaths.MeshNames);

            progress.Label = "textures.bin";
            progress.Current = 3;
            yield return null;
            bakeryTextures.WriteIndex(CachePaths.TexturesIndex);

            progress.Label = "terrain_layers.bin";
            progress.Current = 4;
            yield return null;
            bakeryLayers.WriteTo(CachePaths.TerrainLayers);

            progress.Label = "manifest.bin";
            progress.Current = 5;
            yield return null;
            var manifest = BakeManifest.FromCurrentSources(esmPath, bsaPath);
            manifest.MeshCount = bakeryMeshes.Count;
            manifest.MaterialCount = bakeryMaterials.Count;
            manifest.TextureCount = bakeryTextures.Count;
            manifest.CellCount = exteriorCells.Count;
            manifest.CellGrid = new (int, int)[exteriorCells.Count];
            for (int i = 0; i < exteriorCells.Count; i++)
                manifest.CellGrid[i] = (exteriorCells[i].GridX, exteriorCells[i].GridY);
            manifest.Write(CachePaths.Manifest);

            progress.Stage = "Done";
            progress.Label = $"{exteriorCells.Count} cells, {bakeryMeshes.Count} meshes, {bakeryMaterials.Count} mats, {bakeryTextures.Count} textures, {bakeryLayers.Count} terrain layers";
            progress.Done = true;
        }

        private static PendingModel? EnsureModel(
            string baseId,
            RecordIndex recordIndex,
            Dictionary<string, BsaEntry> bsaByName,
            BsaArchive bsa,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            Dictionary<string, PendingModel> cache,
            HashSet<string> failed)
        {
            if (!recordIndex.TryGet(baseId, out var rec) || string.IsNullOrEmpty(rec.Model)) return null;
            string nifPath = "meshes\\" + rec.Model;
            if (cache.TryGetValue(nifPath, out var hit)) return hit;
            if (failed.Contains(nifPath)) return null;

            if (!bsaByName.TryGetValue(nifPath, out var entry))
            {
                failed.Add(nifPath);
                return null;
            }

            NifFile nif;
            try
            {
                nif = NifFile.Parse(nifPath, bsa.Read(entry));
            }
            catch (Exception ex)
            {
                failed.Add(nifPath);
                Debug.LogWarning($"[VVardenfell] NIF {nifPath} failed: {ex.Message}");
                return null;
            }

            var built = NifMeshBuilder.Build(nif);
            if (built.Count == 0)
            {
                failed.Add(nifPath);
                return null;
            }

            var builtArray = built.ToArray();
            var meshIdx = new int[builtArray.Length];
            var matIdx = new int[builtArray.Length];
            var sliceIdx = new int[builtArray.Length];

            for (int i = 0; i < builtArray.Length; i++)
            {
                meshIdx[i] = meshes.AddOrGet(nifPath, i, builtArray[i]);
                sliceIdx[i] = textures.AddOrGet(builtArray[i].TexturePath);

                ushort apFlags = builtArray[i].AlphaFlags;
                uint matFlags = 0;
                if ((apFlags & 0x0001) != 0) matFlags |= CacheFormat.MatFlagAlphaBlend;
                if ((apFlags & 0x0200) != 0) matFlags |= CacheFormat.MatFlagAlphaClip;
                matFlags = CacheFormat.PackAlphaThreshold(matFlags, builtArray[i].AlphaThreshold);

                matIdx[i] = materials.AddOrGet(matFlags);
            }

            var model = new PendingModel(builtArray, meshIdx, matIdx, sliceIdx);
            cache[nifPath] = model;
            return model;
        }

        private static List<CellBakery.BakedRef> BuildCellBatches(int gridX, int gridY, List<PendingRef> pendingRefs, MeshBakery meshes)
        {
            var result = new List<CellBakery.BakedRef>(pendingRefs.Count);
            if (pendingRefs.Count == 0)
                return result;

            var groups = new Dictionary<BatchKey, List<PendingRef>>(new BatchKeyComparer());
            for (int i = 0; i < pendingRefs.Count; i++)
            {
                var pending = pendingRefs[i];
                var mesh = pending.Built.Mesh;
                bool hasNormals = mesh.normals != null && mesh.normals.Length == mesh.vertexCount;
                bool hasUvs = mesh.uv != null && mesh.uv.Length == mesh.vertexCount;
                var key = new BatchKey(pending.MaterialIndex, pending.SliceIndex, hasNormals, hasUvs);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<PendingRef>();
                list.Add(pending);
            }

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var cellOrigin = new Vector3(gridX * cellMeters, 0f, gridY * cellMeters);

            int batchIndex = 0;
            foreach (var kv in groups)
            {
                var group = kv.Value;
                if (group.Count == 1)
                {
                    var pending = group[0];
                    result.Add(new CellBakery.BakedRef(
                        pending.MeshIndex,
                        pending.MaterialIndex,
                        pending.SliceIndex,
                        pending.Position,
                        pending.Rotation,
                        pending.Scale));
                    continue;
                }

                var combinedMesh = CombineGroupMesh(group, cellOrigin, $"CellBatch({gridX},{gridY})[{batchIndex}]");
                var combinedBuilt = new NifMeshBuilder.BuiltMesh(
                    combinedMesh,
                    group[0].Built.TexturePath,
                    combinedMesh.name,
                    combinedMesh.bounds,
                    group[0].Built.AlphaFlags,
                    group[0].Built.AlphaThreshold);
                int combinedMeshIndex = meshes.AddOrGet($"cellbatch\\{gridX}_{gridY}", batchIndex, combinedBuilt);

                result.Add(new CellBakery.BakedRef(
                    combinedMeshIndex,
                    kv.Key.MaterialIndex,
                    kv.Key.SliceIndex,
                    cellOrigin,
                    Quaternion.identity,
                    1f));
                batchIndex++;
            }

            return result;
        }

        private static Mesh CombineGroupMesh(List<PendingRef> group, Vector3 cellOrigin, string name)
        {
            int totalVerts = 0;
            int totalIndices = 0;
            bool hasNormals = true;
            bool hasUvs = true;

            for (int i = 0; i < group.Count; i++)
            {
                var mesh = group[i].Built.Mesh;
                totalVerts += mesh.vertexCount;
                totalIndices += mesh.triangles.Length;
                hasNormals &= mesh.normals != null && mesh.normals.Length == mesh.vertexCount;
                hasUvs &= mesh.uv != null && mesh.uv.Length == mesh.vertexCount;
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
                var mesh = pending.Built.Mesh;
                var srcVerts = mesh.vertices;
                var srcNormals = mesh.normals;
                var srcUvs = mesh.uv;
                var srcIndices = mesh.triangles;

                for (int v = 0; v < srcVerts.Length; v++)
                {
                    var transformed = pending.Position + pending.Rotation * (srcVerts[v] * pending.Scale) - cellOrigin;
                    verts[vertexOffset + v] = transformed;

                    if (transformed.x < min.x) min.x = transformed.x;
                    if (transformed.y < min.y) min.y = transformed.y;
                    if (transformed.z < min.z) min.z = transformed.z;
                    if (transformed.x > max.x) max.x = transformed.x;
                    if (transformed.y > max.y) max.y = transformed.y;
                    if (transformed.z > max.z) max.z = transformed.z;

                    if (normals != null)
                        normals[vertexOffset + v] = pending.Rotation * srcNormals[v];
                    if (uvs != null)
                        uvs[vertexOffset + v] = srcUvs[v];
                }

                for (int t = 0; t < srcIndices.Length; t++)
                    indices[indexOffset + t] = srcIndices[t] + vertexOffset;

                vertexOffset += srcVerts.Length;
                indexOffset += srcIndices.Length;
            }

            var combined = new Mesh { name = name };
            combined.indexFormat = totalVerts > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            combined.SetVertices(verts);
            if (normals != null) combined.SetNormals(normals);
            if (uvs != null) combined.SetUVs(0, uvs);
            combined.SetTriangles(indices, 0);
            if (normals == null) combined.RecalculateNormals();
            combined.bounds = new Bounds((min + max) * 0.5f, max - min);
            return combined;
        }

        private static void ClearDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
        }
    }
}
