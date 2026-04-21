using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
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

            // --- Stage: ESM scans ---
            progress.Stage = "ESM";
            progress.Label = "Opening archives";
            progress.Current = 0; progress.Total = 5;
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

            // --- Stage: Baking cells ---
            var bakeryMeshes = new MeshBakery();
            var bakeryMaterials = new MaterialBakery();
            var textureResolver = new TexturePathResolver(bsa);
            var bakeryTextures = new TextureBakery(bsa, textureResolver);
            int defaultTexIdx = bakeryTextures.AddOrGet(LtexIndex.DefaultTexturePath);
            var bakeryLayers = new TerrainLayerBakery(defaultTexIdx);

            var bsaByName = new Dictionary<string, BsaEntry>(bsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var e in bsa.Entries) bsaByName[e.Name] = e;

            var modelCache = new Dictionary<string, (int[] mesh, int[] mat, int[] slice)>(StringComparer.OrdinalIgnoreCase);
            var failedNifs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Enable verbose dumping for one target NIF to diagnose orientation.
            Nif.NifMeshBuilder.DebugMeshPath = "Ex_De_Docks_Center";

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
                    try { refs = CellReader.ReadReferences(esmForRefs, cell); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[VVardenfell] cell {cell.GridX},{cell.GridY} refs failed: {ex.Message}");
                        refs = new List<CellReference>();
                    }

                    LandRecord land = null;
                    if (landOffsets.TryGetValue((cell.GridX, cell.GridY), out var landOff))
                    {
                        try { land = LandIndex.ReadAt(esmForLand, landOff); }
                        catch (Exception ex) { Debug.LogWarning($"[VVardenfell] cell {cell.GridX},{cell.GridY} land failed: {ex.Message}"); }
                    }

                    ushort[] layerGrid = null;
                    if (land != null && land.VtexIndices != null)
                    {
                        // Per-cell dedup: same VTEX value → same layer index, so a cell with uniform
                        // terrain only resolves one path instead of 256.
                        var vtexToLayer = new Dictionary<ushort, ushort>(8);
                        layerGrid = bakeryLayers.BuildCellGrid(
                            land.VtexIndices, ltexMap, bakeryTextures, vtexToLayer);
                    }

                    var bakedRefs = new List<CellBakery.BakedRef>(refs.Count);
                    foreach (var r in refs)
                    {
                        if (r.Deleted) continue;
                        var model = EnsureModel(r.BaseId, recordIndex, bsaByName, bsa, bakeryMeshes, bakeryMaterials, bakeryTextures, modelCache, failedNifs);
                        if (model == null) continue;
                        CellBakery.ToUnityTransform(r, out var pos, out var rot);
                        for (int i = 0; i < model.Value.mesh.Length; i++)
                        {
                            bakedRefs.Add(new CellBakery.BakedRef(
                                model.Value.mesh[i],
                                model.Value.mat[i],
                                model.Value.slice[i],
                                pos, rot, r.Scale));
                        }
                    }

                    CellBakery.Write(
                        CachePaths.CellFile(cell.GridX, cell.GridY),
                        cell.GridX, cell.GridY,
                        land, layerGrid, bakedRefs);

                    if ((ci & 7) == 0) yield return null;
                }
            }

            // --- Stage: Writing global tables ---
            progress.Stage = "Writing";
            progress.Total = 5;

            progress.Label = "meshes.bin"; progress.Current = 1; yield return null;
            bakeryMeshes.WriteTo(CachePaths.Meshes);

            progress.Label = "materials.bin"; progress.Current = 2; yield return null;
            bakeryMaterials.WriteTo(CachePaths.Materials);
            bakeryMeshes.WriteNames(CachePaths.MeshNames);

            progress.Label = "textures.bin"; progress.Current = 3; yield return null;
            bakeryTextures.WriteIndex(CachePaths.TexturesIndex);

            progress.Label = "terrain_layers.bin"; progress.Current = 4; yield return null;
            bakeryLayers.WriteTo(CachePaths.TerrainLayers);

            progress.Label = "manifest.bin"; progress.Current = 5; yield return null;
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

        private static (int[] mesh, int[] mat, int[] slice)? EnsureModel(
            string baseId,
            RecordIndex recordIndex,
            Dictionary<string, BsaEntry> bsaByName,
            BsaArchive bsa,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            Dictionary<string, (int[], int[], int[])> cache,
            HashSet<string> failed)
        {
            if (!recordIndex.TryGet(baseId, out var rec) || string.IsNullOrEmpty(rec.Model)) return null;
            string nifPath = "meshes\\" + rec.Model;
            if (cache.TryGetValue(nifPath, out var hit)) return hit;
            if (failed.Contains(nifPath)) return null;

            if (!bsaByName.TryGetValue(nifPath, out var entry)) { failed.Add(nifPath); return null; }

            NifFile nif;
            try { nif = NifFile.Parse(nifPath, bsa.Read(entry)); }
            catch (Exception ex)
            {
                failed.Add(nifPath);
                Debug.LogWarning($"[VVardenfell] NIF {nifPath} failed: {ex.Message}");
                return null;
            }
            var built = NifMeshBuilder.Build(nif);
            if (built.Count == 0) { failed.Add(nifPath); return null; }

            var meshIdx  = new int[built.Count];
            var matIdx   = new int[built.Count];
            var sliceIdx = new int[built.Count];
            for (int i = 0; i < built.Count; i++)
            {
                meshIdx[i]  = meshes.AddOrGet(nifPath, i, built[i]);
                sliceIdx[i] = textures.AddOrGet(built[i].TexturePath); // -1 → no texture; remapped at runtime

                // OpenMW property.hpp: Flag_Blending = 0x0001, Flag_Testing = 0x0200.
                ushort apFlags = built[i].AlphaFlags;
                uint matFlags = 0;
                if ((apFlags & 0x0001) != 0) matFlags |= CacheFormat.MatFlagAlphaBlend;
                if ((apFlags & 0x0200) != 0) matFlags |= CacheFormat.MatFlagAlphaClip;
                matFlags = CacheFormat.PackAlphaThreshold(matFlags, built[i].AlphaThreshold);

                matIdx[i] = materials.AddOrGet(matFlags);
            }
            var triple = (meshIdx, matIdx, sliceIdx);
            cache[nifPath] = triple;
            return triple;
        }

        private static void ClearDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
        }
    }
}
