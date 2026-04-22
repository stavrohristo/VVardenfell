using System;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using BinaryReader = System.IO.BinaryReader;

namespace VVardenfell.Core.Cache
{
    /// <summary>
    /// Thin adapters that let Unity Entities' official blob-serialization APIs consume and
    /// produce plain <see cref="System.IO.Stream"/>s. The outer cache container stays
    /// project-defined (<c>u32 blobLen; byte[blobLen]</c> per collider blob), but the bytes
    /// inside each chunk are always Unity's official <c>version + blob payload</c> encoding.
    /// Used by the importer to pack pre-built Unity Physics collider blobs into our
    /// <c>collisions.bin</c> and per-cell files, and by the runtime loader to read them back.
    ///
    /// Writing a pre-built blob plus reading it at runtime is effectively one <c>Malloc</c>
    /// plus one <c>memcpy</c>; no BVH rebuild. That's the whole point of this pipeline.
    /// </summary>
    public static class BlobStreamIO
    {
        /// <summary>
        /// Serialize <paramref name="blob"/> to a byte array suitable for writing into a
        /// larger binary stream. Does not dispose the blob; caller owns it.
        /// </summary>
        public static unsafe byte[] SerializeBlob<T>(BlobAssetReference<T> blob, int version)
            where T : unmanaged
        {
            using (var w = new MemoryBinaryWriter())
            {
                w.Write(version);
                BlobAssetSerializeExtensions.Write(w, blob);
                var result = new byte[w.Length];
                fixed (byte* dst = result)
                {
                    UnsafeUtility.MemCpy(dst, w.Data, w.Length);
                }
                return result;
            }
        }

        /// <summary>
        /// Deserialize a blob previously written with <see cref="SerializeBlob{T}"/>.
        /// Returns <c>false</c> only when the embedded blob version does not match
        /// <paramref name="version"/>. Structural corruption still throws.
        /// </summary>
        public static bool TryDeserializeBlob<T>(byte[] data, int version, out BlobAssetReference<T> blob)
            where T : unmanaged
        {
            using var ms = new MemoryStream(data, writable: false);
            using var r = new StreamReaderAdapter(ms);
            return BlobAssetReference<T>.TryRead(r, version, out blob);
        }

        /// <summary>
        /// Read a length-prefixed blob chunk from <paramref name="r"/>.
        /// Zero length means "no collider"; any other invalid payload throws.
        /// </summary>
        public static BlobAssetReference<T> ReadLengthPrefixed<T>(BinaryReader r, int version)
            where T : unmanaged
            => ReadLengthPrefixed<T>(r, version, context: null);

        /// <summary>
        /// Read a length-prefixed blob chunk from <paramref name="r"/> with optional context.
        /// Zero length means "no collider"; any other invalid payload throws.
        /// </summary>
        public static BlobAssetReference<T> ReadLengthPrefixed<T>(BinaryReader r, int version, string context)
            where T : unmanaged
        {
            string contextPrefix = string.IsNullOrWhiteSpace(context) ? "blob" : context;
            long lengthPrefixPosition = SafePosition(r);

            int len;
            try
            {
                len = r.ReadInt32();
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException(
                    $"Truncated {contextPrefix} length prefix at offset {lengthPrefixPosition}.", ex);
            }

            if (len == 0) return default;
            if (len < 0)
                throw new InvalidDataException(
                    $"Negative {contextPrefix} length {len} at offset {lengthPrefixPosition}.");

            byte[] bytes = r.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidDataException(
                    $"Truncated {contextPrefix} payload at offset {lengthPrefixPosition}: expected {len} bytes, got {bytes.Length}.");

            try
            {
                if (!TryDeserializeBlob<T>(bytes, version, out var blob))
                    throw new InvalidDataException(
                        $"{contextPrefix} version mismatch. Expected {version}.");
                return blob;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Failed to deserialize {contextPrefix} payload of {len} bytes.", ex);
            }
        }

        /// <summary>Write a length-prefixed blob chunk to <paramref name="w"/>.</summary>
        public static void WriteLengthPrefixed<T>(System.IO.BinaryWriter w,
            BlobAssetReference<T> blob, int version) where T : unmanaged
        {
            if (!blob.IsCreated)
            {
                w.Write(0);
                return;
            }

            byte[] bytes = SerializeBlob(blob, version);
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        private sealed unsafe class StreamWriterAdapter : Unity.Entities.Serialization.BinaryWriter
        {
            private readonly Stream _s;

            public StreamWriterAdapter(Stream s) { _s = s; }

            public long Position { get => _s.Position; set => _s.Position = value; }

            public void WriteBytes(void* data, int bytes)
            {
                var span = new ReadOnlySpan<byte>(data, bytes);
                _s.Write(span);
            }

            public void Dispose() { }
        }

        private sealed unsafe class StreamReaderAdapter : Unity.Entities.Serialization.BinaryReader
        {
            private readonly Stream _s;

            public StreamReaderAdapter(Stream s) { _s = s; }

            public long Position { get => _s.Position; set => _s.Position = value; }

            public void ReadBytes(void* data, int bytes)
            {
                var span = new Span<byte>(data, bytes);
                int totalRead = 0;
                while (totalRead < bytes)
                {
                    int read = _s.Read(span[totalRead..]);
                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            $"Blob stream underflow: wanted {bytes}, got {totalRead}");
                    }

                    totalRead += read;
                }
            }

            public void Dispose() { }
        }

        private static long SafePosition(BinaryReader reader)
        {
            try
            {
                return reader?.BaseStream?.CanSeek == true ? reader.BaseStream.Position : -1L;
            }
            catch
            {
                return -1L;
            }
        }
    }
}
