using System.IO;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeWorldCellBlobFile
    {
        const uint Magic = 0x42435756u; // 'VWCB'

        public static void Write(string path, BlobAssetReference<RuntimeWorldCellBlob> blob)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(CacheFormat.FormatVersion);
            w.Write(CacheFormat.WorldBakePipelineVersion);
            BlobStreamIO.WriteLengthPrefixed(w, blob, CacheFormat.RuntimeWorldCellBlobVersion);
        }

        public static BlobAssetReference<RuntimeWorldCellBlob> Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad runtime world-cell blob magic 0x{magic:X8} in '{path}'.");

            uint formatVersion = r.ReadUInt32();
            if (formatVersion != CacheFormat.FormatVersion)
                throw new InvalidDataException($"Unsupported runtime world-cell blob format version {formatVersion} in '{path}'. Rebake required.");

            uint pipelineVersion = r.ReadUInt32();
            if (pipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"Unsupported runtime world-cell blob pipeline version {pipelineVersion} in '{path}'. Rebake required.");

            BlobAssetReference<RuntimeWorldCellBlob> blob = BlobStreamIO.ReadLengthPrefixed<RuntimeWorldCellBlob>(
                r,
                CacheFormat.RuntimeWorldCellBlobVersion,
                "runtime world-cell blob");
            if (!blob.IsCreated)
                throw new InvalidDataException($"Runtime world-cell blob payload is missing in '{path}'. Rebake required.");

            return blob;
        }
    }
}
