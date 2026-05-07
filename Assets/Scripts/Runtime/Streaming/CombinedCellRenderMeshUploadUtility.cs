using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal static class CombinedCellRenderMeshUploadUtility
    {
        public static Mesh Upload(CombinedCellRenderChunkDef chunk, string name)
        {
            if (chunk == null)
                throw new InvalidDataException("Combined render chunk is null.");
            if ((chunk.MeshFlags & CacheFormat.MeshFlagHasNormals) == 0 || (chunk.MeshFlags & CacheFormat.MeshFlagHasUVs) == 0)
                throw new InvalidDataException($"Combined render chunk '{name}' is missing required normals or UVs.");
            if ((chunk.MeshFlags & CacheFormat.MeshFlagHasTextureSelector) == 0)
                throw new InvalidDataException($"Combined render chunk '{name}' is missing required texture selectors.");
            if ((chunk.MeshFlags & CacheFormat.MeshFlagHasAlphaCutoff) == 0)
                throw new InvalidDataException($"Combined render chunk '{name}' is missing required alpha cutoffs.");
            if ((chunk.MeshFlags & CacheFormat.MeshFlagIndex32) != 0)
                throw new InvalidDataException($"Combined render chunk '{name}' uses unsupported 32-bit indices.");

            const int vertexStride = sizeof(float) * 10;
            int expectedVertexBytes = checked(chunk.VertexCount * vertexStride);
            int expectedIndexBytes = checked(chunk.IndexCount * sizeof(ushort));
            if (chunk.VertexBytes == null || chunk.VertexBytes.Length != expectedVertexBytes)
                throw new InvalidDataException($"Combined render chunk '{name}' vertex payload size mismatch.");
            if (chunk.IndexBytes == null || chunk.IndexBytes.Length != expectedIndexBytes)
                throw new InvalidDataException($"Combined render chunk '{name}' index payload size mismatch.");

            byte[] runtimeVertexBytes = BuildRuntimeVertexBytes(chunk, name);
            var mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt16,
            };

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(
                    chunk.VertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0));
                meshData.SetIndexBufferParams(chunk.IndexCount, IndexFormat.UInt16);

                var vertexData = meshData.GetVertexData<byte>(0);
                var indexData = meshData.GetIndexData<byte>();
                NativeArray<byte>.Copy(runtimeVertexBytes, 0, vertexData, 0, runtimeVertexBytes.Length);
                NativeArray<byte>.Copy(chunk.IndexBytes, 0, indexData, 0, chunk.IndexBytes.Length);

                meshData.subMeshCount = 1;
                meshData.SetSubMesh(
                    0,
                    new SubMeshDescriptor(0, chunk.IndexCount, MeshTopology.Triangles),
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                Mesh.ApplyAndDisposeWritableMeshData(
                    meshDataArray,
                    mesh,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                mesh.bounds = new Bounds(
                    new Vector3(chunk.BoundsCenterX, chunk.BoundsCenterY, chunk.BoundsCenterZ),
                    new Vector3(chunk.BoundsExtentsX, chunk.BoundsExtentsY, chunk.BoundsExtentsZ) * 2f);
                mesh.UploadMeshData(true);
                return mesh;
            }
            catch
            {
                meshDataArray.Dispose();
                Object.Destroy(mesh);
                throw;
            }
        }

        static byte[] BuildRuntimeVertexBytes(CombinedCellRenderChunkDef chunk, string name)
        {
            if (WorldResources.RefBucketIndexByKey == null
                || !WorldResources.RefBucketIndexByKey.TryGetValue(chunk.TextureBucketKey, out int expectedBucketIndex))
            {
                throw new InvalidDataException($"Combined render chunk '{name}' references missing texture bucket key 0x{chunk.TextureBucketKey:X8}.");
            }

            var uniqueTextureIndices = new HashSet<int>();
            var runtimeVertexBytes = new byte[chunk.VertexBytes.Length];
            using var input = new MemoryStream(chunk.VertexBytes, writable: false);
            using var reader = new BinaryReader(input);
            using var output = new MemoryStream(runtimeVertexBytes, writable: true);
            using var writer = new BinaryWriter(output);
            for (int i = 0; i < chunk.VertexCount; i++)
            {
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());
                writer.Write(reader.ReadSingle());

                float selector = reader.ReadSingle();
                float alphaCutoff = reader.ReadSingle();
                if (alphaCutoff < -1f || alphaCutoff > 1f)
                    throw new InvalidDataException($"Combined render chunk '{name}' has out-of-range alpha cutoff {alphaCutoff}.");

                int textureIndex = ReadTextureSelector(selector, name);
                uniqueTextureIndices.Add(textureIndex);
                int slice = ResolveTextureSlice(textureIndex, expectedBucketIndex, name);
                writer.Write((float)slice);
                writer.Write(alphaCutoff);
            }

            return runtimeVertexBytes;
        }

        static int ReadTextureSelector(float selector, string name)
        {
            if (selector < int.MinValue || selector > int.MaxValue)
                throw new InvalidDataException($"Combined render chunk '{name}' has out-of-range texture selector {selector}.");

            int textureIndex = (int)selector;
            if (selector != textureIndex)
                throw new InvalidDataException($"Combined render chunk '{name}' has non-integral texture selector {selector}.");

            return textureIndex;
        }

        static int ResolveTextureSlice(int textureIndex, int expectedBucketIndex, string name)
        {
            int2 bucketSlice;
            if (textureIndex < 0)
            {
                bucketSlice = WorldResources.FallbackBucketSlice;
            }
            else
            {
                if (!WorldResources.TexBucketInfo.IsCreated)
                    throw new InvalidDataException($"Combined render chunk '{name}' cannot resolve texture selectors before texture buckets are loaded.");
                if ((uint)textureIndex >= (uint)WorldResources.TexBucketInfo.Length)
                    throw new InvalidDataException($"Combined render chunk '{name}' references missing texture index {textureIndex}.");

                bucketSlice = WorldResources.TexBucketInfo[textureIndex];
            }

            if (bucketSlice.x != expectedBucketIndex)
            {
                throw new InvalidDataException(
                    $"Combined render chunk '{name}' texture index {textureIndex} resolves to bucket {bucketSlice.x}, expected {expectedBucketIndex}.");
            }
            if (bucketSlice.y < 0)
                throw new InvalidDataException($"Combined render chunk '{name}' texture index {textureIndex} resolves to invalid slice {bucketSlice.y}.");

            return bucketSlice.y;
        }
    }
}
