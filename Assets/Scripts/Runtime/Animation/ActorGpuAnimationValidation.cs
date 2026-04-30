using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Rendering;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorGpuAnimationValidation
    {
        const float PoseTolerance = 0.0005f;
        const float MatrixTolerance = 0.001f;

        static ulong s_LastReportKey;
        static bool s_LastReportSuccess;

        internal static bool Enabled;
        internal static int ActorIndex;

        struct SampledPose
        {
            public float3 Position;
            public quaternion Rotation;
            public float Scale;
            public float3 AxisRotation;
            public byte AxisFlags;
            public byte HasTrack;
            public int AxisOrder;
        }

        struct LocalPose
        {
            public float3 Position;
            public quaternion Rotation;
            public float Scale;
            public byte HasTrack;
        }

        public static void Validate(
            Entity entity,
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            in ActorGpuAnimationState gpuState,
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            DynamicBuffer<ActorSkinMesh> skinMeshes,
            GraphicsBuffer boneMatrixBuffer)
        {
            if (!Enabled || boneMatrixBuffer == null || gpuState.BoneMatrixCount <= 0)
                return;

            ulong reportHash = BuildRequestReportHash(requests, ref catalog);
            if (reportHash == 0UL)
            {
                LogOnce(entity, 0UL, skeleton.SkeletonIndex, true,
                    "[ActorGpuAnimationValidation] No valid GPU animation request to validate.");
                return;
            }

            var referenceLocal = BuildReferenceLocalPose(ref catalog, skeleton, requests);
            var expectedSkinMatrices = new List<float4x4>(gpuState.BoneMatrixCount);
            var matrixSkinMeshIndices = new List<int>(gpuState.BoneMatrixCount);
            var matrixBoneIndices = new List<int>(gpuState.BoneMatrixCount);
            BuildExpectedSkinMatrices(ref catalog, skeleton, skinMeshes, referenceLocal, expectedSkinMatrices, matrixSkinMeshIndices, matrixBoneIndices);

            if (expectedSkinMatrices.Count != gpuState.BoneMatrixCount)
            {
                LogOnce(entity, reportHash, skeleton.SkeletonIndex, false,
                    $"[ActorGpuAnimationValidation] Expected {expectedSkinMatrices.Count} skin matrices but GPU state reserved {gpuState.BoneMatrixCount}, requestHash {reportHash}, skeletonIndex {skeleton.SkeletonIndex}.");
                return;
            }

            var gpuMatrices = new ActorGpuAnimationMatrixGpu[gpuState.BoneMatrixCount];
            boneMatrixBuffer.GetData(gpuMatrices, 0, gpuState.BoneMatrixOffset, gpuState.BoneMatrixCount);

            if (TryFindSkinMatrixMismatch(gpuMatrices, expectedSkinMatrices, matrixSkinMeshIndices, matrixBoneIndices, out int matrixIndex, out float matrixError))
            {
                LogOnce(entity, reportHash, skeleton.SkeletonIndex, false,
                    $"[ActorGpuAnimationValidation] GPU skin matrix mismatch at matrix {matrixIndex} (skinMeshIndex {matrixSkinMeshIndices[matrixIndex]}, boneIndex {matrixBoneIndices[matrixIndex]}), error {matrixError:F6}, requestHash {reportHash}, skeletonIndex {skeleton.SkeletonIndex}. This likely points to GPU packing/layout or remaining compute parity issues.");
                return;
            }

        }

        static ulong BuildRequestReportHash(DynamicBuffer<ActorGpuAnimationRequest> requests, ref ActorAnimationCatalogBlob catalog)
        {
            ulong hash = 14695981039346656037UL;
            bool hasRequest = false;
            for (int i = 0; i < requests.Length; i++)
            {
                var candidate = requests[i];
                if (candidate.Weight <= 0f || (uint)candidate.ClipIndex >= (uint)catalog.Clips.Length)
                    continue;

                hasRequest = true;
                hash ^= candidate.ClipHash;
                hash *= 1099511628211UL;
                hash ^= (uint)candidate.Mask;
                hash *= 1099511628211UL;
            }

            return hasRequest ? hash : 0UL;
        }

        static SampledPose[] BuildReferenceSampledPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, in ActorGpuAnimationRequest request)
        {
            int boneCount = ResolveBoneCount(ref catalog, skeleton);
            var sampled = CreateBindSampledPose(ref catalog, skeleton, boneCount);
            if (boneCount == 0 || request.Weight <= 0f || (uint)request.ClipIndex >= (uint)catalog.Clips.Length)
                return sampled;

            var clip = catalog.Clips[request.ClipIndex];
            SampleClipTracks(ref catalog, clip, request.Time, sampled);
            ApplyXyzRotations(sampled);
            return sampled;
        }

        static LocalPose[] BuildReferenceLocalPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, in ActorGpuAnimationRequest request, SampledPose[] sampled)
        {
            int boneCount = ResolveBoneCount(ref catalog, skeleton);
            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var local = CreateBindLocalPose(ref catalog, skeleton, boneCount);
            if (boneCount == 0)
                return local;

            float weight = math.clamp(request.Weight, 0f, 1f);
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                var bindBone = catalog.Bones[firstBoneIndex + boneIndex];
                if (sampled[boneIndex].HasTrack == 0)
                {
                    local[boneIndex] = new LocalPose
                    {
                        Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(bindBone.BindPosition),
                        Rotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(bindBone.BindRotation),
                        Scale = bindBone.BindScale <= 0f ? 1f : bindBone.BindScale,
                        HasTrack = 0,
                    };
                    continue;
                }

                local[boneIndex] = new LocalPose
                {
                    Position = math.lerp(
                        ActorAnimationSpaceConversion.SourceTranslationToUnity(bindBone.BindPosition),
                        sampled[boneIndex].Position,
                        weight),
                    Rotation = SafeNormalize(math.slerp(
                        ActorAnimationSpaceConversion.SourceQuaternionToUnity(bindBone.BindRotation),
                        SafeNormalize(sampled[boneIndex].Rotation),
                        weight)),
                    Scale = math.lerp(bindBone.BindScale <= 0f ? 1f : bindBone.BindScale, sampled[boneIndex].Scale, weight),
                    HasTrack = 1,
                };
            }

            StripAccumulationBone(ref catalog, skeleton, local);
            return local;
        }

        static SampledPose[] BuildGpuIntentSampledPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, in ActorGpuAnimationRequest request)
        {
            return BuildReferenceSampledPose(ref catalog, skeleton, request);
        }

        static LocalPose[] BuildGpuIntentLocalPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, in ActorGpuAnimationRequest request, SampledPose[] sampled)
        {
            return BuildReferenceLocalPose(ref catalog, skeleton, request, sampled);
        }

        static LocalPose[] BuildReferenceLocalPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, DynamicBuffer<ActorGpuAnimationRequest> requests)
        {
            int boneCount = ResolveBoneCount(ref catalog, skeleton);
            var local = CreateBindLocalPose(ref catalog, skeleton, boneCount);
            if (boneCount == 0)
                return local;

            var bind = CreateBindLocalPose(ref catalog, skeleton, boneCount);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.Weight <= 0f || (uint)request.ClipIndex >= (uint)catalog.Clips.Length)
                    continue;

                var sampled = BuildReferenceSampledPose(ref catalog, skeleton, request);
                float weight = math.clamp(request.Weight, 0f, 1f);
                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    if (sampled[boneIndex].HasTrack == 0)
                        continue;

                    var basePose = request.HasPreviousLayer != 0 ? local[boneIndex] : bind[boneIndex];
                    local[boneIndex] = new LocalPose
                    {
                        Position = math.lerp(basePose.Position, sampled[boneIndex].Position, weight),
                        Rotation = SafeNormalize(math.slerp(basePose.Rotation, SafeNormalize(sampled[boneIndex].Rotation), weight)),
                        Scale = math.lerp(basePose.Scale, sampled[boneIndex].Scale, weight),
                        HasTrack = 1,
                    };
                }
            }

            StripAccumulationBoneTranslation(ref catalog, skeleton, local);
            return local;
        }

        static void BuildExpectedSkinMatrices(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorSkinMesh> skinMeshes,
            LocalPose[] localPoses,
            List<float4x4> skinMatrices,
            List<int> skinMeshIndices,
            List<int> boneIndices)
        {
            var localToRoot = ComposeLocalToRoot(ref catalog, skeleton, localPoses);
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                var skinMesh = catalog.SkinMeshes[skinMeshIndex];
                int reservedCount = math.max(1, skinMesh.SkinBoneCount);
                if (skinMesh.IsRigid != 0)
                {
                    float4x4 attach = (uint)skinMeshes[i].AttachBoneIndex < (uint)localToRoot.Length
                        ? localToRoot[skinMeshes[i].AttachBoneIndex]
                        : float4x4.identity;
                    float4x4 mirror = skinMeshes[i].RigidMirrorX != 0
                        ? new float4x4(
                            new float4(-1f, 0f, 0f, 0f),
                            new float4(0f, 1f, 0f, 0f),
                            new float4(0f, 0f, 1f, 0f),
                            new float4(0f, 0f, 0f, 1f))
                        : float4x4.identity;
                    float4x4 geometryToSkeleton = ActorAnimationSpaceConversion.SourceAffineToUnity(skinMesh.GeometryToSkeleton);
                    skinMatrices.Add(math.mul(attach, math.mul(mirror, geometryToSkeleton)));
                    skinMeshIndices.Add(skinMeshIndex);
                    boneIndices.Add(skinMeshes[i].AttachBoneIndex);

                    for (int pad = 1; pad < reservedCount; pad++)
                    {
                        skinMatrices.Add(float4x4.identity);
                        skinMeshIndices.Add(skinMeshIndex);
                        boneIndices.Add(-1);
                    }
                    continue;
                }

                int firstSkinBone = skinMesh.FirstSkinBoneIndex;
                int end = math.min(catalog.SkinBones.Length, firstSkinBone + skinMesh.SkinBoneCount);
                int written = 0;
                for (int skinBoneIndex = firstSkinBone; skinBoneIndex < end; skinBoneIndex++)
                {
                    var skinBone = catalog.SkinBones[skinBoneIndex];
                    int boneIndex = skinBone.BoneIndex;
                    float4x4 pose = (uint)boneIndex < (uint)localToRoot.Length ? localToRoot[boneIndex] : float4x4.identity;
                    float4x4 unityGeometryToSkeleton = ActorAnimationSpaceConversion.SourceAffineToUnity(skinMesh.GeometryToSkeleton);
                    float4x4 unityBindPose = ActorAnimationSpaceConversion.SourceAffineToUnity(skinBone.BindPose);
                    skinMatrices.Add(math.mul(math.mul(unityGeometryToSkeleton, pose), unityBindPose));
                    skinMeshIndices.Add(skinMeshIndex);
                    boneIndices.Add(boneIndex);
                    written++;
                }

                if (written == 0)
                {
                    skinMatrices.Add(float4x4.identity);
                    skinMeshIndices.Add(skinMeshIndex);
                    boneIndices.Add(-1);
                    written = 1;
                }

                for (int pad = written; pad < reservedCount; pad++)
                {
                    skinMatrices.Add(float4x4.identity);
                    skinMeshIndices.Add(skinMeshIndex);
                    boneIndices.Add(-1);
                }
            }
        }

        static float4x4[] ComposeLocalToRoot(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, LocalPose[] localPoses)
        {
            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var localToRoot = new float4x4[localPoses.Length];
            for (int boneIndex = 0; boneIndex < localPoses.Length; boneIndex++)
            {
                var bone = catalog.Bones[firstBoneIndex + boneIndex];
                float4x4 local;
                if (localPoses[boneIndex].HasTrack == 0)
                {
                    local = ActorAnimationSpaceConversion.SourceAffineToUnity(bone.BindLocalMatrix);
                }
                else
                {
                    float scale = localPoses[boneIndex].Scale <= 0f ? 1f : localPoses[boneIndex].Scale;
                    local = float4x4.TRS(localPoses[boneIndex].Position, SafeNormalize(localPoses[boneIndex].Rotation), new float3(scale));
                }

                localToRoot[boneIndex] = bone.ParentIndex >= 0 && bone.ParentIndex < boneIndex
                    ? math.mul(localToRoot[bone.ParentIndex], local)
                    : local;
            }

            return localToRoot;
        }

        static void SampleClipTracks(
            ref ActorAnimationCatalogBlob catalog,
            ActorAnimationClipBlob clip,
            float layerTime,
            SampledPose[] sampled)
        {
            int trackEnd = math.min(catalog.Tracks.Length, clip.FirstTrackIndex + clip.TrackCount);
            for (int trackIndex = clip.FirstTrackIndex; trackIndex < trackEnd; trackIndex++)
            {
                var track = catalog.Tracks[trackIndex];
                int boneIndex = track.TargetBoneIndex;
                if (track.KeyCount <= 0 || track.FirstKeyIndex < 0 || (uint)boneIndex >= (uint)sampled.Length)
                    continue;

                float trackTime = MapTrackTime(layerTime, track);
                var pose = sampled[boneIndex];
                pose.HasTrack = 1;
                switch (track.Kind)
                {
                    case ActorAnimationTrackKind.Translation:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(value.xyz);
                        break;
                    }
                    case ActorAnimationTrackKind.Rotation:
                    {
                        pose.Rotation = SampleSourceRotation(ref catalog, track, trackTime);
                        break;
                    }
                    case ActorAnimationTrackKind.Scale:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.Scale = value.x <= 0f ? 1f : value.x;
                        break;
                    }
                    case ActorAnimationTrackKind.XRotation:
                    {
                        pose.AxisRotation.x = SampleValue(ref catalog, track, trackTime).x;
                        pose.AxisFlags |= 1;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                    case ActorAnimationTrackKind.YRotation:
                    {
                        pose.AxisRotation.y = SampleValue(ref catalog, track, trackTime).x;
                        pose.AxisFlags |= 2;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                    case ActorAnimationTrackKind.ZRotation:
                    {
                        pose.AxisRotation.z = SampleValue(ref catalog, track, trackTime).x;
                        pose.AxisFlags |= 4;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                }

                sampled[boneIndex] = pose;
            }
        }

        static void ApplyXyzRotations(SampledPose[] sampled)
        {
            for (int i = 0; i < sampled.Length; i++)
            {
                if (sampled[i].AxisFlags == 0)
                    continue;

                sampled[i].Rotation = ComposeSourceXyzRotation(sampled[i].AxisRotation, sampled[i].AxisOrder);
            }
        }

        static void StripAccumulationBone(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, LocalPose[] local)
        {
            if ((uint)skeleton.AccumulationBoneIndex >= (uint)local.Length)
                return;

            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var bindBone = catalog.Bones[firstBoneIndex + skeleton.AccumulationBoneIndex];
            local[skeleton.AccumulationBoneIndex] = new LocalPose
            {
                Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(bindBone.BindPosition),
                Rotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(bindBone.BindRotation),
                Scale = bindBone.BindScale <= 0f ? 1f : bindBone.BindScale,
                HasTrack = 0,
            };
        }

        static void StripAccumulationBoneTranslation(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, LocalPose[] local)
        {
            if ((uint)skeleton.AccumulationBoneIndex >= (uint)local.Length)
                return;

            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var bindBone = catalog.Bones[firstBoneIndex + skeleton.AccumulationBoneIndex];
            var pose = local[skeleton.AccumulationBoneIndex];
            pose.Position.x = ActorAnimationSpaceConversion.SourceTranslationToUnity(bindBone.BindPosition).x;
            pose.Position.z = ActorAnimationSpaceConversion.SourceTranslationToUnity(bindBone.BindPosition).z;
            local[skeleton.AccumulationBoneIndex] = pose;
        }

        static SampledPose[] CreateBindSampledPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, int boneCount)
        {
            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var sampled = new SampledPose[boneCount];
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                var bone = catalog.Bones[firstBoneIndex + boneIndex];
                sampled[boneIndex] = new SampledPose
                {
                    Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(bone.BindPosition),
                    Rotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(bone.BindRotation),
                    Scale = bone.BindScale <= 0f ? 1f : bone.BindScale,
                    AxisRotation = float3.zero,
                    AxisFlags = 0,
                    HasTrack = 0,
                    AxisOrder = 0,
                };
            }

            return sampled;
        }

        static LocalPose[] CreateBindLocalPose(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton, int boneCount)
        {
            int firstBoneIndex = ResolveFirstBoneIndex(ref catalog, skeleton);
            var local = new LocalPose[boneCount];
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                var bone = catalog.Bones[firstBoneIndex + boneIndex];
                local[boneIndex] = new LocalPose
                {
                    Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(bone.BindPosition),
                    Rotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(bone.BindRotation),
                    Scale = bone.BindScale <= 0f ? 1f : bone.BindScale,
                    HasTrack = 0,
                };
            }

            return local;
        }

        static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton)
        {
            if ((uint)skeleton.SkeletonIndex >= (uint)catalog.Skeletons.Length)
                return 0;
            return math.min(skeleton.BoneCount, catalog.Skeletons[skeleton.SkeletonIndex].BoneCount);
        }

        static int ResolveFirstBoneIndex(ref ActorAnimationCatalogBlob catalog, in ActorSkeleton skeleton)
        {
            if ((uint)skeleton.SkeletonIndex >= (uint)catalog.Skeletons.Length)
                return 0;
            return catalog.Skeletons[skeleton.SkeletonIndex].FirstBoneIndex;
        }

        static bool TryFindSampledMismatch(SampledPose[] reference, SampledPose[] gpuIntent, out int boneIndex, out float error)
        {
            boneIndex = -1;
            error = 0f;
            int boneCount = math.min(reference.Length, gpuIntent.Length);
            for (int i = 0; i < boneCount; i++)
            {
                float poseError = math.max(
                    math.length(reference[i].Position - gpuIntent[i].Position),
                    math.max(QuaternionError(reference[i].Rotation, gpuIntent[i].Rotation), math.abs(reference[i].Scale - gpuIntent[i].Scale)));
                if (poseError <= PoseTolerance)
                    continue;

                boneIndex = i;
                error = poseError;
                return true;
            }

            return false;
        }

        static bool TryFindLocalToRootMismatch(
            LocalPose[] reference,
            LocalPose[] gpuIntent,
            in ActorSkeleton skeleton,
            ref ActorAnimationCatalogBlob catalog,
            out int boneIndex,
            out float error)
        {
            boneIndex = -1;
            error = 0f;
            var referenceLocalToRoot = ComposeLocalToRoot(ref catalog, skeleton, reference);
            var gpuLocalToRoot = ComposeLocalToRoot(ref catalog, skeleton, gpuIntent);
            int boneCount = math.min(referenceLocalToRoot.Length, gpuLocalToRoot.Length);
            for (int i = 0; i < boneCount; i++)
            {
                float matrixError = MatrixError(referenceLocalToRoot[i], gpuLocalToRoot[i]);
                if (matrixError <= MatrixTolerance)
                    continue;

                boneIndex = i;
                error = matrixError;
                return true;
            }

            return false;
        }

        static bool TryFindSkinMatrixMismatch(
            ActorGpuAnimationMatrixGpu[] gpuMatrices,
            List<float4x4> expectedMatrices,
            List<int> skinMeshIndices,
            List<int> boneIndices,
            out int matrixIndex,
            out float error)
        {
            matrixIndex = -1;
            error = 0f;
            int matrixCount = math.min(gpuMatrices.Length, expectedMatrices.Count);
            for (int i = 0; i < matrixCount; i++)
            {
                float matrixError = MatrixError(FromGpuMatrix(gpuMatrices[i]), expectedMatrices[i]);
                if (matrixError <= MatrixTolerance)
                    continue;

                matrixIndex = i;
                error = matrixError;
                return true;
            }

            if (gpuMatrices.Length != expectedMatrices.Count)
            {
                matrixIndex = math.min(gpuMatrices.Length, expectedMatrices.Count);
                error = math.abs(gpuMatrices.Length - expectedMatrices.Count);
                return true;
            }

            return false;
        }

        static float4x4 FromGpuMatrix(ActorGpuAnimationMatrixGpu matrix)
        {
            return new float4x4(
                new float4(matrix.Row0.x, matrix.Row1.x, matrix.Row2.x, 0f),
                new float4(matrix.Row0.y, matrix.Row1.y, matrix.Row2.y, 0f),
                new float4(matrix.Row0.z, matrix.Row1.z, matrix.Row2.z, 0f),
                new float4(matrix.Row0.w, matrix.Row1.w, matrix.Row2.w, 1f));
        }

        static float MatrixError(float4x4 a, float4x4 b)
        {
            return math.max(
                math.max(MaxAbs(a.c0 - b.c0), MaxAbs(a.c1 - b.c1)),
                math.max(MaxAbs(a.c2 - b.c2), MaxAbs(a.c3 - b.c3)));
        }

        static float MaxAbs(float4 value)
        {
            return math.cmax(math.abs(value));
        }

        static float QuaternionError(quaternion a, quaternion b)
        {
            float4 direct = math.abs(a.value - b.value);
            float4 negated = math.abs(a.value + b.value);
            return math.min(math.cmax(direct), math.cmax(negated));
        }

        static float MapTrackTime(float layerTime, ActorAnimationTrackBlob track)
        {
            float frequency = track.Frequency == 0f ? 1f : track.Frequency;
            float time = layerTime * frequency + track.Phase;
            if (track.TimeStop <= track.TimeStart)
                return time;

            if (time >= track.TimeStart && time <= track.TimeStop)
                return time;

            const ushort extrapolationMask = 0x6;
            const ushort extrapolationReverse = 0x2;
            const ushort extrapolationConstant = 0x4;
            ushort extrapolation = (ushort)(track.ControllerFlags & extrapolationMask);
            if (extrapolation == extrapolationReverse)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                float cycleFloor = math.floor(cycles);
                float remainder = (cycles - cycleFloor) * duration;
                return ((int)math.abs(cycleFloor) & 1) == 0
                    ? track.TimeStart + remainder
                    : track.TimeStop - remainder;
            }

            if (extrapolation != extrapolationConstant)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                return track.TimeStart + (cycles - math.floor(cycles)) * duration;
            }

            return time < track.TimeStart ? track.TimeStart : track.TimeStop;
        }

        static float4 SampleValue(ref ActorAnimationCatalogBlob catalog, ActorAnimationTrackBlob track, float time)
        {
            int first = track.FirstKeyIndex;
            int count = track.KeyCount;
            if (count <= 0 || first < 0 || first >= catalog.Keys.Length)
                return default;

            int last = math.min(catalog.Keys.Length - 1, first + count - 1);
            if (first >= last || time <= catalog.Keys[first].Time)
                return catalog.Keys[first].Value;
            if (time >= catalog.Keys[last].Time)
                return catalog.Keys[last].Value;

            int right = first + 1;
            while (right <= last && catalog.Keys[right].Time < time)
                right++;

            int left = math.max(first, right - 1);
            var a = catalog.Keys[left];
            var b = catalog.Keys[right];
            float span = math.max(0.00001f, b.Time - a.Time);
            float t = math.saturate((time - a.Time) / span);

            return track.Interpolation switch
            {
                ActorAnimationInterpolation.Constant => a.Value,
                ActorAnimationInterpolation.Quadratic => Hermite(a, b, t, span),
                _ => math.lerp(a.Value, b.Value, t),
            };
        }

        static quaternion SampleSourceRotation(ref ActorAnimationCatalogBlob catalog, ActorAnimationTrackBlob track, float time)
        {
            int first = track.FirstKeyIndex;
            int count = track.KeyCount;
            if (count <= 0 || first < 0 || first >= catalog.Keys.Length)
                return quaternion.identity;

            int last = math.min(catalog.Keys.Length - 1, first + count - 1);
            if (first >= last || time <= catalog.Keys[first].Time)
                return KeyRotation(catalog.Keys[first]);
            if (time >= catalog.Keys[last].Time)
                return KeyRotation(catalog.Keys[last]);

            int right = first + 1;
            while (right <= last && catalog.Keys[right].Time < time)
                right++;

            int left = math.max(first, right - 1);
            var a = catalog.Keys[left];
            var b = catalog.Keys[right];
            if (track.Interpolation == ActorAnimationInterpolation.Constant)
                return KeyRotation(a);

            float span = math.max(0.00001f, b.Time - a.Time);
            float t = math.saturate((time - a.Time) / span);
            return SafeNormalize(math.slerp(KeyRotation(a), KeyRotation(b), t));
        }

        static quaternion KeyRotation(ActorAnimationKeyBlob key)
        {
            return ActorAnimationSpaceConversion.SourceQuaternionToUnity(
                new quaternion(key.Value.x, key.Value.y, key.Value.z, key.Value.w));
        }

        static float4 Hermite(ActorAnimationKeyBlob a, ActorAnimationKeyBlob b, float t, float span)
        {
            float4 p0 = a.Value;
            float4 p1 = b.Value;
            float4 m0 = a.OutTangent * span;
            float4 m1 = b.InTangent * span;
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0
                   + (t3 - 2f * t2 + t) * m0
                   + (-2f * t3 + 3f * t2) * p1
                   + (t3 - t2) * m1;
        }

        static quaternion ComposeSourceXyzRotation(float3 angles, int axisOrder)
        {
            quaternion x = quaternion.AxisAngle(new float3(1f, 0f, 0f), angles.x);
            quaternion y = quaternion.AxisAngle(new float3(0f, 1f, 0f), angles.y);
            quaternion z = quaternion.AxisAngle(new float3(0f, 0f, 1f), angles.z);

            quaternion raw = axisOrder switch
            {
                1 => SafeNormalize(math.mul(math.mul(x, z), y)),
                2 => SafeNormalize(math.mul(math.mul(y, z), x)),
                3 => SafeNormalize(math.mul(math.mul(y, x), z)),
                4 => SafeNormalize(math.mul(math.mul(z, x), y)),
                5 => SafeNormalize(math.mul(math.mul(z, y), x)),
                _ => SafeNormalize(math.mul(math.mul(x, y), z)),
            };
            return ActorAnimationSpaceConversion.SourceQuaternionToUnity(raw);
        }

        static quaternion SafeNormalize(quaternion value)
        {
            return math.lengthsq(value.value) > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;
        }

        static void LogOnce(Entity entity, ulong clipHash, int skeletonIndex, bool success, string message)
        {
            ulong reportKey = ((ulong)(uint)entity.Index << 32)
                              ^ (uint)entity.Version
                              ^ clipHash
                              ^ ((ulong)(uint)skeletonIndex << 16)
                              ^ (success ? 1UL : 0UL);
            if (reportKey == s_LastReportKey && success == s_LastReportSuccess)
                return;

            s_LastReportKey = reportKey;
            s_LastReportSuccess = success;
            if (success)
                Debug.Log(message);
            else
                Debug.LogWarning(message);
        }
    }
}
