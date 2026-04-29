using System;
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
        public int Padding;
    }

    struct ActorGpuAnimationBoneGpu
    {
        public int ParentIndex;
        public uint Mask;
        public float2 Padding;
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
        const ulong StaticUploadVersion = 5UL;
        const GraphicsBuffer.UsageFlags DynamicBufferUsage = GraphicsBuffer.UsageFlags.LockBufferForWrite;

        static readonly int k_ActorCountId = Shader.PropertyToID("_GpuActorCount");
        static readonly int k_SkinMeshWorkCountId = Shader.PropertyToID("_GpuSkinMeshWorkCount");
        static readonly int k_ActorDispatchBaseId = Shader.PropertyToID("_GpuActorDispatchBase");
        static readonly int k_SkinMeshDispatchBaseId = Shader.PropertyToID("_GpuSkinMeshDispatchBase");
        static readonly int k_SkeletonsId = Shader.PropertyToID("_GpuAnimationSkeletons");
        static readonly int k_BonesId = Shader.PropertyToID("_GpuAnimationBones");
        static readonly int k_ClipsId = Shader.PropertyToID("_GpuAnimationClips");
        static readonly int k_TracksId = Shader.PropertyToID("_GpuAnimationTracks");
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
        GraphicsBuffer _keyBuffer;
        GraphicsBuffer _skinMeshBuffer;
        GraphicsBuffer _skinBoneBuffer;
        GraphicsBuffer _skinVertexBuffer;
        GraphicsBuffer _actorBuffer;
        GraphicsBuffer _layerBuffer;
        GraphicsBuffer _localBoneMatrixBuffer;
        GraphicsBuffer _localToRootMatrixBuffer;
        GraphicsBuffer _skinMeshWorkBuffer;
        GraphicsBuffer _boneMatrixBuffer;
        GraphicsBuffer _deformedVertexBuffer;
        int _allocatedBoneMatrixCount;
        int _allocatedDeformedVertexCount;

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
        public GraphicsBuffer BoneMatrixBuffer => _boneMatrixBuffer;
        public int BoneMatrixCount { get; private set; }
        public int AllocatedBoneMatrixCount => _allocatedBoneMatrixCount;
        public int AllocatedDeformedVertexCount => _allocatedDeformedVertexCount;

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
            BoneMatrixCount = 0;
        }

        public void ReserveFrameCapacity(int actorCount, int layerCount, int boneCount, int skinMeshWorkCount, int boneMatrixCount)
        {
            EnsureBuffer(ref _actorBuffer, actorCount, UnsafeUtility.SizeOf<ActorGpuAnimationActorGpu>(), "VV:GpuAnimActors", DynamicBufferUsage);
            EnsureBuffer(ref _layerBuffer, layerCount, UnsafeUtility.SizeOf<ActorGpuAnimationLayerGpu>(), "VV:GpuAnimLayers", DynamicBufferUsage);
            EnsureBuffer(ref _localBoneMatrixBuffer, boneCount, UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimLocalBoneMatrices");
            EnsureBuffer(ref _localToRootMatrixBuffer, boneCount, UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimLocalToRootMatrices");
            EnsureBuffer(ref _skinMeshWorkBuffer, skinMeshWorkCount, UnsafeUtility.SizeOf<ActorGpuAnimationSkinMeshWorkGpu>(), "VV:GpuAnimSkinMeshWork", DynamicBufferUsage);
            EnsureBuffer(ref _boneMatrixBuffer, math.max(boneMatrixCount, _allocatedBoneMatrixCount), UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimBoneMatrices");
            EnsureBuffer(ref _deformedVertexBuffer, _allocatedDeformedVertexCount, UnsafeUtility.SizeOf<ActorGpuDeformedVertexGpu>(), "VV:ActorDeformedVertices");
            BoneMatrixCount = math.max(boneMatrixCount, _allocatedBoneMatrixCount);
        }

        internal ActorGpuAnimationFrameUpload BeginFrameUpload(int actorCount, int layerCount, int skinMeshWorkCount)
        {
            return new ActorGpuAnimationFrameUpload
            {
                Actors = _actorBuffer.LockBufferForWrite<ActorGpuAnimationActorGpu>(0, math.max(1, actorCount)),
                Layers = _layerBuffer.LockBufferForWrite<ActorGpuAnimationLayerGpu>(0, math.max(1, layerCount)),
                SkinMeshes = _skinMeshWorkBuffer.LockBufferForWrite<ActorGpuAnimationSkinMeshWorkGpu>(0, math.max(1, skinMeshWorkCount)),
                ActorCount = actorCount,
                LayerCount = layerCount,
                SkinMeshCount = skinMeshWorkCount,
            };
        }

        internal void EndFrameUpload(ActorGpuAnimationFrameUpload upload)
        {
            _actorBuffer.UnlockBufferAfterWrite<ActorGpuAnimationActorGpu>(math.max(1, upload.ActorCount));
            _layerBuffer.UnlockBufferAfterWrite<ActorGpuAnimationLayerGpu>(math.max(1, upload.LayerCount));
            _skinMeshWorkBuffer.UnlockBufferAfterWrite<ActorGpuAnimationSkinMeshWorkGpu>(math.max(1, upload.SkinMeshCount));
        }

        internal void Dispatch(int actorCount, int skinMeshWorkCount)
        {
            if (!IsSupported)
                return;

            if (actorCount <= 0 || skinMeshWorkCount <= 0 || BoneMatrixCount <= 0)
                return;

            _computeShader.SetInt(k_ActorCountId, actorCount);
            _computeShader.SetInt(k_SkinMeshWorkCountId, skinMeshWorkCount);
            BindStaticBuffers(_sampleLocalBonesKernelIndex);
            BindStaticBuffers(_composeLocalToRootKernelIndex);
            BindStaticBuffers(_buildSkinMatricesKernelIndex);
            BindStaticBuffers(_buildDeformedVerticesKernelIndex);
            BindFrameBuffers(_sampleLocalBonesKernelIndex);
            BindFrameBuffers(_composeLocalToRootKernelIndex);
            BindFrameBuffers(_buildSkinMatricesKernelIndex);
            BindFrameBuffers(_buildDeformedVerticesKernelIndex);

            var cmd = CommandBufferPool.Get("VV ActorGpuAnimation Dispatch");
            DispatchActorKernel(cmd, _sampleLocalBonesKernelIndex, actorCount);
            DispatchActorKernel(cmd, _composeLocalToRootKernelIndex, actorCount);
            DispatchSkinMatrixKernel(cmd, skinMeshWorkCount);
            DispatchSkinMeshKernel(cmd, _buildDeformedVerticesKernelIndex, skinMeshWorkCount);
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            Shader.SetGlobalBuffer(k_DeformedVerticesId, _deformedVertexBuffer);
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

        void DispatchSkinMeshKernel(CommandBuffer cmd, int kernelIndex, int skinMeshWorkCount)
        {
            for (int baseWork = 0; baseWork < skinMeshWorkCount; baseWork += MaxComputeGroupsPerDimension)
            {
                int groupCount = math.min(MaxComputeGroupsPerDimension, skinMeshWorkCount - baseWork);
                cmd.SetComputeIntParam(_computeShader, k_SkinMeshDispatchBaseId, baseWork);
                cmd.DispatchCompute(_computeShader, kernelIndex, groupCount, 1, 1);
            }
        }

        void BindStaticBuffers(int kernelIndex)
        {
            _computeShader.SetBuffer(kernelIndex, k_SkeletonsId, _skeletonBuffer);
            _computeShader.SetBuffer(kernelIndex, k_BonesId, _boneBuffer);
            _computeShader.SetBuffer(kernelIndex, k_ClipsId, _clipBuffer);
            _computeShader.SetBuffer(kernelIndex, k_TracksId, _trackBuffer);
            _computeShader.SetBuffer(kernelIndex, k_KeysId, _keyBuffer);
            _computeShader.SetBuffer(kernelIndex, k_SkinMeshesId, _skinMeshBuffer);
            _computeShader.SetBuffer(kernelIndex, k_SkinBonesId, _skinBoneBuffer);
            _computeShader.SetBuffer(kernelIndex, k_SkinVerticesId, _skinVertexBuffer);
        }

        void BindFrameBuffers(int kernelIndex)
        {
            _computeShader.SetBuffer(kernelIndex, k_ActorsId, _actorBuffer);
            _computeShader.SetBuffer(kernelIndex, k_LayersId, _layerBuffer);
            _computeShader.SetBuffer(kernelIndex, k_LocalMatricesId, _localBoneMatrixBuffer);
            _computeShader.SetBuffer(kernelIndex, k_LocalToRootMatricesId, _localToRootMatrixBuffer);
            _computeShader.SetBuffer(kernelIndex, k_SkinMeshWorkId, _skinMeshWorkBuffer);
            _computeShader.SetBuffer(kernelIndex, k_OutputMatricesId, _boneMatrixBuffer);
            _computeShader.SetBuffer(kernelIndex, k_DeformedVerticesId, _deformedVertexBuffer);
        }

        public void Dispose()
        {
            ReleaseBuffer(ref _skeletonBuffer);
            ReleaseBuffer(ref _boneBuffer);
            ReleaseBuffer(ref _clipBuffer);
            ReleaseBuffer(ref _trackBuffer);
            ReleaseBuffer(ref _keyBuffer);
            ReleaseBuffer(ref _skinMeshBuffer);
            ReleaseBuffer(ref _skinBoneBuffer);
            ReleaseBuffer(ref _skinVertexBuffer);
            ReleaseBuffer(ref _actorBuffer);
            ReleaseBuffer(ref _layerBuffer);
            ReleaseBuffer(ref _localBoneMatrixBuffer);
            ReleaseBuffer(ref _localToRootMatrixBuffer);
            ReleaseBuffer(ref _skinMeshWorkBuffer);
            ReleaseBuffer(ref _boneMatrixBuffer);
            ReleaseBuffer(ref _deformedVertexBuffer);
            _allocatedBoneMatrixCount = 0;
            _allocatedDeformedVertexCount = 0;
        }

        void UploadStaticCatalog(ref ActorAnimationCatalogBlob catalog)
        {
            var skeletons = new ActorGpuAnimationSkeletonGpu[catalog.Skeletons.Length];
            for (int i = 0; i < skeletons.Length; i++)
            {
                var source = catalog.Skeletons[i];
                skeletons[i] = new ActorGpuAnimationSkeletonGpu
                {
                    FirstBoneIndex = source.FirstBoneIndex,
                    BoneCount = source.BoneCount,
                    AccumulationBoneIndex = source.AccumulationBoneIndex,
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

            var tracks = new ActorGpuAnimationTrackGpu[catalog.Tracks.Length];
            for (int i = 0; i < tracks.Length; i++)
            {
                var source = catalog.Tracks[i];
                tracks[i] = new ActorGpuAnimationTrackGpu
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
            if (keys.Length > 0)
                _keyBuffer.SetData(keys);
            if (skinMeshes.Length > 0)
                _skinMeshBuffer.SetData(skinMeshes);
            if (skinBones.Length > 0)
                _skinBoneBuffer.SetData(skinBones);
            if (skinVertices.Length > 0)
                _skinVertexBuffer.SetData(skinVertices);
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
    }
}
