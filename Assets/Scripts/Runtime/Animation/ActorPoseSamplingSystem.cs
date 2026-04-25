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
    [UpdateAfter(typeof(ActorAnimationGraphSystem))]
    public partial struct ActorPoseSamplingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalog.IsCreated)
                return;

            var job = new SampleActorPoseJob { Catalog = catalog };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(CPUAnimation))]
        partial struct SampleActorPoseJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                DynamicBuffer<ActorBone> bones,
                DynamicBuffer<ActorSampledBonePose> sampled,
                DynamicBuffer<ActorAnimationLayer> layers)
            {
                if (!Catalog.IsCreated || bones.Length == 0)
                    return;

                ref var catalog = ref Catalog.Value;
                EnsureSampledLength(sampled, bones.Length);
                ResetToBindPose(bones);

                bool anyLayer = false;
                for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                {
                    var layer = layers[layerIndex];
                    float weight = math.clamp(layer.Weight, 0f, 1f);
                    if (weight <= 0f || (uint)layer.ClipIndex >= (uint)catalog.Clips.Length)
                        continue;

                    var clip = catalog.Clips[layer.ClipIndex];
                    if (clip.TrackCount <= 0 || clip.FirstTrackIndex < 0)
                        continue;

                    CopyBindPose(bones, sampled);
                    SampleClipTracks(ref catalog, clip, layer.Time, sampled);
                    ApplyXyzRotations(sampled);
                    BlendLayerIntoBones(bones, sampled, weight, anyLayer);
                    anyLayer = true;
                }

                ComposeHierarchy(bones);
            }
        }

        static void EnsureSampledLength(DynamicBuffer<ActorSampledBonePose> sampled, int boneCount)
        {
            if (sampled.Length != boneCount)
                sampled.ResizeUninitialized(boneCount);
        }

        static void ResetToBindPose(DynamicBuffer<ActorBone> bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                bone.LocalPosition = bone.BindPosition;
                bone.LocalRotation = SafeNormalize(bone.BindRotation);
                bone.LocalScale = bone.BindScale <= 0f ? 1f : bone.BindScale;
                bones[i] = bone;
            }
        }

        static void CopyBindPose(DynamicBuffer<ActorBone> bones, DynamicBuffer<ActorSampledBonePose> sampled)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                sampled[i] = new ActorSampledBonePose
                {
                    Position = bone.BindPosition,
                    Rotation = SafeNormalize(bone.BindRotation),
                    Scale = bone.BindScale <= 0f ? 1f : bone.BindScale,
                    AxisRotation = float3.zero,
                    AxisFlags = 0,
                    AxisOrder = 0,
                };
            }
        }

        static void SampleClipTracks(
            ref ActorAnimationCatalogBlob catalog,
            ActorAnimationClipBlob clip,
            float layerTime,
            DynamicBuffer<ActorSampledBonePose> sampled)
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
                switch (track.Kind)
                {
                    case ActorAnimationTrackKind.Translation:
                    {
                        float4 value = SampleValue(ref catalog, track, trackTime);
                        pose.Position = new float3(value.x, value.z, value.y) * WorldScale.MwUnitsToMeters;
                        break;
                    }
                    case ActorAnimationTrackKind.Rotation:
                    {
                        pose.Rotation = ToUnityRotation(SampleSourceRotation(ref catalog, track, trackTime));
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
            => SafeNormalize(new quaternion(key.Value.x, key.Value.y, key.Value.z, key.Value.w));

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

                pose.Rotation = ToUnityRotation(ComposeSourceXyzRotation(pose.AxisRotation, pose.AxisOrder));
                sampled[i] = pose;
            }
        }

        static quaternion ComposeSourceXyzRotation(float3 angles, int axisOrder)
        {
            quaternion x = quaternion.AxisAngle(new float3(1f, 0f, 0f), angles.x);
            quaternion y = quaternion.AxisAngle(new float3(0f, 1f, 0f), angles.y);
            quaternion z = quaternion.AxisAngle(new float3(0f, 0f, 1f), angles.z);

            return axisOrder switch
            {
                1 => SafeNormalize(math.mul(math.mul(x, z), y)),
                2 => SafeNormalize(math.mul(math.mul(y, x), z)),
                3 => SafeNormalize(math.mul(math.mul(y, z), x)),
                4 => SafeNormalize(math.mul(math.mul(z, x), y)),
                5 => SafeNormalize(math.mul(math.mul(z, y), x)),
                _ => SafeNormalize(math.mul(math.mul(x, y), z)),
            };
        }

        static quaternion ToUnityRotation(quaternion sourceRotation)
        {
            sourceRotation = SafeNormalize(sourceRotation);
            float3x3 source = new(sourceRotation);
            float3 up = new(source.c2.x, source.c2.z, source.c2.y);
            float3 forward = new(source.c1.x, source.c1.z, source.c1.y);
            return SafeNormalize(quaternion.LookRotationSafe(forward, up));
        }

        static void BlendLayerIntoBones(
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorSampledBonePose> sampled,
            float weight,
            bool hasPreviousLayer)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                var pose = sampled[i];
                float3 basePosition = hasPreviousLayer ? bone.LocalPosition : bone.BindPosition;
                quaternion baseRotation = hasPreviousLayer ? SafeNormalize(bone.LocalRotation) : SafeNormalize(bone.BindRotation);
                float baseScale = hasPreviousLayer ? bone.LocalScale : (bone.BindScale <= 0f ? 1f : bone.BindScale);

                bone.LocalPosition = math.lerp(basePosition, pose.Position, weight);
                bone.LocalRotation = SafeNormalize(math.slerp(baseRotation, SafeNormalize(pose.Rotation), weight));
                bone.LocalScale = math.lerp(baseScale, pose.Scale, weight);
                bones[i] = bone;
            }
        }

        static void ComposeHierarchy(DynamicBuffer<ActorBone> bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                float scale = bone.LocalScale <= 0f ? bone.BindScale : bone.LocalScale;
                if (scale <= 0f)
                    scale = 1f;

                float4x4 local = float4x4.TRS(
                    bone.LocalPosition,
                    SafeNormalize(bone.LocalRotation),
                    new float3(scale));
                bone.LocalToRoot = bone.ParentIndex >= 0 && bone.ParentIndex < i
                    ? math.mul(bones[bone.ParentIndex].LocalToRoot, local)
                    : local;
                bone.SkinMatrix = bone.LocalToRoot;
                bones[i] = bone;
            }
        }

        static quaternion SafeNormalize(quaternion value)
            => math.lengthsq(value.value) > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;
    }
}
