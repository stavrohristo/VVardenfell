using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Bake;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Runtime.Bootstrap
{
    sealed class DirectActorPreviewAssetResolver : IDisposable
    {
        readonly string _dataFilesPath;
        readonly BsaArchive _bsa;
        readonly Dictionary<string, BsaEntry> _entries;
        readonly Dictionary<string, ModelPrefabSource> _modelCache = new(StringComparer.OrdinalIgnoreCase);
        readonly Material _previewMaterial;

        public DirectActorPreviewAssetResolver(string installPath, Material previewMaterial)
        {
            _previewMaterial = previewMaterial != null
                ? previewMaterial
                : throw new InvalidOperationException("Actor Preview Material is required.");
            _dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            string bsaPath = Path.Combine(_dataFilesPath, "Morrowind.bsa");
            if (!File.Exists(bsaPath))
                throw new FileNotFoundException("Morrowind.bsa is required for direct actor preview.", bsaPath);

            _bsa = BsaArchive.Open(bsaPath);
            _entries = new Dictionary<string, BsaEntry>(_bsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _bsa.Entries.Length; i++)
                _entries[_bsa.Entries[i].Name] = _bsa.Entries[i];
        }

        public ModelPrefabSource LoadModelSource(string modelPath)
        {
            string normalized = ActorVisualContentRules.NormalizeModelPath(modelPath);
            if (_modelCache.TryGetValue(normalized, out var cached))
                return cached;

            byte[] bytes = ReadAssetBytes(normalized);
            var nif = NifFile.Parse(normalized, bytes);
            var source = NifModelPrefabBuilder.Build(nif);
            _modelCache[normalized] = source;
            return source;
        }

        public Material GetMaterial(ActorVisualPartReference reference)
            => _previewMaterial;

        public bool HasAsset(string modelPath)
        {
            string normalized = ActorVisualContentRules.NormalizeModelPath(modelPath);
            return File.Exists(Path.Combine(_dataFilesPath, normalized))
                || _entries.ContainsKey(normalized);
        }

        byte[] ReadAssetBytes(string normalizedPath)
        {
            string fullPath = Path.Combine(_dataFilesPath, normalizedPath);
            if (File.Exists(fullPath))
                return File.ReadAllBytes(fullPath);
            if (_entries.TryGetValue(normalizedPath, out var entry))
                return _bsa.Read(entry);

            throw new FileNotFoundException($"Preview asset '{normalizedPath}' was not found in Data Files or Morrowind.bsa.");
        }

        public void Dispose()
        {
            _modelCache.Clear();
            _bsa?.Dispose();
        }
    }
}
