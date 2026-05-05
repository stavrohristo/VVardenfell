using System;
using System.IO;
using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeContentBlobFile
    {
        const uint Magic = 0x42435656u; // 'VVCB'

        public static void Write(string path, GameplayContentData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using BlobAssetReference<RuntimeContentBlob> blob = RuntimeContentBlobBuilder.Build(data);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(CacheFormat.FormatVersion);
            w.Write(CacheFormat.GameplayContentVersion);
            BlobStreamIO.WriteLengthPrefixed(w, blob, CacheFormat.RuntimeContentBlobVersion);
        }

        public static BlobAssetReference<RuntimeContentBlob> Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad runtime content blob magic 0x{magic:X8} in '{path}'.");

            uint formatVersion = r.ReadUInt32();
            if (formatVersion != CacheFormat.FormatVersion)
                throw new InvalidDataException($"Unsupported runtime content blob format version {formatVersion} in '{path}'. Rebake required.");

            uint contentVersion = r.ReadUInt32();
            if (contentVersion != CacheFormat.GameplayContentVersion)
                throw new InvalidDataException($"Unsupported runtime content blob version {contentVersion} in '{path}'. Rebake required.");

            BlobAssetReference<RuntimeContentBlob> blob = BlobStreamIO.ReadLengthPrefixed<RuntimeContentBlob>(
                r,
                CacheFormat.RuntimeContentBlobVersion,
                "runtime content blob");
            if (!blob.IsCreated)
                throw new InvalidDataException($"Runtime content blob payload is missing in '{path}'. Rebake required.");

            return blob;
        }
    }
}
