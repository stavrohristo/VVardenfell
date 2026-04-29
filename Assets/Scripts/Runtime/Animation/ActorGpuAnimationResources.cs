using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VVardenfell.Runtime.Animation
{
    struct ActorGpuAnimationSkeletonGpu
    {
        public int FirstBoneIndex;
        public int BoneCount;
        public int AccumulationBoneIndex;
        public int MaxBoneDepth;
    }

    struct ActorGpuAnimationBoneGpu
    {
        public int ParentIndex;
        public uint Mask;
        public int Depth;
        public float Padding;
        public float3 BindPosition;
        public float BindScale;
        public float4 BindRotation;
        public float4 BindLocalRow0;
        public float4 BindLocalRow1;
        public float4 BindLocalRow2;
        public float4 BindRootRow0;
        public float4 BindRootRow1;
        public float4 BindRootRow2;
    }

    struct ActorGpuAnimationClipGpu
    {
        public int FirstTrackIndex;
        public int TrackCount;
        public float Duration;
        public float Padding;
    }

    struct ActorGpuAnimationTrackGpu
    {
        public int TargetBoneIndex;
        public int Kind;
        public int Interpolation;
        public int AxisOrder;
        public uint ControllerFlags;
        public float Frequency;
        public float Phase;
        public float TimeStart;
        public float TimeStop;
        public int FirstKeyIndex;
        public int KeyCount;
        public uint Mask;
    }

    struct ActorGpuAnimationClipBoneTrackRangeGpu
    {
        public int FirstTrackIndex;
        public int TrackCount;
        public int Padding0;
        public int Padding1;
    }

    struct ActorGpuAnimationKeyGpu
    {
        public float Time;
        public float3 Padding;
        public float4 Value;
        public float4 InTangent;
        public float4 OutTangent;
    }

    struct ActorGpuAnimationSkinMeshGpu
    {
        public int FirstSkinBoneIndex;
        public int SkinBoneCount;
        public int IsRigid;
        public int FirstVertexIndex;
        public float3 RigidOffset;
        public int VertexCount;
        public float4 GeometryRow0;
        public float4 GeometryRow1;
        public float4 GeometryRow2;
        public float4 GeometryRow3;
    }

    struct ActorGpuAnimationSkinBoneGpu
    {
        public int BoneIndex;
        public float3 Padding;
        public float4 BindPoseRow0;
        public float4 BindPoseRow1;
        public float4 BindPoseRow2;
        public float4 BindPoseRow3;
    }

    struct ActorGpuAnimationActorGpu
    {
        public int SkeletonIndex;
        public int FirstLayerIndex;
        public int LayerCount;
        public int LocalBoneOffset;
        public int BoneCount;
        public int BoneMatrixOffset;
        public int BoneMatrixCount;
        public int FirstSkinMeshWorkIndex;
        public int SkinMeshWorkCount;
        public int DeformedVertexOffset;
        public int DeformedVertexCount;
        public int Padding;
    }

    struct ActorGpuAnimationLayerGpu
    {
        public int ClipIndex;
        public float Time;
        public float Weight;
        public int Priority;
        public uint Mask;
        public uint HasPreviousLayer;
        public uint Padding0;
        public uint Padding1;
    }

    struct ActorGpuAnimationSkinMeshWorkGpu
    {
        public int ActorIndex;
        public int SkinMeshIndex;
        public int AttachBoneIndex;
        public int RigidMirrorX;
        public int BoneMatrixOffset;
        public int DeformedVertexOffset;
        public int Padding1;
        public int Padding;
    }

    struct ActorGpuDeformedVertexGpu
    {
        public float3 Position;
        public float Padding0;
        public float3 Normal;
        public float Padding1;
    }

    struct ActorGpuAnimationFrameUpload
    {
        public NativeArray<ActorGpuAnimationActorGpu> Actors;
        public NativeArray<ActorGpuAnimationLayerGpu> Layers;
        public NativeArray<ActorGpuAnimationSkinMeshWorkGpu> SkinMeshes;
        public int ActorCount;
        public int LayerCount;
        public int SkinMeshCount;
    }

    struct ActorGpuAnimationFrameResources
    {
        public GraphicsBuffer ActorBuffer;
        public GraphicsBuffer LayerBuffer;
        public GraphicsBuffer LocalBoneMatrixBuffer;
        public GraphicsBuffer LocalToRootMatrixBuffer;
        public GraphicsBuffer SkinMeshWorkBuffer;
        public GraphicsBuffer BoneMatrixBuffer;
        public GraphicsBuffer DeformedVertexBuffer;
    }

    public struct ActorGpuAnimationPreparedFrame
    {
        internal ulong Version;
        internal int ActorCount;
        internal int LayerCount;
        internal int LocalBoneMatrixCount;
        internal int SkinMeshWorkCount;
        internal int BoneMatrixCount;
        internal int DeformedVertexCount;
        internal GraphicsBuffer ActorBuffer;
        internal GraphicsBuffer LayerBuffer;
        internal GraphicsBuffer LocalBoneMatrixBuffer;
        internal GraphicsBuffer LocalToRootMatrixBuffer;
        internal GraphicsBuffer SkinMeshWorkBuffer;
        internal GraphicsBuffer BoneMatrixBuffer;
        internal GraphicsBuffer DeformedVertexBuffer;

        public bool IsValid => Version != 0UL
                               && ActorCount > 0
                               && LocalBoneMatrixCount > 0
                               && SkinMeshWorkCount > 0
                               && BoneMatrixCount > 0
                               && DeformedVertexCount > 0
                               && ActorBuffer != null
                               && LayerBuffer != null
                               && LocalBoneMatrixBuffer != null
                               && LocalToRootMatrixBuffer != null
                               && SkinMeshWorkBuffer != null
                               && BoneMatrixBuffer != null
                               && DeformedVertexBuffer != null;
    }

    public sealed class ActorGpuAnimationResources : IDisposable
    {
        const string ComputeShaderPath = "ActorGpuAnimation";
        const string SampleLocalBonesKernelName = "SampleLocalBones";
        const string ComposeLocalToRootKernelName = "ComposeLocalToRoot";
        const string BuildSkinMatricesKernelName = "BuildSkinMatrices";
        const string BuildDeformedVerticesKernelName = "BuildDeformedVertices";
        const int BoneThreadsPerGroup = 128;
        const int SkinMatrixThreadsPerGroup = 128;
        const int MaxComputeGroupsPerDimension = 65535;
        const int FrameResourceCount = 3;
        const ulong StaticUploadVersion = 6UL;
        const GraphicsBuffer.UsageFlags DynamicBufferUsage = GraphicsBuffer.UsageFlags.LockBufferForWrite;

        static readonly int k_ActorCountId = Shader.PropertyToID("_GpuActorCount");
        static readonly int k_SkinMeshWorkCountId = Shader.PropertyToID("_GpuSkinMeshWorkCount");
        static readonly int k_ActorDispatchBaseId = Shader.PropertyToID("_GpuActorDispatchBase");
        static readonly int k_SkinMeshDispatchBaseId = Shader.PropertyToID("_GpuSkinMeshDispatchBase");
        static readonly int k_SkeletonsId = Shader.PropertyToID("_GpuAnimationSkeletons");
        static readonly int k_BonesId = Shader.PropertyToID("_GpuAnimationBones");
        static readonly int k_ClipsId = Shader.PropertyToID("_GpuAnimationClips");
        static readonly int k_TracksId = Shader.PropertyToID("_GpuAnimationTracks");
        static readonly int k_ClipBoneTrackRangesId = Shader.PropertyToID("_GpuAnimationClipBoneTrackRanges");
        static readonly int k_KeysId = Shader.PropertyToID("_GpuAnimationKeys");
        static readonly int k_SkinMeshesId = Shader.PropertyToID("_GpuAnimationSkinMeshes");
        static readonly int k_SkinBonesId = Shader.PropertyToID("_GpuAnimationSkinBones");
        static readonly int k_SkinVerticesId = Shader.PropertyToID("_GpuAnimationSkinVertices");
        static readonly int k_ActorsId = Shader.PropertyToID("_GpuAnimationActors");
        static readonly int k_LayersId = Shader.PropertyToID("_GpuAnimationLayers");
        static readonly int k_LocalMatricesId = Shader.PropertyToID("_GpuAnimationLocalBoneMatrices");
        static readonly int k_LocalToRootMatricesId = Shader.PropertyToID("_GpuAnimationLocalToRootMatrices");
        static readonly int k_SkinMeshWorkId = Shader.PropertyToID("_GpuAnimationSkinMeshWork");
        static readonly int k_OutputMatricesId = Shader.PropertyToID("_GpuActorBoneMatrices");
        static readonly int k_DeformedVerticesId = Shader.PropertyToID("_ActorDeformedVertices");
        static readonly int k_SkeletonCountId = Shader.PropertyToID("_GpuSkeletonCount");
        static readonly int k_BoneCountId = Shader.PropertyToID("_GpuBoneCount");
        static readonly int k_ClipCountId = Shader.PropertyToID("_GpuClipCount");
        static readonly int k_TrackCountId = Shader.PropertyToID("_GpuTrackCount");
        static readonly int k_KeyCountId = Shader.PropertyToID("_GpuKeyCount");
        static readonly int k_SkinMeshCountId = Shader.PropertyToID("_GpuSkinMeshCount");
        static readonly int k_SkinBoneCountId = Shader.PropertyToID("_GpuSkinBoneCount");
        static readonly int k_SkinVertexCountId = Shader.PropertyToID("_GpuSkinVertexCount");
        static readonly int k_LayerCountId = Shader.PropertyToID("_GpuLayerCount");
        static readonly int k_LocalBoneMatrixCountId = Shader.PropertyToID("_GpuLocalBoneMatrixCount");
        static readonly int k_ActorBoneMatrixCountId = Shader.PropertyToID("_GpuActorBoneMatrixCount");
        static readonly int k_DeformedVertexCountId = Shader.PropertyToID("_GpuDeformedVertexCount");

        ComputeShader _computeShader;
        int _sampleLocalBonesKernelIndex = -1;
        int _composeLocalToRootKernelIndex = -1;
        int _buildSkinMatricesKernelIndex = -1;
        int _buildDeformedVerticesKernelIndex = -1;
        ulong _catalogSignature;

        GraphicsBuffer _skeletonBuffer;
        GraphicsBuffer _boneBuffer;
        GraphicsBuffer _clipBuffer;
        GraphicsBuffer _trackBuffer;
        GraphicsBuffer _clipBoneTrackRangeBuffer;
        GraphicsBuffer _keyBuffer;
        GraphicsBuffer _skinMeshBuffer;
        GraphicsBuffer _skinBoneBuffer;
        GraphicsBuffer _skinVertexBuffer;
        readonly ActorGpuAnimationFrameResources[] _frames = new ActorGpuAnimationFrameResources[FrameResourceCount];
        int _frameIndex = -1;
        ActorGpuAnimationPreparedFrame _preparedFrame;
        ulong _preparedFrameVersion;
        ulong _lastRecordedFrameVersion;
        int _allocatedBoneMatrixCount;
        int _allocatedDeformedVertexCount;
        int _skeletonCount;
        int _boneCount;
        int _clipCount;
        int _trackCount;
        int _keyCount;
        int _skinMeshCount;
        int _skinBoneCount;
        int _skinVertexCount;

        public ActorGpuAnimationResources()
        {
            _computeShader = Resources.Load<ComputeShader>(ComputeShaderPath);
            if (_computeShader != null)
            {
                _sampleLocalBonesKernelIndex = _computeShader.FindKernel(SampleLocalBonesKernelName);
                _composeLocalToRootKernelIndex = _computeShader.FindKernel(ComposeLocalToRootKernelName);
                _buildSkinMatricesKernelIndex = _computeShader.FindKernel(BuildSkinMatricesKernelName);
                _buildDeformedVerticesKernelIndex = _computeShader.FindKernel(BuildDeformedVerticesKernelName);
            }
        }

        public bool IsSupported => SystemInfo.supportsComputeShaders
                                   && _computeShader != null
                                   && _sampleLocalBonesKernelIndex >= 0
                                   && _composeLocalToRootKernelIndex >= 0
                                   && _buildSkinMatricesKernelIndex >= 0
                                   && _buildDeformedVerticesKernelIndex >= 0;
        public GraphicsBuffer BoneMatrixBuffer => CurrentFrame.BoneMatrixBuffer;
        public int BoneMatrixCount { get; private set; }
        public int AllocatedBoneMatrixCount => _allocatedBoneMatrixCount;
        public int AllocatedDeformedVertexCount => _allocatedDeformedVertexCount;
        public bool HasPreparedFrame => _preparedFrame.IsValid;

        public void AllocateActorRanges(int boneMatrixCount, int deformedVertexCount, out int boneMatrixOffset, out int deformedVertexOffset)
        {
            boneMatrixOffset = _allocatedBoneMatrixCount;
            deformedVertexOffset = _allocatedDeformedVertexCount;
            _allocatedBoneMatrixCount += math.max(0, boneMatrixCount);
            _allocatedDeformedVertexCount += math.max(0, deformedVertexCount);
        }

        public void EnsureStaticResources(ref ActorAnimationCatalogBlob catalog)
        {
            if (!IsSupported)
                return;

            ulong signature = BuildCatalogSignature(ref catalog);
            if (signature == _catalogSignature
                && _skeletonBuffer != null
                && _boneBuffer != null
                && _clipBuffer != null
                && _trackBuffer != null
                && _clipBoneTrackRangeBuffer != null
                && _keyBuffer != null
                && _skinMeshBuffer != null
                && _skinBoneBuffer != null
                && _skinVertexBuffer != null)
            {
                return;
            }

            UploadStaticCatalog(ref catalog);
            _catalogSignature = signature;
        }

        public void BeginFrame()
        {
            _frameIndex = (_frameIndex + 1) % FrameResourceCount;
            _preparedFrame = default;
            BoneMatrixCount = 0;
        }

        public void ReserveFrameCapacity(int actorCount, int layerCount, int boneCount, int skinMeshWorkCount, int boneMatrixCount)
        {
            ref ActorGpuAnimationFrameResources frame = ref CurrentFrame;
            EnsureFrameBuffer(ref frame.ActorBuffer, actorCount, UnsafeUtility.SizeOf<ActorGpuAnimationActorGpu>(), "VV:GpuAnimActors", DynamicBufferUsage);
            EnsureFrameBuffer(ref frame.LayerBuffer, layerCount, UnsafeUtility.SizeOf<ActorGpuAnimationLayerGpu>(), "VV:GpuAnimLayers", DynamicBufferUsage);
            EnsureFrameBuffer(ref frame.LocalBoneMatrixBuffer, boneCount, UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimLocalBoneMatrices");
            EnsureFrameBuffer(ref frame.LocalToRootMatrixBuffer, boneCount, UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimLocalToRootMatrices");
            EnsureFrameBuffer(ref frame.SkinMeshWorkBuffer, skinMeshWorkCount, UnsafeUtility.SizeOf<ActorGpuAnimationSkinMeshWorkGpu>(), "VV:GpuAnimSkinMeshWork", DynamicBufferUsage);
            EnsureFrameBuffer(ref frame.BoneMatrixBuffer, math.max(boneMatrixCount, _allocatedBoneMatrixCount), UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimBoneMatrices");
            EnsureFrameBuffer(ref frame.DeformedVertexBuffer, _allocatedDeformedVertexCount, UnsafeUtility.SizeOf<ActorGpuDeformedVertexGpu>(), "VV:ActorDeformedVertices");
            BoneMatrixCount = math.max(boneMatrixCount, _allocatedBoneMatrixCount);
        }

        internal ActorGpuAnimationFrameUpload BeginFrameUpload(int actorCount, int layerCount, int skinMeshWorkCount)
        {
            ref ActorGpuAnimationFrameResources frame = ref CurrentFrame;
            return new ActorGpuAnimationFrameUpload
            {
                Actors = frame.ActorBuffer.LockBufferForWrite<ActorGpuAnimationActorGpu>(0, math.max(1, actorCount)),
                Layers = frame.LayerBuffer.LockBufferForWrite<ActorGpuAnimationLayerGpu>(0, math.max(1, layerCount)),
                SkinMeshes = frame.SkinMeshWorkBuffer.LockBufferForWrite<ActorGpuAnimationSkinMeshWorkGpu>(0, math.max(1, skinMeshWorkCount)),
                ActorCount = actorCount,
                LayerCount = layerCount,
                SkinMeshCount = skinMeshWorkCount,
            };
        }

        internal void EndFrameUpload(ActorGpuAnimationFrameUpload upload)
        {
            ref ActorGpuAnimationFrameResources frame = ref CurrentFrame;
            frame.ActorBuffer.UnlockBufferAfterWrite<ActorGpuAnimationActorGpu>(math.max(1, upload.ActorCount));
            frame.LayerBuffer.UnlockBufferAfterWrite<ActorGpuAnimationLayerGpu>(math.max(1, upload.LayerCount));
            frame.SkinMeshWorkBuffer.UnlockBufferAfterWrite<ActorGpuAnimationSkinMeshWorkGpu>(math.max(1, upload.SkinMeshCount));
        }

        internal void PrepareFrameForRender(
            int actorCount,
            int layerCount,
            int localBoneMatrixCount,
            int skinMeshWorkCount,
            int boneMatrixCount,
            int deformedVertexCount)
        {
            if (!IsSupported)
                return;

            if (actorCount <= 0
                || localBoneMatrixCount <= 0
                || skinMeshWorkCount <= 0
                || boneMatrixCount <= 0
                || deformedVertexCount <= 0)
                return;

            ref ActorGpuAnimationFrameResources frame = ref CurrentFrame;
            _preparedFrame = new ActorGpuAnimationPreparedFrame
            {
                Version = ++_preparedFrameVersion,
                ActorCount = actorCount,
                LayerCount = layerCount,
                LocalBoneMatrixCount = localBoneMatrixCount,
                SkinMeshWorkCount = skinMeshWorkCount,
                BoneMatrixCount = boneMatrixCount,
                DeformedVertexCount = deformedVertexCount,
                ActorBuffer = frame.ActorBuffer,
                LayerBuffer = frame.LayerBuffer,
                LocalBoneMatrixBuffer = frame.LocalBoneMatrixBuffer,
                LocalToRootMatrixBuffer = frame.LocalToRootMatrixBuffer,
                SkinMeshWorkBuffer = frame.SkinMeshWorkBuffer,
                BoneMatrixBuffer = frame.BoneMatrixBuffer,
                DeformedVertexBuffer = frame.DeformedVertexBuffer,
            };
        }

        public bool TryGetPreparedFrame(out ActorGpuAnimationPreparedFrame frame)
        {
            frame = _preparedFrame;
            return frame.IsValid;
        }

        public void RecordPreparedFrameDispatch(CommandBuffer cmd)
        {
            if (!TryGetPreparedFrame(out var frame))
                return;

            if (_lastRecordedFrameVersion == frame.Version)
            {
                cmd.SetGlobalBuffer(k_DeformedVerticesId, frame.DeformedVertexBuffer);
                return;
            }

            RecordDispatch(cmd, in frame);
            _lastRecordedFrameVersion = frame.Version;
        }

        public void RecordDispatch(CommandBuffer cmd, in ActorGpuAnimationPreparedFrame frame)
        {
            if (!frame.IsValid)
                throw new InvalidOperationException("Actor GPU animation render dispatch was requested without a valid prepared frame.");

            cmd.SetComputeIntParam(_computeShader, k_ActorCountId, frame.ActorCount);
            cmd.SetComputeIntParam(_computeShader, k_SkinMeshWorkCountId, frame.SkinMeshWorkCount);
            cmd.SetComputeIntParam(_computeShader, k_LayerCountId, frame.LayerCount);
            cmd.SetComputeIntParam(_computeShader, k_LocalBoneMatrixCountId, frame.LocalBoneMatrixCount);
            cmd.SetComputeIntParam(_computeShader, k_ActorBoneMatrixCountId, frame.BoneMatrixCount);
            cmd.SetComputeIntParam(_computeShader, k_DeformedVertexCountId, frame.DeformedVertexCount);
            cmd.SetComputeIntParam(_computeShader, k_SkeletonCountId, _skeletonCount);
            cmd.SetComputeIntParam(_computeShader, k_BoneCountId, _boneCount);
            cmd.SetComputeIntParam(_computeShader, k_ClipCountId, _clipCount);
            cmd.SetComputeIntParam(_computeShader, k_TrackCountId, _trackCount);
            cmd.SetComputeIntParam(_computeShader, k_KeyCountId, _keyCount);
            cmd.SetComputeIntParam(_computeShader, k_SkinMeshCountId, _skinMeshCount);
            cmd.SetComputeIntParam(_computeShader, k_SkinBoneCountId, _skinBoneCount);
            cmd.SetComputeIntParam(_computeShader, k_SkinVertexCountId, _skinVertexCount);
            BindStaticBuffers(cmd, _sampleLocalBonesKernelIndex);
            BindStaticBuffers(cmd, _composeLocalToRootKernelIndex);
            BindStaticBuffers(cmd, _buildSkinMatricesKernelIndex);
            BindStaticBuffers(cmd, _buildDeformedVerticesKernelIndex);
            BindFrameBuffers(cmd, _sampleLocalBonesKernelIndex, in frame);
            BindFrameBuffers(cmd, _composeLocalToRootKernelIndex, in frame);
            BindFrameBuffers(cmd, _buildSkinMatricesKernelIndex, in frame);
            BindFrameBuffers(cmd, _buildDeformedVerticesKernelIndex, in frame);

            DispatchActorKernel(cmd, _sampleLocalBonesKernelIndex, frame.ActorCount);
            DispatchActorKernel(cmd, _composeLocalToRootKernelIndex, frame.ActorCount);
            DispatchSkinMatrixKernel(cmd, frame.SkinMeshWorkCount);
            DispatchActorKernel(cmd, _buildDeformedVerticesKernelIndex, frame.ActorCount);
            cmd.SetGlobalBuffer(k_DeformedVerticesId, frame.DeformedVertexBuffer);
        }

        void DispatchActorKernel(CommandBuffer cmd, int kernelIndex, int actorCount)
        {
            for (int baseActor = 0; baseActor < actorCount; baseActor += MaxComputeGroupsPerDimension)
            {
                int groupCount = math.min(MaxComputeGroupsPerDimension, actorCount - baseActor);
                cmd.SetComputeIntParam(_computeShader, k_ActorDispatchBaseId, baseActor);
                cmd.DispatchCompute(_computeShader, kernelIndex, groupCount, 1, 1);
            }
        }

        void DispatchSkinMatrixKernel(CommandBuffer cmd, int skinMeshWorkCount)
        {
            int maxItemsPerDispatch = MaxComputeGroupsPerDimension * SkinMatrixThreadsPerGroup;
            for (int baseWork = 0; baseWork < skinMeshWorkCount; baseWork += maxItemsPerDispatch)
            {
                int itemCount = math.min(maxItemsPerDispatch, skinMeshWorkCount - baseWork);
                int groupCount = math.max(1, (itemCount + SkinMatrixThreadsPerGroup - 1) / SkinMatrixThreadsPerGroup);
                cmd.SetComputeIntParam(_computeShader, k_SkinMeshDispatchBaseId, baseWork);
                cmd.DispatchCompute(_computeShader, _buildSkinMatricesKernelIndex, groupCount, 1, 1);
            }
        }

        void BindStaticBuffers(CommandBuffer cmd, int kernelIndex)
        {
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_SkeletonsId, _skeletonBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_BonesId, _boneBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_ClipsId, _clipBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_TracksId, _trackBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_ClipBoneTrackRangesId, _clipBoneTrackRangeBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_KeysId, _keyBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_SkinMeshesId, _skinMeshBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_SkinBonesId, _skinBoneBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_SkinVerticesId, _skinVertexBuffer);
        }

        void BindFrameBuffers(CommandBuffer cmd, int kernelIndex, in ActorGpuAnimationPreparedFrame frame)
        {
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_ActorsId, frame.ActorBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_LayersId, frame.LayerBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_LocalMatricesId, frame.LocalBoneMatrixBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_LocalToRootMatricesId, frame.LocalToRootMatrixBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_SkinMeshWorkId, frame.SkinMeshWorkBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_OutputMatricesId, frame.BoneMatrixBuffer);
            cmd.SetComputeBufferParam(_computeShader, kernelIndex, k_DeformedVerticesId, frame.DeformedVertexBuffer);
        }

        public void Dispose()
        {
            ReleaseBuffer(ref _skeletonBuffer);
            ReleaseBuffer(ref _boneBuffer);
            ReleaseBuffer(ref _clipBuffer);
            ReleaseBuffer(ref _trackBuffer);
            ReleaseBuffer(ref _clipBoneTrackRangeBuffer);
            ReleaseBuffer(ref _keyBuffer);
            ReleaseBuffer(ref _skinMeshBuffer);
            ReleaseBuffer(ref _skinBoneBuffer);
            ReleaseBuffer(ref _skinVertexBuffer);
            for (int i = 0; i < _frames.Length; i++)
                ReleaseFrameResources(ref _frames[i]);
            _frameIndex = -1;
            _preparedFrame = default;
            _preparedFrameVersion = 0UL;
            _lastRecordedFrameVersion = 0UL;
            _allocatedBoneMatrixCount = 0;
            _allocatedDeformedVertexCount = 0;
            _skeletonCount = 0;
            _boneCount = 0;
            _clipCount = 0;
            _trackCount = 0;
            _keyCount = 0;
            _skinMeshCount = 0;
            _skinBoneCount = 0;
            _skinVertexCount = 0;
        }

        ref ActorGpuAnimationFrameResources CurrentFrame
        {
            get
            {
                int index = _frameIndex < 0 ? 0 : _frameIndex;
                return ref _frames[index];
            }
        }

        void UploadStaticCatalog(ref ActorAnimationCatalogBlob catalog)
        {
            int[] boneDepths = BuildBoneDepths(ref catalog, out int[] skeletonMaxDepths);
            var skeletons = new ActorGpuAnimationSkeletonGpu[catalog.Skeletons.Length];
            for (int i = 0; i < skeletons.Length; i++)
            {
                var source = catalog.Skeletons[i];
                skeletons[i] = new ActorGpuAnimationSkeletonGpu
                {
                    FirstBoneIndex = source.FirstBoneIndex,
                    BoneCount = source.BoneCount,
                    AccumulationBoneIndex = source.AccumulationBoneIndex,
                    MaxBoneDepth = (uint)i < (uint)skeletonMaxDepths.Length ? skeletonMaxDepths[i] : 0,
                };
            }

            var bones = new ActorGpuAnimationBoneGpu[catalog.Bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var source = catalog.Bones[i];
                float4x4 bindLocal = ActorAnimationSpaceConversion.SourceAffineToUnity(source.BindLocalMatrix);
                float4x4 bindRoot = ActorAnimationSpaceConversion.SourceAffineToUnity(source.BindLocalToRootMatrix);
                bones[i] = new ActorGpuAnimationBoneGpu
                {
                    ParentIndex = source.ParentIndex,
                    Mask = (uint)ComputeBoneMask(source.Name),
                    Depth = (uint)i < (uint)boneDepths.Length ? boneDepths[i] : 0,
                    BindPosition = ActorAnimationSpaceConversion.SourceTranslationToUnity(source.BindPosition),
                    BindScale = source.BindScale <= 0f ? 1f : source.BindScale,
                    BindRotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(source.BindRotation).value,
                    BindLocalRow0 = new float4(bindLocal.c0.x, bindLocal.c1.x, bindLocal.c2.x, bindLocal.c3.x),
                    BindLocalRow1 = new float4(bindLocal.c0.y, bindLocal.c1.y, bindLocal.c2.y, bindLocal.c3.y),
                    BindLocalRow2 = new float4(bindLocal.c0.z, bindLocal.c1.z, bindLocal.c2.z, bindLocal.c3.z),
                    BindRootRow0 = new float4(bindRoot.c0.x, bindRoot.c1.x, bindRoot.c2.x, bindRoot.c3.x),
                    BindRootRow1 = new float4(bindRoot.c0.y, bindRoot.c1.y, bindRoot.c2.y, bindRoot.c3.y),
                    BindRootRow2 = new float4(bindRoot.c0.z, bindRoot.c1.z, bindRoot.c2.z, bindRoot.c3.z),
                };
            }

            var clips = new ActorGpuAnimationClipGpu[catalog.Clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var source = catalog.Clips[i];
                clips[i] = new ActorGpuAnimationClipGpu
                {
                    FirstTrackIndex = source.FirstTrackIndex,
                    TrackCount = source.TrackCount,
                    Duration = source.Duration,
                };
            }

            BuildClipBoneTrackLookup(
                ref catalog,
                out ActorGpuAnimationTrackGpu[] tracks,
                out ActorGpuAnimationClipBoneTrackRangeGpu[] clipBoneTrackRanges);

            var keys = new ActorGpuAnimationKeyGpu[catalog.Keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var source = catalog.Keys[i];
                keys[i] = new ActorGpuAnimationKeyGpu
                {
                    Time = source.Time,
                    Value = source.Value,
                    InTangent = source.InTangent,
                    OutTangent = source.OutTangent,
                };
            }

            var skinMeshes = new ActorGpuAnimationSkinMeshGpu[catalog.SkinMeshes.Length];
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                var source = catalog.SkinMeshes[i];
                skinMeshes[i] = new ActorGpuAnimationSkinMeshGpu
                {
                    FirstSkinBoneIndex = source.FirstSkinBoneIndex,
                    SkinBoneCount = source.SkinBoneCount,
                    IsRigid = source.IsRigid,
                    FirstVertexIndex = source.FirstVertexIndex,
                    RigidOffset = source.RigidOffset,
                    VertexCount = source.VertexCount,
                    GeometryRow0 = new float4(source.GeometryToSkeleton.c0.x, source.GeometryToSkeleton.c1.x, source.GeometryToSkeleton.c2.x, source.GeometryToSkeleton.c3.x),
                    GeometryRow1 = new float4(source.GeometryToSkeleton.c0.y, source.GeometryToSkeleton.c1.y, source.GeometryToSkeleton.c2.y, source.GeometryToSkeleton.c3.y),
                    GeometryRow2 = new float4(source.GeometryToSkeleton.c0.z, source.GeometryToSkeleton.c1.z, source.GeometryToSkeleton.c2.z, source.GeometryToSkeleton.c3.z),
                    GeometryRow3 = new float4(source.GeometryToSkeleton.c0.w, source.GeometryToSkeleton.c1.w, source.GeometryToSkeleton.c2.w, source.GeometryToSkeleton.c3.w),
                };
            }

            var skinBones = new ActorGpuAnimationSkinBoneGpu[catalog.SkinBones.Length];
            for (int i = 0; i < skinBones.Length; i++)
            {
                var source = catalog.SkinBones[i];
                skinBones[i] = new ActorGpuAnimationSkinBoneGpu
                {
                    BoneIndex = source.BoneIndex,
                    BindPoseRow0 = new float4(source.BindPose.c0.x, source.BindPose.c1.x, source.BindPose.c2.x, source.BindPose.c3.x),
                    BindPoseRow1 = new float4(source.BindPose.c0.y, source.BindPose.c1.y, source.BindPose.c2.y, source.BindPose.c3.y),
                    BindPoseRow2 = new float4(source.BindPose.c0.z, source.BindPose.c1.z, source.BindPose.c2.z, source.BindPose.c3.z),
                    BindPoseRow3 = new float4(source.BindPose.c0.w, source.BindPose.c1.w, source.BindPose.c2.w, source.BindPose.c3.w),
                };
            }

            var skinVertices = new Rendering.ActorProceduralVertexGpu[catalog.SkinVertices.Length];
            for (int i = 0; i < skinVertices.Length; i++)
            {
                var source = catalog.SkinVertices[i];
                skinVertices[i] = new Rendering.ActorProceduralVertexGpu
                {
                    Position = source.Position,
                    Normal = source.Normal,
                    Uv = source.Uv,
                    BoneIndices0 = source.BoneIndices0,
                    BoneIndices1 = source.BoneIndices1,
                    Weights0 = source.Weights0,
                    Weights1 = source.Weights1,
                };
            }

            EnsureBuffer(ref _skeletonBuffer, skeletons.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkeletonGpu>(), "VV:GpuAnimSkeletons");
            EnsureBuffer(ref _boneBuffer, bones.Length, UnsafeUtility.SizeOf<ActorGpuAnimationBoneGpu>(), "VV:GpuAnimBones");
            EnsureBuffer(ref _clipBuffer, clips.Length, UnsafeUtility.SizeOf<ActorGpuAnimationClipGpu>(), "VV:GpuAnimClips");
            EnsureBuffer(ref _trackBuffer, tracks.Length, UnsafeUtility.SizeOf<ActorGpuAnimationTrackGpu>(), "VV:GpuAnimTracks");
            EnsureBuffer(ref _clipBoneTrackRangeBuffer, clipBoneTrackRanges.Length, UnsafeUtility.SizeOf<ActorGpuAnimationClipBoneTrackRangeGpu>(), "VV:GpuAnimClipBoneTrackRanges");
            EnsureBuffer(ref _keyBuffer, keys.Length, UnsafeUtility.SizeOf<ActorGpuAnimationKeyGpu>(), "VV:GpuAnimKeys");
            EnsureBuffer(ref _skinMeshBuffer, skinMeshes.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkinMeshGpu>(), "VV:GpuAnimSkinMeshes");
            EnsureBuffer(ref _skinBoneBuffer, skinBones.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkinBoneGpu>(), "VV:GpuAnimSkinBones");
            EnsureBuffer(ref _skinVertexBuffer, skinVertices.Length, UnsafeUtility.SizeOf<Rendering.ActorProceduralVertexGpu>(), "VV:GpuAnimSkinVertices");

            if (skeletons.Length > 0)
                _skeletonBuffer.SetData(skeletons);
            if (bones.Length > 0)
                _boneBuffer.SetData(bones);
            if (clips.Length > 0)
                _clipBuffer.SetData(clips);
            if (tracks.Length > 0)
                _trackBuffer.SetData(tracks);
            if (clipBoneTrackRanges.Length > 0)
                _clipBoneTrackRangeBuffer.SetData(clipBoneTrackRanges);
            if (keys.Length > 0)
                _keyBuffer.SetData(keys);
            if (skinMeshes.Length > 0)
                _skinMeshBuffer.SetData(skinMeshes);
            if (skinBones.Length > 0)
                _skinBoneBuffer.SetData(skinBones);
            if (skinVertices.Length > 0)
                _skinVertexBuffer.SetData(skinVertices);

            _skeletonCount = skeletons.Length;
            _boneCount = bones.Length;
            _clipCount = clips.Length;
            _trackCount = tracks.Length;
            _keyCount = keys.Length;
            _skinMeshCount = skinMeshes.Length;
            _skinBoneCount = skinBones.Length;
            _skinVertexCount = skinVertices.Length;
        }

        static int[] BuildBoneDepths(ref ActorAnimationCatalogBlob catalog, out int[] skeletonMaxDepths)
        {
            var boneDepths = new int[catalog.Bones.Length];
            skeletonMaxDepths = new int[catalog.Skeletons.Length];
            for (int skeletonIndex = 0; skeletonIndex < catalog.Skeletons.Length; skeletonIndex++)
            {
                var skeleton = catalog.Skeletons[skeletonIndex];
                int firstBone = skeleton.FirstBoneIndex;
                int boneCount = math.max(0, skeleton.BoneCount);
                if (firstBone < 0 || firstBone >= catalog.Bones.Length || boneCount <= 0)
                    continue;

                int maxBone = math.min(catalog.Bones.Length, firstBone + boneCount);
                for (int absoluteBoneIndex = firstBone; absoluteBoneIndex < maxBone; absoluteBoneIndex++)
                {
                    int localBoneIndex = absoluteBoneIndex - firstBone;
                    int depth = 0;
                    int parent = catalog.Bones[absoluteBoneIndex].ParentIndex;
                    int guard = 0;
                    while (parent >= 0 && parent < localBoneIndex && guard++ < BoneThreadsPerGroup)
                    {
                        depth++;
                        parent = catalog.Bones[firstBone + parent].ParentIndex;
                    }

                    boneDepths[absoluteBoneIndex] = depth;
                    skeletonMaxDepths[skeletonIndex] = math.max(skeletonMaxDepths[skeletonIndex], depth);
                }
            }

            return boneDepths;
        }

        static void BuildClipBoneTrackLookup(
            ref ActorAnimationCatalogBlob catalog,
            out ActorGpuAnimationTrackGpu[] sortedTracks,
            out ActorGpuAnimationClipBoneTrackRangeGpu[] clipBoneTrackRanges)
        {
            int rangeCount = math.max(1, catalog.Clips.Length * BoneThreadsPerGroup);
            clipBoneTrackRanges = new ActorGpuAnimationClipBoneTrackRangeGpu[rangeCount];
            var tracks = new List<ActorGpuAnimationTrackGpu>(catalog.Tracks.Length);
            for (int clipIndex = 0; clipIndex < catalog.Clips.Length; clipIndex++)
            {
                var clip = catalog.Clips[clipIndex];
                int trackStart = clip.FirstTrackIndex;
                int trackEnd = trackStart < 0
                    ? 0
                    : math.min(catalog.Tracks.Length, trackStart + math.max(0, clip.TrackCount));

                for (int boneIndex = 0; boneIndex < BoneThreadsPerGroup; boneIndex++)
                {
                    int firstTrackIndex = tracks.Count;
                    if (trackStart >= 0)
                    {
                        for (int sourceTrackIndex = trackStart; sourceTrackIndex < trackEnd; sourceTrackIndex++)
                        {
                            var source = catalog.Tracks[sourceTrackIndex];
                            if (source.TargetBoneIndex != boneIndex
                                || source.KeyCount <= 0
                                || source.FirstKeyIndex < 0)
                            {
                                continue;
                            }

                            tracks.Add(BuildTrackGpu(source));
                        }
                    }

                    int rangeIndex = clipIndex * BoneThreadsPerGroup + boneIndex;
                    clipBoneTrackRanges[rangeIndex] = new ActorGpuAnimationClipBoneTrackRangeGpu
                    {
                        FirstTrackIndex = firstTrackIndex,
                        TrackCount = tracks.Count - firstTrackIndex,
                    };
                }
            }

            sortedTracks = tracks.Count > 0
                ? tracks.ToArray()
                : Array.Empty<ActorGpuAnimationTrackGpu>();
        }

        static ActorGpuAnimationTrackGpu BuildTrackGpu(ActorAnimationTrackBlob source)
        {
            return new ActorGpuAnimationTrackGpu
            {
                TargetBoneIndex = source.TargetBoneIndex,
                Kind = (int)source.Kind,
                Interpolation = (int)source.Interpolation,
                AxisOrder = source.AxisOrder,
                ControllerFlags = source.ControllerFlags,
                Frequency = source.Frequency,
                Phase = source.Phase,
                TimeStart = source.TimeStart,
                TimeStop = source.TimeStop,
                FirstKeyIndex = source.FirstKeyIndex,
                KeyCount = source.KeyCount,
                Mask = source.BlendMask != 0
                    ? source.BlendMask
                    : (uint)ComputeBoneMask(source.TargetName),
            };
        }

        static ActorAnimationBlendMask ComputeBoneMask(FixedString64Bytes name)
        {
            if (ContainsAsciiIgnoreCase(name, "head") || ContainsAsciiIgnoreCase(name, "neck"))
                return ActorAnimationBlendMask.Torso;
            if (ContainsAsciiIgnoreCase(name, "l clavicle")
                || ContainsAsciiIgnoreCase(name, "l upperarm")
                || ContainsAsciiIgnoreCase(name, "l forearm")
                || ContainsAsciiIgnoreCase(name, "l hand")
                || ContainsAsciiIgnoreCase(name, "weapon bone left")
                || ContainsAsciiIgnoreCase(name, "shield bone"))
            {
                return ActorAnimationBlendMask.LeftArm;
            }
            if (ContainsAsciiIgnoreCase(name, "r clavicle")
                || ContainsAsciiIgnoreCase(name, "r upperarm")
                || ContainsAsciiIgnoreCase(name, "r forearm")
                || ContainsAsciiIgnoreCase(name, "r hand")
                || ContainsAsciiIgnoreCase(name, "weapon bone"))
            {
                return ActorAnimationBlendMask.RightArm;
            }
            if (ContainsAsciiIgnoreCase(name, "pelvis")
                || ContainsAsciiIgnoreCase(name, "groin")
                || ContainsAsciiIgnoreCase(name, "thigh")
                || ContainsAsciiIgnoreCase(name, "calf")
                || ContainsAsciiIgnoreCase(name, "ankle")
                || ContainsAsciiIgnoreCase(name, "foot")
                || ContainsAsciiIgnoreCase(name, "toe")
                || ContainsAsciiIgnoreCase(name, "knee")
                || ContainsAsciiIgnoreCase(name, "leg")
                || ContainsAsciiIgnoreCase(name, "tail"))
            {
                return ActorAnimationBlendMask.LowerBody;
            }

            return ActorAnimationBlendMask.Torso;
        }

        static bool ContainsAsciiIgnoreCase(FixedString64Bytes value, string needle)
        {
            if (string.IsNullOrEmpty(needle) || needle.Length > value.Length)
                return false;

            int maxStart = value.Length - needle.Length;
            for (int start = 0; start <= maxStart; start++)
            {
                bool matches = true;
                for (int i = 0; i < needle.Length; i++)
                {
                    if (ToAsciiLower(value[start + i]) != ToAsciiLower((byte)needle[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return true;
            }

            return false;
        }

        static byte ToAsciiLower(byte value)
        {
            return value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;
        }

        static ulong BuildCatalogSignature(ref ActorAnimationCatalogBlob catalog)
        {
            ulong signature = 14695981039346656037UL;
            Mix(ref signature, StaticUploadVersion);
            Mix(ref signature, (ulong)catalog.Skeletons.Length);
            Mix(ref signature, (ulong)catalog.Bones.Length);
            Mix(ref signature, (ulong)catalog.Clips.Length);
            Mix(ref signature, (ulong)catalog.Tracks.Length);
            Mix(ref signature, (ulong)catalog.Keys.Length);
            Mix(ref signature, (ulong)catalog.SkinMeshes.Length);
            Mix(ref signature, (ulong)catalog.SkinBones.Length);
            return signature;
        }

        static void Mix(ref ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        static void EnsureBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string name,
            GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            count = math.max(1, count);
            if (buffer != null
                && buffer.count >= count
                && buffer.stride == stride
                && buffer.usageFlags == usageFlags)
                return;

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, usageFlags, count, stride)
            {
                name = name,
            };
        }

        static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }

        void EnsureFrameBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string baseName,
            GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            count = math.max(1, count);
            if (buffer != null
                && buffer.count >= count
                && buffer.stride == stride
                && buffer.usageFlags == usageFlags)
                return;

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, usageFlags, count, stride)
            {
                name = $"{baseName}[f{(_frameIndex < 0 ? 0 : _frameIndex)}]",
            };
        }

        static void ReleaseFrameResources(ref ActorGpuAnimationFrameResources frame)
        {
            ReleaseBuffer(ref frame.ActorBuffer);
            ReleaseBuffer(ref frame.LayerBuffer);
            ReleaseBuffer(ref frame.LocalBoneMatrixBuffer);
            ReleaseBuffer(ref frame.LocalToRootMatrixBuffer);
            ReleaseBuffer(ref frame.SkinMeshWorkBuffer);
            ReleaseBuffer(ref frame.BoneMatrixBuffer);
            ReleaseBuffer(ref frame.DeformedVertexBuffer);
        }
    }
}
