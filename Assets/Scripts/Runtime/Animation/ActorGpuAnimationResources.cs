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
        public int Padding;
        public float3 RigidOffset;
        public float Padding1;
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
        public int FirstSkinMeshIndex;
        public int SkinMeshCount;
        public int BoneMatrixOffset;
        public int BoneMatrixCount;
        public int Padding;
    }

    struct ActorGpuAnimationLayerGpu
    {
        public int ClipIndex;
        public float Time;
        public float Weight;
        public uint Mask;
    }

    struct ActorGpuAnimationSkinMeshInstanceGpu
    {
        public int SkinMeshIndex;
        public int AttachBoneIndex;
        public int RigidMirrorX;
        public int BoneMatrixOffset;
    }

    public sealed class ActorGpuAnimationResources : IDisposable
    {
        const string ComputeShaderPath = "ActorGpuAnimation";
        const string KernelName = "CSMain";
        const int ThreadsPerGroup = 8;
        const GraphicsBuffer.UsageFlags DynamicBufferUsage = GraphicsBuffer.UsageFlags.LockBufferForWrite;

        static readonly int k_ActorCountId = Shader.PropertyToID("_ActorCount");
        static readonly int k_SkeletonsId = Shader.PropertyToID("_GpuAnimationSkeletons");
        static readonly int k_BonesId = Shader.PropertyToID("_GpuAnimationBones");
        static readonly int k_ClipsId = Shader.PropertyToID("_GpuAnimationClips");
        static readonly int k_TracksId = Shader.PropertyToID("_GpuAnimationTracks");
        static readonly int k_KeysId = Shader.PropertyToID("_GpuAnimationKeys");
        static readonly int k_SkinMeshesId = Shader.PropertyToID("_GpuAnimationSkinMeshes");
        static readonly int k_SkinBonesId = Shader.PropertyToID("_GpuAnimationSkinBones");
        static readonly int k_ActorsId = Shader.PropertyToID("_GpuAnimationActors");
        static readonly int k_LayersId = Shader.PropertyToID("_GpuAnimationLayers");
        static readonly int k_ActorSkinMeshesId = Shader.PropertyToID("_GpuAnimationActorSkinMeshes");
        static readonly int k_OutputMatricesId = Shader.PropertyToID("_GpuActorBoneMatrices");

        ComputeShader _computeShader;
        int _kernelIndex = -1;
        ulong _catalogSignature;

        GraphicsBuffer _skeletonBuffer;
        GraphicsBuffer _boneBuffer;
        GraphicsBuffer _clipBuffer;
        GraphicsBuffer _trackBuffer;
        GraphicsBuffer _keyBuffer;
        GraphicsBuffer _skinMeshBuffer;
        GraphicsBuffer _skinBoneBuffer;
        GraphicsBuffer _actorBuffer;
        GraphicsBuffer _layerBuffer;
        GraphicsBuffer _actorSkinMeshBuffer;
        GraphicsBuffer _boneMatrixBuffer;
        GraphicsFence _dispatchFence;
        bool _hasDispatchFence;

        public ActorGpuAnimationResources()
        {
            _computeShader = Resources.Load<ComputeShader>(ComputeShaderPath);
            if (_computeShader != null)
                _kernelIndex = _computeShader.FindKernel(KernelName);
        }

        public bool IsSupported => SystemInfo.supportsComputeShaders && _computeShader != null && _kernelIndex >= 0;
        public GraphicsBuffer BoneMatrixBuffer => _boneMatrixBuffer;
        public int BoneMatrixCount { get; private set; }
        public bool HasPendingDispatchFence => _hasDispatchFence;
        public GraphicsFence DispatchFence => _dispatchFence;

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
                && _skinBoneBuffer != null)
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

        public void ReserveFrameCapacity(int actorCount, int layerCount, int skinMeshCount, int boneMatrixCount)
        {
            EnsureBuffer(ref _actorBuffer, actorCount, UnsafeUtility.SizeOf<ActorGpuAnimationActorGpu>(), "VV:GpuAnimActors", DynamicBufferUsage);
            EnsureBuffer(ref _layerBuffer, layerCount, UnsafeUtility.SizeOf<ActorGpuAnimationLayerGpu>(), "VV:GpuAnimLayers", DynamicBufferUsage);
            EnsureBuffer(ref _actorSkinMeshBuffer, skinMeshCount, UnsafeUtility.SizeOf<ActorGpuAnimationSkinMeshInstanceGpu>(), "VV:GpuAnimActorSkinMeshes", DynamicBufferUsage);
            EnsureBuffer(ref _boneMatrixBuffer, boneMatrixCount, UnsafeUtility.SizeOf<Rendering.ActorProceduralMatrixGpu>(), "VV:GpuAnimBoneMatrices");
            BoneMatrixCount = boneMatrixCount;
        }

        internal void Dispatch(
            NativeArray<ActorGpuAnimationActorGpu> actors,
            NativeArray<ActorGpuAnimationLayerGpu> layers,
            NativeArray<ActorGpuAnimationSkinMeshInstanceGpu> skinMeshInstances)
        {
            if (!IsSupported)
                return;

            if (actors.Length <= 0 || BoneMatrixCount <= 0)
                return;

            if (actors.Length > 0)
                WriteBuffer(_actorBuffer, actors);
            if (layers.Length > 0)
                WriteBuffer(_layerBuffer, layers);
            if (skinMeshInstances.Length > 0)
                WriteBuffer(_actorSkinMeshBuffer, skinMeshInstances);

            _computeShader.SetInt(k_ActorCountId, actors.Length);
            _computeShader.SetBuffer(_kernelIndex, k_SkeletonsId, _skeletonBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_BonesId, _boneBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_ClipsId, _clipBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_TracksId, _trackBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_KeysId, _keyBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_SkinMeshesId, _skinMeshBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_SkinBonesId, _skinBoneBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_ActorsId, _actorBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_LayersId, _layerBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_ActorSkinMeshesId, _actorSkinMeshBuffer);
            _computeShader.SetBuffer(_kernelIndex, k_OutputMatricesId, _boneMatrixBuffer);

            int groupCount = math.max(1, (actors.Length + ThreadsPerGroup - 1) / ThreadsPerGroup);
            var cmd = CommandBufferPool.Get("VV ActorGpuAnimation Dispatch");
            cmd.DispatchCompute(_computeShader, _kernelIndex, groupCount, 1, 1);
            _dispatchFence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
            _hasDispatchFence = true;
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            Graphics.WaitOnAsyncGraphicsFence(_dispatchFence);
            _hasDispatchFence = false;
        }

        public void ConsumeDispatchFence(CommandBuffer cmd)
        {
            if (!_hasDispatchFence)
                return;

            cmd.WaitOnAsyncGraphicsFence(_dispatchFence);
            _hasDispatchFence = false;
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
            ReleaseBuffer(ref _actorBuffer);
            ReleaseBuffer(ref _layerBuffer);
            ReleaseBuffer(ref _actorSkinMeshBuffer);
            ReleaseBuffer(ref _boneMatrixBuffer);
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
                bones[i] = new ActorGpuAnimationBoneGpu
                {
                    ParentIndex = source.ParentIndex,
                    Mask = (uint)ComputeBoneMask(source.Name),
                    BindPosition = source.BindPosition,
                    BindScale = source.BindScale <= 0f ? 1f : source.BindScale,
                    BindRotation = source.BindRotation.value,
                    BindLocalRow0 = new float4(source.BindLocalMatrix.c0.x, source.BindLocalMatrix.c1.x, source.BindLocalMatrix.c2.x, source.BindLocalMatrix.c3.x),
                    BindLocalRow1 = new float4(source.BindLocalMatrix.c0.y, source.BindLocalMatrix.c1.y, source.BindLocalMatrix.c2.y, source.BindLocalMatrix.c3.y),
                    BindLocalRow2 = new float4(source.BindLocalMatrix.c0.z, source.BindLocalMatrix.c1.z, source.BindLocalMatrix.c2.z, source.BindLocalMatrix.c3.z),
                    BindRootRow0 = new float4(source.BindLocalToRootMatrix.c0.x, source.BindLocalToRootMatrix.c1.x, source.BindLocalToRootMatrix.c2.x, source.BindLocalToRootMatrix.c3.x),
                    BindRootRow1 = new float4(source.BindLocalToRootMatrix.c0.y, source.BindLocalToRootMatrix.c1.y, source.BindLocalToRootMatrix.c2.y, source.BindLocalToRootMatrix.c3.y),
                    BindRootRow2 = new float4(source.BindLocalToRootMatrix.c0.z, source.BindLocalToRootMatrix.c1.z, source.BindLocalToRootMatrix.c2.z, source.BindLocalToRootMatrix.c3.z),
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
                    RigidOffset = source.RigidOffset,
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

            EnsureBuffer(ref _skeletonBuffer, skeletons.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkeletonGpu>(), "VV:GpuAnimSkeletons");
            EnsureBuffer(ref _boneBuffer, bones.Length, UnsafeUtility.SizeOf<ActorGpuAnimationBoneGpu>(), "VV:GpuAnimBones");
            EnsureBuffer(ref _clipBuffer, clips.Length, UnsafeUtility.SizeOf<ActorGpuAnimationClipGpu>(), "VV:GpuAnimClips");
            EnsureBuffer(ref _trackBuffer, tracks.Length, UnsafeUtility.SizeOf<ActorGpuAnimationTrackGpu>(), "VV:GpuAnimTracks");
            EnsureBuffer(ref _keyBuffer, keys.Length, UnsafeUtility.SizeOf<ActorGpuAnimationKeyGpu>(), "VV:GpuAnimKeys");
            EnsureBuffer(ref _skinMeshBuffer, skinMeshes.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkinMeshGpu>(), "VV:GpuAnimSkinMeshes");
            EnsureBuffer(ref _skinBoneBuffer, skinBones.Length, UnsafeUtility.SizeOf<ActorGpuAnimationSkinBoneGpu>(), "VV:GpuAnimSkinBones");

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
        }

        static ActorAnimationBlendMask ComputeBoneMask(FixedString64Bytes name)
        {
            string lower = name.ToString().ToLowerInvariant();
            if (lower.Contains("head") || lower.Contains("neck"))
                return ActorAnimationBlendMask.Head;
            if (lower.Contains("l clavicle")
                || lower.Contains("l upperarm")
                || lower.Contains("l forearm")
                || lower.Contains("l hand")
                || lower.Contains("weapon bone left")
                || lower.Contains("shield bone"))
            {
                return ActorAnimationBlendMask.LeftArm;
            }
            if (lower.Contains("r clavicle")
                || lower.Contains("r upperarm")
                || lower.Contains("r forearm")
                || lower.Contains("r hand")
                || lower.Contains("weapon bone"))
            {
                return ActorAnimationBlendMask.RightArm;
            }
            if (lower.Contains("pelvis")
                || lower.Contains("groin")
                || lower.Contains("thigh")
                || lower.Contains("calf")
                || lower.Contains("ankle")
                || lower.Contains("foot")
                || lower.Contains("toe")
                || lower.Contains("knee")
                || lower.Contains("leg")
                || lower.Contains("tail"))
            {
                return ActorAnimationBlendMask.LowerBody;
            }

            return ActorAnimationBlendMask.Torso;
        }

        static ulong BuildCatalogSignature(ref ActorAnimationCatalogBlob catalog)
        {
            ulong signature = 14695981039346656037UL;
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

        static void WriteBuffer<T>(GraphicsBuffer buffer, NativeArray<T> data) where T : unmanaged
        {
            var dst = buffer.LockBufferForWrite<T>(0, data.Length);
            dst.CopyFrom(data);
            buffer.UnlockBufferAfterWrite<T>(data.Length);
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
