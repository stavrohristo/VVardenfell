using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Rendering
{
    public sealed class StaticRefInstanceRenderResources : IDisposable
    {
        const int IndirectArgsCount = 5;

        static readonly int BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int InstancesId = Shader.PropertyToID("_StaticRefInstances");
        static readonly int InstanceBaseId = Shader.PropertyToID("_StaticRefInstanceBase");

        readonly struct RenderKey : IEquatable<RenderKey>, IComparable<RenderKey>
        {
            public readonly int RenderShardIndex;
            public readonly int LocalMaterialIndex;
            public readonly int LocalMeshIndex;
            public readonly int TextureSlice;

            public RenderKey(RefEntry entry)
            {
                RenderShardIndex = entry.RenderShardIndex;
                LocalMaterialIndex = entry.LocalMaterialIndex;
                LocalMeshIndex = entry.LocalMeshIndex;
                TextureSlice = ResolveTextureSlice(entry.SliceIndex);
            }

            public bool Equals(RenderKey other)
            {
                return RenderShardIndex == other.RenderShardIndex
                    && LocalMaterialIndex == other.LocalMaterialIndex
                    && LocalMeshIndex == other.LocalMeshIndex
                    && TextureSlice == other.TextureSlice;
            }

            public override bool Equals(object obj)
            {
                return obj is RenderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RenderShardIndex;
                    hash = (hash * 397) ^ LocalMaterialIndex;
                    hash = (hash * 397) ^ LocalMeshIndex;
                    hash = (hash * 397) ^ TextureSlice;
                    return hash;
                }
            }

            public int CompareTo(RenderKey other)
            {
                int result = RenderShardIndex.CompareTo(other.RenderShardIndex);
                if (result != 0)
                    return result;
                result = LocalMaterialIndex.CompareTo(other.LocalMaterialIndex);
                if (result != 0)
                    return result;
                result = LocalMeshIndex.CompareTo(other.LocalMeshIndex);
                if (result != 0)
                    return result;
                return TextureSlice.CompareTo(other.TextureSlice);
            }
        }

        sealed class Batch
        {
            public RenderKey Key;
            public Mesh Mesh;
            public Material Material;
            public int InstanceStart;
            public int InstanceCount;
            public int ArgsOffsetBytes;
        }

        readonly List<Batch> _batches = new();
        readonly MaterialPropertyBlock _properties = new();
        GraphicsBuffer _instanceBuffer;
        GraphicsBuffer _argsBuffer;
        Material[] _ownedMaterials = Array.Empty<Material>();
        Bounds _drawBounds = new(Vector3.zero, Vector3.one);
        int _instanceCount;
        int _sourceRefCount;
        int _uniqueRenderKeyCount;

        public bool IsReady => _instanceBuffer != null && _argsBuffer != null && _batches.Count > 0;
        public int InstanceCount => _instanceCount;
        public int SourceRefCount => _sourceRefCount;
        public int UniqueRenderKeyCount => _uniqueRenderKeyCount;
        public int BatchCount => _batches.Count;
        public int ExpectedForwardDrawCount => _batches.Count;

        public void Build(CacheLoader cache, RefEntry[] refs)
        {
            DisposeBuffersAndMaterials();

            if (cache == null)
                throw new InvalidOperationException("[VVardenfell][StaticRefInstanceRenderer] cache is unavailable.");
            if (refs == null)
                throw new InvalidOperationException("[VVardenfell][StaticRefInstanceRenderer] refs are unavailable.");

            Shader shader = Shader.Find("VVardenfell/MwStaticRefInstanced");
            if (shader == null)
                throw new InvalidOperationException("Missing shader 'VVardenfell/MwStaticRefInstanced'.");

            var grouped = GroupRefs(refs);
            if (grouped.Count == 0)
                throw new InvalidOperationException("[VVardenfell][StaticRefInstanceRenderer] no render-shard refs were available for instanced rendering.");

            var keys = new List<RenderKey>(grouped.Keys);
            keys.Sort();
            _sourceRefCount = refs.Length;
            _uniqueRenderKeyCount = keys.Count;

            var instances = new StaticRefInstanceGpu[refs.Length];
            var args = new uint[keys.Count * IndirectArgsCount];
            var materials = new List<Material>(keys.Count);
            int write = 0;
            _drawBounds = new Bounds(Vector3.zero, Vector3.one);
            bool hasBounds = false;

            for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                RenderKey key = keys[keyIndex];
                var entries = grouped[key];
                var batch = BuildBatch(shader, key, write, entries.Count, keyIndex * IndirectArgsCount * sizeof(uint));

                for (int i = 0; i < entries.Count; i++)
                {
                    RefEntry entry = entries[i];
                    instances[write + i] = BuildInstance(entry, key.TextureSlice);
                    Encapsulate(ref _drawBounds, ref hasBounds, entry, batch.Mesh.bounds);
                }

                int argsIndex = keyIndex * IndirectArgsCount;
                args[argsIndex + 0] = batch.Mesh.GetIndexCount(0);
                args[argsIndex + 1] = (uint)entries.Count;
                args[argsIndex + 2] = batch.Mesh.GetIndexStart(0);
                args[argsIndex + 3] = (uint)batch.Mesh.GetBaseVertex(0);
                args[argsIndex + 4] = 0;

                _batches.Add(batch);
                materials.Add(batch.Material);
                write += entries.Count;
            }

            if (_batches.Count == 0 || write == 0)
                throw new InvalidOperationException("[VVardenfell][StaticRefInstanceRenderer] no valid instanced static ref batches could be built.");

            if (write != instances.Length)
                Array.Resize(ref instances, write);

            _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instances.Length, System.Runtime.InteropServices.Marshal.SizeOf<StaticRefInstanceGpu>());
            _instanceBuffer.SetData(instances);
            _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, args.Length, sizeof(uint));
            _argsBuffer.SetData(args);
            _ownedMaterials = materials.ToArray();
            _instanceCount = write;

            Debug.Log(
                $"[VVardenfell][StaticRefInstanceRenderer] sourceRefs={_sourceRefCount}, instances={_instanceCount}, " +
                $"uniqueRenderKeys={_uniqueRenderKeyCount}, batches={_batches.Count}, expectedForwardDraws={ExpectedForwardDrawCount}.");
        }

        public void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (!IsReady || camera == null || camera.cameraType == CameraType.Preview)
                return;

            var command = CommandBufferPool.Get("VV.StaticRefInstanceRenderer");
            try
            {
                for (int i = 0; i < _batches.Count; i++)
                {
                    var batch = _batches[i];
                    if (batch.Mesh == null || batch.Material == null || batch.InstanceCount <= 0)
                        continue;

                    _properties.Clear();
                    _properties.SetBuffer(InstancesId, _instanceBuffer);
                    _properties.SetInt(InstanceBaseId, batch.InstanceStart);
                    command.DrawMeshInstancedIndirect(
                        batch.Mesh,
                        0,
                        batch.Material,
                        0,
                        _argsBuffer,
                        batch.ArgsOffsetBytes,
                        _properties);
                }

                context.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        public void Dispose()
        {
            DisposeBuffersAndMaterials();
        }

        void DisposeBuffersAndMaterials()
        {
            ReleaseBuffer(ref _instanceBuffer);
            ReleaseBuffer(ref _argsBuffer);
            for (int i = 0; i < _ownedMaterials.Length; i++)
            {
                if (_ownedMaterials[i] != null)
                    UnityEngine.Object.Destroy(_ownedMaterials[i]);
            }

            _ownedMaterials = Array.Empty<Material>();
            _batches.Clear();
            _instanceCount = 0;
            _sourceRefCount = 0;
            _uniqueRenderKeyCount = 0;
        }

        static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }

        static Dictionary<RenderKey, List<RefEntry>> GroupRefs(RefEntry[] refs)
        {
            var result = new Dictionary<RenderKey, List<RefEntry>>();
            for (int i = 0; i < refs.Length; i++)
            {
                RefEntry entry = refs[i];
                if (entry.SpawnModeRaw != (int)RefSpawnMode.RenderShard)
                {
                    throw new InvalidOperationException(
                        $"[VVardenfell][StaticRefInstanceRenderer] ref index {i} uses spawn mode {(RefSpawnMode)entry.SpawnModeRaw}; only RenderShard refs are supported.");
                }
                if (entry.RenderShardIndex < 0 || entry.LocalMeshIndex < 0 || entry.LocalMaterialIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"[VVardenfell][StaticRefInstanceRenderer] ref index {i} has invalid render key " +
                        $"(shard={entry.RenderShardIndex}, material={entry.LocalMaterialIndex}, mesh={entry.LocalMeshIndex}, slice={entry.SliceIndex}).");
                }

                var key = new RenderKey(entry);
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<RefEntry>();
                    result.Add(key, list);
                }

                list.Add(entry);
            }

            return result;
        }

        static Batch BuildBatch(Shader shader, RenderKey key, int instanceStart, int instanceCount, int argsOffsetBytes)
        {
            var rmas = WorldResources.RefsRmas;
            if (rmas == null || (uint)key.RenderShardIndex >= (uint)rmas.Length || rmas[key.RenderShardIndex] == null)
                throw new InvalidOperationException($"[VVardenfell][StaticRefInstanceRenderer] missing RenderMeshArray for shard {key.RenderShardIndex}.");

            var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(key.LocalMaterialIndex, key.LocalMeshIndex);
            Mesh mesh = rmas[key.RenderShardIndex].GetMesh(materialMeshInfo);
            var materials = rmas[key.RenderShardIndex].GetMaterials(materialMeshInfo);
            Material sourceMaterial = materials != null && materials.Count > 0 ? materials[0] : null;
            if (mesh == null || sourceMaterial == null || mesh.GetIndexCount(0) == 0)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][StaticRefInstanceRenderer] missing mesh/material for shard={key.RenderShardIndex}, " +
                    $"material={key.LocalMaterialIndex}, mesh={key.LocalMeshIndex}, slice={key.TextureSlice}.");
            }

            return new Batch
            {
                Key = key,
                Mesh = mesh,
                Material = CreateMaterial(shader, sourceMaterial),
                InstanceStart = instanceStart,
                InstanceCount = instanceCount,
                ArgsOffsetBytes = argsOffsetBytes,
            };
        }

        static Material CreateMaterial(Shader shader, Material source)
        {
            var material = new Material(shader)
            {
                name = $"VV:StaticRefInstanced[{source.name}]",
                enableInstancing = true,
                renderQueue = source.renderQueue,
                doubleSidedGI = source.doubleSidedGI,
            };

            if (source.HasProperty(BaseArrayId))
                material.SetTexture(BaseArrayId, source.GetTexture(BaseArrayId));
            CopyFloat(source, material, CutoffId);
            CopyFloat(source, material, SrcBlendId);
            CopyFloat(source, material, DstBlendId);
            CopyFloat(source, material, ZWriteId);
            CopyKeyword(source, material, "_ALPHATEST_ON");
            CopyKeyword(source, material, "_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        static void CopyFloat(Material source, Material target, int id)
        {
            if (source.HasProperty(id) && target.HasProperty(id))
                target.SetFloat(id, source.GetFloat(id));
        }

        static void CopyKeyword(Material source, Material target, string keyword)
        {
            if (source.IsKeywordEnabled(keyword))
                target.EnableKeyword(keyword);
            else
                target.DisableKeyword(keyword);
        }

        static StaticRefInstanceGpu BuildInstance(RefEntry entry, int textureSlice)
        {
            var position = new Vector3(entry.PosX, entry.PosY, entry.PosZ);
            var rotation = new Quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);
            var matrix = Matrix4x4.TRS(position, rotation, Vector3.one * entry.Scale);
            return new StaticRefInstanceGpu
            {
                Row0 = new float4(matrix.m00, matrix.m01, matrix.m02, matrix.m03),
                Row1 = new float4(matrix.m10, matrix.m11, matrix.m12, matrix.m13),
                Row2 = new float4(matrix.m20, matrix.m21, matrix.m22, matrix.m23),
                Data = new float4(textureSlice, 0f, 0f, 0f),
            };
        }

        static void Encapsulate(ref Bounds bounds, ref bool hasBounds, RefEntry entry, Bounds localMeshBounds)
        {
            var center = new Vector3(entry.PosX, entry.PosY, entry.PosZ);
            var scaledExtents = localMeshBounds.extents * Math.Max(0.0001f, entry.Scale);
            var size = new Vector3(
                Math.Max(1f, scaledExtents.x * 2f),
                Math.Max(1f, scaledExtents.y * 2f),
                Math.Max(1f, scaledExtents.z * 2f));
            var entryBounds = new Bounds(center, size);
            if (!hasBounds)
            {
                bounds = entryBounds;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(entryBounds);
        }

        static int ResolveTextureSlice(int sliceIndex)
        {
            if (sliceIndex < 0)
                return WorldResources.FallbackBucketSlice.y;
            if (!WorldResources.TexBucketInfo.IsCreated || (uint)sliceIndex >= (uint)WorldResources.TexBucketInfo.Length)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][StaticRefInstanceRenderer] ref texture index {sliceIndex} is outside TexBucketInfo.");
            }

            return WorldResources.TexBucketInfo[sliceIndex].y;
        }
    }
}
