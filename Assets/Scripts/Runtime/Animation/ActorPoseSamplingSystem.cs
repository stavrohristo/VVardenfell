#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Burst;
using Unity.Collections;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationControllerSystem))]
    public partial struct ActorPoseSamplingSystem : ISystem
    {
        EntityQuery _query;
        
        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<ActorAnimationState>(),
                    ComponentType.ReadOnly<ActorSkeleton>(),
                    ComponentType.ReadWrite<ActorBone>(),
                    ComponentType.ReadWrite<ActorSampledBonePose>(),
                }
            });
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<ActorAnimationRuntimeSettings>()
                && SystemAPI.GetSingleton<ActorAnimationRuntimeSettings>().Mode == ActorAnimationRuntimeMode.Gpu)
            {
                return;
            }

            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;

            state.Dependency = new SampleActorPoseWithOverlaysJob { Catalog = catalog }
                .ScheduleParallel(state.Dependency);
            state.Dependency = new SampleActorPoseWithoutOverlaysJob { Catalog = catalog }
                .ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct SampleActorPoseWithOverlaysJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                in ActorAnimationState animation,
                in ActorSkeleton skeleton,
                DynamicBuffer<ActorBone> bones,
                DynamicBuffer<ActorSampledBonePose> sampled,
                DynamicBuffer<ActorAnimationOverlayState> overlays)
            {
                if (!Catalog.IsCreated || bones.Length == 0)
                    return;

                ref var catalog = ref Catalog.Value;
                EnsureSampledLength(sampled, bones.Length);
                ResetToBindPose(ref catalog, skeleton, bones);

                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.LowerBody);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.Torso);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.LeftArm);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.RightArm);
                SampleBestOverlayForMask(ref catalog, skeleton, bones, sampled, overlays, ActorAnimationBlendMask.LowerBody);
                SampleBestOverlayForMask(ref catalog, skeleton, bones, sampled, overlays, ActorAnimationBlendMask.Torso);
                SampleBestOverlayForMask(ref catalog, skeleton, bones, sampled, overlays, ActorAnimationBlendMask.LeftArm);
                SampleBestOverlayForMask(ref catalog, skeleton, bones, sampled, overlays, ActorAnimationBlendMask.RightArm);

                ComposeHierarchy(ref catalog, skeleton, bones);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        [WithNone(typeof(ActorAnimationOverlayState))]
        partial struct SampleActorPoseWithoutOverlaysJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                in ActorAnimationState animation,
                in ActorSkeleton skeleton,
                DynamicBuffer<ActorBone> bones,
                DynamicBuffer<ActorSampledBonePose> sampled)
            {
                if (!Catalog.IsCreated || bones.Length == 0)
                    return;

                ref var catalog = ref Catalog.Value;
                EnsureSampledLength(sampled, bones.Length);
                ResetToBindPose(ref catalog, skeleton, bones);

                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.LowerBody);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.Torso);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.LeftArm);
                SampleMainForMask(ref catalog, skeleton, bones, sampled, animation, ActorAnimationBlendMask.RightArm);

                ComposeHierarchy(ref catalog, skeleton, bones);
            }
        }

        static void EnsureSampledLength(DynamicBuffer<ActorSampledBonePose> sampled, int boneCount)
        {
            if (sampled.Length != boneCount)
                sampled.ResizeUninitialized(boneCount);
        }

        static void SampleMainForMask(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled,
            in ActorAnimationState animation,
            ActorAnimationBlendMask mask)
        {
            if (!ActorAnimationPlaybackUtility.IsActive(animation.Playback))
                return;

            if (animation.TransitionActive != 0
                && ActorAnimationPlaybackUtility.CanSample(animation.TransitionPlayback)
                && animation.TransitionDuration > 0f)
            {
                float transitionWeight = math.saturate(animation.TransitionTime / animation.TransitionDuration);
                SamplePlaybackForMask(
                    ref catalog,
                    skeleton,
                    bones,
                    sampled,
                    animation.TransitionPlayback.ClipIndex,
                    animation.TransitionPlayback.Time,
                    1f,
                    mask,
                    hasPreviousLayer: false);
                SamplePlaybackForMask(
                    ref catalog,
                    skeleton,
                    bones,
                    sampled,
                    animation.Playback.ClipIndex,
                    animation.Playback.Time,
                    transitionWeight,
                    mask,
                    hasPreviousLayer: true);
                return;
            }

            SamplePlaybackForMask(
                ref catalog,
                skeleton,
                bones,
                sampled,
                animation.Playback.ClipIndex,
                animation.Playback.Time,
                1f,
                mask,
                hasPreviousLayer: false);
        }

        static void SampleBestOverlayForMask(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ActorAnimationBlendMask mask)
        {
            int selectedOverlayIndex = SelectBestOverlay(overlays, mask);
            if (selectedOverlayIndex < 0)
                return;

            var overlay = overlays[selectedOverlayIndex];
            float weight = math.clamp(overlay.Weight, 0f, 1f);
            if (weight <= 0f)
                return;

            SamplePlaybackForMask(ref catalog, skeleton, bones, sampled, overlay.Playback.ClipIndex, overlay.Playback.Time, weight, mask, hasPreviousLayer: true);
        }

        static void SamplePlaybackForMask(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled,
            int clipIndex,
            float time,
            float weight,
            ActorAnimationBlendMask mask,
            bool hasPreviousLayer)
        {
            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return;

            var clip = catalog.Clips[clipIndex];
            if (clip.TrackCount <= 0 || clip.FirstTrackIndex < 0)
                return;

            CopyBindPose(ref catalog, skeleton, bones, sampled);
            SampleClipTracks(ref catalog, skeleton, clip, time, sampled, bones, mask);
            ApplyXyzRotations(sampled);
            BlendLayerIntoBones(ref catalog, skeleton, bones, sampled, weight, hasPreviousLayer);
        }

        static int SelectBestOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays, ActorAnimationBlendMask mask)
        {
            int bestIndex = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < overlays.Length; i++)
            {
                var overlay = overlays[i];
                if (!ActorAnimationPlaybackUtility.IsActive(overlay.Playback) || overlay.Weight <= 0f || (overlay.Mask & mask) == 0)
                    continue;
                if (overlay.Priority < bestPriority)
                    continue;

                bestPriority = overlay.Priority;
                bestIndex = i;
            }

            return bestIndex;
        }

        static void ResetToBindPose(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (!ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, i, out var bindBone))
                    continue;

                var bone = bones[i];
                bone.LocalPosition = ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(bindBone);
                bone.LocalRotation = SafeNormalize(ActorAnimationCatalogRuntimeUtility.RuntimeBindRotation(bindBone));
                bone.LocalScale = ActorAnimationCatalogRuntimeUtility.RuntimeBindScale(bindBone);
                bone.LocalPoseAnimated = 0;
                bones[i] = bone;
            }
        }

        static void CopyBindPose(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (!ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, i, out var bindBone))
                    continue;

                sampled[i] = new ActorSampledBonePose
                {
                    Position = ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(bindBone),
                    Rotation = SafeNormalize(ActorAnimationCatalogRuntimeUtility.RuntimeBindRotation(bindBone)),
                    Scale = ActorAnimationCatalogRuntimeUtility.RuntimeBindScale(bindBone),
                    AxisRotation = float3.zero,
                    AxisFlags = 0,
                    HasTrack = 0,
                    AxisOrder = 0,
                };
            }
        }

        static void SampleClipTracks(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            ActorAnimationClipBlob clip,
            float layerTime,
            DynamicBuffer<ActorSampledBonePose> sampled,
            DynamicBuffer<ActorBone> bones,
            ActorAnimationBlendMask mask)
        {
            int trackEnd = math.min(catalog.Tracks.Length, clip.FirstTrackIndex + clip.TrackCount);
            for (int trackIndex = clip.FirstTrackIndex; trackIndex < trackEnd; trackIndex++)
            {
                var track = catalog.Tracks[trackIndex];
                int boneIndex = track.TargetBoneIndex;
                if (track.KeyCount <= 0
                    || track.FirstKeyIndex < 0
                    || (uint)boneIndex >= (uint)sampled.Length
                    || (uint)boneIndex >= (uint)bones.Length
                    || (((ActorAnimationBlendMask)track.BlendMask & mask) == 0))
                {
                    continue;
                }

                float trackTime = MapTrackTime(layerTime, track);
                var pose = sampled[boneIndex];
                pose.HasTrack = 1;
                switch (track.Kind)
                {
                    case ActorAnimationTrackKind.Translation:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.Position = ActorAnimationSpaceConversion.SourceTranslationToUnity(value.xyz);
                        if (boneIndex == skeleton.AccumulationBoneIndex
                            && ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, boneIndex, out var bindBone))
                        {
                            float3 bindPosition = ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(bindBone);
                            pose.Position.x = bindPosition.x;
                            pose.Position.z = bindPosition.z;
                        }
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
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.AxisRotation.x = value.x;
                        pose.AxisFlags |= 1;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                    case ActorAnimationTrackKind.YRotation:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.AxisRotation.y = value.x;
                        pose.AxisFlags |= 2;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                    case ActorAnimationTrackKind.ZRotation:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.AxisRotation.z = value.x;
                        pose.AxisFlags |= 4;
                        pose.AxisOrder = track.AxisOrder;
                        break;
                    }
                }
                sampled[boneIndex] = pose;
            }
        }

        static float MapTrackTime(float layerTime, ActorAnimationTrackBlob track)
        {
            float frequency = track.Frequency == 0f ? 1f : track.Frequency;
            float time = layerTime * frequency + track.Phase;
            if (track.TimeStop <= track.TimeStart)
                return time;

            if (time >= track.TimeStart && time <= track.TimeStop)
                return time;

            const ushort ExtrapolationMask = 0x6;
            const ushort ExtrapolationReverse = 0x2;
            const ushort ExtrapolationConstant = 0x4;
            ushort extrapolation = (ushort)(track.ControllerFlags & ExtrapolationMask);
            if (extrapolation == ExtrapolationReverse)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                float cycleFloor = math.floor(cycles);
                float remainder = (cycles - cycleFloor) * duration;
                return ((int)math.abs(cycleFloor) & 1) == 0
                    ? track.TimeStart + remainder
                    : track.TimeStop - remainder;
            }

            if (extrapolation != ExtrapolationConstant)
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
            => ActorAnimationSpaceConversion.SourceQuaternionToUnity(
                new quaternion(key.Value.x, key.Value.y, key.Value.z, key.Value.w));

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

        static void ApplyXyzRotations(DynamicBuffer<ActorSampledBonePose> sampled)
        {
            for (int i = 0; i < sampled.Length; i++)
            {
                var pose = sampled[i];
                if (pose.AxisFlags == 0)
                    continue;

                pose.Rotation = ComposeSourceXyzRotation(pose.AxisRotation, pose.AxisOrder);
                sampled[i] = pose;
            }
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

        static void BlendLayerIntoBones(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled,
            float weight,
            bool hasPreviousLayer)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                var pose = sampled[i];
                if (pose.HasTrack == 0)
                    continue;

                ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, i, out var bindBone);
                float3 basePosition = hasPreviousLayer ? bone.LocalPosition : ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(bindBone);
                quaternion baseRotation = hasPreviousLayer ? SafeNormalize(bone.LocalRotation) : SafeNormalize(ActorAnimationCatalogRuntimeUtility.RuntimeBindRotation(bindBone));
                float baseScale = hasPreviousLayer ? bone.LocalScale : ActorAnimationCatalogRuntimeUtility.RuntimeBindScale(bindBone);

                bone.LocalPosition = math.lerp(basePosition, pose.Position, weight);
                bone.LocalRotation = SafeNormalize(math.slerp(baseRotation, SafeNormalize(pose.Rotation), weight));
                bone.LocalScale = math.lerp(baseScale, pose.Scale, weight);
                bone.LocalPoseAnimated = 1;
                bones[i] = bone;
            }
        }

        static void ComposeHierarchy(
            ref ActorAnimationCatalogBlob catalog,
            in ActorSkeleton skeleton,
            DynamicBuffer<ActorBone> bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (!ActorAnimationCatalogRuntimeUtility.TryGetBoneBlob(ref catalog, skeleton, i, out var bindBone))
                    continue;

                var bone = bones[i];
                if (i == skeleton.AccumulationBoneIndex)
                {
                    float3 bindPosition = ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(bindBone);
                    bone.LocalPosition.x = bindPosition.x;
                    bone.LocalPosition.z = bindPosition.z;
                }

                float4x4 local = bone.LocalPoseAnimated == 0
                    ? ActorAnimationCatalogRuntimeUtility.RuntimeBindLocalMatrix(bindBone)
                    : BuildLocalMatrix(bone);
                int parentIndex = bindBone.ParentIndex;
                bone.LocalToRoot = parentIndex >= 0 && parentIndex < i
                    ? math.mul(bones[parentIndex].LocalToRoot, local)
                    : local;
                bones[i] = bone;
            }
        }

        static float4x4 BuildLocalMatrix(ActorBone bone)
        {
            float scale = bone.LocalScale;
            if (scale <= 0f)
                scale = 1f;

            return float4x4.TRS(
                bone.LocalPosition,
                SafeNormalize(bone.LocalRotation),
                new float3(scale));
        }

        static quaternion SafeNormalize(quaternion value)
            => math.lengthsq(value.value) > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;

    }
}
#endif
